using System;
using System.Collections.Generic;
using System.Numerics;

namespace V12.Core.Input
{
    public class InputActionMap : IInputHandler
    {
        public string Name { get; set; } = "ActionMap";

        private readonly object _lock = new();

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
        private readonly HashSet<string> _pendingPress = new();
        private readonly HashSet<string> _pendingRelease = new();

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
            lock (_lock)
            {
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
                else if (evt.Type == InputEventType.ButtonDown)
                {
                    foreach (var kv in _buttonBindings)
                    {
                        if (evt.Name == kv.Value.InputName)
                        {
                            _rawButtons[kv.Key] = true;
                            _pendingPress.Add(kv.Key);
                            _pendingRelease.Remove(kv.Key);
                        }
                    }
                }
                else if (evt.Type == InputEventType.ButtonUp)
                {
                    foreach (var kv in _buttonBindings)
                    {
                        if (evt.Name == kv.Value.InputName)
                        {
                            _rawButtons[kv.Key] = false;
                            _pendingRelease.Add(kv.Key);
                            _pendingPress.Remove(kv.Key);
                        }
                    }
                }
            }
        }

        // ── Frame lifecycle ──────────────────────────────────────────────────

        public void Update(float deltaTime)
        {
            lock (_lock)
            {
                _justPressed.Clear();
                _justReleased.Clear();

                foreach (var action in _axisBindings.Keys)
                {
                    float pos = _rawPositive.GetValueOrDefault(action, 0f);
                    float neg = _rawNegative.GetValueOrDefault(action, 0f);
                    _axisValues[action] = Math.Clamp(pos - neg, -1f, 1f);
                }

                foreach (var action in _buttonBindings.Keys)
                {
                    bool hasPendingPress = _pendingPress.Contains(action);
                    bool hasPendingRelease = _pendingRelease.Contains(action);
                    bool prev = _prevHeld.Contains(action);
                    bool current = _buttonHeld.TryGetValue(action, out bool held) && held;

                    if (hasPendingPress)
                    {
                        current = true;
                        _justPressed.Add(action);
                    }
                    if (hasPendingRelease)
                    {
                        current = false;
                        _justReleased.Add(action);
                    }

                    _buttonHeld[action] = current;
                    if (current) _prevHeld.Add(action);
                    else _prevHeld.Remove(action);
                }

                // Don't clear raw input here — it's cleared after being consumed.
                // If we clear it here, any input events that arrive between Update() calls
                // (e.g. from the Godot main thread) will be lost, causing jerky/stuttering
                // movement when the V12 worker thread runs at a different rate than the
                // input thread.
                // Instead, we accumulate and let OnInputEvent overwrite with fresh values.
                // The raw values represent "current frame's input state" and are consumed
                // by the axis calculation above. We clear them after computing axis values.
                _rawPositive.Clear();
                _rawNegative.Clear();
                // Don't clear _rawButtons here — button state is latched and cleared
                // after the press/release events are processed.
                _pendingPress.Clear();
                _pendingRelease.Clear();
            }
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
