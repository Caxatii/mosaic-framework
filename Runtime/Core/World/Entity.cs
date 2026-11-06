using System;
using System.Collections.Generic;

namespace Mosaic.Core
{
    public readonly struct Entity : IEquatable<Entity>
    {
        public readonly EntityId Id;
        public readonly World World;

        public Entity(EntityId id, World world)
        {
            Id = id;
            World = world ?? throw new ArgumentNullException(nameof(world));
        }

        public bool IsAlive =>
            World != null && World.IsAlive(Id);

        public bool IsBuilding =>
            World != null && World.IsBuilding(Id);

        public T Get<T>() where T : class, IModel =>
            World.Get<T>(EnsureAlive());

        public bool Has<T>() where T : class, IModel =>
            World.Has<T>(EnsureAlive());

        public IEnumerable<IModel> GetAll() =>
            World.GetAll(EnsureAlive());

        public void Add(IModel component) =>
            World.Add(EnsureAlive(), component);

        public void Add(params IModel[] components)
        {
            var id = EnsureAlive();
            foreach (var component in components)
                World.Add(id, component);
        }

        public void Remove<T>() where T : class, IModel => World.Remove<T>(EnsureAlive());

        public void Destroy() => World.Destroy(EnsureAlive());

        public bool Equals(Entity other) => Id == other.Id && ReferenceEquals(World, other.World);
        public override bool Equals(object obj) => obj is Entity other && Equals(other);
        public override int GetHashCode() => Id.GetHashCode();
        public override string ToString() => Id.ToString();

        private EntityId EnsureAlive()
        {
            if (!IsAlive)
                throw new EntityNotAliveException(Id);
            return Id;
        }
    }
}
