using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    public enum HandSide
    {
        Left,
        Right
    }

    public class XRHandComponent : ComponentBase
    {
        public HandSide Side { get; set; } = HandSide.Right;

        public override string Name => "XRHand";

        public XRHandComponent() { }

        public XRHandComponent(HandSide side)
        {
            Side = side;
        }

        public override IWorldElement BuildUI() => new Element();
    }
}
