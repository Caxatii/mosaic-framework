using System;
using System.Collections.Generic;
using Mosaic.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Mosaic.Ui
{
    /// <summary>
    /// A controls page built in code into a container of your settings window: one row per action (one per part of
    /// a composite such as Move up/down/left/right), one column per binding group ("Keyboard&amp;Mouse", "Gamepad").
    /// Clicking a binding waits for a new control (<see cref="Rebind"/>). After a change <see cref="Changed"/> is
    /// raised, so the window can update its InputBindings draft.
    /// </summary>
    public sealed class ControlsList : IDisposable
    {
        private const float RowHeight = 36;
        private const float ColumnWidth = 170;

        private static readonly Color ButtonColor = new Color(0.22f, 0.26f, 0.33f, 1);
        private static readonly Color WaitingColor = new Color(0.45f, 0.38f, 0.16f, 1);

        private readonly RectTransform _container;
        private readonly List<Cell> _cells = new List<Cell>();
        private readonly TMP_Text _status;
        private readonly Func<InputAction, string, string> _label;
        private Rebind _rebind;

        /// <param name="label">Row names; by default the action name and the composite part ("Move Up").</param>
        public ControlsList(RectTransform container, IEnumerable<InputAction> actions, IReadOnlyList<string> groups,
            Func<InputAction, string, string> label = null, Color? textColor = null)
        {
            _container = container ? container : throw new ArgumentNullException(nameof(container));
            if (actions == null)
                throw new ArgumentNullException(nameof(actions));
            if (groups == null || groups.Count == 0)
                throw new ArgumentException("Name at least one binding group", nameof(groups));

            var color = textColor ?? new Color(0.92f, 0.93f, 0.95f, 1);
            label ??= (action, part) => part == null ? action.name : $"{action.name} {Capitalize(part)}";
            _label = label;

            var layout = _container.GetComponent<VerticalLayoutGroup>() ?? _container.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;

            var header = Row("Header");
            Text(header, "", color, flexible: true);
            foreach (var group in groups)
                Text(header, group, color, flexible: false);

            foreach (var action in actions)
            {
                var parts = PartsOf(action);
                for (var p = 0; p < parts.Count; p++)
                {
                    var part = parts[p];
                    var row = Row(action.name + (part ?? ""));
                    Text(row, label(action, part), color, flexible: true);
                    foreach (var group in groups)
                        AddCell(row, action, IndexOf(action, group, part, firstPart: p == 0), color);
                }
            }

            _status = Text(Row("Status"), "", color, flexible: true);
        }

        public event Action Changed;

        public string WaitingText { get; set; } = "Press a key…";
        public string ConflictText { get; set; } = "Already used by {0}";
        public string CancelledText { get; set; } = "";

        /// <summary>A rebind is waiting for a key: the window should not close on this Escape.</summary>
        public bool IsWaiting => _rebind != null && _rebind.IsWaiting;

        /// <summary>Shows the bindings again, after Revert or Defaults changed them.</summary>
        public void Refresh()
        {
            foreach (var cell in _cells)
                cell.Label.text = Display(cell.Action, cell.Index);
        }

        public void Dispose()
        {
            _rebind?.Dispose();
            _rebind = null;
        }

        private void AddCell(RectTransform row, InputAction action, int index, Color color)
        {
            var button = TMP_DefaultControls.CreateButton(new TMP_DefaultControls.Resources());
            button.transform.SetParent(row, false);
            var element = button.AddComponent<LayoutElement>();
            element.preferredWidth = ColumnWidth;
            element.preferredHeight = RowHeight;
            var image = button.GetComponent<Image>();
            image.color = ButtonColor;
            var text = button.GetComponentInChildren<TMP_Text>();
            text.fontSize = 19;
            text.color = color;

            if (index < 0)
            {
                text.text = "—";
                button.GetComponent<Button>().interactable = false;
                return;
            }

            var cell = new Cell(action, index, text);
            _cells.Add(cell);
            text.text = Display(action, index);
            button.GetComponent<Button>().onClick.AddListener(() =>
            {
                if (IsWaiting)
                    return;

                image.color = WaitingColor;
                text.text = WaitingText;
                _status.text = "";
                _rebind = Rebind.Start(action, index, result =>
                {
                    _rebind = null;
                    image.color = ButtonColor;
                    Refresh();
                    _status.text = result.Outcome switch
                    {
                        RebindOutcome.Conflict => string.Format(ConflictText, _label(result.ConflictsWith, null)),
                        RebindOutcome.Cancelled => CancelledText,
                        _ => "",
                    };
                    if (result.Outcome == RebindOutcome.Done)
                        Changed?.Invoke();
                });
            });
        }

        private static List<string> PartsOf(InputAction action)
        {
            var parts = new List<string>();
            foreach (var binding in action.bindings)
                if (binding.isPartOfComposite && !parts.Contains(binding.name))
                    parts.Add(binding.name);

            if (parts.Count == 0)
                parts.Add(null);
            return parts;
        }

        /// <summary>
        /// The binding of the row in a group. A group with a plain binding where others have a composite (a stick
        /// next to WASD) shows it in the first part's row.
        /// </summary>
        private static int IndexOf(InputAction action, string group, string part, bool firstPart)
        {
            var bindings = action.bindings;
            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding.isComposite || !InGroup(binding, group))
                    continue;
                if (part == null ? !binding.isPartOfComposite : binding.isPartOfComposite && binding.name == part)
                    return i;
            }

            if (part != null && firstPart)
                for (var i = 0; i < bindings.Count; i++)
                    if (!bindings[i].isComposite && !bindings[i].isPartOfComposite && InGroup(bindings[i], group))
                        return i;

            return -1;
        }

        private static bool InGroup(InputBinding binding, string group)
        {
            if (string.IsNullOrEmpty(binding.groups))
                return false;
            foreach (var name in binding.groups.Split(InputBinding.Separator))
                if (string.Equals(name, group, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static string Display(InputAction action, int index)
        {
            var text = action.GetBindingDisplayString(index);
            return string.IsNullOrEmpty(text) ? "—" : text;
        }

        private static string Capitalize(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        private RectTransform Row(string name)
        {
            var row = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var rect = (RectTransform)row.transform;
            rect.SetParent(_container, false);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;
            row.GetComponent<LayoutElement>().preferredHeight = RowHeight;
            return rect;
        }

        private static TMP_Text Text(RectTransform parent, string text, Color color, bool flexible)
        {
            var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            label.transform.SetParent(parent, false);
            var element = label.GetComponent<LayoutElement>();
            if (flexible)
                element.flexibleWidth = 1;
            else
                element.preferredWidth = ColumnWidth;
            var tmp = label.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 20;
            tmp.color = color;
            tmp.alignment = flexible ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
            return tmp;
        }

        private sealed class Cell
        {
            public Cell(InputAction action, int index, TMP_Text label)
            {
                Action = action;
                Index = index;
                Label = label;
            }

            public InputAction Action { get; }
            public int Index { get; }
            public TMP_Text Label { get; }
        }
    }
}
