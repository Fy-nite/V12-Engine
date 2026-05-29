using System;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>
    /// Directional cone-shaped spotlight.
    /// ColorR/G/B are 0–1. Range and Angle are in world-units / degrees.
    /// SpotSoftness controls the attenuation at the cone edge (0 = hard, 1 = very soft).
    /// </summary>
    public class SpotLightComponent : ComponentBase
    {
        private float _colorR       = 1f;
        private float _colorG       = 1f;
        private float _colorB       = 1f;
        private float _range        = 15f;
        private float _energy       = 1f;
        private float _angle        = 45f;
        private float _spotSoftness = 0.5f;

        public override string Name        => "SpotLight";
        public override string Description => "Directional cone spotlight source";

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
        /// <summary>Half-angle of the spotlight cone, in degrees.</summary>
        public float Angle
        {
            get => _angle;
            set { if (Math.Abs(_angle - value) > 0.001f) { _angle = Math.Clamp(value, 0f, 90f); MarkDirty(); } }
        }
        public float SpotSoftness
        {
            get => _spotSoftness;
            set { if (Math.Abs(_spotSoftness - value) > 0.001f) { _spotSoftness = Clamp01(value); MarkDirty(); } }
        }

        public SpotLightComponent() { }
        public SpotLightComponent(float r, float g, float b, float range = 15f, float energy = 1f, float angle = 45f, float spotSoftness = 0.5f)
        {
            _colorR = Clamp01(r); _colorG = Clamp01(g); _colorB = Clamp01(b);
            _range = range; _energy = energy; _angle = angle; _spotSoftness = Clamp01(spotSoftness);
        }

        private static float Clamp01(float v) => Math.Max(0f, Math.Min(1f, v));
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        public override string ToString() =>
            $"SpotLight(RGB:{ColorR:F2},{ColorG:F2},{ColorB:F2} Range:{Range:F1} Energy:{Energy:F2} Angle:{Angle:F1}°)";
    }
}
