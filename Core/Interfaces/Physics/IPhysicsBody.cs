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

        /// <summary>Body mass in kilograms. Runtime staging can change this.</summary>
        float Mass { get => 1f; set { } }

        /// <summary>
        /// Multiplier applied to both uniform gravity and point gravity fields.
        /// 0 makes the body weightless without disabling collision response.
        /// </summary>
        float GravityScale { get => 1f; set { } }

        /// <summary>
        /// Applies an instantaneous linear impulse (kg*m/s). Velocity changes by
        /// <c>impulse / Mass</c> immediately.
        /// </summary>
        void AddImpulse(Vector3 impulse) => AddForce(impulse);

        /// <summary>
        /// Applies a continuous force (newtons) for the next physics step.
        /// The backend integrates it over the step, so velocity changes by
        /// <c>force / Mass * deltaTime</c>.
        /// </summary>
        void AddForce(Vector3 force);

        /// <summary>
        /// Applies an instantaneous angular impulse (kg*m^2/s). Requires a finite
        /// inertia tensor; a body with locked or infinite inertia will not rotate.
        /// </summary>
        void AddAngularImpulse(Vector3 impulse) => AddTorque(impulse);

        /// <summary>
        /// Applies a continuous torque (N*m) for the next physics step.
        /// </summary>
        void AddTorque(Vector3 torque);

        void SetKinematic(bool kinematic);
    }
}
