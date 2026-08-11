using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components.UI
{
    public enum SplitterOrientation
    {
        /// <summary>Children are laid out left-to-right with a draggable divider.</summary>
        Horizontal,
        /// <summary>Children are laid out top-to-bottom with a draggable divider.</summary>
        Vertical
    }

    /// <summary>
    /// Marks an element as a splitter: children are laid out along one axis and
    /// a draggable divider between them lets the user resize the panels.
    /// Frontends (e.g. Godot's WorldCanvasSystem) map this to an
    /// HSplitContainer / VSplitContainer.
    /// </summary>
    public class SplitterComponent : ComponentBase
    {
        public override string Name => "Splitter";
        public override string Description => "Resizable panel splitter";

        public SplitterOrientation Orientation { get; set; } = SplitterOrientation.Horizontal;

        /// <summary>
        /// When true, this splitter absorbs leftover space along the axis its
        /// parent lays out (fills the row height when stacked in a VLayout).
        /// </summary>
        public bool Expand { get; set; } = false;

        /// <summary>Gap between children / around the divider, in pixels.</summary>
        public float Spacing { get; set; } = 4f;

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
