using System;
using System.Collections.Generic;

namespace Mosaic.Core
{
    public sealed class EntityCatalog
    {
        private readonly Dictionary<int, EntityDefinition> _entities = new Dictionary<int, EntityDefinition>();

        public EntityCatalog(IEnumerable<EntityDefinition> entities)
        {
            foreach (var entity in entities)
            {
                if (entity == null)
                    throw new ArgumentException("Null entity definition in the catalog");
                if (_entities.TryGetValue(entity.Id, out var other))
                    throw new ArgumentException($"Id {entity.Id} is used by two entities: {other.Name} and {entity.Name}");
                _entities.Add(entity.Id, entity);
            }

            foreach (var entity in _entities.Values)
                foreach (var (path, reference) in ContentRefs.Collect<EntityRef>(entity.Parts, entity.Name))
                    if (reference.IsSet && !_entities.ContainsKey(reference.Id))
                        throw new ArgumentException($"{path}: references an undefined entity {reference}");
        }

        public IEnumerable<EntityDefinition> All => _entities.Values;

        public EntityDefinition this[EntityRef reference] =>
            _entities.TryGetValue(reference.Id, out var entity) ? entity : throw new KeyNotFoundException($"Entity {reference} is not defined");

        public bool TryGet(EntityRef reference, out EntityDefinition entity) => _entities.TryGetValue(reference.Id, out entity);
    }
}
