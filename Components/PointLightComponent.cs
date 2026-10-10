using System;
using System.Numerics;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;
using V12.Core.UI;
using Color = System.Drawing.Color;

using MongoDB.Bson.Serialization.Attributes;

namespace V12.Components
{
    /// <summary>
    /// Omnidirectional point light that emits light in all directions from a single point.
    /// ColorR/G/B are 0–1. Range is in world units. Energy is the brightness multiplier.
    /// </summary>
    public class PointLightComponent : ComponentBase, ILightRenderable
    {
        private float _colorR  = 1f;
        private float _colorG  = 1f;
        private float _colorB  = 1f;
        private float _range   = 10f;
        private float _energy  = 1f;
        private Matrix4x4 _WorldTransform = Matrix4x4.Identity;

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

        // ── ILightRenderable ──
        /// <summary>
        /// Derived view over <see cref="ColorR"/>/<see cref="ColorG"/>/<see cref="ColorB"/>.
        /// Never serialized: it round-trips as an empty document whose
        /// all-zero default would overwrite the real floats on deserialize.
        /// </summary>
        [BsonIgnore]
        public Color Color
        {
            get => Color.FromArgb(
                (int)(Clamp01(_colorR) * 255),
                (int)(Clamp01(_colorG) * 255),
                (int)(Clamp01(_colorB) * 255));
            set
            {
                ColorR = value.R / 255f;
                ColorG = value.G / 255f;
                ColorB = value.B / 255f;
            }
        }

        public float Intensity
        {
            get => _energy;
            set => Energy = value;
        }

        public LightType Type => LightType.Point;

        public float Angle => 0f;
        public float SpotSoftness => 0f;

        public V12.Core.Interfaces.Renderer.RenderType RenderType => V12.Core.Interfaces.Renderer.RenderType.Light;

        public Matrix4x4 Transform
        {
            get
            {
                if (Owner != null)
                {
                    var t = Owner.GetComponent<TransformComponent>();
                    var s = Owner.GetComponent<ScaleComponent>();
                    Matrix4x4 scale = s != null ? Matrix4x4.CreateScale(s.ScaleX, s.ScaleY, s.ScaleZ) : Matrix4x4.Identity;
                    if (t != null)
                    {
                        return scale
                             * Matrix4x4.CreateFromYawPitchRoll(t.RY, t.RX, t.RZ)
                             * Matrix4x4.CreateTranslation(t.X, t.Y, t.Z);
                    }
                }
                return Matrix4x4.Identity;
            }
        }

        public bool IsWorldLocked => true;

        public Matrix4x4 WorldTransform
        {
            get => _WorldTransform;
            set
            {
                _WorldTransform = value;
                MarkDirty();
            }
        }

        public PointLightComponent() { }
        public PointLightComponent(float r, float g, float b, float range = 10f, float energy = 1f)
        {
            _colorR = Clamp01(r); _colorG = Clamp01(g); _colorB = Clamp01(b);
            _range = range; _energy = energy;
        }

        private static float Clamp01(float v) => Math.Max(0f, Math.Min(1f, v));

        /// <summary>Generate editable point light fields for the inspector.</summary>
        public override void BuildInspector(IInspector inspector)
        {
            inspector.Section("Point Light");
            inspector.Float("Red", () => ColorR, v => ColorR = v);
            inspector.Float("Green", () => ColorG, v => ColorG = v);
            inspector.Float("Blue", () => ColorB, v => ColorB = v);
            inspector.Float("Range", () => Range, v => Range = v);
            inspector.Float("Energy", () => Energy, v => Energy = v);
        }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        public override string ToString() =>
            $"PointLight(RGB:{ColorR:F2},{ColorG:F2},{ColorB:F2} Range:{Range:F1} Energy:{Energy:F2})";
    }
}
