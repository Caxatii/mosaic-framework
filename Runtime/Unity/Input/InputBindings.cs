using System;
using System.Collections.Generic;
using Mosaic.Core;
using UnityEngine.InputSystem;

namespace Mosaic.Infrastructure
{
    /// <summary>
    /// The player's key bindings as settings: the binding overrides of an Input System asset, saved through a
    /// <see cref="SettingsStore{T}"/> with Apply, Revert and defaults like any other settings.
    /// </summary>
    [Serializable]
    public sealed class InputBindings : ISettings<InputBindings>
    {
        public const string Key = "input";

        /// <summary>JSON of InputActionRebindingExtensions.SaveBindingOverridesAsJson; empty means the defaults.</summary>
        public string Overrides = "";

        /// <summary>The current overrides of the actions; empty when every binding is the default.</summary>
        public static InputBindings Of(IInputActionCollection2 actions)
        {
            if (actions == null)
                throw new ArgumentNullException(nameof(actions));

            foreach (var action in actions)
                foreach (var binding in action.bindings)
                    if (!string.IsNullOrEmpty(binding.overridePath))
                        return new InputBindings { Overrides = actions.SaveBindingOverridesAsJson() };

            return new InputBindings();
        }

        /// <summary>Makes the actions use these bindings: overrides not listed here are removed.</summary>
        public void ApplyTo(IInputActionCollection2 actions)
        {
            if (actions == null)
                throw new ArgumentNullException(nameof(actions));

            actions.RemoveAllBindingOverrides();
            if (!string.IsNullOrEmpty(Overrides))
                actions.LoadBindingOverridesFromJson(Overrides);
        }

        public InputBindings Copy() => (InputBindings)MemberwiseClone();

        public void Normalize() => Overrides ??= "";

        public bool Equals(InputBindings other) => other != null && (Overrides ?? "") == (other.Overrides ?? "");

        public override bool Equals(object obj) => obj is InputBindings other && Equals(other);

        public override int GetHashCode() => (Overrides ?? "").GetHashCode();
    }

    public enum RebindOutcome
    {
        Done,
        Cancelled,
        /// <summary>The control is already used by another binding; the old binding was kept.</summary>
        Conflict,
    }

    public readonly struct RebindResult
    {
        public RebindResult(RebindOutcome outcome, InputAction action, int bindingIndex, InputAction conflictsWith = null)
        {
            Outcome = outcome;
            Action = action;
            BindingIndex = bindingIndex;
            ConflictsWith = conflictsWith;
        }

        public RebindOutcome Outcome { get; }
        public InputAction Action { get; }
        public int BindingIndex { get; }
        public InputAction ConflictsWith { get; }
    }

    /// <summary>
    /// Waits for the player to press a new control for one binding. Escape cancels; a control used by another
    /// binding of the same asset is refused (the old binding stays) and reported as a conflict. The action is
    /// disabled while waiting and enabled again afterwards. Dispose cancels.
    /// </summary>
    public sealed class Rebind : IDisposable
    {
        private readonly InputAction _action;
        private readonly int _bindingIndex;
        private readonly Action<RebindResult> _done;
        private readonly bool _wasEnabled;
        private readonly string _previousOverride;
        private InputActionRebindingExtensions.RebindingOperation _operation;

        private Rebind(InputAction action, int bindingIndex, Action<RebindResult> done, string cancelWith)
        {
            _action = action;
            _bindingIndex = bindingIndex;
            _done = done;
            _wasEnabled = action.enabled;
            _previousOverride = action.bindings[bindingIndex].overridePath;

            action.Disable();
            _operation = action.PerformInteractiveRebinding(bindingIndex)
                .WithCancelingThrough(cancelWith)
                .OnMatchWaitForAnother(0.1f)
                .OnCancel(_ => Finish(RebindOutcome.Cancelled))
                .OnComplete(_ => Complete())
                .Start();
        }

        public bool IsWaiting => _operation != null;

        public static Rebind Start(InputAction action, int bindingIndex, Action<RebindResult> done, string cancelWith = "<Keyboard>/escape")
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count || action.bindings[bindingIndex].isComposite)
                throw new ArgumentOutOfRangeException(nameof(bindingIndex), "Pass a binding or a part of a composite, not the composite itself");

            return new Rebind(action, bindingIndex, done, cancelWith);
        }

        /// <summary>Every binding of the asset (or of the map, without an asset) that uses the same control.</summary>
        public static InputAction FindConflict(InputAction action, int bindingIndex)
        {
            var path = action.bindings[bindingIndex].effectivePath;
            if (string.IsNullOrEmpty(path))
                return null;

            var map = action.actionMap;
            IEnumerable<InputAction> actions = map == null ? new[] { action }
                : map.asset != null ? map.asset
                : (IEnumerable<InputAction>)map.actions;
            foreach (var other in actions)
                for (var i = 0; i < other.bindings.Count; i++)
                {
                    if (other == action && i == bindingIndex)
                        continue;

                    var binding = other.bindings[i];
                    if (!binding.isComposite && binding.effectivePath == path)
                        return other;
                }

            return null;
        }

        public void Dispose() => _operation?.Cancel();

        private void Complete()
        {
            var conflict = FindConflict(_action, _bindingIndex);
            if (conflict == null)
            {
                Finish(RebindOutcome.Done);
                return;
            }

            if (string.IsNullOrEmpty(_previousOverride))
                _action.RemoveBindingOverride(_bindingIndex);
            else
                _action.ApplyBindingOverride(_bindingIndex, _previousOverride);
            Finish(RebindOutcome.Conflict, conflict);
        }

        private void Finish(RebindOutcome outcome, InputAction conflict = null)
        {
            var operation = _operation;
            _operation = null;
            operation?.Dispose();

            if (_wasEnabled)
                _action.Enable();
            _done?.Invoke(new RebindResult(outcome, _action, _bindingIndex, conflict));
        }
    }
}
