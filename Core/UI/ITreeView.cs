using System;

namespace V12.UI
{
    /// <summary>
    /// A single node in an <see cref="ITreeView"/>. Items are created and owned
    /// by the caller; the view only renders them and reports interactions.
    /// <see cref="UserData"/> carries whatever the caller attached (e.g. a scene
    /// element, an asset record) back through every callback.
    /// </summary>
    public interface ITreeItem
    {
        /// <summary>Opaque caller data attached at AddItem time. Read-only.</summary>
        object? UserData { get; }

        string Label { get; set; }

        /// <summary>
        /// Key of a well-known icon the provider maps to a glyph (e.g. "element",
        /// "camera", "script", "scene", "folder", "asset"). Unknown keys render no icon.
        /// </summary>
        string? IconKey { get; set; }

        /// <summary>Key of a right-aligned trailing icon (e.g. visibility "eye").</summary>
        string? TrailingIconKey { get => null; set { } }

        /// <summary>Whether the trailing icon reads as active (e.g. element visible).</summary>
        bool TrailingActive { get => false; set { } }

        bool Expanded { get; set; }

        /// <summary>When false the item and its subtree are hidden but not removed.</summary>
        bool Visible { get; set; }
    }

    /// <summary>Where a dropped item landed relative to the hovered item.</summary>
    public enum TreeDropPosition
    {
        /// <summary>Dropped on empty space: move to the root.</summary>
        Root,
        /// <summary>On the item's upper band: insert as the previous sibling.</summary>
        Before,
        /// <summary>On the item's body: make it a child of the item.</summary>
        Child,
        /// <summary>On the item's lower band: insert as the next sibling.</summary>
        After,
    }

    /// <summary>
    /// Collapsible item tree with selection, drag-and-drop reparenting, inline
    /// rename and per-item context menus. The tree does not interpret the
    /// attached user data — callers receive the dragged and target items (with
    /// their UserData) and decide.
    /// </summary>
    public interface ITreeView : IWidget
    {
        /// <summary>Adds an item under <paramref name="parent"/>, or at the root when null.</summary>
        ITreeItem AddItem(ITreeItem? parent, string label, object? userData = null);

        bool RemoveItem(ITreeItem item);
        void Clear();

        ITreeItem? SelectedItem { get; }
        void SetSelected(ITreeItem? item);

        /// <summary>Brings an item into view, expanding ancestors if needed.</summary>
        void ScrollTo(ITreeItem item);

        void SetOnSelectionChanged(Action<ITreeItem?> onSelectionChanged);

        /// <summary>Puts the item into inline edit mode (keyboard rename, e.g. F2).</summary>
        void BeginRename(ITreeItem item);

        /// <summary>Raised when an inline rename is committed with a non-empty label.</summary>
        void SetOnRenamed(Action<ITreeItem, string> onRenamed);

        /// <summary>Enables dragging items onto other items.</summary>
        void SetDropEnabled(bool enabled);

        /// <summary>Raised when an item is dropped: (dragged, target, position).
        /// Target is null for a drop on empty space (position is Root).</summary>
        void SetOnItemDropped(Action<ITreeItem, ITreeItem?, TreeDropPosition> onItemDropped);

        /// <summary>
        /// Asked when the user requests a context menu on an item. Return the menu
        /// to show, or null for no menu. The provider displays and disposes it.
        /// </summary>
        void SetOnContextMenuRequested(Func<ITreeItem, IContextMenu?> onContextMenuRequested);

        /// <summary>Raised when the trailing icon is clicked.</summary>
        void SetOnTrailingIconClicked(Action<ITreeItem> onTrailingIconClicked) { }

        /// <summary>Text shown when the tree has no visible items.</summary>
        void SetEmptyMessage(string message) { }
    }
}
