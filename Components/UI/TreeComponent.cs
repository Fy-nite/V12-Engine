using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components.UI
{
    /// <summary>
    /// Marks an element as a hierarchical tree container.
    ///
    /// Frontends (e.g. Godot's WorldCanvasSystem) render this as a native Tree
    /// control. The element's own child tree IS the hierarchy: each descendant
    /// element becomes a TreeItem, nested by its element nesting, with native
    /// collapse arrows and indentation. Selection invokes the descendant's
    /// ButtonComponent click (if present), so tree rows behave like buttons.
    /// </summary>
    public class TreeComponent : ComponentBase
    {
        public override string Name => "Tree";
        public override string Description => "Hierarchical tree container";

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
