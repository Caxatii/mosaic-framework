using System;
using Mosaic.Core;
using UnityEngine;

namespace Mosaic.Ui
{
    public abstract class UiScreen : IState
    {
        private GameObject _view;

        protected UiLayers Layers { get; private set; }

        internal void Attach(UiLayers layers) => Layers = layers;

        void IState.Enter() => Enter();

        void IState.Exit()
        {
            Exit();
            if (_view != null)
                UnityEngine.Object.Destroy(_view);
        }

        void IState.Tick(float deltaTime) => Tick(deltaTime);

        protected T Show<T>(T prefab) where T : Component
        {
            var view = UnityEngine.Object.Instantiate(prefab, Layers.Root.ScreenLayer);
            _view = view.gameObject;
            return view;
        }

        protected internal virtual void Enter() { }

        protected internal virtual void Leave(Action done) => done();

        protected internal virtual void Exit() { }

        protected internal virtual void Tick(float deltaTime) { }

        protected internal virtual void LateTick(float deltaTime) { }

        protected internal virtual bool Allows(UiWindow window) => true;

        protected internal virtual bool HandleEscape() => false;

        protected internal virtual void PauseRequested(bool requested) { }
    }
}
