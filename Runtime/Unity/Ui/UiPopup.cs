using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Mosaic.Ui
{
    public abstract class UiPopup
    {
        private GameObject _view;

        protected UiLayers Layers { get; private set; }

        public object Owner { get; private set; }

        public virtual bool HoldsPause => false;

        internal void Attach(UiLayers layers, object owner)
        {
            Layers = layers;
            Owner = owner;
        }

        internal void DestroyView()
        {
            if (_view != null)
                UnityEngine.Object.Destroy(_view);
            _view = null;
        }

        public void Close() => Layers?.Close(this);

        protected T Show<T>(T prefab) where T : Component
        {
            var view = UnityEngine.Object.Instantiate(prefab, Layers.Root.PopupLayer);
            _view = view.gameObject;
            return view;
        }

        protected internal virtual void Opened() { }

        protected internal virtual void Tick(float deltaTime) { }

        protected internal virtual bool HandleEscape() => false;
    }

    public sealed class ConfirmPopup : UiPopup
    {
        private readonly ConfirmPopupView _prefab;
        private readonly string _title;
        private readonly string _yes;
        private readonly string _no;
        private readonly Action<bool> _answered;
        private ConfirmPopupView _view;
        private float? _timeLeft;
        private readonly Func<string, int, string> _countdown;

        public ConfirmPopup(ConfirmPopupView prefab, string title, Action<bool> answered, string yes = "Yes", string no = "No", float? timeout = null,
            Func<string, int, string> countdown = null)
        {
            _countdown = countdown ?? DefaultCountdown;
            _prefab = prefab;
            _title = title;
            _answered = answered;
            _yes = yes;
            _no = no;
            _timeLeft = timeout;
        }

        protected internal override void Opened()
        {
            _view = Show(_prefab);
            _view.YesLabel.text = _yes;
            _view.NoLabel.text = _no;
            _view.Yes.onClick.AddListener(() => Answer(true));
            _view.No.onClick.AddListener(() => Answer(false));
            ShowTitle();
        }

        protected internal override void Tick(float deltaTime)
        {
            if (_timeLeft is { } left)
            {
                _timeLeft = left - deltaTime;
                ShowTitle();
                if (_timeLeft <= 0)
                    Answer(false);
            }
        }

        protected internal override bool HandleEscape()
        {
            Answer(false);
            return true;
        }

        private void ShowTitle() =>
            _view.Title.text = _timeLeft is { } left ? _countdown(_title, Mathf.CeilToInt(left)) : _title;

        private static string DefaultCountdown(string title, int seconds) => $"{title}\n(reverting in {seconds} s)";

        private void Answer(bool yes)
        {
            Close();
            _answered?.Invoke(yes);
        }
    }
}
