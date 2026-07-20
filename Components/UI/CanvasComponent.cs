using System;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components.UI;

public class CanvasComponent : ComponentBase
{
    public override string Name => "CanvasComponent";
    private int _x, _y;
    public int X => _x;
    public int Y => _y;

    /// <summary>
    /// When true, this canvas renders directly to screen space (like an IWidget)
    /// instead of baking to a 3D world quad. The canvas UI appears as an overlay.
    /// </summary>
    public bool ScreenSpace { get; set; } = false;

    /// <summary>Normalized anchor in screen space (0-1). (0,0) = top-left, (1,1) = bottom-right.</summary>
    public float AnchorX { get; set; } = 0.5f;
    public float AnchorY { get; set; } = 0.5f;

    /// <summary>Size in screen-space pixels. 0 = auto-size from content.</summary>
    public int Width { get; set; } = 0;
    public int Height { get; set; } = 0;

    public CanvasComponent()
    {
        _x = 0;
        _y = 0;
    }
    public CanvasComponent(int x, int y)
    {
        _x = x; _y = y;
    }
    public override IWorldElement BuildUI()
    {
        return new Element();
    }

}
