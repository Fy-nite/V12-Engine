
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components.UI;

/// <summary>
/// Marks an element as a scrollable region: frontends wrap its children in a
/// scroll container. The element's own size (LayoutElementComponent min or
/// preferred height, or FlexibleHeight inside a layout) bounds the viewport;
/// children that overflow it scroll vertically.
/// </summary>
public class ScrollComponent : ComponentBase
{
    public override string Name => "Scroll";
    public override string Description => "Scrollable region for its children";

    public bool Vertical { get; set; } = true;
    public bool Horizontal { get; set; }

    public override IWorldElement BuildUI() => new Element();
}
