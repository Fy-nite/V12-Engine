using System;
using System.Collections.Generic;
using Prowl.Quill;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace V12.Core.UI.Paper
{
    /// <summary>
    /// Engine-level <see cref="ICanvasRenderer"/> for Paper/Quill.
    /// Implements the same contract as <c>Paper/Samples/OpenTK/PaperRenderer.cs</c>
    /// but instead of issuing OpenGL calls it captures the Quill vertex/index
    /// buffers into <see cref="PaperFrame"/> so a Godot host (Nova.Shared) can
    /// upload them as meshes. See that sample for the full GL implementation;
    /// this is the headless/engine side - GodotPaperCanvasRenderer in Nova.Shared
    /// does the actual Godot upload when available.
    ///
    /// When running headless (no Godot), RenderCalls just caches the draw data
    /// for inspection / tests.
    /// </summary>
    public class PaperCanvasRenderer : ICanvasRenderer, IDisposable
    {
        public bool SupportsBackdropBlur => false;

        private int _w, _h;
        public PaperFrame LastFrame { get; private set; } = new();

        public virtual object CreateTexture(uint width, uint height) => new TextureStub(width, height);
        public virtual Int2 GetTextureSize(object texture) => texture is TextureStub t ? new Int2((int)t.W, (int)t.H) : new Int2(1,1);
        public virtual void SetTextureData(object texture, IntRect bounds, byte[] data) { /* headless: no-op */ }
        public virtual void Dispose() { }

        public virtual void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls)
        {
            try
            {
                if (drawCalls == null || drawCalls.Count == 0)
                {
                    LastFrame = new PaperFrame { Meshes = new List<PaperMesh>(), Width = _w, Height = _h, CaptureTicks = Environment.TickCount64 };
                    return;
                }
                var verts = canvas.VertexBuffer;
                var idx = canvas.IndexBuffer;
                var meshes = new List<PaperMesh>(drawCalls.Count);
                int indexOffset = 0;
                // Quill vertex layout: x,y,u,v,r,g,b,a (20 bytes) - map to PaperMesh for Godot
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
                        allCol[i] = new Prowl.Vector.Color(verts[i].r / 255f, verts[i].g / 255f, verts[i].b / 255f, verts[i].a / 255f);
                    }
                }
                foreach (var dc in drawCalls)
                {
                    int count = dc.ElementCount;
                    // Slice indices for this draw call - indices are global, but ElementCount is number of indices for this call
                    uint[] slice = Array.Empty<uint>();
                    if (idx != null && count > 0 && indexOffset + count <= idx.Length)
                    {
                        slice = new uint[count];
                        Array.Copy(idx, indexOffset, slice, 0, count);
                    }
                    else if (idx != null && count > 0)
                    {
                        // Fallback: clamp to available
                        int avail = Math.Max(0, idx.Length - indexOffset);
                        slice = new uint[avail];
                        if (avail > 0) Array.Copy(idx, indexOffset, slice, 0, avail);
                    }
                    meshes.Add(new PaperMesh
                    {
                        Indices = slice,
                        Positions = allPos,
                        UVs = allUv,
                        Colors = allCol,
                        VertexCount = verts?.Length ?? 0,
                        IndexOffset = indexOffset,
                        ElementCount = count,
                        TextureId = dc.Texture != null ? dc.Texture.GetHashCode().ToString() : "",
                        FontAtlasId = dc.FontAtlas != null ? dc.FontAtlas.GetHashCode().ToString() : "",
                        Brush = dc.Brush,
                        IsText = dc.FontAtlas != null,
                    });
                    indexOffset += count;
                }
                LastFrame = new PaperFrame { Meshes = meshes, Width = _w, Height = _h, CaptureTicks = Environment.TickCount64 };
            }
            catch (Exception ex)
            {
                V12.Core.GameRoot.Log?.Warning(ex, "[PaperCanvasRenderer] RenderCalls capture failed");
            }
        }

        public void SetViewport(int w, int h) { _w = w; _h = h; }

        private sealed class TextureStub
        {
            public uint W, H;
            public TextureStub(uint w, uint h) { W = w; H = h; }
        }
    }
}
