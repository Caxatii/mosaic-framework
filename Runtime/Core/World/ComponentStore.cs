using System;
using System.Collections.Generic;

namespace Mosaic.Core
{
    internal static class ComponentTypes
    {
        private static readonly Dictionary<Type, int> Ids = new Dictionary<Type, int>();
        private static readonly object Lock = new object();

        public static int Count { get; private set; }

        public static int IdOf(Type type)
        {
            lock (Lock)
            {
                if (!Ids.TryGetValue(type, out var id))
                    Ids.Add(type, id = Count++);
                return id;
            }
        }
    }

    internal static class ComponentType<T> where T : class, IModel
    {
        public static readonly int Id = ComponentTypes.IdOf(typeof(T));
    }

    internal abstract class ComponentStore
    {
        protected int[] Sparse = Array.Empty<int>();
        protected int[] Owners = new int[4];

        public int Count { get; protected set; }

        public int Version { get; protected set; }

        public bool Has(int entity) => entity < Sparse.Length && Sparse[entity] != 0;

        public int OwnerAt(int position) => Owners[position];

        public abstract IModel GetModel(int entity);

        public abstract void SetModel(int entity, IModel component);

        public abstract void Remove(int entity);

        public static ComponentStore Create(Type type) =>
            (ComponentStore)Activator.CreateInstance(typeof(ComponentStore<>).MakeGenericType(type));
    }

    internal sealed class ComponentStore<T> : ComponentStore where T : class, IModel
    {
        private T[] _items = new T[4];

        public T Get(int entity) => entity < Sparse.Length && Sparse[entity] is var slot && slot != 0 ? _items[slot - 1] : null;

        public T ItemAt(int position) => _items[position];

        public override IModel GetModel(int entity) => Get(entity);

        public override void SetModel(int entity, IModel component) => Set(entity, (T)component);

        public void Set(int entity, T component)
        {
            if (entity >= Sparse.Length)
                Array.Resize(ref Sparse, Math.Max(entity + 1, Sparse.Length * 2));

            var slot = Sparse[entity];
            if (slot != 0)
            {
                _items[slot - 1] = component;
                return;
            }

            if (Count == _items.Length)
            {
                Array.Resize(ref _items, Count * 2);
                Array.Resize(ref Owners, Count * 2);
            }

            _items[Count] = component;
            Owners[Count] = entity;
            Sparse[entity] = ++Count;
            Version++;
        }

        public override void Remove(int entity)
        {
            if (!Has(entity))
                return;

            var position = Sparse[entity] - 1;
            var last = Count - 1;
            if (position != last)
            {
                _items[position] = _items[last];
                Owners[position] = Owners[last];
                Sparse[Owners[position]] = position + 1;
            }

            _items[last] = null;
            Sparse[entity] = 0;
            Count = last;
            Version++;
        }
    }
}
