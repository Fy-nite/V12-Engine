using System;
using System.Collections.Generic;
using System.Linq;
using V12.Core.Interfaces;
using V12.Core.Rendering;
using PQuill = global::Prowl.Quill;
using PVector = global::Prowl.Vector;
using PPal = V12.Core.UI.Paper.PaperPalette;
using PPaper = global::Prowl.PaperUI.Paper;

namespace V12.Core.UI.Paper
{
    public sealed class ProwlPaperRenderer : IUIRenderer
    {
        public static bool UseProwlPaper { get; set; } = true;
        private readonly PaperCanvasRenderer _canvasRenderer;
        private PPaper _paper;
        private UIFrame _lastFrame = new();
        private int _vpW = 1280, _vpH = 720;
        private float _dpi = 1f;
        /// <summary>Id offset used when emitting a foreign UI frame into a host's
        /// own Paper instance, so element ids never collide with host widget ids.</summary>
        private readonly long _idBase;

        /// <summary>When emitting into a host row, size a screen-space canvas to
        /// its content (stretch width, auto height) instead of the viewport.</summary>
        public bool AutoSizeScreenCanvas { get; set; }

        /// <summary>Id of the screen-space canvas whose children are being emitted
        /// this frame (so their layout can hug content instead of stretching).</summary>
        private long _screenCanvasId;
        private readonly Dictionary<long, global::Prowl.OrigamiUI.CodeDocument> _codeDocs = new();
        private readonly Dictionary<long, OrigamiDockHost> _dockHosts = new();
        private Dictionary<long, List<V12.Core.Rendering.UINode>> _byParent = new();
        private Dictionary<long, V12.Core.Rendering.UINode> _nodesById = new();
        private Prowl.Scribe.FontFile? _fallbackFont;

        public ProwlPaperRenderer(int viewportWidth = 1280, int viewportHeight = 720, float dpiScale = 1f)
        {
            _vpW = viewportWidth; _vpH = viewportHeight; _dpi = dpiScale;
            _canvasRenderer = new PaperCanvasRenderer();
            _canvasRenderer.SetViewport(_vpW, _vpH);
            _paper = new PPaper(_canvasRenderer, _vpW, _vpH, new PQuill.FontAtlasSettings());
            TryAddFallbackFont(_paper);
            V12.Core.GameRoot.Log?.Information("[ProwlPaperRenderer] Paper {W}x{H} dpi={Dpi} ready", _vpW, _vpH, _dpi);
        }
        public ProwlPaperRenderer(PaperCanvasRenderer godotRenderer, int w = 1280, int h = 720, float dpi = 1f)
        {
            _canvasRenderer = godotRenderer ?? throw new ArgumentNullException(nameof(godotRenderer));
            _vpW = w; _vpH = h; _dpi = dpi;
            _canvasRenderer.SetViewport(w,h);
            _paper = new PPaper(_canvasRenderer, w, h, new PQuill.FontAtlasSettings());
            TryAddFallbackFont(_paper);
        }
        /// <summary>
        /// Renderer that emits <see cref="UIFrame"/> nodes into a Paper instance the
        /// host already owns (e.g. the editor chrome's Paper). It never drives a
        /// canvas itself: the host calls <see cref="Emit"/> between its own
        /// BeginFrame/EndFrame pair.
        /// </summary>
        private ProwlPaperRenderer(PPaper paper, int w, int h, float dpi, Prowl.Scribe.FontFile? font)
        {
            _canvasRenderer = null!;
            _paper = paper ?? throw new ArgumentNullException(nameof(paper));
            _vpW = w; _vpH = h; _dpi = dpi;
            _fallbackFont = font;
            // Keep ids away from the host widget ids (which start at 1 and grow).
            _idBase = 100_000_000;
        }

        /// <summary>Creates a frame emitter for a host-owned Paper instance.</summary>
        public static ProwlPaperRenderer CreateEmitter(PPaper paper, int w, int h, Prowl.Scribe.FontFile? font = null)
            => new(paper, w, h, 1f, font);

        /// <summary>Rebinds an emitter to the host's current Paper (it is recreated on resize).</summary>
        public void SetPaper(PPaper paper, int w, int h, Prowl.Scribe.FontFile? font = null)
        {
            if (paper != null) _paper = paper;
            _vpW = w; _vpH = h;
            if (font != null) _fallbackFont = font;
        }

        /// <summary>Emits a captured UI frame into the current Paper instance.</summary>
        public void Emit(UIFrame frame)
        {
            _lastFrame = frame ?? new UIFrame();
            if (!UseProwlPaper || _lastFrame.Nodes.Count == 0) return;
            BuildPaperTree(_lastFrame);
        }

        private void TryAddFallbackFont(PPaper paper)
        {
            string[] candidates = new[] {
                @"C:\Windows\Fonts\segoeui.ttf",
                @"C:\Windows\Fontsrial.ttf",
                @"C:\Windows\Fonts\calibri.ttf",
                @"/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
                @"/usr/share/fonts/TTF/DejaVuSans.ttf",
            };
            foreach(var path in candidates)
            {
                try
                {
                    if (System.IO.File.Exists(path))
                    {
                        var ff = new Prowl.Scribe.FontFile(path);
                        paper.AddFallbackFont(ff);
                        _fallbackFont = ff;
                        V12.Core.GameRoot.Log?.Information("[ProwlPaperRenderer] Fallback font loaded {Path}", path);
                        return;
                    }
                } catch (Exception ex) { V12.Core.GameRoot.Log?.Debug(ex, "[ProwlPaperRenderer] font load failed {Path}", path); }
            }
            try
            {
                var fonts = paper.EnumerateSystemFonts()?.ToList();
                if (fonts != null && fonts.Count > 0)
                {
                    object? picked = null;
                    foreach(var f in fonts)
                    {
                        var s = f.ToString() ?? "";
                        if (s.IndexOf("Segoe", StringComparison.OrdinalIgnoreCase)>=0 || s.IndexOf("Arial", StringComparison.OrdinalIgnoreCase)>=0)
                        { picked = f; break; }
                    }
                    picked ??= fonts[0];
                    try{ paper.AddFallbackFont((dynamic)picked); _fallbackFont = picked as Prowl.Scribe.FontFile; V12.Core.GameRoot.Log?.Information("[ProwlPaperRenderer] Fallback via EnumerateSystemFonts {Font}", picked); }catch{}
                    return;
                }
            } catch{}
        }
        public PaperFrame LastPaperFrame => _canvasRenderer.LastFrame;
        public PPaper PaperInstance => _paper;
        public void ApplyUI(UIFrame frame){ if(!UseProwlPaper) return; _lastFrame = frame ?? new UIFrame(); }
        public void step()
        {
            if(!UseProwlPaper || _lastFrame==null) return;
            try
            {
                _paper.BeginFrame(1f/60f, _dpi);
                global::Prowl.OrigamiUI.Origami.BeginFrame(_paper, 1f/60f);
                BuildPaperTree(_lastFrame);
                global::Prowl.OrigamiUI.Origami.EndFrame(_paper);
                _paper.EndFrame();
            }
            catch(Exception ex){ V12.Core.GameRoot.Log?.Warning(ex, "[ProwlPaperRenderer] step"); }
        }
        public void Resize(int w,int h,float dpi=1f){ _vpW=w; _vpH=h; _dpi=dpi; _canvasRenderer.SetViewport(w,h);}

        private void BuildPaperTree(UIFrame frame)
        {
            var byParent = frame.Nodes.GroupBy(n=>n.ParentId).ToDictionary(g=>g.Key, g=>g.ToList());
            _byParent = byParent;
            _nodesById = frame.Nodes.ToDictionary(n => n.Id);
            if(!byParent.TryGetValue(0,out var roots)||roots.Count==0) return;
            foreach(var root in roots){
                // The engine path treats a canvas as a grouping node: the dock/UI
                // widget inside draws itself. A hosted chrome row needs the canvas
                // box itself so the anchor/content sizing applies.
                if (AutoSizeScreenCanvas && root.Kind == UIWidgetKind.Canvas)
                {
                    BuildNode(root, byParent, 0);
                    continue;
                }
                if(byParent.TryGetValue(root.Id, out var children))
                    foreach(var c in children) BuildNode(c, byParent, 0);
            }
        }

        /// <summary>Emit a dock panel's element children into the current Paper scope.</summary>
        internal void EmitDockPanelBody(OrigamiDockPanel panel, long elementId, float width, float height)
        {
            using (_paper.Column($"dockbody#{elementId}", unchecked((int)(elementId & 0x7fffffff)), 0)
                       .Width(_paper.Stretch()).Height(_paper.Stretch()).Clip()
                       .JustifyContent(global::Prowl.PaperUI.LayoutJustification.Start)
                       .AlignItems(global::Prowl.PaperUI.LayoutAlignment.Stretch)
                       .Enter())
            {
                if (_byParent.TryGetValue(elementId, out var kids))
                    foreach (var c in kids) BuildNode(c, _byParent, 0);
            }
        }

        internal void NotifyDockPanelClosed(long elementId)
        {
            foreach (var host in _dockHosts.Values) host.Forget(elementId);
        }

        private void BuildDockSpace(UINode node, Dictionary<long,List<UINode>> byParent)
        {
            if (!_dockHosts.TryGetValue(node.Id, out var host))
            {
                host = new OrigamiDockHost(this, node.Id);
                _dockHosts[node.Id] = host;
            }

            var panels = byParent.TryGetValue(node.Id, out var kids)
                ? kids.Where(k => k.Kind == UIWidgetKind.DockPanel).ToList()
                : new List<UINode>();

            host.Sync(panels);

            float w = node.Width > 0 ? node.Width : _vpW;
            float h = node.Height > 0 ? node.Height : _vpH;
            float x = node.DockPadding;
            float y = node.DockPadding;
            host.Draw(_paper, x, y, w - node.DockPadding * 2f, h - node.DockPadding * 2f);
        }
        private void BuildNode(UINode node, Dictionary<long,List<UINode>> byParent,int depth){
            if(depth>64) return;
            string idStr = _idBase == 0 ? node.Id.ToString() : $"wui_{node.Id}";
            int pid=(int)((node.Id + _idBase) & 0x7fffffff);
            global::Prowl.PaperUI.ElementBuilder el;
            // Debug: ensure Paper state is valid
            // Console.WriteLine($"BuildNode {node.Kind} {node.Name}");
            switch(node.Kind){
                case UIWidgetKind.Canvas:
                {
                    el = _paper.Box(idStr, pid, 0);
                    if (node.ScreenSpace)
                    {
                        _screenCanvasId = node.Id;
                        el.LayoutType(global::Prowl.PaperUI.LayoutType.Column);
                        if (AutoSizeScreenCanvas)
                        {
                            // Hosted as a shell row: hug the content's height.
                            el.Width(_paper.Stretch()).Height(_paper.Auto)
                              .AlignItems(global::Prowl.PaperUI.LayoutAlignment.Start);
                        }
                        else
                        {
                            // Overlay canvas: a definite viewport-sized box (Stretch
                            // alone collapses at the Paper root), anchors place content.
                            el.Width(_paper.Pixels(_vpW)).Height(_paper.Pixels(_vpH))
                              .AlignItems(node.AnchorX <= 0.01f
                                  ? global::Prowl.PaperUI.LayoutAlignment.Start
                                  : node.AnchorX >= 0.99f
                                      ? global::Prowl.PaperUI.LayoutAlignment.End
                                      : global::Prowl.PaperUI.LayoutAlignment.Center)
                              .JustifyContent(node.AnchorY <= 0.01f
                                  ? global::Prowl.PaperUI.LayoutJustification.Start
                                  : node.AnchorY >= 0.99f
                                      ? global::Prowl.PaperUI.LayoutJustification.End
                                      : global::Prowl.PaperUI.LayoutJustification.Center);
                        }
                    }
                    else
                    {
                        if (node.Width > 0) el.Width(node.Width); else el.Width(_paper.Auto);
                        if (node.Height > 0) el.Height(node.Height); else el.Height(_paper.Auto);
                    }
                    break;
                }
                case UIWidgetKind.Label:
                {
                    el=_paper.Box(idStr,pid,0).Width(_paper.Auto).Height(_paper.Auto);
                    // Flat labels are plain text (status strips); the default stays
                    // a raised chip for game HUDs.
                    if (!node.Flat)
                        el.BackgroundColor(PPal.Raised).Rounded(4f).Padding(4);
                    else if (node.Padding > 0)
                        el.Padding(node.Padding);

                    var ink = node.StyleHint switch
                    {
                        "muted" => PPal.Muted,
                        "accent" => PPal.Accent,
                        "danger" => PPal.Danger,
                        _ => PPal.Text,
                    };
                    float size = node.FontSize > 0 ? node.FontSize
                        : node.StyleHint == "title" ? 16f : 13f;
                    try { el.Text(node.Text ?? node.Name ?? "", _fallbackFont).FontSize(size).TextColor(ink); } catch {}
                    break;
                }
                case UIWidgetKind.Button:
                    el=_paper.Box(idStr,pid,0).Width(_paper.Auto).Height(_paper.Auto).BackgroundColor(PPal.Accent).Rounded(6f).Padding(8).OnClick(_=>node.OnClick?.Invoke());
                    try { el.Text(node.Text ?? node.Name ?? "Button", _fallbackFont).FontSize(13f).TextColor(PPal.Text); } catch {}
                    break;
                case UIWidgetKind.TextInput:
                    el=_paper.Box(idStr,pid,0).Width(_paper.Auto).Height(_paper.Auto).BackgroundColor(PPal.Canvas).Rounded(4f).Padding(6).BorderWidth(1f).BorderColor(PPal.Border);
                    try { el.Text(node.Text ?? node.Placeholder ?? "", _fallbackFont).FontSize(13f).TextColor(PPal.Text); } catch {}
                    break;
                case UIWidgetKind.CodeEditor:{
                    if(!_codeDocs.TryGetValue(node.Id,out var doc)){ doc=new global::Prowl.OrigamiUI.CodeDocument(node.Text??""); _codeDocs[node.Id]=doc; }
                    else { var cur=doc.GetText(); if(cur!=(node.Text??"")) doc.SetText(node.Text??""); }
                    el=_paper.Box(idStr,pid,0).BackgroundColor(PPal.Canvas).Rounded(6f).Padding(8).BorderWidth(1f).BorderColor(PPal.Border);
                    var preview = doc.GetText(); if (preview.Length > 400) preview = preview.Substring(0,400) + "...";
                    try { el.Text(preview, _fallbackFont).FontSize(11f).TextColor(PPal.Accent); } catch {}
                    break;}
                case UIWidgetKind.HLayout:
                    el=_paper.Box(idStr,pid,0).LayoutType(global::Prowl.PaperUI.LayoutType.Row).Gap(node.Spacing);
                    if (node.Padding > 0) el.Padding(node.Padding);
                    // Rows hug their content. A row in a stretch column otherwise
                    // absorbs vertical space and pushes the rows after it apart.
                    el.Height(_paper.Auto);
                    break;
                case UIWidgetKind.VLayout:
                    el=_paper.Box(idStr,pid,0).LayoutType(global::Prowl.PaperUI.LayoutType.Column).Gap(node.Spacing);
                    if (node.Padding > 0) el.Padding(node.Padding);
                    if (_screenCanvasId != 0 && node.ParentId == _screenCanvasId)
                        el.Height(_paper.Auto);
                    // A tab page sits in the tabs' auto-height column: stretch height
                    // collapses it to 0, which would overlap the next sibling.
                    if (node.TabIndex >= 0)
                        el.Width(_paper.Stretch()).Height(_paper.Auto);
                    break;
                case UIWidgetKind.Rect:
                    el=_paper.Box(idStr,pid,0).BackgroundColor(ParseColor(node.Color, PPal.Raised)).Rounded(node.CornerRadius);
                    break;
                case UIWidgetKind.ProgressBar:
                    el=_paper.Box(idStr,pid,0).Width(_paper.Auto).Height(_paper.Auto).BackgroundColor(PPal.Canvas).Rounded(4f).Padding(2);
                    break;
                case UIWidgetKind.Slider:
                case UIWidgetKind.Toggle:
                case UIWidgetKind.Checkbox:
                    el=_paper.Box(idStr,pid,0).Width(_paper.Auto).Height(_paper.Auto).BackgroundColor(node.Bool?PPal.Accent:PPal.Border).Rounded(4f).Padding(6).Text((node.Text??"")+(node.Bool?" [x]":" [ ]"), null).TextColor(PPal.Text).OnClick(_=>{node.Bool=!node.Bool; try{node.OnToggled?.Invoke(node.Bool);}catch{}});
                    break;
                case UIWidgetKind.Scroll:
                    el=_paper.Box(idStr,pid,0).Clip();
                    break;
                case UIWidgetKind.Splitter:
                    el=_paper.Box(idStr,pid,0).LayoutType(node.Vertical?global::Prowl.PaperUI.LayoutType.Column:global::Prowl.PaperUI.LayoutType.Row).Gap(node.Spacing);
                    break;
                case UIWidgetKind.Tree:
                case UIWidgetKind.NodeGraph:
                case UIWidgetKind.Viewport:
                case UIWidgetKind.Image:
                case UIWidgetKind.Icon:
                case UIWidgetKind.Panel:
                default:
                    el=_paper.Box(idStr,pid,0);
                    if(!string.IsNullOrEmpty(node.Text)) el.Text(node.Text, _fallbackFont).FontSize(12f);
                    break;
                case UIWidgetKind.DockSpace:
                {
                    // Origami owns the layout: build/reuse the dock tree, then let it draw.
                    BuildDockSpace(node, byParent);
                    return;
                }
                case UIWidgetKind.DockPanel:
                    // Rendered by OrigamiDockPanel.OnGUI; never emitted directly.
                    return;
                case UIWidgetKind.PropertyGrid:
                {
                    el = _paper.Box(idStr, pid, 0).LayoutType(global::Prowl.PaperUI.LayoutType.Column)
                        .Width(_paper.Stretch()).Height(_paper.Auto);
                    using (el.Enter())
                    {
                        for (int t = 0; t < node.Targets.Count; t++)
                        {
                            var target = node.Targets[t];
                            if (target == null) continue;
                            if (node.Text == "1")
                            {
                                _paper.Box($"{idStr}_h{t}", pid ^ (t * 7919), 0)
                                    .Width(_paper.Stretch()).Height(22f).Padding(4, 4, 0, 0)
                                    .Text(target.GetType().Name, _fallbackFont)
                                    .FontSize(12f).TextColor(PPal.Muted);
                            }
                            try
                            {
                                var adapter = new PaperInspectorAdapter(
                                    _paper, $"{idStr}_pg{t}",
                                    changed: () => { try { node.OnDirty?.Invoke(); } catch { } },
                                    font: _fallbackFont);
                                adapter.Render(target);
                            }
                            catch (Exception ex)
                            {
                                V12.Core.GameRoot.Log?.Warning(ex, "[ProwlPaperRenderer] PropertyGrid {Id}", node.Id);
                            }
                        }
                    }
                    return;
                }
                case UIWidgetKind.Tabs:
                {
                    el = _paper.Box(idStr, pid, 0).LayoutType(global::Prowl.PaperUI.LayoutType.Column)
                        .Gap(4f).Width(_paper.Stretch()).Height(_paper.Auto);
                    using (el.Enter())
                    {
                        if (node.Items.Count > 0)
                        {
                            var strip = global::Prowl.OrigamiUI.Origami.Tabs(
                                _paper, $"{idStr}_strip", node.SelectedIndex,
                                i => { try { node.OnSelected?.Invoke(i); } catch { } });
                            if (node.Bool) strip.Pills();
                            if (node.Height > 0) strip.Height(node.Height);
                            foreach (var title in node.Items) strip.Tab(title ?? "");
                            strip.Show();
                        }

                        if (byParent.TryGetValue(node.Id, out var tabKids))
                            foreach (var c in tabKids)
                            {
                                if (c.TabIndex >= 0 && c.TabIndex != node.SelectedIndex) continue;
                                BuildNode(c, byParent, depth + 1);
                            }
                    }
                    return;
                }
                case UIWidgetKind.Table:
                {
                    var table = global::Prowl.OrigamiUI.Origami.Table(
                        _paper, idStr, node.SelectedIndex,
                        i => { try { node.OnSelected?.Invoke(i); } catch { } });
                    foreach (var header in node.Columns) table.Column(header ?? "");
                    table.RowHeight(node.RowHeight > 0 ? node.RowHeight : 26f);
                    table.Bordered(node.Bordered);
                    if (node.Width > 0 && node.Height > 0) table.Scroll(node.Width, node.Height);
                    var ink = global::Prowl.OrigamiUI.Origami.Current.Ink.C500;
                    foreach (var row in node.Rows)
                    {
                        var r = table.Row();
                        foreach (var cell in row) r.Cell(cell ?? "", ink);
                    }
                    table.Show();
                    return;
                }
                case UIWidgetKind.Dropdown:
                {
                    var dd = global::Prowl.OrigamiUI.Origami.Dropdown(
                        _paper, idStr, node.SelectedIndex,
                        i => { try { node.OnSelected?.Invoke(i); } catch { } },
                        node.Items);
                    if (!string.IsNullOrEmpty(node.Placeholder)) dd.Placeholder(node.Placeholder);
                    if (node.Bool) dd.Searchable();
                    if (node.Width > 0) dd.Width(global::Prowl.PaperUI.LayoutEngine.UnitValue.Pixels(node.Width));
                    dd.Show();
                    return;
                }
                case UIWidgetKind.Separator:
                    global::Prowl.OrigamiUI.Origami.Separator(_paper, idStr).Show();
                    return;
                case UIWidgetKind.Spinner:
                {
                    var sp = global::Prowl.OrigamiUI.Origami.Spinner(_paper, idStr);
                    float size = node.Width;
                    if (size > 0 && size <= 16) sp.XS();
                    else if (size > 0 && size <= 24) sp.SM();
                    else if (size > 0 && size <= 40) sp.MD();
                    else sp.LG();
                    sp.Show();
                    return;
                }
            }
            using(el.Enter()){
                if(byParent.TryGetValue(node.Id,out var kids))
                    foreach(var c in kids) BuildNode(c, byParent, depth+1);
            }
        }
        private static PVector.Color ParseColor(string hex, PVector.Color fallback){
            if(string.IsNullOrWhiteSpace(hex)) return fallback;
            try{
                hex=hex.Trim().TrimStart('#');
                if(hex.Length==6) return new PVector.Color(Convert.ToByte(hex.Substring(0,2),16)/255f, Convert.ToByte(hex.Substring(2,2),16)/255f, Convert.ToByte(hex.Substring(4,2),16)/255f, 1f);
                if(hex.Length==8) return new PVector.Color(Convert.ToByte(hex.Substring(0,2),16)/255f, Convert.ToByte(hex.Substring(2,2),16)/255f, Convert.ToByte(hex.Substring(4,2),16)/255f, Convert.ToByte(hex.Substring(6,2),16)/255f);
            }catch{}
            return fallback;
        }
    }
}




