using Assimp;
using System;
using System.Numerics;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

namespace V12.Components.Renderables
{
    public class QuadMesh : ComponentBase, IMeshRenderable
    {
        private Vector2 _size = Vector2.One;
        private double[] _meshPoints;
        private uint[] _indices;
        private Material _material;

        public RenderType RenderType => RenderType.Mesh;
        public double[] MeshPoints => _meshPoints ??= GenerateVertices();
        public uint[] Indices => _indices ??= GenerateIndices();
        public Material Material => _material ??= new Material();
        public bool IsWorldLocked => true;
        public TRS LocalTransform => throw new NotImplementedException();
        public Matrix4x4 WorldTransform => Owner?.WorldTransform ?? Matrix4x4.Identity;

        public QuadMesh() { }
        public QuadMesh(string name) { Name = name; }

        public Vector2 Size
        {
            get => _size;
            set { _size = value; _meshPoints = null; }
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
