using System;
using System.Collections.Generic;
using Mosaic.Core;

namespace Mosaic.Testing
{
    /// <summary>
    /// Collects every event of one type from a bus, for assertions in tests. Dispose it to unsubscribe.
    /// </summary>
    public sealed class EventRecorder<T> : IDisposable where T : struct, IEvent
    {
        private readonly List<T> _received = new List<T>();
        private readonly IDisposable _subscription;

        public EventRecorder(IEventSource events) =>
            _subscription = (events ?? throw new ArgumentNullException(nameof(events))).Subscribe<T>(_received.Add);

        public IReadOnlyList<T> Received => _received;

        public int Count => _received.Count;

        public T Last => _received.Count > 0
            ? _received[_received.Count - 1]
            : throw new InvalidOperationException($"No {typeof(T).Name} was received");

        public void Clear() => _received.Clear();

        public void Dispose() => _subscription.Dispose();
    }
}
