using System;
using System.Collections.Generic;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components.UI
{
    /// <summary>One item in a <see cref="TreeComponent"/> model.</summary>
    public sealed class TreeItem
    {
        public string Label { get; set; } = "";
        public string? IconKey { get; set; }
        public string? TrailingIconKey { get; set; }
        public bool TrailingActive { get; set; }
        public bool Expanded { get; set; } = true;
        public bool Visible { get; set; } = true;
        public object? UserData { get; set; }
        public List<TreeItem> Children { get; } = new();
    }

    /// <summary>
    /// Marks an element as a hierarchical tree container.
    ///
    /// Renderers may either read the child element hierarchy (Godot) or the
    /// explicit <see cref="Items"/> model (Paper/other retained renderers).
    /// Selection invokes <see cref="OnSelected"/> with the model item.
    /// </summary>
    [ComponentEntry("UI", "Tree", SingleInstance = true, SortOrder = 23, Icon = "list")]
    public class TreeComponent : ComponentBase
    {
        public override string Name => "Tree";
        public override string Description => "Hierarchical tree container";

        public List<TreeItem> Items { get; } = new();
        public TreeItem? SelectedItem { get; private set; }

        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public Action<TreeItem?>? OnSelected { get; set; }

        public void Select(TreeItem? item, bool notify = true)
        {
            SelectedItem = item;
            if (notify)
            {
                try { OnSelected?.Invoke(item); } catch { }
            }
            MarkDirty();
        }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
