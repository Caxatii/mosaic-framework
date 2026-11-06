using System;
using System.Collections.Generic;
using Mosaic.Core;

namespace Mosaic.Ui
{
    /// <summary>
    /// Screen per domain state. Screens may lag behind the domain (to finish an animation) but never run ahead of it.
    /// </summary>
    public sealed class ScreenStateMachine : IDisposable
    {
        private readonly StateMachine<UiScreen> _machine = new StateMachine<UiScreen>();
        private readonly Dictionary<Type, Func<object, UiScreen>> _screens = new Dictionary<Type, Func<object, UiScreen>>();
        private readonly UiLayers _layers;

        private object _target;
        private bool _leaving;

        public ScreenStateMachine(UiLayers layers) =>
            _layers = layers ?? throw new ArgumentNullException(nameof(layers));

        public UiScreen Current => _machine.Current;

        public void Map<TState>(Func<TState, UiScreen> create) where TState : class =>
            _screens.Add(typeof(TState), state => create((TState)state));

        public void FollowDomain(object domainState)
        {
            _target = domainState;
            if (Current == null)
            {
                Switch();
                return;
            }

            if (_leaving)
                return;

            _leaving = true;
            Current.Leave(() =>
            {
                _leaving = false;
                Switch();
            });
        }

        public void Tick(float deltaTime) => _machine.Tick(deltaTime);

        public void LateTick(float deltaTime) => Current?.LateTick(deltaTime);

        public void Dispose()
        {
            _layers.CloseAll();
            _machine.Stop();
            _layers.SetScreen(null);
        }

        private void Switch()
        {
            if (!_screens.TryGetValue(_target.GetType(), out var create))
                throw new InvalidOperationException($"No screen mapped for state {_target.GetType().Name}");

            _layers.CloseAll();

            var next = create(_target);
            next.Attach(_layers);
            _layers.SetScreen(next);
            _machine.Change(next);
        }
    }
}
