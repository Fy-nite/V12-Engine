using System;

namespace V12.UI
{
    public interface ICheckbox : IWidget
    {
        string Text { get; set; }
        bool Checked { get; set; }
        bool Enabled { get; set; }
        void SetOnChanged(Action<bool> onChanged);
    }
}
