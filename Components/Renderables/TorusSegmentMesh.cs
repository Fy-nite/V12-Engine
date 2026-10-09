using Assimp;
using System;
using System.Numerics;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

namespace V12.Components.Renderables
{
    /// <summary>Procedural torus (ring) mesh, built with the ring in the XZ
    /// plane and its axis along +Y — the same convention as the cylinder and
    /// cone primitives, so an element rotation mapping +Y onto the handle
    /// axis orients it. Pure geometry: dimensions live in the vertices,
    /// placement lives only on the element.</summary>
    public class TorusSegmentMesh : ComponentBase, IMeshRenderable
    {
        private float _ringRadius = 0.5f;
        private float _tubeRadius = 0.1f;
        private float _arcRadians = MathF.PI * 2f;
        private int _radialSegments = 10;
        private int _tubularSegments = 32;
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

        public TorusSegmentMesh() { }
        public TorusSegmentMesh(string name) { Name = name; }

        /// <summary>Centerline radius of the ring.</summary>
        public float RingRadius { get => _ringRadius; set { _ringRadius = Math.Max(1e-4f, value); InvalidateMesh(); } }
        /// <summary>Radius of the tube around the centerline.</summary>
        public float TubeRadius { get => _tubeRadius; set { _tubeRadius = Math.Max(1e-4f, value); InvalidateMesh(); } }
        /// <summary>Sweep of the arc in radians (2π = closed ring). Partial
        /// arcs leave the tube ends open.</summary>
        public float ArcRadians { get => _arcRadians; set { _arcRadians = Math.Clamp(value, 0.05f, MathF.PI * 2f); InvalidateMesh(); } }
        public int RadialSegments { get => _radialSegments; set { _radialSegments = Math.Max(3, value); InvalidateMesh(); } }
        public int TubularSegments { get => _tubularSegments; set { _tubularSegments = Math.Max(3, value); InvalidateMesh(); } }

        private void InvalidateMesh()
        {
            _meshPoints = null;
            _indices = null;
        }

        private double[] GenerateVertices()
        {
            int tub = _tubularSegments;
            int rad = _radialSegments;
            bool closed = _arcRadians >= MathF.PI * 2f - 1e-4f;
            int tubCount = closed ? tub : tub + 1;
            var pts = new System.Collections.Generic.List<double>(tubCount * (rad + 1) * 3);
            for (int i = 0; i < tubCount; i++)
            {
                double u = (double)i / tub * _arcRadians;
                double cu = Math.Cos(u), su = Math.Sin(u);
                for (int j = 0; j <= rad; j++)
                {
                    double v = 2 * Math.PI * j / rad;
                    double cv = Math.Cos(v), sv = Math.Sin(v);
                    double x = (_ringRadius + _tubeRadius * cv) * cu;
                    double y = _tubeRadius * sv;
                    double z = (_ringRadius + _tubeRadius * cv) * su;
                    pts.Add(x); pts.Add(y); pts.Add(z);
                }
            }
            return pts.ToArray();
        }

        private uint[] GenerateIndices()
        {
            int tub = _tubularSegments;
            int rad = _radialSegments;
            bool closed = _arcRadians >= MathF.PI * 2f - 1e-4f;
            uint stride = (uint)(rad + 1);
            var idx = new System.Collections.Generic.List<uint>(tub * rad * 6);
            // Winding is CCW-outward (Godot backface-culls; MonoGame is CullNone).
            for (int i = 0; i < tub; i++)
            {
                uint r0 = (uint)i * stride;
                uint r1 = closed ? (uint)((i + 1) % tub) * stride : (uint)(i + 1) * stride;
                for (uint j = 0; j < (uint)rad; j++)
                {
                    idx.Add(r0 + j); idx.Add(r1 + j + 1); idx.Add(r1 + j);
                    idx.Add(r0 + j); idx.Add(r0 + j + 1); idx.Add(r1 + j + 1);
                }
            }
            return idx.ToArray();
        }

        public override IWorldElement BuildUI() => throw new NotImplementedException();
    }
}
