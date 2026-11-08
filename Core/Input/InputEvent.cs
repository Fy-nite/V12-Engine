using System;
using System.Numerics;
using System.Collections.Generic;

namespace V12.Core.Input
{
    public class InputEvent
    {
        public InputEventType Type { get; set; }
        public string Name { get; set; } = string.Empty; // logical name e.g. "Jump", "MoveX"
        /// <summary>Physical key for raw key edges (null for named actions/axes).</summary>
        public InputKey? Key { get; set; }
        /// <summary>Physical gamepad button for raw pad edges.</summary>
        public GamepadButton? PadButton { get; set; }
        /// <summary>Physical gamepad axis carrying <see cref="Value"/>.</summary>
        public GamepadAxis? PadAxis { get; set; }
        /// <summary>Gamepad device index that produced the event (0 when unset).</summary>
        public int Device { get; set; }
        public float Value { get; set; } = 0f; // axis or analog magnitude
        public System.Numerics.Vector2 Pointer { get; set; }
        public PointerButton Button { get; set; }
        public char Character { get; set; }
        public bool IsPressed { get; set; }
        /// <summary>Set by a UI/input consumer to stop propagation to later handlers.</summary>
        public bool Handled { get; set; }
        public double Timestamp { get; set; } = DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds;
        public Dictionary<string, object>? Meta { get; set; }
    }
}

