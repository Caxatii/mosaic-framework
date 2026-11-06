using System;
using System.Collections.Generic;
using UnityEngine;
using EntityId = Mosaic.Core.EntityId;

namespace Mosaic.Ui
{
    public sealed class EntityView : MonoBehaviour
    {
        private readonly Dictionary<Type, Component> _parts = new Dictionary<Type, Component>();

        public EntityId Id { get; private set; }

        public bool TryGet<T>(out T part) where T : Component
        {
            if (!_parts.TryGetValue(typeof(T), out var cached))
                _parts[typeof(T)] = cached = GetComponentInChildren<T>(includeInactive: true);

            part = (T)cached;
            return part != null;
        }

        internal void Bind(EntityId id) => Id = id;
    }
}
