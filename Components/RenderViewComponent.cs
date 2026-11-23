using System;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Rendering;

namespace V12.Components
{
    /// <summary>
    /// Defines one camera view: which world it renders, which camera it uses,
    /// what it clears to, and which layers it sees.
    ///
    /// The view is independent of the UI surface that displays it. A
    /// <see cref="V12.Components.UI.ViewportComponent"/> on the same element (or
    /// on another element referenced by <see cref="ViewportId"/>) supplies the
    /// screen rectangle; this component supplies the render target contents.
    /// </summary>
    [ComponentEntry("RENDER", "Render View", SingleInstance = true, SortOrder = 5, Icon = "window-maximize")]
    public class RenderViewComponent : ComponentBase
    {
        public override string Name => "Render View";
        public override string Description => "Camera view rendered into a viewport surface";

        /// <summary>
        /// Element id of the <see cref="V12.Components.UI.ViewportComponent"/>
        /// surface that displays this view. 0 uses this element itself, which is
        /// the common case for a panel that owns both components.
        /// </summary>
        public long ViewportId { get; set; }

        /// <summary>
        /// World to render. <see cref="Guid.Empty"/> renders the selected world.
        /// Set an explicit id for an editor scene view, a play world, or a
        /// preview world.
        /// </summary>
        public Guid WorldId { get; set; }

        /// <summary>
        /// Camera element id. 0 uses the first current camera in the view's
        /// world. Editors usually set this explicitly so the scene camera and
        /// the game camera can coexist.
        /// </summary>
        public long CameraId { get; set; }

        /// <summary>Layers this view renders. Defaults to the scene layer.</summary>
        public uint LayerMask { get; set; } = RenderLayers.Default;

        /// <summary>How the view clears before drawing.</summary>
        public ClearMode Clear { get; set; } = ClearMode.Sky;

        /// <summary>Clear colour as "#rrggbb" or "#aarrggbb".</summary>
        public string ClearColor { get; set; } = "#1a1a20";

        /// <summary>Draw order for overlapping views. Higher draws later.</summary>
        public int Priority { get; set; }

        /// <summary>False keeps the view's render target alive but stops updating it.</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Explicit target width in pixels. 0 uses the UI surface rect. Set both
        /// Width and Height for an offscreen view that has no UI surface.
        /// </summary>
        public int Width { get; set; }

        /// <summary>Explicit target height in pixels. 0 uses the UI surface rect.</summary>
        public int Height { get; set; }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }

        public override void BuildInspector(V12.Core.UI.IInspector inspector)
        {
            inspector.Section("Render View");
            inspector.String("Viewport Id", () => ViewportId.ToString(), v =>
            {
                ViewportId = long.TryParse(v, out var parsed) ? parsed : 0;
            });
            inspector.String("World Id", () => WorldId.ToString(), v =>
            {
                WorldId = Guid.TryParse(v, out var parsed) ? parsed : Guid.Empty;
            });
            inspector.String("Camera Id", () => CameraId.ToString(), v =>
            {
                CameraId = long.TryParse(v, out var parsed) ? parsed : 0;
            });
            inspector.Enum("Clear", () => Clear, v => Clear = v);
            inspector.String("Clear Color", () => ClearColor, v => ClearColor = v);
            inspector.Bool("Active", () => IsActive, v => IsActive = v);
        }
    }
}
