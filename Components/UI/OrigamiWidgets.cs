using System;
using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components.UI
{
    /// <summary>
    /// Origami property grid. Reflects over the public fields of the owning element's
    /// data components (or of the element named by <see cref="TargetElementId"/>) and
    /// draws Origami's editable field grid for each one.
    /// </summary>
    [ComponentEntry("UI", "Property Grid", SortOrder = 60, Icon = "table-cells")]
    public class PropertyGridComponent : ComponentBase
    {
        public override string Name => "PropertyGrid";
        public override string Description => "Origami property grid (public fields)";

        /// <summary>Element to inspect. 0 inspects the owning element.</summary>
        public long TargetElementId { get; set; }

        /// <summary>Draw a header above each inspected object.</summary>
        public bool ShowHeader { get; set; } = true;

        /// <summary>Starting nesting depth for the grid.</summary>
        public int Depth { get; set; }

        /// <summary>Expand nested objects and lists by default.</summary>
        public bool ExpandByDefault { get; set; }

        public void NotifyChanged() => MarkDirty();

        public override IWorldElement BuildUI() => new Element();
    }

    /// <summary>
    /// Origami tab strip. The selected index is owned by this component; child elements
    /// carrying a <see cref="TabPageComponent"/> whose Index matches are the body.
    /// </summary>
    [ComponentEntry("UI", "Tabs", SortOrder = 61, Icon = "folder")]
    public class TabsComponent : ComponentBase
    {
        public override string Name => "Tabs";
        public override string Description => "Origami tab strip";

        public List<string> Tabs { get; set; } = new();
        public int SelectedIndex { get; set; }

        /// <summary>Pill-style strip instead of the default underline.</summary>
        public bool Pills { get; set; }

        /// <summary>Strip height in pixels; 0 uses the theme default.</summary>
        public float TabHeight { get; set; }

        [BsonIgnore]
        public Action<int>? OnSelected { get; set; }

        public void InvokeSelected(int index)
        {
            SelectedIndex = index;
            try { OnSelected?.Invoke(index); } catch { }
            MarkDirty();
        }

        public override IWorldElement BuildUI() => new Element();
    }

    /// <summary>Marks a child of a <see cref="TabsComponent"/> as the body of one tab.</summary>
    [ComponentEntry("UI", "Tab Page", SortOrder = 62, Icon = "file")]
    public class TabPageComponent : ComponentBase
    {
        public override string Name => "TabPage";
        public override string Description => "Body of one tab in a Tabs component";

        /// <summary>Zero-based index of the tab this page belongs to.</summary>
        public int Index { get; set; }

        public string Title { get; set; } = "";

        public override IWorldElement BuildUI() => new Element();
    }

    public class UITableColumn
    {
        public string Header { get; set; } = "";
        public float Flex { get; set; } = 1f;
        public bool Sortable { get; set; }
    }

    public class UITableRow
    {
        public List<string> Cells { get; set; } = new();
    }

    /// <summary>Origami data table (columns + string rows).</summary>
    [ComponentEntry("UI", "Table", SortOrder = 63, Icon = "table")]
    public class TableComponent : ComponentBase
    {
        public override string Name => "Table";
        public override string Description => "Origami data table";

        public List<UITableColumn> Columns { get; set; } = new();
        public List<UITableRow> Rows { get; set; } = new();

        public float RowHeight { get; set; } = 26f;
        public bool Bordered { get; set; } = true;

        /// <summary>Scroll viewport size; 0 leaves the table auto-sized.</summary>
        public float ScrollWidth { get; set; }
        public float ScrollHeight { get; set; }

        public int SelectedIndex { get; set; }

        [BsonIgnore]
        public Action<int>? OnSelected { get; set; }

        public void InvokeSelected(int index)
        {
            SelectedIndex = index;
            try { OnSelected?.Invoke(index); } catch { }
            MarkDirty();
        }

        public override IWorldElement BuildUI() => new Element();
    }

    /// <summary>Origami dropdown (single choice from a string list).</summary>
    [ComponentEntry("UI", "Dropdown", SortOrder = 64, Icon = "caret-down")]
    public class DropdownComponent : ComponentBase
    {
        public override string Name => "Dropdown";
        public override string Description => "Origami dropdown";

        public List<string> Items { get; set; } = new();
        public int SelectedIndex { get; set; }
        public string Placeholder { get; set; } = "";
        public bool Searchable { get; set; }
        public float Width { get; set; }

        [BsonIgnore]
        public Action<int>? OnSelected { get; set; }

        public void InvokeSelected(int index)
        {
            SelectedIndex = index;
            try { OnSelected?.Invoke(index); } catch { }
            MarkDirty();
        }

        public override IWorldElement BuildUI() => new Element();
    }

    /// <summary>Origami horizontal rule.</summary>
    [ComponentEntry("UI", "Separator", SortOrder = 65, Icon = "minus")]
    public class SeparatorComponent : ComponentBase
    {
        public override string Name => "Separator";
        public override string Description => "Origami separator rule";

        public override IWorldElement BuildUI() => new Element();
    }

    /// <summary>Origami loading spinner.</summary>
    [ComponentEntry("UI", "Spinner", SortOrder = 66, Icon = "spinner")]
    public class SpinnerComponent : ComponentBase
    {
        public override string Name => "Spinner";
        public override string Description => "Origami loading spinner";

        public float Size { get; set; }

        public override IWorldElement BuildUI() => new Element();
    }
}
