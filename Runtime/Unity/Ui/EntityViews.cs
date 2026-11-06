using System;
using System.Collections.Generic;
using Mosaic.Core;
using UnityEngine;
using EntityId = Mosaic.Core.EntityId;

namespace Mosaic.Ui
{
    public sealed class EntityViews
    {
        private readonly Dictionary<EntityId, EntityView> _views = new Dictionary<EntityId, EntityView>();

        public event Action<EntityId, int, EntityView> Created;

        public event Action<EntityId, EntityView> Removed;

        public Dictionary<EntityId, EntityView>.ValueCollection All => _views.Values;

        public bool TryGet(EntityId id, out EntityView view) => _views.TryGetValue(id, out view);

        public bool TryGet<TPart>(EntityId id, out TPart part) where TPart : Component
        {
            part = null;
            return _views.TryGetValue(id, out var view) && view.TryGet(out part);
        }

        internal void Add(EntityId id, int kind, EntityView view)
        {
            _views[id] = view;
            Created?.Invoke(id, kind, view);
        }

        internal bool Remove(EntityId id, out EntityView view)
        {
            if (!_views.TryGetValue(id, out view))
                return false;

            _views.Remove(id);
            Removed?.Invoke(id, view);
            return true;
        }

        internal void Clear() => _views.Clear();
    }
}
