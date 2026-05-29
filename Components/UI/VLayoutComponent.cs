using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components.UI
{
    /// <summary>
    /// Marks an element as a vertical layout container.
    /// Frontends that read this component should arrange all children
    /// of the owning element top-to-bottom.
    /// </summary>
    public class VLayoutComponent : ComponentBase
    {
        public override string Name => "VLayout";
        public override string Description => "Vertical layout container";

        /// <summary>Pixels (or world-units) of spacing between children.</summary>
        public float Spacing { get; set; } = 4f;

        /// <summary>Uniform padding applied inside all four edges.</summary>
        public float Padding { get; set; } = 0f;
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
