using System;
using System.Collections.Generic;

namespace V12.UI
{
    public interface IDropdown : IWidget
    {
        void SetOptions(IEnumerable<string> options);
        int SelectedIndex { get; set; }
        void SetOnSelected(Action<int> onSelected);
    }
}