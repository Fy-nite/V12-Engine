using System;

namespace V12.UI
{
    public interface ISlider : IWidget
    {
        float Min { get; set; }
        float Max { get; set; }
        float Step { get; set; }
        float Value { get; set; }
        bool Enabled { get; set; }
        void SetOnChanged(Action<float> onChanged);
    }
}
