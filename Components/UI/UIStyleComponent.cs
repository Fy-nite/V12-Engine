using System;

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
        /// Custom attributes for frontend-specific logic.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, string> Attributes { get; } = new();
    }
}
