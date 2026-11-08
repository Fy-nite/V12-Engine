using System;
using System.Collections.Generic;
using System.Numerics;

namespace V12.Core.Input
{
    /// <summary>
    /// Maps raw input events from an <see cref="InputService"/> into gameplay
    /// actions: 1D axes, buttons and 2D vectors, with per-frame pressed/released
    /// edges.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An action can be driven by any mix of sources: a logical event name, a
    /// physical <see cref="InputKey"/>, a <see cref="GamepadButton"/> or a
    /// <see cref="GamepadAxis"/>. <c>Bind*</c> replaces an action's sources;
    /// <c>Add*</c> appends one, so a single action can answer to both a pad
    /// button and a key:
    /// </para>
    /// <code>
    /// var actions = new InputActionMap();
    /// actions.BindButton("Jump", GamepadButton.A);
    /// actions.AddButton("Jump", InputKey.Space);
    ///
    /// actions.BindAxis("Move", GamepadAxis.LeftX, GamepadAxis.LeftX);
    /// actions.AddAxis("MoveX", InputKey.D, InputKey.A);
    /// </code>
    /// <para>
    /// Key and pad-button axes read 1 while held; pad axes read their analog
    /// value; name axes are a peak-hold over the frame window. Button edges are
    /// derived from the combined held state of every source.
    /// </para>
    /// <para>
    /// <see cref="OnInputEvent"/> may be called from another thread; the
    /// <c>Get*</c> readers should be queried from the thread that calls
    /// <see cref="Update"/>.
    /// </para>
    /// </remarks>
    public class InputActionMap : IInputHandler
    {
        /// <summary>Descriptive name for this map (for logging/debugging).</summary>
        public string Name { get; set; } = "ActionMap";

        private readonly object _lock = new();

        /// <summary>One binding source: a logical name, a key, a pad button or a
        /// pad axis. Exactly one field is populated.</summary>
        private readonly struct InputSource
        {
            public readonly string? Name;
            public readonly InputKey? Key;
            public readonly GamepadButton? PadButton;
            public readonly GamepadAxis? PadAxis;

            public bool IsKey => Key.HasValue;
            public bool IsPadButton => PadButton.HasValue;
            public bool IsPadAxis => PadAxis.HasValue;

            public InputSource(string name)
            {
                Name = name ?? string.Empty;
                Key = null;
                PadButton = null;
                PadAxis = null;
            }

            public InputSource(InputKey key)
            {
                Name = null;
                Key = key;
                PadButton = null;
                PadAxis = null;
            }

            public InputSource(GamepadButton button)
            {
                Name = null;
                Key = null;
                PadButton = button;
                PadAxis = null;
            }

            public InputSource(GamepadAxis axis)
            {
                Name = null;
                Key = null;
                PadButton = null;
                PadAxis = axis;
            }

            public bool IsName => Name is not null;
        }

        private sealed class AxisBinding
        {
            public readonly List<InputSource> Positive = new();
            public readonly List<InputSource> Negative = new();
        }

        private sealed class ButtonBinding
        {
            public readonly List<InputSource> Sources = new();
        }

        private readonly Dictionary<string, AxisBinding> _axisBindings = new();
        private readonly Dictionary<string, ButtonBinding> _buttonBindings = new();

        // Raw state published by the host.
        private readonly HashSet<InputKey> _keyHeld = new();
        private readonly HashSet<GamepadButton> _padHeld = new();
        private readonly Dictionary<GamepadAxis, float> _padAxisValues = new();
        private readonly HashSet<string> _nameHeld = new();
        private readonly Dictionary<string, float> _nameAxisValues = new();

        // Resolved per-frame action state.
        private readonly Dictionary<string, float> _axisValues = new();
        private readonly Dictionary<string, bool> _buttonHeld = new();
        private readonly HashSet<string> _justPressed = new();
        private readonly HashSet<string> _justReleased = new();

        // ── Binding API ─────────────────────────────────────────────────────

        /// <summary>Replaces an axis action with a name-based pair. Pass
        /// <c>""</c> for a one-directional axis.</summary>
        public void BindAxis(string actionName, string positiveInput, string negativeInput)
        {
            lock (_lock)
                _axisBindings[actionName] = NameAxis(positiveInput, negativeInput);
        }

        /// <summary>Replaces an axis action with a single physical key.</summary>
        public void BindAxis(string actionName, InputKey positiveKey)
        {
            lock (_lock)
                _axisBindings[actionName] = KeyAxis(positiveKey, null);
        }

        /// <summary>Replaces an axis action with a physical key pair.</summary>
        public void BindAxis(string actionName, InputKey positiveKey, InputKey negativeKey)
        {
            lock (_lock)
                _axisBindings[actionName] = KeyAxis(positiveKey, negativeKey);
        }

        /// <summary>Replaces an axis action with a single gamepad axis.</summary>
        public void BindAxis(string actionName, GamepadAxis positiveAxis)
        {
            lock (_lock)
                _axisBindings[actionName] = PadAxis(positiveAxis, null);
        }

        /// <summary>Replaces an axis action with a gamepad axis pair.</summary>
        public void BindAxis(string actionName, GamepadAxis positiveAxis, GamepadAxis negativeAxis)
        {
            lock (_lock)
                _axisBindings[actionName] = PadAxis(positiveAxis, negativeAxis);
        }

        /// <summary>Appends a key pair to an axis action, keeping existing sources.</summary>
        public void AddAxis(string actionName, InputKey positiveKey, InputKey? negativeKey = null)
        {
            lock (_lock)
            {
                var binding = GetOrAddAxis(actionName);
                binding.Positive.Add(new InputSource(positiveKey));
                if (negativeKey.HasValue)
                    binding.Negative.Add(new InputSource(negativeKey.Value));
            }
        }

        /// <summary>Appends a gamepad axis pair to an axis action, keeping existing sources.</summary>
        public void AddAxis(string actionName, GamepadAxis positiveAxis, GamepadAxis? negativeAxis = null)
        {
            lock (_lock)
            {
                var binding = GetOrAddAxis(actionName);
                binding.Positive.Add(new InputSource(positiveAxis));
                if (negativeAxis.HasValue)
                    binding.Negative.Add(new InputSource(negativeAxis.Value));
            }
        }

        /// <summary>Convenience: binds a 2D action as two name-based axes.</summary>
        public void BindVector2(string actionName,
            string positiveX, string negativeX,
            string positiveY, string negativeY)
        {
            BindAxis(actionName + "_X", positiveX, negativeX);
            BindAxis(actionName + "_Y", positiveY, negativeY);
        }

        /// <summary>Convenience: binds a 2D action to four physical keys.</summary>
        public void BindVector2(string actionName,
            InputKey positiveX, InputKey negativeX,
            InputKey positiveY, InputKey negativeY)
        {
            BindAxis(actionName + "_X", positiveX, negativeX);
            BindAxis(actionName + "_Y", positiveY, negativeY);
        }

        /// <summary>Convenience: binds a 2D action to two gamepad axes.</summary>
        public void BindVector2(string actionName, GamepadAxis xAxis, GamepadAxis yAxis)
        {
            BindAxis(actionName + "_X", xAxis);
            BindAxis(actionName + "_Y", yAxis);
        }

        /// <summary>Replaces a button action with a name-based source.</summary>
        public void BindButton(string actionName, string inputName)
        {
            lock (_lock)
                _buttonBindings[actionName] = SingleButton(new InputSource(inputName));
        }

        /// <summary>Replaces a button action with a physical key.</summary>
        public void BindButton(string actionName, InputKey key)
        {
            lock (_lock)
                _buttonBindings[actionName] = SingleButton(new InputSource(key));
        }

        /// <summary>Replaces a button action with a gamepad button.</summary>
        public void BindButton(string actionName, GamepadButton button)
        {
            lock (_lock)
                _buttonBindings[actionName] = SingleButton(new InputSource(button));
        }

        /// <summary>Appends a key to a button action, keeping existing sources.</summary>
        public void AddButton(string actionName, InputKey key)
        {
            lock (_lock)
                GetOrAddButton(actionName).Sources.Add(new InputSource(key));
        }

        /// <summary>Appends a gamepad button to a button action, keeping existing sources.</summary>
        public void AddButton(string actionName, GamepadButton button)
        {
            lock (_lock)
                GetOrAddButton(actionName).Sources.Add(new InputSource(button));
        }

        /// <summary>Appends a logical event name to a button action.</summary>
        public void AddButton(string actionName, string inputName)
        {
            lock (_lock)
                GetOrAddButton(actionName).Sources.Add(new InputSource(inputName));
        }

        // ── Rebinding / cleanup ─────────────────────────────────────────────

        /// <summary>Rebinds a button action to a single key.</summary>
        public void RebindButton(string actionName, InputKey key) => BindButton(actionName, key);

        /// <summary>Rebinds an axis action to physical keys.</summary>
        public void RebindAxis(string actionName, InputKey positiveKey, InputKey? negativeKey = null)
        {
            if (negativeKey.HasValue) BindAxis(actionName, positiveKey, negativeKey.Value);
            else BindAxis(actionName, positiveKey);
        }

        /// <summary>Removes a button binding.</summary>
        public void UnbindButton(string actionName)
        {
            lock (_lock) _buttonBindings.Remove(actionName);
        }

        /// <summary>Removes an axis binding and its cached value.</summary>
        public void UnbindAxis(string actionName)
        {
            lock (_lock)
            {
                _axisBindings.Remove(actionName);
                _axisValues.Remove(actionName);
            }
        }

        /// <summary>Removes every binding and resets cached action state.</summary>
        public void ClearBindings()
        {
            lock (_lock)
            {
                _axisBindings.Clear();
                _buttonBindings.Clear();
                _axisValues.Clear();
                _buttonHeld.Clear();
                _justPressed.Clear();
                _justReleased.Clear();
                _keyHeld.Clear();
                _padHeld.Clear();
                _padAxisValues.Clear();
                _nameHeld.Clear();
                _nameAxisValues.Clear();
            }
        }

        /// <summary>Returns the first physical key bound to a button action, or null.</summary>
        public InputKey? GetButtonKey(string actionName)
        {
            lock (_lock)
            {
                if (!_buttonBindings.TryGetValue(actionName, out var binding))
                    return null;
                foreach (var source in binding.Sources)
                    if (source.IsKey) return source.Key;
                return null;
            }
        }

        /// <summary>Returns the first physical keys bound to an axis action.</summary>
        public (InputKey? Positive, InputKey? Negative) GetAxisKeys(string actionName)
        {
            lock (_lock)
            {
                if (!_axisBindings.TryGetValue(actionName, out var binding))
                    return (null, null);

                InputKey? FirstKey(List<InputSource> sources)
                {
                    foreach (var source in sources)
                        if (source.IsKey) return source.Key;
                    return null;
                }

                return (FirstKey(binding.Positive), FirstKey(binding.Negative));
            }
        }

        // ── Read API ────────────────────────────────────────────────────────

        /// <summary>Current value of an axis action, clamped to [-1, 1].</summary>
        public float GetAxis(string actionName) =>
            _axisValues.TryGetValue(actionName, out var v) ? v : 0f;

        /// <summary>The (X, Y) pair of an action bound with <see cref="BindVector2"/>.</summary>
        public Vector2 GetVector2(string actionName) =>
            new(GetAxis(actionName + "_X"), GetAxis(actionName + "_Y"));

        /// <summary>True while any source of a button action is held.</summary>
        public bool GetButton(string actionName) =>
            _buttonHeld.TryGetValue(actionName, out var v) && v;

        /// <summary>True only on the frame the button was pressed.</summary>
        public bool GetButtonDown(string actionName) => _justPressed.Contains(actionName);

        /// <summary>True only on the frame the button was released.</summary>
        public bool GetButtonUp(string actionName) => _justReleased.Contains(actionName);

        // ── IInputHandler ───────────────────────────────────────────────────

        /// <summary>Receives raw input events and stages them for the next
        /// <see cref="Update"/>. Safe to call from any thread.</summary>
        public void OnInputEvent(InputEvent evt)
        {
            if (evt is null) return;

            lock (_lock)
            {
                switch (evt.Type)
                {
                    case InputEventType.Axis:
                        if (evt.PadAxis.HasValue)
                        {
                            _padAxisValues[evt.PadAxis.Value] = evt.Value;
                        }
                        else if (!string.IsNullOrEmpty(evt.Name))
                        {
                            // Name axes are a peak-hold over the frame window.
                            _nameAxisValues[evt.Name] = Math.Max(
                                _nameAxisValues.GetValueOrDefault(evt.Name), evt.Value);
                        }
                        break;

                    case InputEventType.ButtonDown:
                    {
                        var key = ResolveEventKey(evt);
                        if (key.HasValue) _keyHeld.Add(key.Value);
                        if (evt.PadButton.HasValue) _padHeld.Add(evt.PadButton.Value);
                        if (!string.IsNullOrEmpty(evt.Name)) _nameHeld.Add(evt.Name);
                        break;
                    }

                    case InputEventType.ButtonUp:
                    {
                        var key = ResolveEventKey(evt);
                        if (key.HasValue) _keyHeld.Remove(key.Value);
                        if (evt.PadButton.HasValue) _padHeld.Remove(evt.PadButton.Value);
                        if (!string.IsNullOrEmpty(evt.Name)) _nameHeld.Remove(evt.Name);
                        break;
                    }
                }
            }
        }

        /// <summary>Resolves the physical key an event represents, falling back
        /// to parsing the event name so name-only hosts still work.</summary>
        private static InputKey? ResolveEventKey(InputEvent evt)
        {
            if (evt.Key.HasValue) return evt.Key.Value;
            if (string.IsNullOrEmpty(evt.Name)) return null;
            return Enum.TryParse<InputKey>(evt.Name, ignoreCase: false, out var parsed)
                   && parsed != InputKey.None
                   && Enum.IsDefined(typeof(InputKey), parsed)
                ? parsed
                : null;
        }

        // ── Frame lifecycle ─────────────────────────────────────────────────

        /// <summary>Recomputes every action from the raw state, resolves edges,
        /// then clears the per-frame name accumulators.</summary>
        public void Update(float deltaTime)
        {
            lock (_lock)
            {
                _justPressed.Clear();
                _justReleased.Clear();

                foreach (var (action, binding) in _axisBindings)
                {
                    // Sum, not max: a one-directional pad axis must be able to
                    // read negative (a stick pushed up), and key pairs cancel.
                    float pos = SumSourceValues(binding.Positive);
                    float neg = SumSourceValues(binding.Negative);
                    _axisValues[action] = Math.Clamp(pos - neg, -1f, 1f);
                }

                foreach (var (action, binding) in _buttonBindings)
                {
                    bool current = false;
                    foreach (var source in binding.Sources)
                    {
                        if (SourceHeld(source))
                        {
                            current = true;
                            break;
                        }
                    }

                    bool previous = _buttonHeld.GetValueOrDefault(action);
                    _buttonHeld[action] = current;

                    if (current && !previous) _justPressed.Add(action);
                    if (!current && previous) _justReleased.Add(action);
                }

                // Name axes are published every frame by the host; the window
                // ends here.
                _nameAxisValues.Clear();
            }
        }

        private float SumSourceValues(List<InputSource> sources)
        {
            float total = 0f;
            foreach (var source in sources)
                total += SourceValue(source);
            return total;
        }

        private float SourceValue(in InputSource source)
        {
            if (source.IsKey)
                return _keyHeld.Contains(source.Key.GetValueOrDefault()) ? 1f : 0f;
            if (source.IsPadButton)
                return _padHeld.Contains(source.PadButton.GetValueOrDefault()) ? 1f : 0f;
            if (source.IsPadAxis)
                return _padAxisValues.GetValueOrDefault(source.PadAxis.GetValueOrDefault());
            return _nameAxisValues.GetValueOrDefault(source.Name ?? string.Empty);
        }

        private bool SourceHeld(in InputSource source)
        {
            if (source.IsKey)
                return _keyHeld.Contains(source.Key.GetValueOrDefault());
            if (source.IsPadButton)
                return _padHeld.Contains(source.PadButton.GetValueOrDefault());
            if (source.IsPadAxis)
                return _padAxisValues.GetValueOrDefault(source.PadAxis.GetValueOrDefault()) > 0.5f;
            return _nameHeld.Contains(source.Name ?? string.Empty);
        }

        private AxisBinding GetOrAddAxis(string actionName)
        {
            if (!_axisBindings.TryGetValue(actionName, out var binding))
                _axisBindings[actionName] = binding = new AxisBinding();
            return binding;
        }

        private ButtonBinding GetOrAddButton(string actionName)
        {
            if (!_buttonBindings.TryGetValue(actionName, out var binding))
                _buttonBindings[actionName] = binding = new ButtonBinding();
            return binding;
        }

        private static AxisBinding NameAxis(string positive, string negative)
        {
            var binding = new AxisBinding();
            if (!string.IsNullOrEmpty(positive)) binding.Positive.Add(new InputSource(positive));
            if (!string.IsNullOrEmpty(negative)) binding.Negative.Add(new InputSource(negative));
            return binding;
        }

        private static AxisBinding KeyAxis(InputKey positive, InputKey? negative)
        {
            var binding = new AxisBinding { Positive = { new InputSource(positive) } };
            if (negative.HasValue) binding.Negative.Add(new InputSource(negative.Value));
            return binding;
        }

        private static AxisBinding PadAxis(GamepadAxis positive, GamepadAxis? negative)
        {
            var binding = new AxisBinding { Positive = { new InputSource(positive) } };
            if (negative.HasValue) binding.Negative.Add(new InputSource(negative.Value));
            return binding;
        }

        private static ButtonBinding SingleButton(InputSource source)
        {
            var binding = new ButtonBinding();
            binding.Sources.Add(source);
            return binding;
        }

        // ── Registration helpers ────────────────────────────────────────────

        /// <summary>Registers this map with an <see cref="InputService"/>.</summary>
        public void Attach(InputService service) => service.RegisterHandler(this);

        /// <summary>Unregisters this map from an <see cref="InputService"/>.</summary>
        public void Detach(InputService service) => service.UnregisterHandler(this);
    }
}
