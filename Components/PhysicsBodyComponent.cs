using BepuPhysics;
using V12.Core;
using V12.Core.Core.Interfaces;
using MongoDB.Bson.Serialization.Attributes;

namespace V12.Components
{
    public class PhysicsBodyComponent : ComponentBase
    {
        [BsonIgnore]
        public BodyHandle BodyHandle { get; set; }
        public bool IsKinematic { get; set; } = false;

        public PhysicsBodyComponent() { }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
