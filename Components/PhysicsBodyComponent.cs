using BepuPhysics;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    public class PhysicsBodyComponent : ComponentBase
    {
        public BodyHandle BodyHandle { get; set; }
        public bool IsKinematic { get; set; } = false;

        public PhysicsBodyComponent() { }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
