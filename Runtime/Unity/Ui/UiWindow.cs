using UnityEngine;

namespace Mosaic.Ui
{
    public abstract class UiWindow
    {
        private GameObject _view;
        private Component _viewComponent;
        private Object _viewPrefab;

        protected UiLayers Layers { get; private set; }

        public bool IsOpen { get; internal set; }
        public bool HasFocus => Layers != null && Layers.Focused == this;

        public virtual bool IsModal => false;

        public virtual bool HoldsPause => false;

        public virtual bool CapturesKeyboard => false;

        public virtual bool CapturesPointer => IsModal;

        public bool TakesFocusOnOpen { get; set; } = true;

        /// <summary>
        /// Hide the view on close instead of destroying it, and show the same instance on the next open.
        /// Use for windows opened often (inventory): no instantiate cost and no garbage per open.
        /// The kept view is destroyed when the screen changes.
        /// </summary>
        public virtual bool KeepsViewWhenClosed => false;

        internal bool HasView => _view != null;

        internal void Attach(UiLayers layers) => Layers = layers;

        internal void ReleaseView()
        {
            if (_view == null)
                return;

            if (KeepsViewWhenClosed)
                _view.SetActive(false);
            else
                DestroyView();
        }

        internal void DestroyView()
        {
            if (_view != null)
                Object.Destroy(_view);
            _view = null;
            _viewComponent = null;
            _viewPrefab = null;
        }

        internal void BringToFront()
        {
            if (_view != null)
                _view.transform.SetAsLastSibling();
        }

        public void Close() => Layers?.Close(this);

        protected T Show<T>(T prefab) where T : Component
        {
            if (_view != null && ReferenceEquals(_viewPrefab, prefab) && _viewComponent is T kept)
            {
                _view.SetActive(true);
                _view.transform.SetAsLastSibling();
                return kept;
            }

            DestroyView();
            var view = Object.Instantiate(prefab, Layers.Root.WindowLayer);
            _view = view.gameObject;
            _viewComponent = view;
            _viewPrefab = prefab;
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
