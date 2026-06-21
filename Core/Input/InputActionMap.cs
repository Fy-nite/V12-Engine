using System;
using System.Collections.Generic;
using System.Numerics;

namespace V12.Core.Input
{
    public class InputActionMap : IInputHandler
    {
        public string Name { get; set; } = "ActionMap";

        private struct AxisBinding
        {
            public string PositiveInput;
            public string NegativeInput;
            public AxisBinding(string pos, string neg) { PositiveInput = pos; NegativeInput = neg; }
        }

        private struct ButtonBinding
        {
            public string InputName;
            public ButtonBinding(string name) { InputName = name; }
        }

        private readonly Dictionary<string, AxisBinding> _axisBindings = new();
        private readonly Dictionary<string, ButtonBinding> _buttonBindings = new();

        private readonly Dictionary<string, float> _rawPositive = new();
        private readonly Dictionary<string, float> _rawNegative = new();
        private readonly Dictionary<string, bool> _rawButtons = new();

        private readonly Dictionary<string, float> _axisValues = new();
        private readonly Dictionary<string, bool> _buttonHeld = new();

        private readonly HashSet<string> _justPressed = new();
        private readonly HashSet<string> _justReleased = new();
        private readonly HashSet<string> _prevHeld = new();

        // ── Binding API ──────────────────────────────────────────────────────

        public void BindAxis(string actionName, string positiveInput, string negativeInput)
        {
            _axisBindings[actionName] = new AxisBinding(positiveInput, negativeInput);
        }

        public void BindButton(string actionName, string inputName)
        {
            _buttonBindings[actionName] = new ButtonBinding(inputName);
        }

        public void BindVector2(string actionName,
            string positiveX, string negativeX,
            string positiveY, string negativeY)
        {
            BindAxis(actionName + "_X", positiveX, negativeX);
            BindAxis(actionName + "_Y", positiveY, negativeY);
        }

        // ── Read API ─────────────────────────────────────────────────────────

        public float GetAxis(string actionName)
        {
            return _axisValues.TryGetValue(actionName, out var v) ? v : 0f;
        }

        public Vector2 GetVector2(string actionName)
        {
            return new Vector2(GetAxis(actionName + "_X"), GetAxis(actionName + "_Y"));
        }

        public bool GetButton(string actionName)
        {
            return _buttonHeld.TryGetValue(actionName, out var v) && v;
        }

        public bool GetButtonDown(string actionName)
        {
            return _justPressed.Contains(actionName);
        }

        public bool GetButtonUp(string actionName)
        {
            return _justReleased.Contains(actionName);
        }

        // ── IInputHandler ────────────────────────────────────────────────────

        public void OnInputEvent(InputEvent evt)
        {
            if (evt == null) return;

            if (evt.Type == InputEventType.Axis)
            {
                foreach (var kv in _axisBindings)
                {
                    var binding = kv.Value;
                    if (evt.Name == binding.PositiveInput)
                        _rawPositive[kv.Key] = Math.Max(_rawPositive.GetValueOrDefault(kv.Key), evt.Value);
                    if (evt.Name == binding.NegativeInput)
                        _rawNegative[kv.Key] = Math.Max(_rawNegative.GetValueOrDefault(kv.Key), evt.Value);
                }
            }
            else if (evt.Type == InputEventType.ButtonDown || evt.Type == InputEventType.ButtonUp)
            {
                foreach (var kv in _buttonBindings)
                {
                    if (evt.Name == kv.Value.InputName)
                        _rawButtons[kv.Key] = evt.Type == InputEventType.ButtonDown;
                }
            }
        }

        // ── Frame lifecycle ──────────────────────────────────────────────────

        public void Update(float deltaTime)
        {
            foreach (var action in _axisBindings.Keys)
            {
                float pos = _rawPositive.GetValueOrDefault(action, 0f);
                float neg = _rawNegative.GetValueOrDefault(action, 0f);
                _axisValues[action] = Math.Clamp(pos - neg, -1f, 1f);
            }

            foreach (var action in _buttonBindings.Keys)
            {
                bool current = _rawButtons.GetValueOrDefault(action, false);
                bool prev = _prevHeld.Contains(action);
                _buttonHeld[action] = current;
                if (current && !prev) _justPressed.Add(action);
                if (!current && prev) _justReleased.Add(action);
            }

            _prevHeld.Clear();
            foreach (var action in _buttonBindings.Keys)
            {
                if (_rawButtons.GetValueOrDefault(action, false))
                    _prevHeld.Add(action);
            }

            _rawPositive.Clear();
            _rawNegative.Clear();
            _rawButtons.Clear();
        }

        // ── Registration helpers ─────────────────────────────────────────────

        public void Attach(InputService service)
        {
            service.RegisterHandler(this);
        }

        public void Detach(InputService service)
        {
            service.UnregisterHandler(this);
        }
    }
}
