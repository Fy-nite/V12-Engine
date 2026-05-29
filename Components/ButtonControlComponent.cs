using System;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>
    /// A clickable button rendered in 3D world space.
    /// Maps to a <c>Button</c> inside a SubViewport + QuadMesh in the Godot scene.
    /// Subscribe to <see cref="OnPressed"/> to respond to clicks.
    /// </summary>
    public class ButtonControlComponent : ComponentBase
    {
        private string _label   = "Button";
        private float  _width   = 0.5f;
        private float  _height  = 0.5f;
        private float  _offsetX = 0f;
        private float  _offsetY = 0f;
        private float  _offsetZ = 0f;
        private float  _bgR = 0.20f, _bgG = 0.22f, _bgB = 0.35f, _bgA = 1f;
        private float  _textR = 1f, _textG = 1f, _textB = 1f, _textA = 1f;

        public override string Name        => "ButtonControl";
        public override string Description => "World-space clickable button";

        /// <summary>Fired on the Godot main thread when the button is pressed.</summary>
        public event Action? OnPressed;

        public string Label
        {
            get => _label;
            set { if (_label != value) { _label = value ?? string.Empty; MarkDirty(); } }
        }

        public float Width   { get => _width;   set { if (Math.Abs(_width   - value) > 0.001f) { _width   = MathF.Max(0.05f, value); MarkDirty(); } } }
        public float Height  { get => _height;  set { if (Math.Abs(_height  - value) > 0.001f) { _height  = MathF.Max(0.05f, value); MarkDirty(); } } }
        public float OffsetX { get => _offsetX; set { if (Math.Abs(_offsetX - value) > 0.001f) { _offsetX = value; MarkDirty(); } } }
        public float OffsetY { get => _offsetY; set { if (Math.Abs(_offsetY - value) > 0.001f) { _offsetY = value; MarkDirty(); } } }
        public float OffsetZ { get => _offsetZ; set { if (Math.Abs(_offsetZ - value) > 0.001f) { _offsetZ = value; MarkDirty(); } } }

        public float BgR { get => _bgR; set { if (Math.Abs(_bgR - value) > 0.001f) { _bgR = Clamp01(value); MarkDirty(); } } }
        public float BgG { get => _bgG; set { if (Math.Abs(_bgG - value) > 0.001f) { _bgG = Clamp01(value); MarkDirty(); } } }
        public float BgB { get => _bgB; set { if (Math.Abs(_bgB - value) > 0.001f) { _bgB = Clamp01(value); MarkDirty(); } } }
        public float BgA { get => _bgA; set { if (Math.Abs(_bgA - value) > 0.001f) { _bgA = Clamp01(value); MarkDirty(); } } }

        public float TextR { get => _textR; set { if (Math.Abs(_textR - value) > 0.001f) { _textR = Clamp01(value); MarkDirty(); } } }
        public float TextG { get => _textG; set { if (Math.Abs(_textG - value) > 0.001f) { _textG = Clamp01(value); MarkDirty(); } } }
        public float TextB { get => _textB; set { if (Math.Abs(_textB - value) > 0.001f) { _textB = Clamp01(value); MarkDirty(); } } }
        public float TextA { get => _textA; set { if (Math.Abs(_textA - value) > 0.001f) { _textA = Clamp01(value); MarkDirty(); } } }

        public ButtonControlComponent() { }

        public ButtonControlComponent(string label, float offsetX = 0f, float offsetY = 0f, float offsetZ = 0f)
        {
            _label   = label ?? "Button";
            _offsetX = offsetX;
            _offsetY = offsetY;
            _offsetZ = offsetZ;
        }

        /// <summary>Called by the Godot binding when the physical button is pressed.</summary>
        public void InvokePressed() => OnPressed?.Invoke();

        private static float Clamp01(float v) => Math.Max(0f, Math.Min(1f, v));
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
