using System;

namespace V12.Components
{
    /// <summary>
    /// Registers a component in the editor's Add Component menu. This replaces
    /// the old hard-coded ComponentCatalog table: stamping this attribute on a
    /// component class is all that is needed for it to appear in the menu.
    /// <para>Category is one of <c>ComponentCatalog.Categories</c>
    /// (TRANSFORM / RENDER / LIGHTING / PHYSICS / AUDIO / GAMEPLAY);
    /// <see cref="SortOrder"/> preserves the menu order within a category.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ComponentEntryAttribute : Attribute
    {
        public string Icon;

        /// <summary>Catalogue group key (e.g. "RENDER").</summary>
        public string Category { get; }

        /// <summary>Name shown in the Add Component menu.</summary>
        public string DisplayName { get; }

        /// <summary>When true the element may hold only one instance of this
        /// component (the add menu blocks a second one).</summary>
        public bool SingleInstance { get; set; } = true;

        /// <summary>Order within the category; lower sorts first.</summary>
        public int SortOrder { get; set; }

        public ComponentEntryAttribute(string category, string displayName, string Icon = null)
        {
            Category = category;
            DisplayName = displayName;
            this.Icon = Icon;
        }
    }
}
