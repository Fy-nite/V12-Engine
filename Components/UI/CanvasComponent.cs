using System;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components.UI;

public class CanvasComponent : ComponentBase
{
    public override string Name => "CanvasComponent";
    //TODO: Fix this and add vaules
    public CanvasComponent()
    {
        
    }
    public override IWorldElement BuildUI()
    {
        return new Element();
    }

}
