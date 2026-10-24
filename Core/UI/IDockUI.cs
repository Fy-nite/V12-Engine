using System;
using System.Collections.Generic;

namespace V12.UI
{
    /// <summary>Split axis for a dock layout node.</summary>
    public enum DockOrientation
    {
        /// <summary>Panes side by side (vertical divider).</summary>
        Horizontal,
        /// <summary>Panes stacked (horizontal divider).</summary>
        Vertical,
    }

    /// <summary>
    /// Opaque node in a dock layout tree. Build with
    /// <see cref="IDockSpaceWidget.Tabs"/> and <see cref="IDockSpaceWidget.Split"/>.
    /// </summary>
    public interface IDockNode { }

    /// <summary>One tab in a dock leaf: caption plus the widget rendered in its body.</summary>
    public readonly struct DockTab
    {
        public readonly string Title;
        public readonly IWidget Content;
        public readonly bool Closable;

        public DockTab(string title, IWidget content, bool closable = true)
        {
            Title = title ?? "Panel";
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Closable = closable;
        }
    }

    /// <summary>
    /// Optional capability: a provider that can host a real docking layout
    /// (splitters the user drags, tabs that reorder and tear out into floating
    /// windows). Providers without it fall back to plain splitters/tabs.
    ///
    /// This is a separate interface so <see cref="IUIProvider"/> stays usable by
    /// hosts that only have static layout.
    /// </summary>
    public interface IDockProvider
    {
        IDockSpaceWidget CreateDockSpace();
    }

    /// <summary>A docking surface. Compose a tree, then <see cref="SetRoot"/>.</summary>
    public interface IDockSpaceWidget : IWidget
    {
        /// <summary>A leaf holding one or more tabs.</summary>
        IDockNode Tabs(params DockTab[] tabs);

        /// <summary>Two nodes split along an axis; <paramref name="ratio"/> is the first share (0..1).</summary>
        IDockNode Split(DockOrientation orientation, float ratio, IDockNode first, IDockNode second);

        /// <summary>Install the tree. Re-calling with an equal shape keeps the user's layout.</summary>
        void SetRoot(IDockNode root);

        /// <summary>Makes the tab with this title the active tab in its leaf.</summary>
        void ShowTab(string title) { }
    }

    /// <summary>
    /// Optional capability: the provider exposes named UI palettes and can switch
    /// between them at runtime (menu, settings, keyboard shortcut). Hosts the
    /// <see cref="IUIProvider"/> for a themed frontend implement this.
    /// </summary>
    public interface IPaletteProvider
    {
        IReadOnlyList<string> PaletteNames { get; }
        string CurrentPalette { get; }
        void SetPalette(string name);
    }
}
