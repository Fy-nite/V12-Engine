using System;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>
    /// Omnidirectional point light that emits light in all directions from a single point.
    /// ColorR/G/B are 0–1. Range is in world units. Energy is the brightness multiplier.
    /// </summary>
    public class PointLightComponent : ComponentBase
    {
        private float _colorR  = 1f;
        private float _colorG  = 1f;
        private float _colorB  = 1f;
        private float _range   = 10f;
        private float _energy  = 1f;

        public override string Name        => "PointLight";
        public override string Description => "Omnidirectional point light source";

        public float ColorR
        {
            get => _colorR;
            set { if (Math.Abs(_colorR - value) > 0.001f) { _colorR = Clamp01(value); MarkDirty(); } }
        }
        public float ColorG
        {
            get => _colorG;
            set { if (Math.Abs(_colorG - value) > 0.001f) { _colorG = Clamp01(value); MarkDirty(); } }
        }
        public float ColorB
        {
            get => _colorB;
            set { if (Math.Abs(_colorB - value) > 0.001f) { _colorB = Clamp01(value); MarkDirty(); } }
        }
        public float Range
        {
            get => _range;
            set { if (Math.Abs(_range - value) > 0.001f) { _range = MathF.Max(0f, value); MarkDirty(); } }
        }
        public float Energy
        {
            get => _energy;
            set { if (Math.Abs(_energy - value) > 0.001f) { _energy = MathF.Max(0f, value); MarkDirty(); } }
        }

        public PointLightComponent() { }
        public PointLightComponent(float r, float g, float b, float range = 10f, float energy = 1f)
        {
            _colorR = Clamp01(r); _colorG = Clamp01(g); _colorB = Clamp01(b);
            _range = range; _energy = energy;
        }

        private static float Clamp01(float v) => Math.Max(0f, Math.Min(1f, v));

        public override string ToString() =>
            $"PointLight(RGB:{ColorR:F2},{ColorG:F2},{ColorB:F2} Range:{Range:F1} Energy:{Energy:F2})";
    }
}
