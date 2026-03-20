using System;

namespace V12.Components
{
    /// <summary>
    /// A toggleable checkbox rendered in 3D world space.
    /// Maps to a <c>CheckBox</c> inside a SubViewport + QuadMesh in the Godot scene.
    /// Subscribe to <see cref="OnToggled"/> to respond to state changes.
    /// </summary>
    public class CheckboxControlComponent : ComponentBase
    {
        private string _label   = "Checkbox";
        private bool   _checked = false;
        private float  _width   = 0.4f;
        private float  _height  = 0.10f;
        private float  _offsetX = 0f;
        private float  _offsetY = 0f;
        private float  _offsetZ = 0f;

        public override string Name        => "CheckboxControl";
        public override string Description => "World-space toggleable checkbox";

        /// <summary>Fired on the Godot main thread when the checkbox is toggled.</summary>
        public event Action<bool>? OnToggled;

        public string Label
        {
            get => _label;
            set { if (_label != value) { _label = value ?? string.Empty; MarkDirty(); } }
        }

        public bool Checked
        {
            get => _checked;
            set { if (_checked != value) { _checked = value; MarkDirty(); } }
        }

        public float Width   { get => _width;   set { if (Math.Abs(_width   - value) > 0.001f) { _width   = MathF.Max(0.05f, value); MarkDirty(); } } }
        public float Height  { get => _height;  set { if (Math.Abs(_height  - value) > 0.001f) { _height  = MathF.Max(0.05f, value); MarkDirty(); } } }
        public float OffsetX { get => _offsetX; set { if (Math.Abs(_offsetX - value) > 0.001f) { _offsetX = value; MarkDirty(); } } }
        public float OffsetY { get => _offsetY; set { if (Math.Abs(_offsetY - value) > 0.001f) { _offsetY = value; MarkDirty(); } } }
        public float OffsetZ { get => _offsetZ; set { if (Math.Abs(_offsetZ - value) > 0.001f) { _offsetZ = value; MarkDirty(); } } }

        public CheckboxControlComponent() { }

        public CheckboxControlComponent(string label, bool isChecked = false, float offsetX = 0f, float offsetY = 0f, float offsetZ = 0f)
        {
            _label   = label ?? "Checkbox";
            _checked = isChecked;
            _offsetX = offsetX;
            _offsetY = offsetY;
            _offsetZ = offsetZ;
        }

        /// <summary>Called by the Godot binding when the checkbox state changes.</summary>
        public void InvokeToggled(bool state) => OnToggled?.Invoke(state);
    }
}
