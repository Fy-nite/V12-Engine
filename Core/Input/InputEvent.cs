using System;
using System.Numerics;
using System.Collections.Generic;

namespace V12.Core.Input
{
    public class InputEvent
    {
        public InputEventType Type { get; set; }
        public string Name { get; set; } = string.Empty; // logical name e.g. "Jump", "MoveX"
        public float Value { get; set; } = 0f; // axis or analog magnitude
        public System.Numerics.Vector2 Pointer { get; set; }
        public bool IsPressed { get; set; }
        public double Timestamp { get; set; } = DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds;
        public Dictionary<string, object>? Meta { get; set; }
    }
}

