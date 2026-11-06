using System;
using System.Collections.Generic;

namespace Mosaic.Core
{
    /// <summary>
    /// Connects features: an event of one feature triggers a change through Operations of another.
    /// Subscriptions go through On and are released by Dispose. Has no access to publishing.
    /// </summary>
    public abstract class Reaction : IDisposable
    {
        private readonly IEventBus _events;
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();

        protected Reaction(GameplayContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            World = context.World;
            _events = context.Events;
        }

        protected World World { get; }

        protected void On<T>(Action<T> handler) where T : struct, IEvent =>
            _subscriptions.Add(_events.Subscribe(handler));

        public void Dispose()
        {
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
            _subscriptions.Clear();
        }
    }
}
