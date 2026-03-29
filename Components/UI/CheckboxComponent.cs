using System;

namespace V12.Components.UI
{
    public class CheckboxComponent : ComponentBase
    {
        public override string Name => "Checkbox";
        public override string Description => "Checkbox control";

        public string Label { get; set; } = string.Empty;
        public bool Checked { get; set; }
        public Action<bool>? OnChanged { get; set; }
    }
}

