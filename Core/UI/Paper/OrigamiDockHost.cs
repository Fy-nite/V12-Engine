using System;
using System.Collections.Generic;
using System.Linq;
using POrigami = global::Prowl.OrigamiUI;

namespace V12.Core.UI.Paper
{
    /// <summary>
    /// Bridges a <see cref="V12.Core.Rendering.UINode"/> subtree onto Origami's real
    /// <c>DockSpace</c> / <c>DockNode</c> / <c>DockPanel</c> tree.
    ///
    /// Engine elements own the dock structure (a <c>DockSpaceComponent</c> with
    /// <c>DockPanelComponent</c> children), and this host builds the Origami tree once per
    /// structural change, then lets Origami draw and manage it: drag splitters, reorder and
    /// tear out tabs, floating windows, tab close buttons.
    ///
    /// Each <see cref="OrigamiDockPanel"/> renders the owning element's children into its body
    /// through the renderer's normal node→Paper mapping, so panel content is ordinary V12 UI
    /// (labels, buttons, code editors, viewports...).
    /// </summary>
    internal sealed class OrigamiDockHost
    {
        private readonly ProwlPaperRenderer _renderer;
        private readonly long _elementId;
        private string _signature = "";

        private POrigami.DockSpace? _space;
        private readonly Dictionary<long, OrigamiDockPanel> _panels = new();

        public OrigamiDockHost(ProwlPaperRenderer renderer, long elementId)
        {
            _renderer = renderer;
            _elementId = elementId;
        }

        /// <summary>True when the panel set changed and the Origami tree was rebuilt.</summary>
        public bool Sync(IReadOnlyList<V12.Core.Rendering.UINode> panels)
        {
            var sig = string.Join("|", panels.Select(p =>
                $"{p.Id}:{p.Text}:{p.DockSplit}:{p.SplitRatio:F3}:{(p.Floating ? 1 : 0)}"));

            if (sig == _signature && _space != null) return false;
            _signature = sig;

            // Reuse per-panel adapters so Origami keeps tab/dock identity across rebuilds.
            var adapters = new List<OrigamiDockPanel>(panels.Count);
            foreach (var p in panels)
            {
                if (!_panels.TryGetValue(p.Id, out var adapter))
                {
                    adapter = new OrigamiDockPanel(_renderer, p.Id);
                    _panels[p.Id] = adapter;
                }
                adapter.Configure(p.Text, p.Closable);
                adapters.Add(adapter);
            }

            // Build the tree: tab joins accumulate into the current leaf; split joins nest.
            POrigami.DockNode? root = null;
            POrigami.DockNode? currentLeaf = null;

            for (int i = 0; i < panels.Count; i++)
            {
                var panel = panels[i];
                var adapter = adapters[i];

                if (panel.Floating)
                {
                    adapter.IsOpen = true;
                    _pendingFloating.Add((adapter, panel));
                    continue;
                }

                if (root == null)
                {
                    currentLeaf = POrigami.DockNode.Leaf(adapter);
                    root = currentLeaf;
                    continue;
                }

                switch (panel.DockSplit)
                {
                    case 1: // horizontal
                    {
                        var leaf = POrigami.DockNode.Leaf(adapter);
                        root = POrigami.DockNode.Split(
                            POrigami.SplitDirection.Horizontal,
                            Math.Clamp(panel.SplitRatio, 0.05f, 0.95f), root, leaf);
                        currentLeaf = leaf;
                        break;
                    }
                    case 2: // vertical
                    {
                        var leaf = POrigami.DockNode.Leaf(adapter);
                        root = POrigami.DockNode.Split(
                            POrigami.SplitDirection.Vertical,
                            Math.Clamp(panel.SplitRatio, 0.05f, 0.95f), root, leaf);
                        currentLeaf = leaf;
                        break;
                    }
                    default: // tab
                    {
                        currentLeaf ??= POrigami.DockNode.Leaf();
                        currentLeaf.Tabs ??= new List<POrigami.DockPanel>();
                        currentLeaf.Tabs.Add(adapter);
                        currentLeaf.ActiveTabIndex = currentLeaf.Tabs.Count - 1;
                        if (root == null) root = currentLeaf;
                        break;
                    }
                }
            }

            _space = new POrigami.DockSpace(root ?? POrigami.DockNode.Leaf());

            foreach (var (adapter, panel) in _pendingFloating)
            {
                _space.FloatingWindows.Add(new POrigami.FloatingWindow(
                    POrigami.DockNode.Leaf(adapter),
                    new Prowl.Vector.Float2(panel.FloatingX, panel.FloatingY),
                    new Prowl.Vector.Float2(panel.FloatingWidth, panel.FloatingHeight)));
            }
            _pendingFloating.Clear();

            return true;
        }

        private readonly List<(OrigamiDockPanel Panel, V12.Core.Rendering.UINode Node)> _pendingFloating = new();

        public void Draw(Prowl.PaperUI.Paper paper, float x, float y, float w, float h)
            => _space?.Draw(paper, x, y, w, h);

        public void Forget(long panelId) => _panels.Remove(panelId);
    }

    /// <summary>
    /// Origami <see cref="POrigami.DockPanel"/> backed by a V12 element: the panel body is
    /// rendered by the host renderer emitting that element's children.
    /// </summary>
    internal sealed class OrigamiDockPanel : POrigami.DockPanel
    {
        private readonly ProwlPaperRenderer _renderer;
        private readonly long _elementId;
        private string _title = "Panel";
        private bool _closable = true;

        public OrigamiDockPanel(ProwlPaperRenderer renderer, long elementId)
        {
            _renderer = renderer;
            _elementId = elementId;
        }

        public long ElementId => _elementId;

        public void Configure(string? title, bool closable)
        {
            _title = string.IsNullOrEmpty(title) ? "Panel" : title!;
            _closable = closable;
        }

        public override string Title => _title;

        public override void OnGUI(Prowl.PaperUI.Paper paper, float width, float height)
        {
            _renderer.EmitDockPanelBody(this, _elementId, width, height);
        }

        public override void OnClosed()
        {
            IsOpen = false;
            try { _renderer.NotifyDockPanelClosed(_elementId); } catch { }
        }
    }
}
