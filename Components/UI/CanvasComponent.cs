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
