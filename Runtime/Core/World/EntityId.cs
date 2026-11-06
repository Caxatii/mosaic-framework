using System;

namespace Mosaic.Core
{
    public readonly struct EntityId : IEquatable<EntityId>
    {
        public readonly int Index;
        public readonly uint Generation;

        public EntityId(int index, uint generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool Equals(EntityId other) =>
            Index == other.Index && Generation == other.Generation;

        public override bool Equals(object obj) =>
            obj is EntityId other && Equals(other);

        public override int GetHashCode() =>
            (Index * 397) ^ (int)Generation;

        public static bool operator ==(EntityId a, EntityId b) => a.Equals(b);
        public static bool operator !=(EntityId a, EntityId b) => !a.Equals(b);

        public override string ToString() => $"Entity({Index}:{Generation})";
    }
}
