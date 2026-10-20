using System;

namespace V12.UI
{
    /// <summary>
    /// A button that latches between two states — for menu-style ON/OFF toggles
    /// where a checkbox would be the wrong affordance.
    /// </summary>
    public interface IToggle : IWidget
    {
        string Text { get; set; }
        bool Toggled { get; set; }
        bool Enabled { get; set; }
        void SetOnToggled(Action<bool> onToggled);
    }
}
