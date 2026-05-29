using System;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>
    /// A single-line text input rendered in 3D world space.
    /// Maps to a <c>LineEdit</c> inside a SubViewport + QuadMesh in the Godot scene.
    /// Subscribe to <see cref="OnTextChanged"/> to respond to typing.
    /// </summary>
    public class TextInputControlComponent : ComponentBase
    {
        private string _text        = string.Empty;
        private string _placeholder = string.Empty;
        private string _label       = string.Empty;
        private float  _width       = 0.5f;
        private float  _height      = 0.13f;
        private float  _offsetX     = 0f;
        private float  _offsetY     = 0f;
        private float  _offsetZ     = 0f;

        public override string Name        => "TextInputControl";
        public override string Description => "World-space single-line text input";

        /// <summary>Fired on the Godot main thread whenever the typed text changes.</summary>
        public event Action<string>? OnTextChanged;

        public string Text
        {
            get => _text;
            set { if (_text != value) { _text = value ?? string.Empty; MarkDirty(); } }
        }

        public string Placeholder
        {
            get => _placeholder;
            set { if (_placeholder != value) { _placeholder = value ?? string.Empty; MarkDirty(); } }
        }

        /// <summary>Optional header label displayed above the input field.</summary>
        public string Label
        {
            get => _label;
            set { if (_label != value) { _label = value ?? string.Empty; MarkDirty(); } }
        }

        public float Width   { get => _width;   set { if (Math.Abs(_width   - value) > 0.001f) { _width   = MathF.Max(0.1f,  value); MarkDirty(); } } }
        public float Height  { get => _height;  set { if (Math.Abs(_height  - value) > 0.001f) { _height  = MathF.Max(0.05f, value); MarkDirty(); } } }
        public float OffsetX { get => _offsetX; set { if (Math.Abs(_offsetX - value) > 0.001f) { _offsetX = value; MarkDirty(); } } }
        public float OffsetY { get => _offsetY; set { if (Math.Abs(_offsetY - value) > 0.001f) { _offsetY = value; MarkDirty(); } } }
        public float OffsetZ { get => _offsetZ; set { if (Math.Abs(_offsetZ - value) > 0.001f) { _offsetZ = value; MarkDirty(); } } }

        public TextInputControlComponent() { }

        public TextInputControlComponent(string label, string placeholder = "", string text = "",
                                         float offsetX = 0f, float offsetY = 0f, float offsetZ = 0f)
        {
            _label       = label       ?? string.Empty;
            _placeholder = placeholder ?? string.Empty;
            _text        = text        ?? string.Empty;
            _offsetX     = offsetX;
            _offsetY     = offsetY;
            _offsetZ     = offsetZ;
        }
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        /// <summary>Called by the Godot binding when the LineEdit text changes.</summary>
        public void InvokeTextChanged(string text) => OnTextChanged?.Invoke(text);
    }
}
