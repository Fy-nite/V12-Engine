using System;

namespace V12.Components
{
    /// <summary>Defines how a UILabel billboards toward the camera.</summary>
    public enum UIBillboardMode
    {
        Disabled,
        Enabled,
        YAxis
    }

    /// <summary>
    /// Renders a text label in 3D world space above the element.
    /// Maps to a <c>Label3D</c> child node in the Godot scene.
    /// Actual world height = <see cref="FontSize"/> × <see cref="PixelSize"/>.
    /// </summary>
    public class UILabelComponent : ComponentBase
    {
        private string          _text      = string.Empty;
        private float           _fontSize  = 48f;
        private float           _pixelSize = 0.005f;
        private float           _r = 1f, _g = 1f, _b = 1f, _a = 1f;
        private UIBillboardMode _billboard = UIBillboardMode.YAxis;
        private float           _offsetY   = 1.5f;

        public override string Name        => "UILabel";
        public override string Description => "World-space text label";

        public string Text
        {
            get => _text;
            set { if (_text != value) { _text = value ?? string.Empty; MarkDirty(); } }
        }

        /// <summary>Font size in pixels. Controls the sharpness of the rendered text.</summary>
        public float FontSize
        {
            get => _fontSize;
            set { if (Math.Abs(_fontSize - value) > 0.5f) { _fontSize = MathF.Max(1f, value); MarkDirty(); } }
        }

        /// <summary>World-space size of one pixel. Actual world height = FontSize × PixelSize.</summary>
        public float PixelSize
        {
            get => _pixelSize;
            set { if (Math.Abs(_pixelSize - value) > 0.00001f) { _pixelSize = MathF.Max(0.0001f, value); MarkDirty(); } }
        }

        public float ColorR { get => _r; set { if (Math.Abs(_r - value) > 0.001f) { _r = Clamp01(value); MarkDirty(); } } }
        public float ColorG { get => _g; set { if (Math.Abs(_g - value) > 0.001f) { _g = Clamp01(value); MarkDirty(); } } }
        public float ColorB { get => _b; set { if (Math.Abs(_b - value) > 0.001f) { _b = Clamp01(value); MarkDirty(); } } }
        public float ColorA { get => _a; set { if (Math.Abs(_a - value) > 0.001f) { _a = Clamp01(value); MarkDirty(); } } }

        public UIBillboardMode Billboard
        {
            get => _billboard;
            set { if (_billboard != value) { _billboard = value; MarkDirty(); } }
        }

        /// <summary>Vertical offset above the element's origin in world units.</summary>
        public float OffsetY
        {
            get => _offsetY;
            set { if (Math.Abs(_offsetY - value) > 0.001f) { _offsetY = value; MarkDirty(); } }
        }

        public UILabelComponent() { }

        public UILabelComponent(string text, float offsetY = 1.5f, UIBillboardMode billboard = UIBillboardMode.YAxis)
        {
            _text      = text ?? string.Empty;
            _offsetY   = offsetY;
            _billboard = billboard;
        }

        private static float Clamp01(float v) => Math.Max(0f, Math.Min(1f, v));
    }
}
