using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    public class XRHeadComponent : ComponentBase
    {
        public override string Name => "XRHead";

        public XRHeadComponent() { }

        public override IWorldElement BuildUI() => new Element();
    }
}
