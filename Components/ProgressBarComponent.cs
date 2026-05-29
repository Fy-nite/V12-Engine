using System;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>
    /// A read-only progress bar rendered in 3D world space.
    /// Maps to a <c>ProgressBar</c> inside a SubViewport + QuadMesh in the Godot scene.
    /// </summary>
    public class ProgressBarComponent : ComponentBase
    {
        private string _label          = string.Empty;
        private float  _value          = 0f;
        private bool   _showPercentage = true;
        private float  _width          = 0.5f;
        private float  _height         = 0.10f;
        private float  _offsetX        = 0f;
        private float  _offsetY        = 0f;
        private float  _offsetZ        = 0f;
        private float  _fillR = 0.20f, _fillG = 0.80f, _fillB = 0.30f, _fillA = 1f;
        private float  _bgR   = 0.15f, _bgG   = 0.15f, _bgB   = 0.15f, _bgA   = 1f;

        public override string Name        => "ProgressBar";
        public override string Description => "World-space progress bar";

        public string Label
        {
            get => _label;
            set { if (_label != value) { _label = value ?? string.Empty; MarkDirty(); } }
        }

        /// <summary>Normalised fill amount in [0, 1].</summary>
        public float Value
        {
            get => _value;
            set
            {
                var clamped = Math.Clamp(value, 0f, 1f);
                if (Math.Abs(_value - clamped) > 0.0001f) { _value = clamped; MarkDirty(); }
            }
        }

        public bool ShowPercentage
        {
            get => _showPercentage;
            set { if (_showPercentage != value) { _showPercentage = value; MarkDirty(); } }
        }

        public float Width   { get => _width;   set { if (Math.Abs(_width   - value) > 0.001f) { _width   = MathF.Max(0.1f,  value); MarkDirty(); } } }
        public float Height  { get => _height;  set { if (Math.Abs(_height  - value) > 0.001f) { _height  = MathF.Max(0.05f, value); MarkDirty(); } } }
        public float OffsetX { get => _offsetX; set { if (Math.Abs(_offsetX - value) > 0.001f) { _offsetX = value; MarkDirty(); } } }
        public float OffsetY { get => _offsetY; set { if (Math.Abs(_offsetY - value) > 0.001f) { _offsetY = value; MarkDirty(); } } }
        public float OffsetZ { get => _offsetZ; set { if (Math.Abs(_offsetZ - value) > 0.001f) { _offsetZ = value; MarkDirty(); } } }

        // Fill colour (default: green)
        public float FillR { get => _fillR; set { if (Math.Abs(_fillR - value) > 0.001f) { _fillR = Clamp01(value); MarkDirty(); } } }
        public float FillG { get => _fillG; set { if (Math.Abs(_fillG - value) > 0.001f) { _fillG = Clamp01(value); MarkDirty(); } } }
        public float FillB { get => _fillB; set { if (Math.Abs(_fillB - value) > 0.001f) { _fillB = Clamp01(value); MarkDirty(); } } }
        public float FillA { get => _fillA; set { if (Math.Abs(_fillA - value) > 0.001f) { _fillA = Clamp01(value); MarkDirty(); } } }

        // Background colour (default: dark grey)
        public float BgR { get => _bgR; set { if (Math.Abs(_bgR - value) > 0.001f) { _bgR = Clamp01(value); MarkDirty(); } } }
        public float BgG { get => _bgG; set { if (Math.Abs(_bgG - value) > 0.001f) { _bgG = Clamp01(value); MarkDirty(); } } }
        public float BgB { get => _bgB; set { if (Math.Abs(_bgB - value) > 0.001f) { _bgB = Clamp01(value); MarkDirty(); } } }
        public float BgA { get => _bgA; set { if (Math.Abs(_bgA - value) > 0.001f) { _bgA = Clamp01(value); MarkDirty(); } } }

        public ProgressBarComponent() { }

        public ProgressBarComponent(string label, float value = 0f, bool showPercentage = true)
        {
            _label          = label ?? string.Empty;
            _value          = Math.Clamp(value, 0f, 1f);
            _showPercentage = showPercentage;
        }
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        private static float Clamp01(float v) => Math.Max(0f, Math.Min(1f, v));
    }
}
