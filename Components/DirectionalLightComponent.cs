using System;
using V12.Core.Core.Interfaces;
using V12.Core;
namespace V12.Components
{
    /// <summary>
    /// Infinite directional light (sun / moon).
    /// Color channels are 0–1. Energy is a brightness multiplier.
    /// </summary>
    public class DirectionalLightComponent : ComponentBase
    {
        private float _colorR        = 1f;
        private float _colorG        = 1f;
        private float _colorB        = 1f;
        private float _energy        = 1f;
        private bool  _shadowEnabled = true;

        public override string Name        => "DirectionalLight";
        public override string Description => "Infinite directional light source";

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
        public float Energy
        {
            get => _energy;
            set { if (Math.Abs(_energy - value) > 0.001f) { _energy = MathF.Max(0f, value); MarkDirty(); } }
        }
        /// <summary>Whether the light casts shadows.</summary>
        public bool ShadowEnabled
        {
            get => _shadowEnabled;
            set { if (_shadowEnabled != value) { _shadowEnabled = value; MarkDirty(); } }
        }

        public DirectionalLightComponent() { }
        public DirectionalLightComponent(float r, float g, float b, float energy = 1f, bool shadowEnabled = true)
        {
            _colorR = Clamp01(r); _colorG = Clamp01(g); _colorB = Clamp01(b);
            _energy = energy; _shadowEnabled = shadowEnabled;
        }

        private static float Clamp01(float v) => Math.Max(0f, Math.Min(1f, v));
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        public override string ToString() =>
            $"DirectionalLight(RGB:{ColorR:F2},{ColorG:F2},{ColorB:F2} Energy:{Energy:F2} Shadow:{ShadowEnabled})";
    }
}
