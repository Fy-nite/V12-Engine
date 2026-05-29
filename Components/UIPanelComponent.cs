using System;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>
    /// Renders a 2D UI panel onto a quad mesh in 3D world space using a SubViewport.
    /// Useful for in-world screens, monitors, or informational panels attached to physical objects.
    /// Maps to a <c>SubViewport</c> + <c>MeshInstance3D</c> (QuadMesh) combination in the Godot scene.
    /// </summary>
    public class UIPanelComponent : ComponentBase
    {
        private string _title  = string.Empty;
        private string _body   = string.Empty;
        private float  _width  = 1.0f;
        private float  _height = 0.5f;
        private float  _bgR = 0.08f, _bgG = 0.08f, _bgB = 0.12f, _bgA = 0.90f;
        private float  _textR = 1f, _textG = 1f, _textB = 1f, _textA = 1f;
        private float  _offsetY = 1.0f;
        public System.Numerics.Vector3 Position = new System.Numerics.Vector3(0, 1.0f, 0);
        public System.Numerics.Quaternion Orientation = System.Numerics.Quaternion.Identity;

        public override string Name        => "UIPanel";
        public override string Description => "World-space UI panel";

        public string Title
        {
            get => _title;
            set { if (_title != value) { _title = value ?? string.Empty; MarkDirty(); } }
        }

        public string Body
        {
            get => _body;
            set { if (_body != value) { _body = value ?? string.Empty; MarkDirty(); } }
        }

        /// <summary>Panel width in world units.</summary>
        public float Width
        {
            get => _width;
            set { if (Math.Abs(_width - value) > 0.001f) { _width = MathF.Max(0.1f, value); MarkDirty(); } }
        }

        /// <summary>Panel height in world units.</summary>
        public float Height
        {
            get => _height;
            set { if (Math.Abs(_height - value) > 0.001f) { _height = MathF.Max(0.1f, value); MarkDirty(); } }
        }

        public float BackgroundR { get => _bgR; set { if (Math.Abs(_bgR - value) > 0.001f) { _bgR = Clamp01(value); MarkDirty(); } } }
        public float BackgroundG { get => _bgG; set { if (Math.Abs(_bgG - value) > 0.001f) { _bgG = Clamp01(value); MarkDirty(); } } }
        public float BackgroundB { get => _bgB; set { if (Math.Abs(_bgB - value) > 0.001f) { _bgB = Clamp01(value); MarkDirty(); } } }
        public float BackgroundA { get => _bgA; set { if (Math.Abs(_bgA - value) > 0.001f) { _bgA = Clamp01(value); MarkDirty(); } } }

        public float TextR { get => _textR; set { if (Math.Abs(_textR - value) > 0.001f) { _textR = Clamp01(value); MarkDirty(); } } }
        public float TextG { get => _textG; set { if (Math.Abs(_textG - value) > 0.001f) { _textG = Clamp01(value); MarkDirty(); } } }
        public float TextB { get => _textB; set { if (Math.Abs(_textB - value) > 0.001f) { _textB = Clamp01(value); MarkDirty(); } } }
        public float TextA { get => _textA; set { if (Math.Abs(_textA - value) > 0.001f) { _textA = Clamp01(value); MarkDirty(); } } }

        /// <summary>Vertical offset above the element's origin in world units.</summary>
        public float OffsetY
        {
            get => _offsetY;
            set { if (Math.Abs(_offsetY - value) > 0.001f) { _offsetY = value; MarkDirty(); } }
        }

        public UIPanelComponent() { }

        public UIPanelComponent(string title, string body = "", float width = 1.0f, float height = 0.5f, float offsetY = 1.0f)
        {
            _title   = title ?? string.Empty;
            _body    = body  ?? string.Empty;
            _width   = MathF.Max(0.1f, width);
            _height  = MathF.Max(0.1f, height);
            _offsetY = offsetY;
        }
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        private static float Clamp01(float v) => Math.Max(0f, Math.Min(1f, v));
    }
}
