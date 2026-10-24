using System;

namespace V12.UI
{
    /// <summary>
    /// A popup menu assembled on demand and shown at a position. The provider
    /// owns the underlying native object and reclaims it after it closes.
    /// </summary>
    public interface IContextMenu : IWidget
    {
        /// <param name="icon">Host-defined icon key (e.g. "cube"); may be ignored.</param>
        void AddItem(string label, Action onClick, bool enabled = true, string icon = "");
        void AddSeparator();

        /// <summary>
        /// Adds a submenu: an item that opens <paramref name="submenu"/> (its
        /// own items) when hovered or clicked. The submenu is owned by this
        /// menu once added. Selecting any of its items closes this menu too.
        /// </summary>
        void AddSubmenu(string label, IContextMenu submenu, string icon = "");

        /// <summary>
        /// Ask the host to show a search field at the top of the menu. Hosts that
        /// support it flatten the submenu tree into a filterable list; others ignore it.
        /// </summary>
        void Searchable(string placeholder = "Search...") { }

        /// <summary>Shows the menu with its top-left corner at the given screen position.</summary>
        void Show(float screenX, float screenY);

        /// <summary>Shows the menu just below the given anchor widget.</summary>
        void ShowAt(IWidget anchor);
    }
}
