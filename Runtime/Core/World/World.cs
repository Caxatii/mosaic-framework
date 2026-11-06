using System;
using System.Collections.Generic;

namespace Mosaic.Core
{
    /// <summary>
    /// How World reacts when a component is read while an Add/Remove for it is still buffered.
    /// </summary>
    public enum StaleReadCheck
    {
        Off,
        Warn,
        Throw,
    }

    /// <summary>
    /// Stores entities and their components. Inside a Simulation action (deferred mode):
    /// an entity spawned in the action is "being built" and sees its own components through Get, but appears in queries only after the action;
    /// Add and Remove on existing entities are buffered and become visible after the action;
    /// Destroy makes the entity unavailable at once, and its slot is released after the action.
    /// </summary>
    public sealed class World
    {
        private readonly List<uint> _generations = new();
        private readonly List<bool> _alive = new();
        private readonly Queue<int> _freeSlots = new();
        private readonly Dictionary<Type, ComponentStore> _storesByType = new();
        private ComponentStore[] _stores = new ComponentStore[16];
        private readonly List<PendingChange> _pending = new();
        private readonly List<EntityId> _destroyed = new();
        private readonly List<EntityId> _spawned = new();

        private readonly Dictionary<int, Dictionary<Type, IModel>> _building = new();

        private readonly HashSet<(int Index, Type Type)> _pendingByType = new();

        private int _deferDepth;

        public bool IsDeferred => _deferDepth > 0;
        public int Count { get; private set; }

        public StaleReadCheck StaleReads { get; set; }

        public event Action<EntityId, Type, string> StaleRead;

        public Entity Spawn(int kind)
        {
            if (kind == 0)
                throw new ArgumentException("An entity needs a kind (its config id)", nameof(kind));

            var entity = CreateEntity();
            if (IsDeferred)
                _building.Add(entity.Id.Index, new Dictionary<Type, IModel>());

            Add(entity.Id, new Kind(kind));
            _spawned.Add(entity.Id);
            return entity;
        }

        public Entity Spawn(EntityDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            var entity = Spawn(definition.Id);
            foreach (var part in definition.Parts)
                Add(entity.Id, part.CreateModel());
            return entity;
        }

        internal Entity CreateEntity()
        {
            int index;

            if (_freeSlots.Count > 0)
            {
                index = _freeSlots.Dequeue();
                _alive[index] = true;
            }
            else
            {
                index = _generations.Count;
                _generations.Add(0);
                _alive.Add(true);
            }

            Count++;
            return new Entity(new EntityId(index, _generations[index]), this);
        }

        public IEnumerable<EntityId> Entities
        {
            get
            {
                for (var index = 0; index < _alive.Count; index++)
                    if (_alive[index])
                        yield return new EntityId(index, _generations[index]);
            }
        }

        internal Entity EntityAt(int index) => new Entity(new EntityId(index, _generations[index]), this);

        internal bool IsAliveAt(int index) => _alive[index];

        internal ComponentStore<T> StoreOf<T>() where T : class, IModel
        {
            var id = ComponentType<T>.Id;
            return id < _stores.Length ? (ComponentStore<T>)_stores[id] : null;
        }

        public bool IsAlive(EntityId id) =>
            id.Index >= 0 && id.Index < _alive.Count && _alive[id.Index] && _generations[id.Index] == id.Generation;

        /// <summary>
        /// True for an entity spawned in the current action: its components are visible through Get only.
        /// </summary>
        public bool IsBuilding(EntityId id) => IsAlive(id) && _building.ContainsKey(id.Index);

        public bool TryGetEntity(EntityId id, out Entity entity)
        {
            entity = IsAlive(id) ? new Entity(id, this) : default;
            return entity.World != null;
        }

        public Entity? GetEntity(EntityId id) => IsAlive(id) ? new Entity(id, this) : (Entity?)null;

        public T Get<T>(EntityId id) where T : class, IModel
        {
            if (!IsAlive(id))
                return null;

            if (_building.Count > 0 && _building.TryGetValue(id.Index, out var building))
                return building.TryGetValue(typeof(T), out var built) ? (T)built : null;

            if (_pendingByType.Count > 0 && _pendingByType.Contains((id.Index, typeof(T))))
                ReportStaleRead(id, typeof(T));

            return StoreOf<T>()?.Get(id.Index);
        }

        public bool Has<T>(EntityId id) where T : class, IModel => Get<T>(id) != null;

        public IEnumerable<IModel> GetAll(EntityId id)
        {
            if (!IsAlive(id))
                yield break;

            if (_building.TryGetValue(id.Index, out var building))
            {
                foreach (var component in building.Values)
                    if (component != null)
                        yield return component;
                yield break;
            }

            foreach (var store in _stores)
                if (store != null && store.Has(id.Index))
                    yield return store.GetModel(id.Index);
        }

        public void Add(EntityId id, IModel component)
        {
            if (component == null)
                throw new ArgumentNullException(nameof(component));
            EnsureAlive(id);

            if (!IsDeferred)
            {
                ApplyAdd(id, component);
                return;
            }

            _pending.Add(PendingChange.Add(id, component));
            if (_building.TryGetValue(id.Index, out var building))
                building[component.GetType()] = component;
            else
                TrackPending(id, component.GetType());
        }

        public void Remove<T>(EntityId id) where T : class, IModel
        {
            EnsureAlive(id);

            if (!IsDeferred)
            {
                ApplyRemove(id, typeof(T));
                return;
            }

            _pending.Add(PendingChange.Remove(id, typeof(T)));
            if (_building.TryGetValue(id.Index, out var building))
                building.Remove(typeof(T));
            else
                TrackPending(id, typeof(T));
        }

        /// <summary>
        /// The entity becomes unavailable at once: IsAlive is false, Get returns null, queries and systems skip it,
        /// commands to it are rejected. Inside an action its slot is released and EntityDestroyed is published
        /// when the action ends.
        /// </summary>
        public void Destroy(EntityId id)
        {
            EnsureAlive(id);

            _alive[id.Index] = false;
            Count--;

            if (IsDeferred)
                _pending.Add(PendingChange.Destroy(id));
            else
                ReleaseSlot(id);
        }

        public EntityQuery<T> Query<T>() where T : class, IModel => new EntityQuery<T>(this, StoreOf<T>());

        public EntityQuery<T1, T2> Query<T1, T2>()
            where T1 : class, IModel
            where T2 : class, IModel =>
            new EntityQuery<T1, T2>(this, StoreOf<T1>(), StoreOf<T2>());

        public EntityQuery<T1, T2, T3> Query<T1, T2, T3>()
            where T1 : class, IModel
            where T2 : class, IModel
            where T3 : class, IModel =>
            new EntityQuery<T1, T2, T3>(this, StoreOf<T1>(), StoreOf<T2>(), StoreOf<T3>());

        public DeferScope Defer()
        {
            _deferDepth++;
            return new DeferScope(this);
        }

        public void Flush()
        {
            for (var i = 0; i < _pending.Count; i++)
            {
                var change = _pending[i];

                if (change.Kind == ChangeKind.Destroy)
                {
                    if (_generations[change.Id.Index] == change.Id.Generation)
                        ReleaseSlot(change.Id);
                    continue;
                }

                if (!IsAlive(change.Id))
                    continue;

                if (change.Kind == ChangeKind.Add)
                    ApplyAdd(change.Id, change.Component);
                else
                    ApplyRemove(change.Id, change.ComponentType);
            }

            _pending.Clear();
            _pendingByType.Clear();
            _building.Clear();
        }

        public IReadOnlyList<EntityId> DrainSpawned()
        {
            if (_spawned.Count == 0)
                return Array.Empty<EntityId>();

            var result = new List<EntityId>(_spawned.Count);
            foreach (var id in _spawned)
                if (IsAlive(id))
                    result.Add(id);
            _spawned.Clear();
            return result;
        }

        public IReadOnlyList<EntityId> DrainDestroyed()
        {
            if (_destroyed.Count == 0)
                return Array.Empty<EntityId>();

            var result = _destroyed.ToArray();
            _destroyed.Clear();
            return result;
        }

        private void TrackPending(EntityId id, Type type)
        {
            if (StaleReads != StaleReadCheck.Off)
                _pendingByType.Add((id.Index, type));
        }

        private void ReportStaleRead(EntityId id, Type type)
        {
            var message = $"{type.Name} of {id} was read while an Add/Remove for it is still buffered: " +
                          "the reader sees the old state. The change becomes visible after the action (see World).";

            if (StaleReads == StaleReadCheck.Throw)
                throw new InvalidOperationException(message);

            StaleRead?.Invoke(id, type, message);
        }

        private void ApplyAdd(EntityId id, IModel component)
        {
            var type = component.GetType();

            if (!_storesByType.TryGetValue(type, out var store))
            {
                store = ComponentStore.Create(type);
                _storesByType.Add(type, store);

                var typeId = ComponentTypes.IdOf(type);
                if (typeId >= _stores.Length)
                    Array.Resize(ref _stores, Math.Max(typeId + 1, _stores.Length * 2));
                _stores[typeId] = store;
            }

            store.SetModel(id.Index, component);
        }

        private void ApplyRemove(EntityId id, Type type)
        {
            if (_storesByType.TryGetValue(type, out var store))
                store.Remove(id.Index);
        }

        private void ReleaseSlot(EntityId id)
        {
            foreach (var store in _stores)
                store?.Remove(id.Index);

            unchecked { _generations[id.Index]++; }
            _freeSlots.Enqueue(id.Index);
            _destroyed.Add(id);
        }

        private void EnsureAlive(EntityId id)
        {
            if (!IsAlive(id))
                throw new EntityNotAliveException(id);
        }

        private void EndDefer() => _deferDepth--;

        public readonly struct DeferScope : IDisposable
        {
            private readonly World _world;

            internal DeferScope(World world) => _world = world;

            public void Dispose() => _world?.EndDefer();
        }

        private enum ChangeKind { Add, Remove, Destroy }

        private readonly struct PendingChange
        {
            public readonly ChangeKind Kind;
            public readonly EntityId Id;
            public readonly IModel Component;
            public readonly Type ComponentType;

            private PendingChange(ChangeKind kind, EntityId id, IModel component, Type componentType)
            {
                Kind = kind;
                Id = id;
                Component = component;
                ComponentType = componentType;
            }

            public static PendingChange Add(EntityId id, IModel component) => new(ChangeKind.Add, id, component, null);
            public static PendingChange Remove(EntityId id, Type type) => new(ChangeKind.Remove, id, null, type);
            public static PendingChange Destroy(EntityId id) => new(ChangeKind.Destroy, id, null, null);
        }
    }
}
