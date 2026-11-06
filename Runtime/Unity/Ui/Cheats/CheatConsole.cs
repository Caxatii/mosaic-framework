using System.Collections.Generic;
using System.Linq;
using Mosaic.Core;
using UnityEngine.InputSystem;

namespace Mosaic.Ui
{
    /// <summary>
    /// The cheat console: type a cheat and press Enter. Tab completes cheat names and the choices of word arguments,
    /// Up and Down walk through earlier lines, <c>help</c> lists the cheats. A non-modal window that captures the
    /// keyboard; bind it to a key with <c>UiLayers.Bind</c>.
    /// </summary>
    public sealed class CheatConsole : UiWindow
    {
        private readonly CheatRegistry _cheats;
        private readonly CheatConsoleView _prefab;
        private readonly int _historySize;
        private readonly List<string> _output = new List<string>();
        private readonly List<string> _typed = new List<string>();
        private int _recall;
        private CheatConsoleView _view;

        public CheatConsole(CheatRegistry cheats, CheatConsoleView prefab, int historySize = 8)
        {
            _cheats = cheats ?? throw new System.ArgumentNullException(nameof(cheats));
            _prefab = prefab ? prefab : throw new System.ArgumentNullException(nameof(prefab));
            _historySize = historySize;
        }

        /// <summary>The character of the key that opens the console, so pressing it doesn't type it.</summary>
        public char IgnoredCharacter { get; set; } = '`';

        public override bool CapturesKeyboard => true;

        protected internal override void Opened()
        {
            _view = Show(_prefab);
            ShowHint();
            ShowOutput();
            _view.Input.text = "";
            _view.Input.onValidateInput = (text, index, added) => added == IgnoredCharacter ? '\0' : added;
            _view.Input.onSubmit.RemoveAllListeners();
            _view.Input.onSubmit.AddListener(Submit);
            _recall = _typed.Count;
        }

        protected internal override void Closed() => _view = null;

        protected internal override void Tick(float deltaTime)
        {
            if (_view == null)
                return;

            if (HasFocus && !_view.Input.isFocused)
                _view.Input.ActivateInputField();
            else if (!HasFocus && _view.Input.isFocused)
                _view.Input.DeactivateInputField();

            if (!HasFocus || Keyboard.current is not { } keyboard)
                return;

            if (keyboard.tabKey.wasPressedThisFrame)
                Complete();
            else if (keyboard.upArrowKey.wasPressedThisFrame)
                Recall(-1);
            else if (keyboard.downArrowKey.wasPressedThisFrame)
                Recall(1);
        }

        private void Submit(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            var result = _cheats.Execute(line);
            Write($"> {line}");
            foreach (var text in result.Message.Split('\n'))
                Write(result.IsOk ? "  " + text : "  ! " + text);

            _typed.Remove(line);
            _typed.Add(line);
            _recall = _typed.Count;
            _view.Input.text = "";
            ShowOutput();
            ShowHint();
        }

        private void Complete()
        {
            var line = _view.Input.text.Replace("\t", "");
            var options = _cheats.Complete(line);
            if (options.Count == 0)
            {
                SetInput(line);
                return;
            }

            var cut = line.Length == 0 || char.IsWhiteSpace(line[line.Length - 1]) ? line.Length : line.LastIndexOf(' ') + 1;
            var common = options.Aggregate(CommonPrefix);
            var completed = line.Substring(0, cut) + common + (options.Count == 1 ? " " : "");
            SetInput(completed);
            _view.Help.text = options.Count == 1 ? Usage(completed) : string.Join("  ", options);
        }

        private void Recall(int step)
        {
            if (_typed.Count == 0)
                return;

            _recall = System.Math.Max(0, System.Math.Min(_typed.Count, _recall + step));
            SetInput(_recall < _typed.Count ? _typed[_recall] : "");
        }

        private void SetInput(string text)
        {
            _view.Input.text = text;
            _view.Input.caretPosition = text.Length;
            _view.Input.stringPosition = text.Length;
        }

        private string Usage(string line)
        {
            var name = line.Trim().Split(' ')[0];
            return _cheats.Find(name) is { } cheat ? $"{cheat.Usage}   {cheat.Description}" : "";
        }

        private void ShowHint() => _view.Help.text = $"{CheatRegistry.HelpCommand} — list of cheats · Tab — complete · ↑↓ — history";

        private void ShowOutput() => _view.History.text = string.Join("\n", _output);

        private void Write(string line)
        {
            _output.Add(line);
            while (_output.Count > _historySize)
                _output.RemoveAt(0);
        }

        private static string CommonPrefix(string a, string b)
        {
            var length = 0;
            while (length < a.Length && length < b.Length && char.ToLowerInvariant(a[length]) == char.ToLowerInvariant(b[length]))
                length++;
            return a.Substring(0, length);
        }
    }
}
