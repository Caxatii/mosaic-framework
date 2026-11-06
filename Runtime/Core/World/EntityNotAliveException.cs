using System;

namespace Mosaic.Core
{
    public sealed class EntityNotAliveException : InvalidOperationException
    {
        public EntityNotAliveException(EntityId id)
            : base($"{id} is not alive")
        {
            EntityId = id;
        }

        public EntityId EntityId { get; }
    }
}
