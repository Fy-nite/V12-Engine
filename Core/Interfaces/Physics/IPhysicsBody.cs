using System.Numerics;

namespace V12.Core.Interfaces.Physics
{
    public interface IPhysicsBody
    {
        Vector3 Position { get; set; }
        Quaternion Rotation { get; set; }
        Vector3 LinearVelocity { get; set; }
        Vector3 AngularVelocity { get; set; }
        bool IsDynamic { get; }
        /// <summary>
        /// True when the body is a character controller (a move-and-slide body)
        /// rather than a plain physics-server body. Such bodies own their own
        /// collision response, so callers should write <see cref="LinearVelocity"/>
        /// and read back <see cref="Position"/> / <see cref="IsOnFloor"/> instead
        /// of teleporting.
        /// </summary>
        bool IsCharacterController { get; }
        /// <summary>Whether the body is currently resting on the ground (only meaningful for character controllers).</summary>
        bool IsOnFloor { get; }
        /// <summary>When true the backend should keep the body upright and ignore angular motion.</summary>
        bool LockRotation { get; set; }
        void AddForce(Vector3 force);
        void AddTorque(Vector3 torque);
        void SetKinematic(bool kinematic);
    }
}
