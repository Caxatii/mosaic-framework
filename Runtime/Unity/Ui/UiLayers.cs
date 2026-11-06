using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mosaic.Ui
{
    public sealed class UiLayers : IDisposable
    {
        private static readonly Predicate<UiWindow> IsModal = w => w.IsModal;

        private readonly List<UiWindow> _windows = new List<UiWindow>();
        private readonly List<UiPopup> _popups = new List<UiPopup>();
        private readonly List<UiOverlay> _overlays = new List<UiOverlay>();
        private readonly List<(Func<bool> Pressed, UiWindow Window)> _hotkeys = new List<(Func<bool>, UiWindow)>();
        private readonly List<UiWindow> _keptViews = new List<UiWindow>();

        private UiScreen _screen;
        private bool _pauseRequested;

        public UiLayers(UiRootView prefab) => Root = UnityEngine.Object.Instantiate(prefab);

        public UiRootView Root { get; }

        public UiWindow Focused { get; private set; }

        public bool KeyboardCaptured => _popups.Count > 0 || Focused is { CapturesKeyboard: true };

        public bool PointerCaptured
        {
            get
            {
                if (_popups.Count > 0)
                    return true;
                foreach (var window in _windows)
                    if (window.CapturesPointer)
                        return true;
                return false;
            }
        }

        public bool IsBlocked => _popups.Count > 0 || _windows.FindLastIndex(IsModal) >= 0;

        public void Bind(Key key, UiWindow window) =>
            _hotkeys.Add((() => Keyboard.current is { } keyboard && keyboard[key].wasPressedThisFrame, window));

        /// <summary>
        /// A hotkey from an Input System action, so it works with a gamepad and follows rebinding. The action is
        /// enabled here.
        /// </summary>
        public void Bind(InputAction action, UiWindow window)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            action.Enable();
            _hotkeys.Add((action.WasPressedThisFrame, window));
        }

        /// <summary>
        /// An extra action that works as Escape (a gamepad's B or Start button). The Escape key always works.
        /// </summary>
        public InputAction EscapeAction
        {
            get => _escapeAction;
            set
            {
                _escapeAction = value;
                _escapeAction?.Enable();
            }
        }

        private InputAction _escapeAction;

        public bool Open(UiWindow window)
        {
            if (_screen == null || !_screen.Allows(window))
                return false;

            if (window.IsOpen)
            {
                Focus(window);
                return true;
            }

            window.Attach(this);
            window.IsOpen = true;
            _windows.Add(window);
            window.Opened();
            if (window.TakesFocusOnOpen)
                Focus(window);
            UpdatePause();
            return true;
        }

        public void Toggle(UiWindow window)
        {
            if (window.IsOpen)
                window.Close();
            else
                Open(window);
        }

        public void Close(UiWindow window)
        {
            if (!_windows.Remove(window))
                return;

            for (var i = _popups.Count - 1; i >= 0; i--)
                if (_popups[i].Owner == window)
                    Close(_popups[i]);

            window.IsOpen = false;
            if (Focused == window)
                Focused = _windows.Count > 0 ? _windows[_windows.Count - 1] : null;
            window.Closed();
            window.ReleaseView();
            if (window.HasView && !_keptViews.Contains(window))
                _keptViews.Add(window);
            UpdatePause();
        }

        public void Focus(UiWindow window)
        {
            if (!window.IsOpen)
                return;

            Focused = window;
            _windows.Remove(window);
            _windows.Add(window);
            window.BringToFront();
        }

        public void Show(UiPopup popup, object owner = null)
        {
            popup.Attach(this, owner);
            _popups.Add(popup);
            popup.Opened();
            UpdatePause();
        }

        public void Close(UiPopup popup)
        {
            if (!_popups.Remove(popup))
                return;

            popup.DestroyView();
            UpdatePause();
        }

        public void Add(UiOverlay overlay)
        {
            overlay.Attach(this);
            _overlays.Add(overlay);
            overlay.Opened();
        }

        public void Remove(UiOverlay overlay)
        {
            if (_overlays.Remove(overlay))
                overlay.DestroyView();
        }

        public void Tick(float deltaTime)
        {
            var escape = Keyboard.current is { } keyboard && keyboard.escapeKey.wasPressedThisFrame ||
                         _escapeAction != null && _escapeAction.WasPressedThisFrame();
            if (escape)
                RouteEscape();
            else
                RouteHotkeys();

            for (var i = _windows.Count - 1; i >= 0; i--)
                if (i < _windows.Count)
                    _windows[i].Tick(deltaTime);
            for (var i = _popups.Count - 1; i >= 0; i--)
                if (i < _popups.Count)
                    _popups[i].Tick(deltaTime);
            for (var i = _overlays.Count - 1; i >= 0; i--)
                if (i < _overlays.Count)
                    _overlays[i].Tick(deltaTime);
        }

        public void Dispose()
        {
            CloseAll();
            DestroyKeptViews();
            if (Root != null)
                UnityEngine.Object.Destroy(Root.gameObject);
        }

        internal void SetScreen(UiScreen screen)
        {
            if (screen != _screen)
                DestroyKeptViews();

            _screen = screen;
            _pauseRequested = false;
        }

        private void DestroyKeptViews()
        {
            foreach (var window in _keptViews)
                if (!window.IsOpen)
                    window.DestroyView();
            _keptViews.Clear();
        }

        internal void CloseAll()
        {
            for (var i = _popups.Count - 1; i >= 0; i--)
                Close(_popups[i]);
            for (var i = _windows.Count - 1; i >= 0; i--)
                Close(_windows[i]);
        }

        private void RouteEscape()
        {
            if (_popups.Count > 0)
                _popups[_popups.Count - 1].HandleEscape();
            else if (Focused == null || !Focused.HandleEscape())
                _screen?.HandleEscape();
        }

        private void RouteHotkeys()
        {
            if (_popups.Count > 0)
                return;

            foreach (var (pressed, window) in _hotkeys)
            {
                if (!pressed())
                    continue;

                if (Focused is { CapturesKeyboard: true } && Focused != window)
                    continue;

                if (IsBlocked && !window.IsOpen)
                    continue;

                Toggle(window);
                return;
            }
        }

        private void UpdatePause()
        {
            var requested = _windows.Exists(w => w.HoldsPause) || _popups.Exists(p => p.HoldsPause);
            if (requested == _pauseRequested || _screen == null)
                return;

            _pauseRequested = requested;
            _screen.PauseRequested(requested);
        }
    }
}
