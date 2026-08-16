using System;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.UI;

namespace V12.Components
{
    /// <summary>
    /// Defines a local volumetric fog region.
    /// Color channels are 0–1. Density controls opacity.
    /// HeightFalloff causes the fog to thin out above the element's position.
    /// </summary>
    public class FogComponent : ComponentBase
    {
        private float _colorR       = 0.8f;
        private float _colorG       = 0.8f;
        private float _colorB       = 0.8f;
        private float _density      = 0.1f;
        private float _fogHeight    = 10f;
        private float _heightFalloff = 1f;

        public override string Name        => "Fog";
        public override string Description => "Volumetric fog volume";

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
        /// <summary>Volumetric density (0 = invisible, 1 = fully opaque).</summary>
        public float Density
        {
            get => _density;
            set { if (Math.Abs(_density - value) > 0.0001f) { _density = Clamp01(value); MarkDirty(); } }
        }
        /// <summary>World-unit height of the fog volume above the element's origin.</summary>
        public float FogHeight
        {
            get => _fogHeight;
            set { if (Math.Abs(_fogHeight - value) > 0.001f) { _fogHeight = MathF.Max(0f, value); MarkDirty(); } }
        }
        /// <summary>Controls how sharply the fog fades with height. Higher = sharper edge.</summary>
        public float HeightFalloff
        {
            get => _heightFalloff;
            set { if (Math.Abs(_heightFalloff - value) > 0.001f) { _heightFalloff = MathF.Max(0f, value); MarkDirty(); } }
        }

        public FogComponent() { }
        public FogComponent(float r, float g, float b, float density = 0.1f, float fogHeight = 10f)
        {
            _colorR = Clamp01(r); _colorG = Clamp01(g); _colorB = Clamp01(b);
            _density = Clamp01(density); _fogHeight = fogHeight;
        }

        private static float Clamp01(float v) => Math.Max(0f, Math.Min(1f, v));

        /// <summary>Generate editable fog fields for the inspector.</summary>
        public override void BuildInspector(IInspector inspector)
        {
            inspector.Section("Fog");
            inspector.Float("Red", () => ColorR, v => ColorR = v);
            inspector.Float("Green", () => ColorG, v => ColorG = v);
            inspector.Float("Blue", () => ColorB, v => ColorB = v);
            inspector.Float("Density", () => Density, v => Density = v);
            inspector.Float("Height", () => FogHeight, v => FogHeight = v);
            inspector.Float("Height Falloff", () => HeightFalloff, v => HeightFalloff = v);
        }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        public override string ToString() =>
            $"Fog(RGB:{ColorR:F2},{ColorG:F2},{ColorB:F2} Density:{Density:F3} Height:{FogHeight:F1})";
    }
}
