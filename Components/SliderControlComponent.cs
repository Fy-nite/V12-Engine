using System;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>
    /// A horizontal slider rendered in 3D world space.
    /// Maps to an <c>HSlider</c> inside a SubViewport + QuadMesh in the Godot scene.
    /// Subscribe to <see cref="OnValueChanged"/> to respond to drag events.
    /// </summary>
    public class SliderControlComponent : ComponentBase
    {
        private string _label    = string.Empty;
        private float  _minValue = 0f;
        private float  _maxValue = 1f;
        private float  _value    = 0f;
        private float  _step     = 0f;
        private float  _width    = 0.6f;
        private float  _height   = 0.13f;
        private float  _offsetX  = 0f;
        private float  _offsetY  = 0f;
        private float  _offsetZ  = 0f;

        public override string Name        => "SliderControl";
        public override string Description => "World-space horizontal slider";

        /// <summary>Fired when the slider value changes via interaction.</summary>
        public event Action<float>? OnValueChanged;

        public string Label
        {
            get => _label;
            set { if (_label != value) { _label = value ?? string.Empty; MarkDirty(); } }
        }

        public float MinValue
        {
            get => _minValue;
            set { if (Math.Abs(_minValue - value) > 0.0001f) { _minValue = value; MarkDirty(); } }
        }

        public float MaxValue
        {
            get => _maxValue;
            set { if (Math.Abs(_maxValue - value) > 0.0001f) { _maxValue = value; MarkDirty(); } }
        }

        public float Value
        {
            get => _value;
            set
            {
                var clamped = Math.Clamp(value, _minValue, _maxValue);
                if (Math.Abs(_value - clamped) > 0.0001f) { _value = clamped; MarkDirty(); }
            }
        }

        /// <summary>Snap increment. 0 = continuous.</summary>
        public float Step
        {
            get => _step;
            set { if (Math.Abs(_step - value) > 0.0001f) { _step = MathF.Max(0f, value); MarkDirty(); } }
        }

        public float Width   { get => _width;   set { if (Math.Abs(_width   - value) > 0.001f) { _width   = MathF.Max(0.1f, value);  MarkDirty(); } } }
        public float Height  { get => _height;  set { if (Math.Abs(_height  - value) > 0.001f) { _height  = MathF.Max(0.05f, value); MarkDirty(); } } }
        public float OffsetX { get => _offsetX; set { if (Math.Abs(_offsetX - value) > 0.001f) { _offsetX = value; MarkDirty(); } } }
        public float OffsetY { get => _offsetY; set { if (Math.Abs(_offsetY - value) > 0.001f) { _offsetY = value; MarkDirty(); } } }
        public float OffsetZ { get => _offsetZ; set { if (Math.Abs(_offsetZ - value) > 0.001f) { _offsetZ = value; MarkDirty(); } } }

        public SliderControlComponent() { }

        public SliderControlComponent(string label, float min = 0f, float max = 1f, float value = 0f)
        {
            _label    = label ?? string.Empty;
            _minValue = min;
            _maxValue = max;
            _value    = Math.Clamp(value, min, max);
        }
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        /// <summary>Called by the Godot binding when the slider moves.</summary>
        public void InvokeValueChanged(float v) => OnValueChanged?.Invoke(v);
    }
}
