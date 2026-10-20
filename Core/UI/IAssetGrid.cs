using System;
using System.Collections.Generic;

namespace V12.UI
{
    public sealed record AssetGridEntry(string Name, string Type, string IconKey, object UserData);

    public interface IAssetGrid : IWidget
    {
        object? SelectedUserData { get; }
        void SetItems(IEnumerable<AssetGridEntry> items);
        void SetOnSelectionChanged(Action<object?> onSelectionChanged);
        void SetOnItemActivated(Action<object> onItemActivated);
    }
}
