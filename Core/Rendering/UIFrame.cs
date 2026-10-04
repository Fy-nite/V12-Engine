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

        // ── Value widgets ───────────────────────────────────────────────────
        public float Value;
        public float Min;
        public float Max = 1f;
        public float Step;
        public bool Bool;   // toggle/checkbox state, or progress Indeterminate

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
