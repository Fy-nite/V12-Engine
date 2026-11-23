using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components.UI
{
    /// <summary>
    /// A UI surface that displays a render view's texture.
    ///
    /// This component only owns presentation: the screen rectangle, background
    /// and active state. The camera, world, layers and clear mode belong to the
    /// <see cref="V12.Components.RenderViewComponent"/> that feeds the surface.
    /// </summary>
    [ComponentEntry("UI", "Viewport", SingleInstance = true, SortOrder = 25, Icon = "window-maximize")]
    public class ViewportComponent : ComponentBase
    {
        public override string Name => "Viewport";
        public override string Description => "Displays a render view inside UI";

        /// <summary>
        /// Element id of the <see cref="V12.Components.RenderViewComponent"/> to
        /// display. 0 uses a render view on this element, or the element id as
        /// the implicit view id.
        /// </summary>
        public long ViewId { get; set; }

        /// <summary>Background colour of the surface as "#rrggbb" or "#aarrggbb".</summary>
        public string BackgroundColor { get; set; } = "#1a1a20";

        /// <summary>False stops laying out and drawing the surface.</summary>
        public bool IsActive { get; set; } = true;

        public override IWorldElement BuildUI()
        {
            return new Element();
        }

        public override void BuildInspector(V12.Core.UI.IInspector inspector)
        {
            inspector.Section("Viewport");
            inspector.String("View Id", () => ViewId.ToString(), v =>
            {
                ViewId = long.TryParse(v, out var parsed) ? parsed : 0;
            });
            inspector.String("Background Color", () => BackgroundColor, v => BackgroundColor = v);
            inspector.Bool("Active", () => IsActive, v => IsActive = v);
        }
    }
}
