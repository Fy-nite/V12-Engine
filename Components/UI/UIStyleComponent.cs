using System;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components.UI
{
    /// <summary>
    /// Metadata component for UI elements to carry style hints and layout attributes
    /// that are not captured by specific layout components.
    /// </summary>
    public class UIStyleComponent : ComponentBase
    {
        public override string Name => "UIStyle";
        public override string Description => "Style hints and custom attributes for UI elements";

        /// <summary>
        /// Hint for the renderer: "muted", "accent", "title", "danger", "selected", "titlebar", "compheader".
        /// </summary>
        public string? StyleHint { get; set; }

        /// <summary>
        /// Whether the element should be rendered "flat" (no background/border).
        /// </summary>
        public bool Flat { get; set; }

        /// <summary>
        /// Layout anchor within the parent: "center", "top-left", "top", "top-right",
        /// "left", "right", "bottom-left", "bottom", "bottom-right". Null/empty = default flow.
        /// A backend maps this to its own anchoring (e.g. Gum's <c>Anchor</c>).
        /// </summary>
        public string? Anchor { get; set; }
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        /// <summary>
        /// Custom attributes for frontend-specific logic.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, string> Attributes { get; } = new();
    }
}
