using System;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>
    /// Non-uniform scale applied to an element's visual representation.
    /// Defaults to (1, 1, 1) — i.e. no scaling.
    /// </summary>
    public class ScaleComponent : ComponentBase
    {
        private float _scaleX = 1f;
        private float _scaleY = 1f;
        private float _scaleZ = 1f;

        public override string Name        => "Scale";
        public override string Description => "Non-uniform XYZ scale";

        public float ScaleX
        {
            get => _scaleX;
            set { if (Math.Abs(_scaleX - value) > 0.0001f) { _scaleX = value; MarkDirty(); } }
        }
        public float ScaleY
        {
            get => _scaleY;
            set { if (Math.Abs(_scaleY - value) > 0.0001f) { _scaleY = value; MarkDirty(); } }
        }
        public float ScaleZ
        {
            get => _scaleZ;
            set { if (Math.Abs(_scaleZ - value) > 0.0001f) { _scaleZ = value; MarkDirty(); } }
        }

        public ScaleComponent() { }
        public ScaleComponent(float x, float y, float z) { _scaleX = x; _scaleY = y; _scaleZ = z; }
        /// <summary>Uniform scale shorthand.</summary>
        public ScaleComponent(float uniform) : this(uniform, uniform, uniform) { }

        public override string ToString() => $"Scale({ScaleX:F3}, {ScaleY:F3}, {ScaleZ:F3})";
    }
}
