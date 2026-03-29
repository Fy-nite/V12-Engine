using System;

namespace V12.Components.UI
{
    public class SliderComponent : ComponentBase
    {
        public override string Name => "Slider";
        public override string Description => "Slider control";

        public float Value { get; set; }
        public float Min { get; set; } = 0f;
        public float Max { get; set; } = 1f;
        public float Step { get; set; } = 0f;
        public Action<float>? OnChanged { get; set; }
    }
}

