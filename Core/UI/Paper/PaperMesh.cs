using System.Collections.Generic;
using System.Numerics;
using V12.Rendering;

namespace V12.Core.UI.Paper
{
    /// <summary>
    /// Brush state for one UI draw batch, in neutral primitives. The Paper
    /// implementation fills it; the engine never needs a toolkit type.
    /// </summary>
    public struct UiBrush
    {
        public int Type;
        public Vector4 Color1;
        public Vector4 Color2;
        public Vector2 Point1;
        public Vector2 Point2;
        public float CornerRadii;
        public float Feather;
    }

    /// <summary>
    /// One draw batch produced by the UI canvas. It contains both the CPU mesh
    /// and the resolved brush/scissor/texture state needed by an ordinary UI
    /// material. UI backends should not need to inspect toolkit types directly.
    /// </summary>
    public sealed class PaperMesh
    {
        public Vector2[] Positions = System.Array.Empty<Vector2>();
        public Vector2[] UVs = System.Array.Empty<Vector2>();
        public Vector4[] Colors = System.Array.Empty<Vector4>();
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
        public UiBrush Brush;
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

    /// <summary>
    /// Marks a Paper texture as a render-target reference instead of a CPU
    /// image. Backends resolve the view id to a GPU texture and bind it
    /// directly, avoiding a readback through the UI canvas.
    /// </summary>
    public sealed class PaperTextureRef
    {
        public long ViewId { get; }

        public PaperTextureRef(long viewId)
        {
            ViewId = viewId;
        }

        public override int GetHashCode() => ViewId.GetHashCode();

        public override bool Equals(object? obj)
            => obj is PaperTextureRef other && other.ViewId == ViewId;
    }

    public sealed class PaperViewportInfo
    {
        /// <summary>UI surface element id.</summary>
        public long Id;

        /// <summary>Render view id displayed by the surface.</summary>
        public long ViewId;

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
