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

    // Additional common UI elements

    /// <summary>
    /// Generic rectangular region useful for plain backgrounds, spacers, or
    /// layout hinting. Frontends may honour Width/Height or use flex rules.
    /// </summary>
    public sealed class RectElement : UiElement
    {
        public float Width  { get; set; } = 0f; // 0 = auto
        public float Height { get; set; } = 0f; // 0 = auto
        /// <summary>CSS-like colour string (e.g. "#RRGGBB" or named token) for frontends that support it.</summary>
        public string? BackgroundColor { get; set; }
        public float CornerRadius { get; set; } = 0f;
    }

    public sealed class ImageElement : UiElement
    {
        /// <summary>Path or resource identifier understood by the frontend.</summary>
        public string Source { get; set; } = string.Empty;
        public bool   PreserveAspect { get; set; } = true;
        /// <summary>Optional tint colour (frontend specific format).</summary>
        public string? Tint { get; set; }
    }

    public sealed class ToggleElement : UiElement
    {
        public string Label { get; set; } = string.Empty;
        public bool IsOn { get; set; }
        public Action<bool>? OnToggled { get; set; }
    }

    public sealed class CheckboxElement : UiElement
    {
        public string Label { get; set; } = string.Empty;
        public bool Checked { get; set; }
        public Action<bool>? OnChanged { get; set; }
    }

    public sealed class SliderElement : UiElement
    {
        public float Value { get; set; }
        public float Min { get; set; } = 0f;
        public float Max { get; set; } = 1f;
        public float Step { get; set; } = 0f; // 0 = continuous
        public Action<float>? OnChanged { get; set; }
    }

    public sealed class ProgressBarElement : UiElement
    {
        /// <summary>Progress between 0 and 1.</summary>
        public float Value { get; set; }
        public bool Indeterminate { get; set; }
    }

    public sealed class IconElement : UiElement
    {
        /// <summary>Name or id of the icon glyph expected by the frontend.
        /// Frontends may map this to a sprite or font glyph.</summary>
        public string Icon { get; set; } = string.Empty;
        public float Size { get; set; } = 16f;
    }

    /// <summary>
    /// Alias for a single-line text field; kept for convenience alongside TextFieldElement.
    /// </summary>
    public sealed class InputFieldElement : UiElement
    {
        public string Text { get; set; } = string.Empty;
        public string Placeholder { get; set; } = string.Empty;
        public Action<string>? OnCommit { get; set; }
        public Func<string>? LiveValue { get; set; }
    }
}
