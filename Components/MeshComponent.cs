using System;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>Defines what primitive shape an element's mesh should use.</summary>
    public enum MeshShape
    {
        Box,
        Sphere,
        Capsule,
        Cylinder,
        Plane
    }

    /// <summary>
    /// Controls the rendered shape of an element.
    /// Width / Height / Depth map to the three axes of the primitive.
    /// For Sphere / Capsule, Width is used as the radius.
    /// </summary>
    public class MeshComponent : ComponentBase
    {
        private MeshShape _shape  = MeshShape.Box;
        private float     _width  = 1f;
        private float     _height = 1f;
        private float     _depth  = 1f;

        public override string Name        => "Mesh";
        public override string Description => "Primitive mesh shape";

        public MeshShape Shape
        {
            get => _shape;
            set { if (_shape != value) { _shape = value; MarkDirty(); } }
        }

        public float Width
        {
            get => _width;
            set { if (Math.Abs(_width - value) > 0.0001f) { _width = value; MarkDirty(); } }
        }

        public float Height
        {
            get => _height;
            set { if (Math.Abs(_height - value) > 0.0001f) { _height = value; MarkDirty(); } }
        }

        public float Depth
        {
            get => _depth;
            set { if (Math.Abs(_depth - value) > 0.0001f) { _depth = value; MarkDirty(); } }
        }

        public MeshComponent() { }
        public MeshComponent(MeshShape shape, float width = 1f, float height = 1f, float depth = 1f)
        {
            _shape  = shape;
            _width  = width;
            _height = height;
            _depth  = depth;
        }

        public override string ToString() =>
            $"Mesh(Shape:{Shape}, {Width:F2}x{Height:F2}x{Depth:F2})";
    }
}
