using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    public class XRRootComponent : ComponentBase
    {
        public override string Name => "XRRoot";

        public XRRootComponent() { }

        public override IWorldElement BuildUI() => new Element();
    }
}
