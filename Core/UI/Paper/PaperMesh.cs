using System.Collections.Generic;
using System.Numerics;

namespace V12.Core.UI.Paper
{
    /// <summary>
    /// One draw batch produced by Paper/Quill for the Godot mesh bridge.
    /// Mirrors OpenTK PaperRenderer's vertex/index upload: Paper builds a Quill
    /// Canvas (vertices + indices) and splits it into DrawCalls; each call
    /// becomes one PaperMesh the Godot host can upload as an ArrayMesh.
    /// </summary>
    public sealed class PaperMesh
    {
        public Vector2[] Positions = System.Array.Empty<Vector2>();
        public Vector2[] UVs = System.Array.Empty<Vector2>();
        public Prowl.Vector.Color[] Colors = System.Array.Empty<Prowl.Vector.Color>();
        public uint[] Indices = System.Array.Empty<uint>();
        public Vector4 Color = new(1,1,1,1);
        public string TextureId = "";
        public string FontAtlasId = "";
        public Prowl.Quill.Brush Brush;
        public bool IsText;
        public int VertexCount;
        public int IndexOffset;
        public int ElementCount;
    }

    public sealed class PaperFrame
    {
        public List<PaperMesh> Meshes = new();
        public int Width;
        public int Height;
        public long CaptureTicks;
    }
}
