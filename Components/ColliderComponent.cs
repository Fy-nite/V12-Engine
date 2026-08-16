using System;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.UI;

namespace V12.Components
{
    /// <summary>
    /// Defines a physics collision shape.
    /// Shape mirrors MeshShape so a box element can also be a box collider etc.
    /// IsTrigger = true → overlap-only (no physics response).
    /// </summary>
    public class ColliderComponent : ComponentBase
    {
        private MeshShape _shape    = MeshShape.Box;
        private float     _width    = 1f;
        private float     _height   = 1f;
        private float     _depth    = 1f;
        private bool      _isTrigger = false;

        public override string Name        => "Collider";
        public override string Description => "Physics collision shape";

        public MeshShape Shape
        {
            get => _shape;
            set { if (_shape != value) { _shape = value; MarkDirty(); } }
        }
        public float Width
        {
            get => _width;
            set { if (Math.Abs(_width - value) > 0.0001f) { _width = MathF.Max(0f, value); MarkDirty(); } }
        }
        public float Height
        {
            get => _height;
            set { if (Math.Abs(_height - value) > 0.0001f) { _height = MathF.Max(0f, value); MarkDirty(); } }
        }
        public float Depth
        {
            get => _depth;
            set { if (Math.Abs(_depth - value) > 0.0001f) { _depth = MathF.Max(0f, value); MarkDirty(); } }
        }
        /// <summary>Trigger colliders raise overlap events but do not produce a physics response.</summary>
        public bool IsTrigger
        {
            get => _isTrigger;
            set { if (_isTrigger != value) { _isTrigger = value; MarkDirty(); } }
        }

        public ColliderComponent() { }
        public ColliderComponent(MeshShape shape, float width = 1f, float height = 1f, float depth = 1f, bool isTrigger = false)
        {
            _shape = shape; _width = width; _height = height; _depth = depth; _isTrigger = isTrigger;
        }

        /// <summary>Generate editable collider fields for the inspector.</summary>
        public override void BuildInspector(IInspector inspector)
        {
            inspector.Section("Collider");
            inspector.Enum("Shape", () => Shape, v => Shape = v);
            inspector.Float("Width", () => Width, v => Width = v);
            inspector.Float("Height", () => Height, v => Height = v);
            inspector.Float("Depth", () => Depth, v => Depth = v);
            inspector.Bool("Is Trigger", () => IsTrigger, v => IsTrigger = v);
        }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        public override string ToString() =>
            $"Collider(Shape:{Shape} {Width:F2}x{Height:F2}x{Depth:F2} Trigger:{IsTrigger})";
    }
}
