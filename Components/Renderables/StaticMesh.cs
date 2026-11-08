using Assimp;
using System;
using System.Numerics;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

namespace V12.Components.Renderables
{
    /// <summary>
    /// A plain mesh built from in-memory arrays rather than a file. This is what
    /// <see cref="ModelInstance"/> puts on each node of a reconstructed model:
    /// the geometry is one part of the source file, already flattened to raw
    /// vertex streams, and the surface comes from a sibling
    /// <c>MaterialComponent</c>.
    /// </summary>
    [ComponentEntry("RENDER", "Static Mesh", SingleInstance = true, SortOrder = 5, Icon = "mesh")]
    public class StaticMesh : ComponentBase, IMeshRenderable
    {
        public double[] MeshPoints { get; set; } = Array.Empty<double>();
        public double[]? MeshUvs { get; set; }
        public double[]? MeshNormals { get; set; }
        public double[]? MeshTangents { get; set; }
        public uint[] Indices { get; set; } = Array.Empty<uint>();

        public RenderType RenderType => RenderType.Mesh;

        public Material Material { get; set; } = new Material();

        public bool IsWorldLocked => true;

        /// <summary>Meshes carry no transform of their own: the node's placement
        /// lives on the element this component is attached to.</summary>
        public TRS LocalTransform => default;

        public Matrix4x4 WorldTransform => Owner?.WorldTransform ?? Matrix4x4.Identity;

        public StaticMesh() { }

        public StaticMesh(double[] points, uint[] indices)
        {
            MeshPoints = points;
            Indices = indices;
        }

        /// <summary>Read-only geometry summary. The raw arrays are deliberately
        /// not exposed as editable rows; a rebuilt model part is derived data.</summary>
        public override void BuildInspector(V12.Core.UI.IInspector inspector)
        {
            inspector.Section("Geometry");
            inspector.ReadOnly("Vertices", MeshPoints.Length / 3);
            inspector.ReadOnly("Triangles", Indices.Length / 3);
            inspector.ReadOnly("UVs", MeshUvs is null ? "none (projected)" : "UV0");
            inspector.ReadOnly("Normals", MeshNormals is null ? "derived from winding" : "authored");
            inspector.ReadOnly("Tangents", MeshTangents is null ? "none" : "authored");
        }

        public override string ToString() =>
            $"StaticMesh({MeshPoints.Length / 3} verts, {Indices.Length / 3} tris)";
    }
}
