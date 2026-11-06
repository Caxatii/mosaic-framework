using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace Mosaic.Core
{
    public interface IEvent
    {
    }

    /// <summary>
    /// Something events can be received from: the domain EventBus, or an EventFeed for presentation.
    /// </summary>
    public interface IEventSource
    {
        IDisposable Subscribe<T>(Action<T> handler) where T : struct, IEvent;
    }

    public interface IEventBus : IEventSource
    {
        void Publish<T>(in T message) where T : struct, IEvent;
    }

    public readonly struct EntitySpawned : IEvent
    {
        public EntitySpawned(EntityId entity, int kind)
        {
            Entity = entity;
            Kind = kind;
        }

        public EntityId Entity { get; }
        public int Kind { get; }
    }

    public readonly struct EntityDestroyed : IEvent
    {
        public EntityDestroyed(EntityId entity) => Entity = entity;

        public EntityId Entity { get; }
    }

    /// <summary>
    /// Allocation-free event queue. Events published during an action are delivered after it, in publish order;
    /// events published by subscribers go to the end of the queue.
    /// </summary>
    public sealed class EventBus : IEventBus
    {
        private readonly Dictionary<Type, Channel> _channels = new Dictionary<Type, Channel>();
        private readonly Queue<Channel> _order = new Queue<Channel>();
        private readonly ILog _log;

        public EventBus(ILog log, int maxEventsPerDispatch = 10000)
        {
            _log = log ?? NullLog.Instance;
            MaxEventsPerDispatch = maxEventsPerDispatch;
        }

        public bool ThrowOnSubscriberException { get; set; }
        public int MaxEventsPerDispatch { get; }
        public int PendingCount => _order.Count;

        internal SimulationTrace Trace { get; set; }

        internal ISimulationProfiler Profiler { get; set; }

        public void Publish<T>(in T message) where T : struct, IEvent
        {
            Trace?.Line("+ " + TraceFormat.Of(message));

            var channel = ChannelOf<T>();
            channel.Pending.Enqueue(message);
            _order.Enqueue(channel);
        }

        public IDisposable Subscribe<T>(Action<T> handler) where T : struct, IEvent
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            return ChannelOf<T>().Add(handler);
        }

        public void Dispatch(Action afterEachSubscriber = null)
        {
            var delivered = 0;

            while (_order.Count > 0)
            {
                if (++delivered > MaxEventsPerDispatch)
                {
                    _log.Error($"EventBus: more than {MaxEventsPerDispatch} events in one action, queue dropped ({_order.Count} events)");
                    foreach (var channel in _channels.Values)
                        channel.ClearPending();
                    _order.Clear();
                    return;
                }

                _order.Dequeue().DeliverNext(this, afterEachSubscriber);
            }
        }

        internal void ClearPending()
        {
            foreach (var channel in _channels.Values)
                channel.ClearPending();
            _order.Clear();
        }

        private Channel<T> ChannelOf<T>() where T : struct, IEvent
        {
            if (_channels.TryGetValue(typeof(T), out var channel))
                return (Channel<T>)channel;

            var created = new Channel<T>();
            _channels.Add(typeof(T), created);
            return created;
        }

        private void Report(Exception exception, string owner, string eventName)
        {
            if (ThrowOnSubscriberException)
                ExceptionDispatchInfo.Capture(exception).Throw();

            _log.Error($"EventBus: subscriber {owner} failed on {eventName}", exception);
        }

        private abstract class Channel
        {
            public abstract void DeliverNext(EventBus bus, Action afterEachSubscriber);
            public abstract void ClearPending();
        }

        private sealed class Channel<T> : Channel where T : struct, IEvent
        {
            private static readonly Predicate<Subscription> IsDisposed = s => s.IsDisposed;

            private readonly List<Subscription> _subscribers = new List<Subscription>();
            private int _delivering;
            private bool _hasDisposed;

            public readonly Queue<T> Pending = new Queue<T>();

            public IDisposable Add(Action<T> handler)
            {
                var subscription = new Subscription(this, handler);
                _subscribers.Add(subscription);
                return subscription;
            }

            public override void DeliverNext(EventBus bus, Action afterEachSubscriber)
            {
                var message = Pending.Dequeue();
                var count = _subscribers.Count;

                _delivering++;
                try
                {
                    for (var i = 0; i < count; i++)
                    {
                        var subscription = _subscribers[i];
                        if (subscription.IsDisposed)
                            continue;

                        var trace = bus.Trace;
                        trace?.Enter(typeof(T).Name + " → " + subscription.Owner, quiet: true);
                        var profiler = bus.Profiler;
                        profiler?.Begin(subscription.ProfileName);
                        try
                        {
                            subscription.Handler(message);
                        }
                        catch (Exception exception)
                        {
                            trace?.Line("! " + exception.GetType().Name + ": " + exception.Message);
                            bus.Report(exception, subscription.Owner, typeof(T).Name);
                        }
                        finally
                        {
                            profiler?.End();
                            trace?.Exit();
                        }

                        afterEachSubscriber?.Invoke();
                    }
                }
                finally
                {
                    _delivering--;
                    RemoveDisposed();
                }
            }

            public override void ClearPending() => Pending.Clear();

            private void MarkDisposed()
            {
                _hasDisposed = true;
                RemoveDisposed();
            }

            private void RemoveDisposed()
            {
                if (_delivering > 0 || !_hasDisposed)
                    return;

                _hasDisposed = false;
                _subscribers.RemoveAll(IsDisposed);
            }

            public sealed class Subscription : IDisposable
            {
                private readonly Channel<T> _channel;

                public Subscription(Channel<T> channel, Action<T> handler)
                {
                    _channel = channel;
                    Handler = handler;
                }

                public Action<T> Handler { get; }
                public string Owner => TraceFormat.OwnerOf(Handler);
                public string ProfileName => _profileName ??= typeof(T).Name + " → " + Owner;
                private string _profileName;
                public bool IsDisposed { get; private set; }

                public void Dispose()
                {
                    if (IsDisposed)
                        return;

                    IsDisposed = true;
                    _channel.MarkDisposed();
                }
            }
        }
    }
}
