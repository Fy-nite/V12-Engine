using System;

namespace V12.Components.UI
{
    public class ToggleComponent : ComponentBase
    {
        public override string Name => "Toggle";
        public override string Description => "Toggle control";

        public string Label { get; set; } = string.Empty;
        public bool IsOn { get; set; }
        public Action<bool>? OnToggled { get; set; }
    }
}

