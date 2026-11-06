using System;

namespace Mosaic.Core
{
    public interface IState
    {
        void Enter();
        void Exit();
        void Tick(float deltaTime);
    }

    public sealed class StateMachine<TState> where TState : class, IState
    {
        private TState _next;
        private bool _changing;

        public TState Current { get; private set; }

        public event Action<TState> Changed;

        public void Change(TState next)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            if (_changing)
                return;

            _changing = true;
            try
            {
                while (_next != null)
                {
                    var state = _next;
                    _next = null;

                    Current?.Exit();
                    Current = state;
                    state.Enter();
                    Changed?.Invoke(state);
                }
            }
            finally
            {
                _changing = false;
            }
        }

        public void Tick(float deltaTime) => Current?.Tick(deltaTime);

        public void Stop()
        {
            Current?.Exit();
            Current = null;
        }
    }
}
