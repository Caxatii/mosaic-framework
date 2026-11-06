using System;

namespace Mosaic.Core
{
    /// <summary>
    /// Application state (menu, loading, gameplay). Requests to a state that is no longer active are ignored.
    /// </summary>
    public abstract class FlowState<TSelf> : IState where TSelf : FlowState<TSelf>
    {
        private readonly StateMachine<TSelf> _machine;

        protected FlowState(StateMachine<TSelf> machine) =>
            _machine = machine ?? throw new ArgumentNullException(nameof(machine));

        public bool IsActive { get; private set; }

        void IState.Enter()
        {
            IsActive = true;
            OnEnter();
        }

        void IState.Exit()
        {
            IsActive = false;
            OnExit();
        }

        void IState.Tick(float deltaTime) => Tick(deltaTime);

        protected virtual void OnEnter() { }

        protected virtual void OnExit() { }

        protected virtual void Tick(float deltaTime) { }

        protected void Go(TSelf next)
        {
            if (IsActive)
                _machine.Change(next);
        }
    }
}
