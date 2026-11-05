using System;
using System.Collections.Generic;
using V12.Core.Rendering;

namespace V12.Core.UI.Paper
{
    /// <summary>
    /// Builds Paper UI for world-space canvases (<c>CanvasComponent.ScreenSpace == false</c>).
    /// Each canvas owns an independent Paper surface sized in content pixels, so hosts can
    /// bake its <see cref="PaperFrame"/> to a texture and draw it on a world quad. Nested
    /// canvases are not folded in; they are expected to be handled by their own host path.
    /// </summary>
    public sealed class PaperWorldCanvasSystem : IDisposable
    {
        private readonly Func<long, int, int, PaperCanvasRenderer> _rendererFactory;
        private readonly Dictionary<long, Surface> _surfaces = new();

        public PaperWorldCanvasSystem(Func<long, int, int, PaperCanvasRenderer> rendererFactory)
        {
            _rendererFactory = rendererFactory ?? throw new ArgumentNullException(nameof(rendererFactory));
        }

        public IReadOnlyDictionary<long, Surface> Surfaces => _surfaces;

        public sealed class Surface : IDisposable
        {
            public long Id;
            public int PixelWidth;
            public int PixelHeight;
            public ProwlPaperRenderer Paper = null!;
            public PaperCanvasRenderer Renderer = null!;
            public UIFrame Frame = new();

            /// <summary>Host-owned baked texture (MonoGame render target, Godot viewport texture...).</summary>
            public object? Texture;

            public PaperFrame? LastFrame => Paper.LastPaperFrame;

            /// <summary>Feed the canvas-local pointer (pixels) before the next Paper step.</summary>
            public void FeedPointer(float x, float y, bool down)
            {
                var paper = Paper.PaperInstance;
                paper.SetPointerState(global::Prowl.PaperUI.PaperMouseBtn.Unknown, x, y, false, true);
                paper.SetPointerState(global::Prowl.PaperUI.PaperMouseBtn.Left, x, y, down, false);
            }

            public void Dispose()
            {
                try { Renderer.Dispose(); } catch { }
            }
        }

        public void Update(UIFrame? frame, Func<UINode, (int Width, int Height)>? sizeSelector)
        {
            if (frame == null) return;

            var byParent = new Dictionary<long, List<UINode>>();
            foreach (var node in frame.Nodes)
            {
                if (!byParent.TryGetValue(node.ParentId, out var list))
                    byParent[node.ParentId] = list = new List<UINode>();
                list.Add(node);
            }

            var live = new HashSet<long>();
            if (byParent.TryGetValue(0, out var roots))
            {
                foreach (var root in roots)
                {
                    if (root.Kind != UIWidgetKind.Canvas || root.ScreenSpace) continue;
                    live.Add(root.Id);

                    var (width, height) = ResolveSize(root, sizeSelector);
                    if (!_surfaces.TryGetValue(root.Id, out var surface) ||
                        surface.PixelWidth != width || surface.PixelHeight != height)
                    {
                        surface?.Dispose();
                        var renderer = _rendererFactory(root.Id, width, height);
                        renderer.SetViewport(width, height);
                        surface = new Surface
                        {
                            Id = root.Id,
                            PixelWidth = width,
                            PixelHeight = height,
                            Renderer = renderer,
                            Paper = new ProwlPaperRenderer(renderer, width, height, 1f),
                        };
                        _surfaces[root.Id] = surface;
                    }

                    surface.Frame = BuildSubFrame(root, byParent);
                    surface.Paper.ApplyUI(surface.Frame);
                    surface.Paper.step();
                }
            }

            List<long>? stale = null;
            foreach (var id in _surfaces.Keys)
                if (!live.Contains(id)) (stale ??= new List<long>()).Add(id);
            if (stale != null)
            {
                foreach (var id in stale)
                {
                    _surfaces[id].Dispose();
                    _surfaces.Remove(id);
                }
            }
        }

        private static (int Width, int Height) ResolveSize(
            UINode root, Func<UINode, (int Width, int Height)>? sizeSelector)
        {
            int width = 0, height = 0;
            if (sizeSelector != null)
            {
                try { (width, height) = sizeSelector(root); } catch { }
            }
            if (width <= 0 || height <= 0)
            {
                width = root.Width > 0f ? (int)MathF.Round(root.Width) : 512;
                height = root.Height > 0f ? (int)MathF.Round(root.Height) : 288;
            }
            return (Math.Clamp(width, 64, 4096), Math.Clamp(height, 64, 4096));
        }

        private static UIFrame BuildSubFrame(
            UINode root, Dictionary<long, List<UINode>> byParent)
        {
            var source = new UIFrame
            {
                Nodes =
                {
                    new UINode
                    {
                        Id = root.Id,
                        ParentId = 0,
                        Name = root.Name,
                        Kind = UIWidgetKind.Canvas,
                        ScreenSpace = true,
                        AnchorX = 0f,
                        AnchorY = 0f,
                    },
                },
            };

            var seen = new HashSet<long> { root.Id };
            var stack = new Stack<long>();
            stack.Push(root.Id);
            while (stack.Count > 0)
            {
                long parent = stack.Pop();
                if (!byParent.TryGetValue(parent, out var children)) continue;
                foreach (var child in children)
                {
                    if (child.Kind == UIWidgetKind.Canvas) continue;
                    if (!seen.Add(child.Id)) continue;
                    source.Nodes.Add(child);
                    stack.Push(child.Id);
                }
            }

            return source;
        }

        public void Dispose()
        {
            foreach (var surface in _surfaces.Values) surface.Dispose();
            _surfaces.Clear();
        }
    }
}
