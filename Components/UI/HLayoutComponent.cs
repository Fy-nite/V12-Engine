using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components.UI
{
    /// <summary>
    /// Marks an element as a horizontal layout container.
    /// Frontends that read this component should arrange all children
    /// of the owning element left-to-right.
    /// </summary>
    public class HLayoutComponent : ComponentBase
    {
        public override string Name => "HLayout";
        public override string Description => "Horizontal layout container";

        /// <summary>Pixels (or world-units) of spacing between children.</summary>
        public float Spacing { get; set; } = 4f;

        /// <summary>Uniform padding applied inside all four edges.</summary>
        public float Padding { get; set; } = 0f;

        /// <summary>
        /// When true, this container absorbs leftover space along the axis its
        /// parent lays out (fills the column when stacked in a VLayout, fills
        /// the row height when stacked in a VLayout). Set on panels that should
        /// stretch to fill the screen, e.g. the main content area.
        /// </summary>
        public bool Expand { get; set; } = false;

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
