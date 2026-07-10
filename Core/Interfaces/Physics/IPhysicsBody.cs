using System.Numerics;

namespace V12.Core.Interfaces.Physics
{
    public interface IPhysicsBody
    {
        Vector3 Position { get; set; }
        Quaternion Rotation { get; set; }
        Vector3 LinearVelocity { get; set; }
        bool IsDynamic { get; }
        void AddForce(Vector3 force);
        void SetKinematic(bool kinematic);
    }
}
// hey guys... my penis hurts (╥﹏╥)