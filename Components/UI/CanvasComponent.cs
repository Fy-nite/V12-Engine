using System;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components.UI;
    [ComponentEntry("UI", "Canvas", SingleInstance = true, SortOrder = 26, Icon = "object-group")]
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

    /// <summary>
    /// Screen-space only: draw this canvas as an absolute overlay instead of a
    /// flow row in the UI root. Its content is positioned by
    /// <see cref="AnchorX"/>/<see cref="AnchorY"/> over the viewport, and it does
    /// not push or reflow other canvases. Ignored for world-space canvases.
    /// </summary>
    public bool Overlay { get; set; }

    /// <summary>
    /// Sorting weight across surfaces. Higher draws later. For an overlay
    /// screen canvas it is added to Paper's overlay layer, so a crosshair at
    /// priority 20 draws above an overlay HUD at priority 10. World canvases
    /// use it to order overlapping surfaces at the same distance.
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// False makes a canvas display-only: it still renders, but the canvas box
    /// is skipped as a pointer/focus target. Child widgets are still emitted.
    /// Useful for crosshairs and decorative screen overlays, whose full-viewport
    /// boxes would otherwise swallow clicks meant for gameplay. Applies to
    /// screen-space and world-space canvases.
    /// </summary>
    public bool Interactive { get; set; } = true;

    /// <summary>
    /// Optional world-canvas shader profile. Empty uses the standard Paper
    /// world shader. <c>flat</c> uses a simpler solid/text shader that ignores
    /// pixel-space brush and scissor masks, which is useful for wrist and HUD
    /// panels that need to stay readable at steep angles.
    /// </summary>
    public string WorldShader { get; set; } = string.Empty;

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
