using System;
using System.Drawing;
using System.Numerics;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

namespace V12.Components
{
    public class SvgComponent : ComponentBase, ISvgRenderable
    {
        public override string Name { get; set; } = "Svg";
        public override string Description => "Inline SVG vector graphic";

        public string SvgContent { get; set; } = "";
        public float Width { get; set; } = 1f;
        public float Height { get; set; } = 1f;

        private float _tintR = 1f, _tintG = 1f, _tintB = 1f, _tintA = 1f;

        public float TintR { get => _tintR; set => _tintR = Math.Clamp(value, 0f, 1f); }
        public float TintG { get => _tintG; set => _tintG = Math.Clamp(value, 0f, 1f); }
        public float TintB { get => _tintB; set => _tintB = Math.Clamp(value, 0f, 1f); }
        public float TintA { get => _tintA; set => _tintA = Math.Clamp(value, 0f, 1f); }

        public Vector2 Size => new Vector2(Width, Height);

        public Color Tint => Color.FromArgb(
            (int)(_tintA * 255),
            (int)(_tintR * 255),
            (int)(_tintG * 255),
            (int)(_tintB * 255));

        public RenderType RenderType => RenderType.Svg;

        public Matrix4x4 Transform
        {
            get
            {
                if (Owner != null)
                {
                    var t = Owner.GetComponent<TransformComponent>();
                    var s = Owner.GetComponent<ScaleComponent>();
                    float sx = s?.ScaleX ?? 1f;
                    float sy = s?.ScaleY ?? 1f;
                    float sz = s?.ScaleZ ?? 1f;
                    if (t != null)
                        return Matrix4x4.CreateScale(sx, sy, sz)
                             * Matrix4x4.CreateFromYawPitchRoll(t.RY, t.RX, t.RZ)
                             * Matrix4x4.CreateTranslation(t.X, t.Y, t.Z);
                }
                return Matrix4x4.Identity;
            }
        }

        public bool IsWorldLocked => true;

        public TRS LocalTransform => throw new NotImplementedException();
        public Matrix4x4 WorldTransform => throw new NotImplementedException();

        public override IWorldElement BuildUI() => new Element();
    }
}
