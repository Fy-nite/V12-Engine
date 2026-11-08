using System;
using System.Collections.Generic;
using Prowl.Quill;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using V12.Rendering;

namespace V12.Core.UI.Paper
{
    /// <summary>
    /// Engine-level <see cref="ICanvasRenderer"/>. It captures the canvas
    /// vertex/index buffers and draw-call state into a <see cref="PaperFrame"/>
    /// and keeps a CPU copy of every image/font texture so a backend can upload
    /// ordinary textured meshes without touching the toolkit's own types.
    /// </summary>
    public class PaperCanvasRenderer : ICanvasRenderer, IDisposable
    {
        public bool SupportsBackdropBlur => false;

        private int _w, _h;
        private List<PaperMesh> _pendingMeshes = new();
        public PaperFrame LastFrame { get; private set; } = new();

        /// <summary>Starts accumulation for one Paper frame. Call before Paper.BeginFrame.</summary>
        public virtual void BeginFrame()
        {
            _pendingMeshes = new List<PaperMesh>();
            LastFrame = new PaperFrame
            {
                Meshes = _pendingMeshes,
                Width = _w,
                Height = _h,
                CaptureTicks = Environment.TickCount64,
            };
        }

        public virtual object CreateTexture(uint width, uint height) =>
            new PaperTexture(Math.Max(1, (int)width), Math.Max(1, (int)height));

        public virtual Int2 GetTextureSize(object texture) =>
            texture is PaperTexture t ? new Int2(t.Width, t.Height) : new Int2(1, 1);

        public virtual void SetTextureData(object texture, IntRect bounds, byte[] data)
        {
            if (texture is PaperTexture paperTexture)
                paperTexture.SetData(bounds, data);
        }

        public virtual TextureData? GetTextureData(object? texture) =>
            texture is PaperTexture t ? t.ToTextureData() : null;

        public virtual long GetTextureVersion(object? texture) =>
            texture is PaperTexture t ? t.Version : 0;

        public virtual void Dispose() { }

        public virtual void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls)
        {
            try
            {
                if (drawCalls == null || drawCalls.Count == 0)
                    return;

                var verts = canvas.VertexBuffer;
                var idx = canvas.IndexBuffer;
                int indexOffset = 0;

                System.Numerics.Vector2[] allPos = Array.Empty<System.Numerics.Vector2>();
                System.Numerics.Vector2[] allUv = Array.Empty<System.Numerics.Vector2>();
                Prowl.Vector.Color[] allCol = Array.Empty<Prowl.Vector.Color>();
                if (verts != null && verts.Length > 0)
                {
                    allPos = new System.Numerics.Vector2[verts.Length];
                    allUv = new System.Numerics.Vector2[verts.Length];
                    allCol = new Prowl.Vector.Color[verts.Length];
                    for (int i = 0; i < verts.Length; i++)
                    {
                        allPos[i] = new System.Numerics.Vector2(verts[i].Position.X, verts[i].Position.Y);
                        allUv[i] = new System.Numerics.Vector2(verts[i].UV.X, verts[i].UV.Y);
                        allCol[i] = new Prowl.Vector.Color(
                            verts[i].r / 255f,
                            verts[i].g / 255f,
                            verts[i].b / 255f,
                            verts[i].a / 255f);
                    }
                }

                float fbScale = canvas.FramebufferScale <= 0f ? 1f : canvas.FramebufferScale;
                float sdfRange = 4f;
                object? engineAtlas = null;
                try
                {
                    var engine = canvas.Text?.FontEngine;
                    if (engine != null)
                    {
                        sdfRange = engine.DistanceRange;
                        engineAtlas = engine.Texture;
                    }
                }
                catch { }

                foreach (var dc in drawCalls)
                {
                    int count = dc.ElementCount;
                    uint[] slice = Array.Empty<uint>();
                    if (idx != null && count > 0 && indexOffset + count <= idx.Length)
                    {
                        slice = new uint[count];
                        Array.Copy(idx, indexOffset, slice, 0, count);
                    }
                    else if (idx != null && count > 0)
                    {
                        int available = Math.Max(0, idx.Length - indexOffset);
                        slice = new uint[available];
                        if (available > 0)
                            Array.Copy(idx, indexOffset, slice, 0, available);
                    }

                    dc.GetScissor(fbScale, out var sT, out var sTr, out var sExt);
                    dc.GetBrushTransform(fbScale, out var bT, out var bTr);
                    dc.GetTextureTransform(fbScale, out var tT, out var tTr);

                    object? atlasObject = dc.FontAtlas ?? engineAtlas;
                    var atlasTexture = atlasObject as PaperTexture;

                    _pendingMeshes.Add(new PaperMesh
                    {
                        Indices = slice,
                        Positions = allPos,
                        UVs = allUv,
                        Colors = allCol,
                        VertexCount = verts?.Length ?? 0,
                        IndexOffset = indexOffset,
                        ElementCount = count,
                        Texture = dc.Texture,
                        FontAtlas = atlasObject,
                        TextureData = GetTextureData(dc.Texture),
                        FontAtlasData = GetTextureData(atlasObject),
                        TextureVersion = GetTextureVersion(dc.Texture),
                        FontAtlasVersion = GetTextureVersion(atlasObject),
                        TextureId = dc.Texture != null ? dc.Texture.GetHashCode().ToString() : "",
                        FontAtlasId = atlasObject != null ? atlasObject.GetHashCode().ToString() : "",
                        Brush = dc.Brush,
                        IsText = atlasObject != null,
                        ScissorTransform = new System.Numerics.Vector4(sT.X, sT.Y, sT.Z, sT.W),
                        ScissorTranslation = new System.Numerics.Vector2(sTr.X, sTr.Y),
                        ScissorExt = new System.Numerics.Vector2(sExt.X, sExt.Y),
                        BrushTransform = new System.Numerics.Vector4(bT.X, bT.Y, bT.Z, bT.W),
                        BrushTranslation = new System.Numerics.Vector2(bTr.X, bTr.Y),
                        TextureTransform = new System.Numerics.Vector4(tT.X, tT.Y, tT.Z, tT.W),
                        TextureTranslation = new System.Numerics.Vector2(tTr.X, tTr.Y),
                        SdfPxRange = sdfRange,
                        AtlasTexelSize = atlasTexture != null
                            ? new System.Numerics.Vector2(
                                1f / Math.Max(1, atlasTexture.Width),
                                1f / Math.Max(1, atlasTexture.Height))
                            : new System.Numerics.Vector2(1f / 1024f, 1f / 1024f),
                    });

                    indexOffset += count;
                }

                LastFrame = new PaperFrame
                {
                    Meshes = _pendingMeshes,
                    Width = _w,
                    Height = _h,
                    CaptureTicks = Environment.TickCount64,
                };
            }
            catch (Exception ex)
            {
                V12.Core.GameRoot.Log?.Warning(ex, "[PaperCanvasRenderer] RenderCalls capture failed");
            }
        }

        public void SetViewport(int w, int h)
        {
            _w = w;
            _h = h;
        }

        public sealed class PaperTexture
        {
            public int Width { get; }
            public int Height { get; }
            public byte[] Pixels { get; }
            public long Version { get; private set; }

            private TextureData? _cached;
            private long _cachedVersion = -1;

            public PaperTexture(int width, int height)
            {
                Width = Math.Max(1, width);
                Height = Math.Max(1, height);
                Pixels = new byte[Width * Height * 4];
            }

            public void SetData(IntRect bounds, byte[] data)
            {
                if (data == null || data.Length == 0)
                    return;

                if (data.Length == Width * Height * 4)
                {
                    Array.Copy(data, Pixels, Pixels.Length);
                    Touch();
                    return;
                }

                int x = bounds.Min.X;
                int y = bounds.Min.Y;
                int w = Math.Max(0, bounds.Max.X - x);
                int h = Math.Max(0, bounds.Max.Y - y);

                if (w <= 0 || h <= 0 || data.Length < w * h * 4)
                    return;

                x = Math.Clamp(x, 0, Width);
                y = Math.Clamp(y, 0, Height);
                w = Math.Min(w, Width - x);
                h = Math.Min(h, Height - y);
                int rowBytes = w * 4;

                for (int row = 0; row < h; row++)
                {
                    int src = row * rowBytes;
                    int dst = ((y + row) * Width + x) * 4;
                    Array.Copy(data, src, Pixels, dst, rowBytes);
                }

                Touch();
            }

            public TextureData ToTextureData()
            {
                if (_cached != null && _cachedVersion == Version)
                    return _cached;

                int rowPitch = Width * 4;
                var subresource = new TextureSubresource(0, 0, rowPitch, rowPitch * Height, Pixels);
                _cached = new TextureData(
                    TextureDimension.Texture2D,
                    Width,
                    Height,
                    1,
                    1,
                    TextureFormat.Rgba8Unorm,
                    TextureColorSpace.Linear,
                    TextureUsage.Sampled,
                    new[] { subresource });
                _cachedVersion = Version;
                return _cached;
            }

            private void Touch()
            {
                Version++;
                _cached = null;
            }
        }
    }
}
