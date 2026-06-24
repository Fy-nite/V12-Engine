using System.Numerics;

namespace V12.Core.Interfaces.Physics
{
    public interface IPhysicsBody
    {
        Vector3 Position { get; set; }
        Quaternion Rotation { get; set; }
        Vector3 LinearVelocity { get; set; }
        void AddForce(Vector3 force);
    }
}
