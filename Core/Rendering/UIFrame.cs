using System;
using System.Collections.Generic;

namespace V12.Core.Rendering
{
    /// <summary>What a <see cref="UINode"/> represents.</summary>
    public enum UIWidgetKind
    {
        Canvas,
        Panel,
        Label,
        Button,
        Toggle,
        Checkbox,
        Slider,
        ProgressBar,
        TextInput,
        Image,
        Icon,
        Rect,
        HLayout,
        VLayout,
        Viewport,
        Splitter,
        Tree,
        Scroll,
        // ── Origami widgets (engine-level, rendered by ProwlPaperRenderer) ──
        CodeEditor,
        DockSpace,
        DockPanel,
        PropertyGrid,
        Tabs,
        Table,
        Dropdown,
        Separator,
        Spinner,
        NodeGraph,
        Unknown,
    }

    /// <summary>
    /// One node of the UI tree V12 hands to an <c>IUIRenderer</c>. Nodes are keyed by
    /// <see cref="Id"/> (the owning element's id) so a retained backend can reconcile
    /// create/update/destroy across frames.
    /// </summary>
    public sealed class UINode
    {
        /// <summary>Element id — the reconciliation key.</summary>
        public long Id;

        /// <summary>Parent node id; 0 means "child of the canvas root".</summary>
        public long ParentId;

        public string Name = "";
        public UIWidgetKind Kind;

        // ── Canvas ──────────────────────────────────────────────────────────
        public bool ScreenSpace = true;
        public float AnchorX = 0.5f;
        public float AnchorY = 0.5f;

        // ── Box / layout ────────────────────────────────────────────────────
        public float Width;
        public float Height;
        public float CornerRadius;
        public float Spacing;
        public float Padding;

        public float MinWidth = -1f, PreferredWidth = -1f, FlexibleWidth;
        public float MinHeight = -1f, PreferredHeight = -1f, FlexibleHeight;

        /// <summary>VLayout/HLayout/Splitter Expand flag: this container absorbs leftover
        /// space along the axis its parent lays out.</summary>
        public bool Expand;

        /// <summary>Splitter orientation: true = Vertical (top-to-bottom). false = Horizontal.</summary>
        public bool Vertical;

        // ── Content ─────────────────────────────────────────────────────────
        public string Text = "";
        public string Placeholder = "";   // text-input hint
        public float FontSize;            // label/button font size (0 = backend default)
        public string ImageSource = "";
        public bool PreserveAspect = true;

        /// <summary>"#rrggbb" background/tint; empty = use the backend default/theme.</summary>
        public string Color = "";

        /// <summary>Style hint from <c>UIStyleComponent</c> (e.g. "muted","accent","title","danger").</summary>
        public string StyleHint = "";
        public bool Flat;

        /// <summary>Anchor within the parent from <c>UIStyleComponent.Anchor</c> (e.g. "center"). Empty = flow layout.</summary>
        public string Anchor = "";

        // ── Value widgets ───────────────────────────────────────────────────
        public float Value;
        public float Min;
        public float Max = 1f;
        public float Step;
        public bool Bool;   // toggle/checkbox state, or progress Indeterminate

        // ── Origami widgets ─────────────────────────────────────────────────
        /// <summary>Language id for CodeEditor (csharp, hlsl, json...)</summary>
        public string Language = "csharp";
        public bool ReadOnly;
        public int TabSize = 4;
        public int SelectedIndex;
        public Action<int>? OnSelected;

        // ── Origami dock space / panels ─────────────────────────────────────
        /// <summary>Dock space id a panel belongs to.</summary>
        public string DockId = "";
        /// <summary>Panel tab icon key.</summary>
        public string Icon = "";
        /// <summary>Panel tab can be closed.</summary>
        public bool Closable = true;
        /// <summary>Panel floats in its own window.</summary>
        public bool Floating;
        /// <summary>How the panel joins the tree: 0 = tab, 1 = horizontal split, 2 = vertical split.</summary>
        public int DockSplit;
        /// <summary>Split share (0..1) for non-tab joins.</summary>
        public float SplitRatio = 0.5f;
        /// <summary>Dock area inset in pixels.</summary>
        public float DockPadding;
        public bool AllowSplitting = true;
        public bool AllowFloating = true;
        public float FloatingX, FloatingY, FloatingWidth = 520f, FloatingHeight = 360f;
        /// <summary>Serialized Origami layout JSON (optional).</summary>
        public string LayoutJson = "";

        // ── Origami data widgets ───────────────────────────────────────────
        /// <summary>Tab strip titles, dropdown options, or breadcrumb labels.</summary>
        public List<string> Items = new();
        /// <summary>Table column headers (one flex column each).</summary>
        public List<string> Columns = new();
        /// <summary>Table rows; each row holds one string per column.</summary>
        public List<List<string>> Rows = new();
        public float RowHeight;
        public bool Bordered = true;
        /// <summary>Tab page index (-1 = not a tab page) and its label.</summary>
        public int TabIndex = -1;
        public string TabTitle = "";
        /// <summary>Objects a PropertyGrid inspects (public fields), plus the sink that
        /// re-renders when a field is edited.</summary>
        public List<object> Targets = new();
        public Action? OnDirty;
        /// <summary>PropertyGrid nesting depth.</summary>
        public int Depth;

        // ── Interaction sinks (the backend invokes these) ───────────────────
        public Action? OnClick;
        public Action<float>? OnValueChanged;
        public Action<bool>? OnToggled;
        public Action<string>? OnTextChanged;
    }

    /// <summary>A captured UI description handed to <see cref="V12.Core.Interfaces.IUIRenderer"/>.</summary>
    public sealed class UIFrame
    {
        public List<UINode> Nodes = new();
    }
}
