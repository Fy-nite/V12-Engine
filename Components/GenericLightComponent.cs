using System;
using V12.Core.Core.Interfaces;
using V12.Core;
using V12.Core.Interfaces.Renderer;
using V12.Core.UI;
using System.Drawing;
using System.Numerics;
using BepuPhysics.Constraints;
using MongoDB.Bson.Serialization.Attributes;
namespace V12.Components
{
    public class GenericLightComponent : ComponentBase, ILightRenderable
    {
        private float _colorR        = 1f;
        private float _colorG        = 1f;
        private float _colorB        = 1f;
        private float _energy        = 1f;
        private bool  _shadowEnabled = true;
        private Color _shadowColor;
        private float _shadowEnergy;
        private float _Range = 100f;
        private float _angle = 45f;
        private float _spotSoftness = 0.5f;

        private LightType _lightType = LightType.Directional;

        private Matrix4x4 _WorldTransform;


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
        public LightType Type
        {
            get => _lightType;
            set
            {
                if (_lightType != value)
                {
                    _lightType = value;
                    MarkDirty();
                }
        }
        }

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
                var r = Clamp01(value.R / 255f);
                var g = Clamp01(value.G / 255f);
                var b = Clamp01(value.B / 255f);
                if (Math.Abs(_colorR - r) > 0.001f || Math.Abs(_colorG - g) > 0.001f || Math.Abs(_colorB - b) > 0.001f)
                {
                    _colorR = r; _colorG = g; _colorB = b;
                    MarkDirty();
                }
            }
        }

        public float Intensity
        {
            get { return _energy; }
            set
            {
                if (Math.Abs(_energy - value) > 0.001f)
                {
                    _energy = MathF.Max(0f, value);
                    MarkDirty();
                }
            }
        }

        public float Range
        {
            get => _Range;
            set
            {
                if (Math.Abs(_Range - value) > 0.001f)
                {
                    _Range = MathF.Max(0f, value);
                    MarkDirty();
                }
            }
        }

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

        public RenderType RenderType => RenderType.Light;

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

        public GenericLightComponent() {
            _WorldTransform = Matrix4x4.Identity;
        }
        public GenericLightComponent(float r, float g, float b, float energy = 1f, bool shadowEnabled = true)
        {
            _colorR = Clamp01(r); _colorG = Clamp01(g); _colorB = Clamp01(b);
            _energy = energy; _shadowEnabled = shadowEnabled;
            _WorldTransform = Matrix4x4.Identity ;
            
        }

        private static float Clamp01(float v) => Math.Max(0f, Math.Min(1f, v));
        public override IWorldElement BuildUI()
        {
            return new Element();
        }

        /// <summary>Generate editable light fields for the inspector.</summary>
        public override void BuildInspector(IInspector inspector)
        {
            inspector.Section("Light");
            inspector.Enum("Type", () => Type, v => Type = v);
            inspector.Float("Intensity", () => Energy, v => Energy = v);
            inspector.Float("Red", () => ColorR, v => ColorR = v);
            inspector.Float("Green", () => ColorG, v => ColorG = v);
            inspector.Float("Blue", () => ColorB, v => ColorB = v);
            inspector.Float("Range", () => Range, v => Range = v);
            inspector.Float("Angle", () => Angle, v => Angle = v);
            inspector.Bool("Shadows", () => ShadowEnabled, v => ShadowEnabled = v);
        }

        public override string ToString() =>
            $"DirectionalLight(RGB:{ColorR:F2},{ColorG:F2},{ColorB:F2} Energy:{Energy:F2} Shadow:{ShadowEnabled} Type:{Type})";
    }
}
