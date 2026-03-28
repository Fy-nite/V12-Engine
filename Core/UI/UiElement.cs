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

    public sealed class PanelElement : UiElement { }
    public sealed class LabelElement : UiElement
    {
        public string Text { get; set; } = string.Empty;
    }

    public sealed class ButtonElement : UiElement
    {
        public string Text { get; set; } = string.Empty;
        public Action? OnPressed { get; set; }
    }

    public sealed class TextFieldElement : UiElement
    {
        public string Text { get; set; } = string.Empty;
        // invoked when the field is committed by the UI renderer
        public Action<string>? OnCommit { get; set; }
    }

    public sealed class HBoxElement : UiElement { }
    public sealed class VBoxElement : UiElement { }
}

