using Assimp;
using System;
using System.Numerics;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

namespace V12.Components.Renderables
{
    public class ConeMesh : ComponentBase, IMeshRenderable
    {
        private float _radius = 0.5f;
        private float _height = 1f;
        private int _slices = 24;
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

        public ConeMesh() { }
        public ConeMesh(string name) { Name = name; }

        public float Radius { get => _radius; set { _radius = Math.Max(1e-4f, value); _meshPoints = null; _indices = null; } }
        public float Height { get => _height; set { _height = Math.Max(1e-4f, value); _meshPoints = null; _indices = null; } }
        public int Slices { get => _slices; set { _slices = Math.Max(3, value); _meshPoints = null; _indices = null; } }

        private double[] GenerateVertices()
        {
            var h = _height * 0.5f;
            var r = _radius;
            var pts = new System.Collections.Generic.List<double>(_slices * 3 + 1 + _slices + 1);
            pts.Add(0); pts.Add(h); pts.Add(0);
            for (int i = 0; i < _slices; i++) { double a = 2*Math.PI*i/_slices; pts.Add(Math.Cos(a)*r); pts.Add(-h); pts.Add(Math.Sin(a)*r); }
            pts.Add(0); pts.Add(-h); pts.Add(0);
            for (int i = 0; i < _slices; i++) { double a = 2*Math.PI*i/_slices; pts.Add(Math.Cos(a)*r); pts.Add(-h); pts.Add(Math.Sin(a)*r); }
            return pts.ToArray();
        }

        private uint[] GenerateIndices()
        {
            var s = (uint)_slices;
            var idx = new System.Collections.Generic.List<uint>(_slices * 6);
            uint tip = 0;
            for (uint i = 0; i < s; i++) { idx.Add(tip); idx.Add(1 + (i+1)%s); idx.Add(1 + i); }
            uint basec = 1 + s;
            for (uint i = 0; i < s; i++) { idx.Add(basec); idx.Add(basec+1 + i); idx.Add(basec+1 + (i+1)%s); }
            return idx.ToArray();
        }

        public override IWorldElement BuildUI() => throw new NotImplementedException();
    }
}
