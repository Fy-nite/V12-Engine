using Assimp;
using System;
using System.Numerics;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

namespace V12.Components
{
    /// <summary>Defines what primitive shape an element's mesh should use.</summary>
    public enum MeshShape
    {
        Box,
        Sphere,
        Capsule,
        Cylinder,
        Plane,
        Custom
    }

    /// <summary>
    /// Controls the rendered shape of an element.
    /// Width / Height / Depth map to the three axes of the primitive.
    /// For Sphere / Capsule, Width is used as the radius.
    /// </summary>
    public class MeshComponent : ComponentBase, IMeshRenderable
    {
        private MeshShape _shape  = MeshShape.Box;
        private float     _width  = 1f;
        private float     _height = 1f;
        private float     _depth  = 1f;

        // Cached mesh data
        private double[] _meshPoints;
        private uint[] _indices;

        // Optional custom vertex/index data (used when Shape == Custom)
        public double[] CustomMeshPoints { get; set; }
        public uint[] CustomIndices { get; set; }

		public override string Name { get; set; } = "Mesh";
        public override string Description => "Primitive mesh shape";

        public MeshShape Shape
        {
            get => _shape;
            set { if (_shape != value) { _shape = value; InvalidateCache(); MarkDirty(); } }
        }

        public float Width
        {
            get => _width;
            set { if (Math.Abs(_width - value) > 0.0001f) { _width = value; InvalidateCache(); MarkDirty(); } }
        }

        public float Height
        {
            get => _height;
            set { if (Math.Abs(_height - value) > 0.0001f) { _height = value; InvalidateCache(); MarkDirty(); } }
        }

        public float Depth
        {
            get => _depth;
            set { if (Math.Abs(_depth - value) > 0.0001f) { _depth = value; InvalidateCache(); MarkDirty(); } }
        }

        public override void OnAttach(IWorldElement element)
        {
            base.OnAttach(element);
            var collider = element.GetComponent<ColliderComponent>();
            if (collider != null)
            {
                _width = collider.Width;
                _height = collider.Height;
                _depth = collider.Depth;
            }
        }

        public RenderType RenderType => RenderType.Mesh;

        public double[] MeshPoints => Shape == MeshShape.Custom && CustomMeshPoints != null ? CustomMeshPoints : (_meshPoints ??= GenerateMeshPoints());

        public uint[] Indices => Shape == MeshShape.Custom && CustomIndices != null ? CustomIndices : (_indices ??= GenerateIndices());

        public Material Material => null; // Use MaterialComponent from element

        public Matrix4x4 Transform
        {
            get
            {
                if (Owner != null)
                {
                    var t = Owner.GetComponent<TransformComponent>();
                    var s = Owner.GetComponent<ScaleComponent>();
                    
                    float scaleX = Width * (s?.ScaleX ?? 1f);
                    float scaleY = Height * (s?.ScaleY ?? 1f);
                    float scaleZ = Depth * (s?.ScaleZ ?? 1f);

                    if (t != null)
                    {
                        return Matrix4x4.CreateScale(scaleX, scaleY, scaleZ)
                             * Matrix4x4.CreateFromYawPitchRoll(t.RY, t.RX, t.RZ)
                             * Matrix4x4.CreateTranslation(t.X, t.Y, t.Z);
                    }
                    // Fallback: use Element's LocalTransform
                    var lt = Owner.LocalTransform;
                    return Matrix4x4.CreateScale(scaleX, scaleY, scaleZ)
                         * Matrix4x4.CreateFromQuaternion(lt.Rotation)
                         * Matrix4x4.CreateTranslation(lt.Position);
                }
                return Matrix4x4.Identity;
            }
        }

        public bool IsWorldLocked => true;

        public TRS LocalTransform => throw new NotImplementedException();

        public Matrix4x4 WorldTransform => throw new NotImplementedException();

        public MeshComponent() { }
        public MeshComponent(MeshShape shape, float width = 1f, float height = 1f, float depth = 1f)
        {
            _shape  = shape;
            _width  = width;
            _height = height;
            _depth  = depth;
        }

        private void InvalidateCache()
        {
            _meshPoints = null;
            _indices = null;
        }

        private double[] GenerateMeshPoints()
        {
            switch (Shape)
            {
                case MeshShape.Box:
                    return GenerateBoxPoints();
                case MeshShape.Plane:
                    return GeneratePlanePoints();
                case MeshShape.Sphere:
                    // StereoKit handles sphere generation better, but we provide points for generic renderers
                    return GenerateSpherePoints(16, 8);
                default:
                    return GenerateBoxPoints();
            }
        }

        private uint[] GenerateIndices()
        {
            switch (Shape)
            {
                case MeshShape.Box:
                    return GenerateBoxIndices();
                case MeshShape.Plane:
                    return GeneratePlaneIndices();
                case MeshShape.Sphere:
                    return GenerateSphereIndices(16, 8);
                default:
                    return GenerateBoxIndices();
            }
        }

        private double[] GenerateBoxPoints()
        {
            // Unit box points (transform handles scaling)
            return new double[] {
                -0.5, -0.5,  0.5,  0.5, -0.5,  0.5,  0.5,  0.5,  0.5, -0.5,  0.5,  0.5, // Front
                -0.5, -0.5, -0.5, -0.5,  0.5, -0.5,  0.5,  0.5, -0.5,  0.5, -0.5, -0.5, // Back
                -0.5,  0.5, -0.5, -0.5,  0.5,  0.5,  0.5,  0.5,  0.5,  0.5,  0.5, -0.5, // Top
                -0.5, -0.5, -0.5,  0.5, -0.5, -0.5,  0.5, -0.5,  0.5, -0.5, -0.5,  0.5, // Bottom
                 0.5, -0.5, -0.5,  0.5,  0.5, -0.5,  0.5,  0.5,  0.5,  0.5, -0.5,  0.5, // Right
                -0.5, -0.5, -0.5, -0.5, -0.5,  0.5, -0.5,  0.5,  0.5, -0.5,  0.5, -0.5  // Left
            };
        }

        private uint[] GenerateBoxIndices()
        {
            return new uint[] {
                0,1,2, 0,2,3, 4,5,6, 4,6,7, 8,9,10, 8,10,11,
                12,13,14, 12,14,15, 16,17,18, 16,18,19, 20,21,22, 20,22,23
            };
        }

        private double[] GeneratePlanePoints()
        {
            return new double[] {
                -0.5, 0, -0.5,
                 0.5, 0, -0.5,
                 0.5, 0,  0.5,
                -0.5, 0,  0.5
            };
        }

        private uint[] GeneratePlaneIndices()
        {
            return new uint[] { 0, 1, 2, 0, 2, 3 };
        }

        private double[] GenerateSpherePoints(int stacks, int slices)
        {
            var points = new System.Collections.Generic.List<double>();
            for (int i = 0; i <= stacks; i++)
            {
                double phi = Math.PI * i / stacks;
                for (int j = 0; j <= slices; j++)
                {
                    double theta = 2 * Math.PI * j / slices;
                    points.Add(0.5 * Math.Sin(phi) * Math.Cos(theta));
                    points.Add(0.5 * Math.Cos(phi));
                    points.Add(0.5 * Math.Sin(phi) * Math.Sin(theta));
                }
            }
            return points.ToArray();
        }

        private uint[] GenerateSphereIndices(int stacks, int slices)
        {
            var indices = new System.Collections.Generic.List<uint>();
            for (int i = 0; i < stacks; i++)
            {
                for (int j = 0; j < slices; j++)
                {
                    uint first = (uint)(i * (slices + 1) + j);
                    uint second = (uint)(first + slices + 1);
                    indices.Add(first); indices.Add(second); indices.Add(first + 1);
                    indices.Add(second); indices.Add(second + 1); indices.Add(first + 1);
                }
            }
            return indices.ToArray();
        }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        public override string ToString() =>
            $"Mesh(Shape:{Shape}, {Width:F2}x{Height:F2}x{Depth:F2})";
    }
}
