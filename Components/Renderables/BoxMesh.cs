using Assimp;
using System;
using System.Collections.Generic;
using System.Numerics;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

namespace V12.Components.Renderables
{
    public class BoxMesh : ComponentBase, IMeshRenderable
    {
        // Box dimensions (pure geometry — placement lives on the element).
        private Vector3 _size = Vector3.One;
        // Cached mesh data
        private double[] _meshPoints;
        private uint[] _indices;
        private Material _material;

        public RenderType RenderType => RenderType.Mesh;

        public double[] MeshPoints => _meshPoints ??= GenerateBoxVertices();

        public uint[] Indices => _indices ??= GenerateBoxIndices();

        public Material Material => _material ??= new Material();
        public BoxMesh() { }
        public BoxMesh(string name)
        {
            Name = name;
        }
        public BoxMesh(Vector3 size, string name, double[] meshPoints, uint[] indices, Material material)
        {
            Size = size;
            Name = name;
            _meshPoints = meshPoints;
            _indices = indices;
            _material = material;
        }

        public bool IsWorldLocked => true;

        public Vector3 Size
        {
            get => _size;
            set
            {
                _size = value;
                _meshPoints = null; // Invalidate cache
            }
        }

        public TRS LocalTransform => throw new NotImplementedException();

        public Matrix4x4 WorldTransform => Owner?.WorldTransform ?? Matrix4x4.Identity;

        private double[] GenerateBoxVertices()

        {
            var half = _size * 0.5f;
            var vertices = new Vector3[]
            {
                // Front face
                new(-half.X, -half.Y, half.Z),
                new(half.X, -half.Y, half.Z),
                new(half.X, half.Y, half.Z),
                new(-half.X, half.Y, half.Z),

                // Back face
                new(-half.X, -half.Y, -half.Z),
                new(-half.X, half.Y, -half.Z),
                new(half.X, half.Y, -half.Z),
                new(half.X, -half.Y, -half.Z),

                // Top face
                new(-half.X, half.Y, -half.Z),
                new(-half.X, half.Y, half.Z),
                new(half.X, half.Y, half.Z),
                new(half.X, half.Y, -half.Z),

                // Bottom face
                new(-half.X, -half.Y, -half.Z),
                new(half.X, -half.Y, -half.Z),
                new(half.X, -half.Y, half.Z),
                new(-half.X, -half.Y, half.Z),

                // Right face
                new(half.X, -half.Y, -half.Z),
                new(half.X, half.Y, -half.Z),
                new(half.X, half.Y, half.Z),
                new(half.X, -half.Y, half.Z),

                // Left face
                new(-half.X, -half.Y, -half.Z),
                new(-half.X, -half.Y, half.Z),
                new(-half.X, half.Y, half.Z),
                new(-half.X, half.Y, -half.Z),
            };

            var result = new double[vertices.Length * 3];
            for (int i = 0; i < vertices.Length; i++)
            {
                result[i * 3] = vertices[i].X;
                result[i * 3 + 1] = vertices[i].Y;
                result[i * 3 + 2] = vertices[i].Z;
            }
            return result;
        }

        private uint[] GenerateBoxIndices()
        {
            return new uint[]
            {
                // Front
                0, 1, 2, 0, 2, 3,
                // Back
                4, 5, 6, 4, 6, 7,
                // Top
                8, 9, 10, 8, 10, 11,
                // Bottom
                12, 13, 14, 12, 14, 15,
                // Right
                16, 17, 18, 16, 18, 19,
                // Left
                20, 21, 22, 20, 22, 23,
            };
        }

        public override IWorldElement BuildUI()
        {
            throw new NotImplementedException();
        }
    }
}