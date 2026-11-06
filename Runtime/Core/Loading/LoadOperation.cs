using System;
using System.Linq;

namespace Mosaic.Core
{
    /// <summary>
    /// Work that takes several frames: loading a scene, assets, content from a server. The domain can't await Unity,
    /// so a game state polls it in Tick. Dispose cancels the work or releases what it loaded; the owner disposes it
    /// when it leaves (a state in OnExit).
    /// </summary>
    public interface ILoadOperation : IDisposable
    {
        /// <summary>0..1.</summary>
        float Progress { get; }

        /// <summary>Finished, successfully or not.</summary>
        bool IsDone { get; }

        /// <summary>Why it failed; null while running and on success.</summary>
        Exception Error { get; }
    }

    public interface ILoadOperation<out T> : ILoadOperation
    {
        /// <summary>The loaded value. Throws if the operation is not done or failed.</summary>
        T Result { get; }
    }

    public static class LoadOperation
    {
        public static ILoadOperation<T> Completed<T>(T result)
        {
            var operation = new ManualLoadOperation<T>();
            operation.Complete(result);
            return operation;
        }

        public static ILoadOperation<T> Failed<T>(Exception error)
        {
            var operation = new ManualLoadOperation<T>();
            operation.Fail(error);
            return operation;
        }

        /// <summary>Synchronous work run when the operation is first polled; an exception becomes its Error.</summary>
        public static ILoadOperation<T> Of<T>(Func<T> work) => new DeferredOperation<T>(work ?? throw new ArgumentNullException(nameof(work)));

        /// <summary>All operations at once; progress is weighted by the given weights; fails on the first error.</summary>
        public static ILoadOperation All(params (ILoadOperation Operation, float Weight)[] operations) => new AllOperation(operations);

        public static ILoadOperation All(params ILoadOperation[] operations) =>
            new AllOperation(operations.Select(operation => (operation, 1f)).ToArray());

        /// <summary>
        /// Starts <paramref name="next"/> with the result of <paramref name="first"/> once it is done.
        /// <paramref name="firstShare"/> is the part of the progress bar the first operation takes.
        /// </summary>
        public static ILoadOperation<TNext> Then<T, TNext>(this ILoadOperation<T> first, Func<T, ILoadOperation<TNext>> next, float firstShare = 0.5f) =>
            new ThenOperation<T, TNext>(first, next, firstShare);

        /// <summary>Turns the result into another value, for example a config asset into a domain definition.</summary>
        public static ILoadOperation<TResult> Select<T, TResult>(this ILoadOperation<T> operation, Func<T, TResult> select) =>
            operation.Then(result => Of(() => select(result)), firstShare: 1);

        private static float Clamp01(float value) => value < 0 ? 0 : value > 1 ? 1 : value;

        private sealed class DeferredOperation<T> : ILoadOperation<T>
        {
            private Func<T> _work;
            private T _result;
            private Exception _error;

            public DeferredOperation(Func<T> work) => _work = work;

            public float Progress => Run() ? 1 : 0;
            public bool IsDone => Run();
            public Exception Error => Run() ? _error : null;

            public T Result => Run() && _error == null ? _result : throw ResultError(_error);

            public void Dispose() => _work = null;

            private bool Run()
            {
                if (_work == null)
                    return true;

                var work = _work;
                _work = null;
                try
                {
                    _result = work();
                }
                catch (Exception exception)
                {
                    _error = exception;
                }

                return true;
            }
        }

        private sealed class AllOperation : ILoadOperation
        {
            private readonly (ILoadOperation Operation, float Weight)[] _operations;
            private readonly float _total;

            public AllOperation((ILoadOperation Operation, float Weight)[] operations)
            {
                _operations = operations ?? throw new ArgumentNullException(nameof(operations));
                if (_operations.Any(part => part.Operation == null || part.Weight < 0))
                    throw new ArgumentException("Every part needs an operation and a non-negative weight", nameof(operations));
                _total = _operations.Sum(part => part.Weight);
            }

            public float Progress
            {
                get
                {
                    if (IsDone && Error == null)
                        return 1;
                    return _total <= 0 ? 0 : Clamp01(_operations.Sum(part => part.Weight * part.Operation.Progress) / _total);
                }
            }

            public bool IsDone => Error != null || _operations.All(part => part.Operation.IsDone);

            public Exception Error => _operations.Select(part => part.Operation.Error).FirstOrDefault(error => error != null);

            public void Dispose()
            {
                foreach (var part in _operations)
                    part.Operation.Dispose();
            }
        }

        private sealed class ThenOperation<T, TNext> : ILoadOperation<TNext>
        {
            private readonly ILoadOperation<T> _first;
            private readonly Func<T, ILoadOperation<TNext>> _createNext;
            private readonly float _firstShare;
            private ILoadOperation<TNext> _next;
            private Exception _error;
            private bool _disposed;

            public ThenOperation(ILoadOperation<T> first, Func<T, ILoadOperation<TNext>> next, float firstShare)
            {
                _first = first ?? throw new ArgumentNullException(nameof(first));
                _createNext = next ?? throw new ArgumentNullException(nameof(next));
                _firstShare = Clamp01(firstShare);
            }

            public float Progress
            {
                get
                {
                    Advance();
                    return _next == null
                        ? _firstShare * _first.Progress
                        : Clamp01(_firstShare + (1 - _firstShare) * _next.Progress);
                }
            }

            public bool IsDone
            {
                get
                {
                    Advance();
                    return _error != null || _first.Error != null || _next != null && _next.IsDone;
                }
            }

            public Exception Error
            {
                get
                {
                    Advance();
                    return _error ?? _first.Error ?? _next?.Error;
                }
            }

            public TNext Result => IsDone && Error == null ? _next.Result : throw ResultError(Error);

            public void Dispose()
            {
                _disposed = true;
                _first.Dispose();
                _next?.Dispose();
            }

            private void Advance()
            {
                if (_next != null || _error != null || _disposed || !_first.IsDone || _first.Error != null)
                    return;

                try
                {
                    _next = _createNext(_first.Result) ?? throw new InvalidOperationException("The next operation is null");
                }
                catch (Exception exception)
                {
                    _error = exception;
                }
            }
        }

        internal static Exception ResultError(Exception error) =>
            error != null
                ? new InvalidOperationException("The operation failed: " + error.Message, error)
                : new InvalidOperationException("The operation is not done yet");
    }

    /// <summary>
    /// An operation finished by hand: for tests ("loading takes three frames, then fails") and for adapters of
    /// callback-based APIs.
    /// </summary>
    public sealed class ManualLoadOperation<T> : ILoadOperation<T>
    {
        private T _result;

        public float Progress { get; private set; }
        public bool IsDone { get; private set; }
        public Exception Error { get; private set; }
        public bool IsDisposed { get; private set; }

        public T Result => IsDone && Error == null ? _result : throw LoadOperation.ResultError(Error);

        public void SetProgress(float progress)
        {
            if (!IsDone)
                Progress = progress < 0 ? 0 : progress > 1 ? 1 : progress;
        }

        public void Complete(T result)
        {
            EnsureRunning();
            _result = result;
            Progress = 1;
            IsDone = true;
        }

        public void Fail(Exception error)
        {
            EnsureRunning();
            Error = error ?? throw new ArgumentNullException(nameof(error));
            IsDone = true;
        }

        public void Dispose() => IsDisposed = true;

        private void EnsureRunning()
        {
            if (IsDone)
                throw new InvalidOperationException("The operation is already done");
        }
    }
}
