using System;
using System.Collections.Generic;

namespace Mosaic.Core
{
    public interface IEntityPart
    {
        IModel CreateModel();
    }

    [Serializable]
    public struct EntityRef : IEquatable<EntityRef>
    {
        public int Id;

        public EntityRef(int id) => Id = id;

        public bool IsSet => Id != 0;

        public bool Equals(EntityRef other) => Id == other.Id;
        public override bool Equals(object obj) => obj is EntityRef other && Equals(other);
        public override int GetHashCode() => Id;
        public override string ToString() => $"#{Id}";

        public static bool operator ==(EntityRef a, EntityRef b) => a.Id == b.Id;
        public static bool operator !=(EntityRef a, EntityRef b) => a.Id != b.Id;
    }

    public sealed class EntityDefinition
    {
        public EntityDefinition(int id, string name, IReadOnlyList<IEntityPart> parts)
        {
            Id = id;
            Name = name ?? $"#{id}";
            Parts = parts ?? Array.Empty<IEntityPart>();
        }

        public int Id { get; }

        public string Name { get; }

        public IReadOnlyList<IEntityPart> Parts { get; }

        public EntityRef Ref => new EntityRef(Id);

        public T Get<T>() where T : class, IEntityPart
        {
            foreach (var part in Parts)
                if (part is T typed)
                    return typed;
            return null;
        }

        public bool Has<T>() where T : class, IEntityPart => Get<T>() != null;

        public override string ToString() => $"{Name} #{Id}";
    }
}
