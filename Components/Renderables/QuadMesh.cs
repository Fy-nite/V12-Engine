using System;
using System.Numerics;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

namespace V12.Components.Renderables
{
    public class QuadMesh : ComponentBase, IMeshRenderable
    {
        private Vector2 _size = Vector2.One;
        private Vector3 _position = Vector3.Zero;
        private Quaternion _rotation = Quaternion.Identity;
        private Vector3 _scale = Vector3.One;
        private double[] _meshPoints;
        private uint[] _indices;
        private Material _material;
        private Matrix4x4 _transform;
        private bool _transformDirty = true;

        public RenderType RenderType => RenderType.Mesh;
        public double[] MeshPoints => _meshPoints ??= GenerateVertices();
        public uint[] Indices => _indices ??= GenerateIndices();
        public Material Material => _material ??= new Material();
        public bool IsWorldLocked => true;
        public TRS LocalTransform => throw new NotImplementedException();
        public Matrix4x4 WorldTransform => throw new NotImplementedException();

        public QuadMesh() { }
        public QuadMesh(string name) { Name = name; }

        public Vector2 Size
        {
            get => _size;
            set { _size = value; _meshPoints = null; _transformDirty = true; }
        }

        public Vector3 Position
        {
            get => _position;
            set { _position = value; _transformDirty = true; }
        }

        public Quaternion Rotation
        {
            get => _rotation;
            set { _rotation = value; _transformDirty = true; }
        }

        public Vector3 Scale
        {
            get => _scale;
            set { _scale = value; _transformDirty = true; }
        }

        public Matrix4x4 Transform
        {
            get
            {
                if (_transformDirty)
                {
                    _transform = Matrix4x4.CreateScale(new Vector3(_scale.X * _size.X, _scale.Y * _size.Y, _scale.Z))
                        * Matrix4x4.CreateFromQuaternion(_rotation)
                        * Matrix4x4.CreateTranslation(_position);
                    _transformDirty = false;
                }
                return _transform;
            }
        }

        private double[] GenerateVertices()
        {
            var half = new Vector2(_size.X * 0.5f, _size.Y * 0.5f);
            var verts = new Vector3[]
            {
                new(-half.X, -half.Y, 0),
                new(half.X, -half.Y, 0),
                new(half.X, half.Y, 0),
                new(-half.X, half.Y, 0),
            };
            var res = new double[verts.Length * 3];
            for (int i = 0; i < verts.Length; i++)
            {
                res[i * 3] = verts[i].X;
                res[i * 3 + 1] = verts[i].Y;
                res[i * 3 + 2] = verts[i].Z;
            }
            return res;
        }

        private uint[] GenerateIndices() => new uint[] { 0, 1, 2, 0, 2, 3 };

        public override IWorldElement BuildUI() => throw new NotImplementedException();
    }
}
