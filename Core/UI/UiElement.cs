namespace V12.Core.UI
{
    using System;
    using System.Collections.Generic;

    // Lightweight UI element model for engine-agnostic V12 UI.
    public abstract class UiElement
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string? Name { get; set; }
        public List<UiElement> Children { get; } = new List<UiElement>();
        public IDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();
    }

    public sealed class PanelElement : UiElement
    {
        /// <summary>Hint for the frontend renderer: "titlebar", "compheader", etc.</summary>
        public string? StyleHint { get; set; }
        public float MinHeight { get; set; }
    }

    public sealed class LabelElement : UiElement
    {
        public string Text { get; set; } = string.Empty;
        /// <summary>"muted", "accent", "title", "danger" or null for default.</summary>
        public string? StyleHint { get; set; }
        /// <summary>0 = use theme/renderer default.</summary>
        public int FontSize { get; set; }
        public float MinHeight { get; set; }
    }

    public sealed class ButtonElement : UiElement
    {
        public string Text { get; set; } = string.Empty;
        public Action? OnPressed { get; set; }
        /// <summary>"danger", "accent", "selected" or null for default.</summary>
        public string? StyleHint { get; set; }
        public bool Flat { get; set; }
    }

    public sealed class TextFieldElement : UiElement
    {
        public string Text { get; set; } = string.Empty;
        /// <summary>Invoked when the user commits the field (Enter / focus-out).</summary>
        public Action<string>? OnCommit { get; set; }
        /// <summary>
        /// Optional: called by the renderer at its refresh interval to get the current
        /// live value without requiring a full UI rebuild.
        /// </summary>
        public Func<string>? LiveValue { get; set; }
    }

    public sealed class HBoxElement : UiElement { }
    public sealed class VBoxElement : UiElement { }

    public sealed class SeparatorElement : UiElement { }

    public sealed class ScrollContainerElement : UiElement { }

    public sealed class SplitContainerElement : UiElement
    {
        public bool  Horizontal         { get; set; } = true;
        /// <summary>Minimum pixel width of the first child (left pane).</summary>
        public float FirstChildMinSize  { get; set; } = 130f;
    }

    public sealed class OptionPickerElement : UiElement
    {
        public List<string> Items          { get; } = new List<string>();
        public int          SelectedIndex  { get; set; }
        public Action<int>? OnSelectionChanged { get; set; }
    }
}
