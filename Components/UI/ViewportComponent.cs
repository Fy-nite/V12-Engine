using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components.UI
{
    /// <summary>
    /// Marks an element as a 3D scene viewport embedded in UI.
    ///
    /// Frontends (e.g. Godot's WorldCanvasSystem) render this as a
    /// SubViewportContainer + SubViewport inside the screen-space canvas.
    /// Every renderable element whose parent chain passes through an element
    /// bearing this component renders its 3D nodes inside that viewport
    /// instead of the main screen, giving panels like an editor's game view
    /// their own camera and scene.
    /// </summary>
    public class ViewportComponent : ComponentBase
    {
        public override string Name => "Viewport";
        public override string Description => "Embeds a 3D scene viewport in UI";

        /// <summary>
        /// When true, the viewport renders the entire world it belongs to
        /// (everything not already claimed by a nested viewport), not just the
        /// elements under this element. Useful for an editor "game view" that
        /// shows the selected world.
        /// </summary>
        public bool RenderWorld { get; set; } = false;

        /// <summary>Background color of the viewport's clear colour.</summary>
        public string BackgroundColor { get; set; } = "#1a1a20";

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
