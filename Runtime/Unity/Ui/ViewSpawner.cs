using System;
using System.Collections.Generic;
using Mosaic.Core;
using UnityEngine;
using EntityId = Mosaic.Core.EntityId;

namespace Mosaic.Ui
{
    public sealed class ViewSpawner : IDisposable
    {
        private readonly World _world;
        private readonly Func<int, EntityView> _prefabOf;
        private readonly Func<EntityId, bool> _isVisible;
        private readonly ViewPool _pool;
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();

        public ViewSpawner(World world, IEventBus events, Func<int, EntityView> prefabOf, Func<EntityId, bool> isVisible, ViewPool pool)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _prefabOf = prefabOf ?? throw new ArgumentNullException(nameof(prefabOf));
            _isVisible = isVisible ?? (_ => true);
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));

            _subscriptions.Add(events.Subscribe<EntitySpawned>(m => Refresh(m.Entity)));
            _subscriptions.Add(events.Subscribe<EntityDestroyed>(m => Hide(m.Entity)));
        }

        public EntityViews Views { get; } = new EntityViews();

        public void CatchUp()
        {
            foreach (var entity in _world.Query<Kind>())
                Refresh(entity.Id);
        }

        public void Refresh(EntityId id)
        {
            var shouldShow = _world.Get<Kind>(id) is { } kind && _isVisible(id) && _prefabOf(kind.Id) != null;
            var shown = Views.TryGet(id, out _);

            if (shouldShow && !shown)
                Show(id, _world.Get<Kind>(id).Id);
            else if (!shouldShow && shown)
                Hide(id);
        }

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
            _subscriptions.Clear();
            Views.Clear();
        }

        private void Show(EntityId id, int kind)
        {
            var view = _pool.Get(_prefabOf(kind));
            view.Bind(id);
            Views.Add(id, kind, view);
        }

        private void Hide(EntityId id)
        {
            if (Views.Remove(id, out var view) && view != null)
                _pool.Release(view);
        }
    }
}
