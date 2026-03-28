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
    }
}
