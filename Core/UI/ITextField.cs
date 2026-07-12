using System;

namespace V12.UI
{
    public interface ITextField : IWidget
    {
        string Text { get; set; }
        string Placeholder { get; set; }
        void SetOnChanged(Action<string> onChanged);
    }
}