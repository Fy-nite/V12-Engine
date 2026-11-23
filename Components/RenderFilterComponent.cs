using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Rendering;

namespace V12.Components
{
    /// <summary>
    /// Per-element render routing. The layer mask controls which views see the
    /// element; <see cref="ViewId"/> restricts it to one explicit view (used by
    /// nested 3D UI), while 0 means every matching view.
    /// </summary>
    [ComponentEntry("RENDER", "Render Filter", SingleInstance = true, SortOrder = 6, Icon = "filter")]
    public class RenderFilterComponent : ComponentBase
    {
        public override string Name => "Render Filter";
        public override string Description => "Layer and view routing for a renderable";

        public uint LayerMask { get; set; } = RenderLayers.Default;

        /// <summary>0 renders in every matching view; otherwise only this view.</summary>
        public long ViewId { get; set; }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }

        public override void BuildInspector(V12.Core.UI.IInspector inspector)
        {
            inspector.Section("Render Filter");
            inspector.String("Layer Mask", () => LayerMask.ToString(), v =>
            {
                LayerMask = uint.TryParse(v, out var parsed) ? parsed : RenderLayers.Default;
            });
            inspector.String("View Id", () => ViewId.ToString(), v =>
            {
                ViewId = long.TryParse(v, out var parsed) ? parsed : 0;
            });
        }
    }
}
