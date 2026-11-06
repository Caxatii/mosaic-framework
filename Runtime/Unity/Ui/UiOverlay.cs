using UnityEngine;

namespace Mosaic.Ui
{
    public abstract class UiOverlay
    {
        private GameObject _view;

        protected UiLayers Layers { get; private set; }

        internal void Attach(UiLayers layers) => Layers = layers;

        internal void DestroyView()
        {
            if (_view != null)
                Object.Destroy(_view);
            _view = null;
        }

        public void Remove() => Layers?.Remove(this);

        protected T Show<T>(T prefab) where T : Component
        {
            var view = Object.Instantiate(prefab, Layers.Root.OverlayLayer);
            _view = view.gameObject;
            return view;
        }

        protected internal virtual void Opened() { }

        protected internal virtual void Tick(float deltaTime) { }
    }

    public sealed class ToastOverlay : UiOverlay
    {
        private readonly ToastView _prefab;
        private readonly string _text;
        private float _timeLeft;

        public ToastOverlay(ToastView prefab, string text, float duration = 2f)
        {
            _prefab = prefab;
            _text = text;
            _timeLeft = duration;
        }

        protected internal override void Opened() => Show(_prefab).Text.text = _text;

        protected internal override void Tick(float deltaTime)
        {
            if ((_timeLeft -= deltaTime) <= 0)
                Remove();
        }
    }
}
