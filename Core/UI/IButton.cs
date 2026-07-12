using System;

namespace V12.UI
{
    public interface IButton : IWidget
    {
        string Text { get; set; }
        bool Enabled { get; set; }
        void SetOnClick(Action onClick);
    }
}