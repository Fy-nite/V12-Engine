using Assimp;
using System;
using System.Numerics;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

namespace V12.Components.Renderables
{
    public class CylinderMesh : ComponentBase, IMeshRenderable
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

        public CylinderMesh() { }
        public CylinderMesh(string name) { Name = name; }

        public float Radius { get => _radius; set { _radius = Math.Max(1e-4f, value); _meshPoints = null; _indices = null; } }
        public float Height { get => _height; set { _height = Math.Max(1e-4f, value); _meshPoints = null; _indices = null; } }
        public int Slices { get => _slices; set { _slices = Math.Max(3, value); _meshPoints = null; _indices = null; } }

        private double[] GenerateVertices()
        {
            var h = _height * 0.5f;
            var r = _radius;
            var pts = new System.Collections.Generic.List<double>(_slices * 4 * 3);
            for (int i = 0; i < _slices; i++)
            {
                double a0 = 2 * Math.PI * i / _slices;
                double a1 = 2 * Math.PI * ((i + 1) % _slices) / _slices;
                double x0 = Math.Cos(a0) * r, z0 = Math.Sin(a0) * r;
                double x1 = Math.Cos(a1) * r, z1 = Math.Sin(a1) * r;
                pts.Add(x0); pts.Add(-h); pts.Add(z0);
                pts.Add(x1); pts.Add(-h); pts.Add(z1);
                pts.Add(x1); pts.Add(h);  pts.Add(z1);
                pts.Add(x0); pts.Add(h);  pts.Add(z0);
            }
            pts.Add(0); pts.Add(-h); pts.Add(0);
            for (int i = 0; i < _slices; i++) { double a = 2*Math.PI*i/_slices; pts.Add(Math.Cos(a)*r); pts.Add(-h); pts.Add(Math.Sin(a)*r); }
            pts.Add(0); pts.Add(h); pts.Add(0);
            for (int i = 0; i < _slices; i++) { double a = 2*Math.PI*i/_slices; pts.Add(Math.Cos(a)*r); pts.Add(h); pts.Add(Math.Sin(a)*r); }
            var arr = pts.ToArray();
            return arr;
        }

        private uint[] GenerateIndices()
        {
            var s = (uint)_slices;
            var idx = new System.Collections.Generic.List<uint>(_slices * 12);
            // Winding is CCW-outward (Godot backface-culls; MonoGame is CullNone).
            uint baseSide = 0;
            for (uint i = 0; i < s; i++) { idx.Add(baseSide+i*4); idx.Add(baseSide+i*4+2); idx.Add(baseSide+i*4+1); idx.Add(baseSide+i*4); idx.Add(baseSide+i*4+3); idx.Add(baseSide+i*4+2); }
            uint c0 = (uint)(_slices * 4);
            for (uint i = 0; i < s; i++) { idx.Add(c0); idx.Add(c0+1 + i); idx.Add(c0+1 + (i+1)%s); }
            uint c1 = c0 + 1 + (uint)_slices;
            for (uint i = 0; i < s; i++) { idx.Add(c1); idx.Add(c1+1 + (i+1)%s); idx.Add(c1+1 + i); }
            return idx.ToArray();
        }

        public override IWorldElement BuildUI() => throw new NotImplementedException();
    }
}
