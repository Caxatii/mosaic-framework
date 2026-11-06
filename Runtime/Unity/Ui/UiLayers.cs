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
        private readonly List<(Key Key, UiWindow Window)> _hotkeys = new List<(Key, UiWindow)>();

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

        public void Bind(Key key, UiWindow window) => _hotkeys.Add((key, window));

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
            window.DestroyView();
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
            if (Keyboard.current is { } keyboard)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                    RouteEscape();
                else
                    RouteHotkeys(keyboard);
            }

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
            if (Root != null)
                UnityEngine.Object.Destroy(Root.gameObject);
        }

        internal void SetScreen(UiScreen screen)
        {
            _screen = screen;
            _pauseRequested = false;
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

        private void RouteHotkeys(Keyboard keyboard)
        {
            if (_popups.Count > 0)
                return;

            foreach (var (key, window) in _hotkeys)
            {
                if (!keyboard[key].wasPressedThisFrame)
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
