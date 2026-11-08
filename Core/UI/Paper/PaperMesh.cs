using System.Collections.Generic;
using System.Numerics;
using V12.Rendering;

namespace V12.Core.UI.Paper
{
    /// <summary>
    /// One draw batch produced by the UI canvas. It contains both the CPU mesh
    /// and the resolved brush/scissor/texture state needed by an ordinary UI
    /// material. UI backends should not need to inspect toolkit types directly.
    /// </summary>
    public sealed class PaperMesh
    {
        public Vector2[] Positions = System.Array.Empty<Vector2>();
        public Vector2[] UVs = System.Array.Empty<Vector2>();
        public Prowl.Vector.Color[] Colors = System.Array.Empty<Prowl.Vector.Color>();
        public uint[] Indices = System.Array.Empty<uint>();
        public Vector4 Color = new(1, 1, 1, 1);

        public object? Texture;
        public object? FontAtlas;
        public TextureData? TextureData;
        public TextureData? FontAtlasData;
        public long TextureVersion;
        public long FontAtlasVersion;

        public string TextureId = "";
        public string FontAtlasId = "";
        public Prowl.Quill.Brush Brush;
        public bool IsText;

        public int VertexCount;
        public int IndexOffset;
        public int ElementCount;

        // Canvas-space uniform state. Values are already scaled by the canvas
        // framebuffer scale, matching the draw-call transform contract.
        public Vector4 ScissorTransform = new(1, 0, 0, 1);
        public Vector2 ScissorTranslation;
        public Vector2 ScissorExt = new(-1, -1);

        public Vector4 BrushTransform = new(1, 0, 0, 1);
        public Vector2 BrushTranslation;

        public Vector4 TextureTransform = new(1, 0, 0, 1);
        public Vector2 TextureTranslation;

        public float SdfPxRange = 4f;
        public Vector2 AtlasTexelSize = new(1f / 1024f, 1f / 1024f);
    }

    public sealed class PaperViewportInfo
    {
        public long Id;
        public System.Numerics.Vector4 Rect;
        public object? Texture;
    }

    public sealed class PaperFrame
    {
        public List<PaperMesh> Meshes = new();
        public List<PaperViewportInfo> Viewports = new();
        public int Width;
        public int Height;
        public long CaptureTicks;
        public string ShapeShaderUri = "shader://v12.paper/paper";
        public string TextShaderUri = "shader://v12.paper/text";
        // Legacy alias for renderer code that used FramebufferScale
        public float FramebufferScale => 1f;
    }
}
