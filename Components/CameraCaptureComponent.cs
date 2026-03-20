using System;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>
    /// Creates a render-to-texture camera on an element.
    /// The captured texture path can be referenced by other elements' materials.
    /// </summary>
    public class CameraCaptureComponent : ComponentBase
    {
        private float _fov = 75f;
        private float _near = 0.05f;
        private float _far  = 1000f;
        private int   _textureWidth  = 512;
        private int   _textureHeight = 512;
        private string _textureName  = string.Empty; // logical name consumers might reference

        public override string Name => "CameraCapture";
        public override string Description => "Render-to-texture camera capture";

        public float Fov { get => _fov; set { if (Math.Abs(_fov - value) > 0.001f) { _fov = Math.Clamp(value, 1f, 179f); MarkDirty(); } } }
        public float Near { get => _near; set { if (Math.Abs(_near - value) > 0.0001f) { _near = MathF.Max(0.001f, value); MarkDirty(); } } }
        public float Far  { get => _far;  set { if (Math.Abs(_far  - value) > 0.001f) { _far  = MathF.Max(_near + 0.1f, value); MarkDirty(); } } }

        public int TextureWidth  { get => _textureWidth;  set { if (_textureWidth != value) { _textureWidth = Math.Max(8, value); MarkDirty(); } } }
        public int TextureHeight { get => _textureHeight; set { if (_textureHeight != value) { _textureHeight = Math.Max(8, value); MarkDirty(); } } }

        public string TextureName { get => _textureName; set { if (_textureName != value) { _textureName = value ?? string.Empty; MarkDirty(); } } }

        public CameraCaptureComponent() { }

        public CameraCaptureComponent(int w, int h, string name)
        {
            _textureWidth = Math.Max(8, w); _textureHeight = Math.Max(8, h); _textureName = name ?? string.Empty;
        }
    }
}
