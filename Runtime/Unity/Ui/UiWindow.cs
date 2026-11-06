using UnityEngine;

namespace Mosaic.Ui
{
    public abstract class UiWindow
    {
        private GameObject _view;

        protected UiLayers Layers { get; private set; }

        public bool IsOpen { get; internal set; }
        public bool HasFocus => Layers != null && Layers.Focused == this;

        public virtual bool IsModal => false;

        public virtual bool HoldsPause => false;

        public virtual bool CapturesKeyboard => false;

        public virtual bool CapturesPointer => IsModal;

        public bool TakesFocusOnOpen { get; set; } = true;

        internal void Attach(UiLayers layers) => Layers = layers;

        internal void DestroyView()
        {
            if (_view != null)
                Object.Destroy(_view);
            _view = null;
        }

        internal void BringToFront()
        {
            if (_view != null)
                _view.transform.SetAsLastSibling();
        }

        public void Close() => Layers?.Close(this);

        protected T Show<T>(T prefab) where T : Component
        {
            var view = Object.Instantiate(prefab, Layers.Root.WindowLayer);
            _view = view.gameObject;
            _view.AddComponent<UiPointerDown>().Pressed += () => Layers.Focus(this);
            return view;
        }

        protected internal virtual void Opened() { }

        protected internal virtual void Closed() { }

        protected internal virtual void Tick(float deltaTime) { }

        protected internal virtual bool HandleEscape()
        {
            Close();
            return true;
        }
    }
}
