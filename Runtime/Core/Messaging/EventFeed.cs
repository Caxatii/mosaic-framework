using System;
using System.Collections.Generic;

namespace Mosaic.Core
{
    /// <summary>
    /// Domain events for code outside the simulation: presentation, sound, effects, analytics. Events are collected
    /// while the simulation runs and handed out by <see cref="Deliver"/>, which the owner calls once a frame after
    /// the match has ticked (a screen in LateTick). Handlers therefore run outside any action: a command they send is
    /// external input, so it is recorded and replays the same, and they can't change the world in the middle of an
    /// action. Order across event types is kept. Dispose the feed when its owner goes away.
    /// </summary>
    public sealed class EventFeed : IEventSource, IDisposable
    {
        private readonly IEventBus _source;
        private readonly Simulation _simulation;
        private readonly EventBus _queue;
        private readonly GameplayContext _context;
        private readonly Dictionary<Type, IDisposable> _forwarders = new Dictionary<Type, IDisposable>();
        private bool _disposed;

        public EventFeed(GameplayContext context)
            : this(context?.Events ?? throw new ArgumentNullException(nameof(context)), context.Simulation, context.Log,
                context.EventBus.ThrowOnSubscriberException) =>
            _context = context;

        public EventFeed(IEventBus source, Simulation simulation = null, ILog log = null, bool throwOnSubscriberException = false)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _simulation = simulation;
            _queue = new EventBus(log) { ThrowOnSubscriberException = throwOnSubscriberException };
        }

        /// <summary>Events waiting for the next Deliver.</summary>
        public int PendingCount => _queue.PendingCount;

        public IDisposable Subscribe<T>(Action<T> handler) where T : struct, IEvent
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(EventFeed));

            if (!_forwarders.ContainsKey(typeof(T)))
                _forwarders.Add(typeof(T), _source.Subscribe<T>(Forward));

            return _queue.Subscribe(handler);
        }

        /// <summary>
        /// Hands out the collected events in the order they happened. Call it outside the simulation, once a frame.
        /// </summary>
        public void Deliver()
        {
            if (_simulation != null && _simulation.IsRunning)
                throw new InvalidOperationException(
                    "EventFeed.Deliver runs inside a simulation action. Call it after the match has ticked, for example in LateTick.");

            _queue.Profiler = _context?.Profiler;
            _queue.Dispatch();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            foreach (var forwarder in _forwarders.Values)
                forwarder.Dispose();
            _forwarders.Clear();
            _queue.ClearPending();
        }

        private void Forward<T>(T message) where T : struct, IEvent => _queue.Publish(message);
    }
}
