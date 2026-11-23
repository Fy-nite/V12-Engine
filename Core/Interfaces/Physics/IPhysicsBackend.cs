using System.Numerics;

namespace V12.Core.Interfaces.Physics
{
    public interface IPhysicsBackend
    {
        /// <summary>Selection priority when several backends are registered. Higher wins.</summary>
        int Priority { get; }

        /// <summary>When true the backend advances itself; the engine should not call <see cref="Step"/>.</summary>
        bool AutoStep { get; set; }

        /// <summary>
        /// Uniform gravity applied to dynamic bodies, in addition to any
        /// <see cref="IGravityField"/> the backend supports. Set to zero for a
        /// pure point-gravity scene.
        /// </summary>
        Vector3 Gravity { get; set; }

        IPhysicsBody CreateBody(in PhysicsBodyDesc desc);
        void DestroyBody(IPhysicsBody body);
        bool Raycast(in Ray ray, out RaycastHit hit);

        /// <summary>
        /// Advance the simulation by <paramref name="deltaTime"/>. The engine's
        /// fixed clock calls this with a constant step; backends should not use
        /// wall-clock time here.
        /// </summary>
        void Step(float deltaTime);

        /// <summary>
        /// Move a kinematic body by <paramref name="delta"/> (world units). The
        /// body's position is updated and the actual translation applied is
        /// returned. A backend that supports collide-and-slide may return less
        /// than <paramref name="delta"/>; a backend without it returns the full
        /// delta.
        /// </summary>
        Vector3 MoveKinematic(IPhysicsBody body, Vector3 delta);

        /// <summary>
        /// Move a kinematic body by <paramref name="delta"/> over <paramref name="deltaTime"/>
        /// (seconds) as continuous motion. Backends should publish
        /// <c>delta / deltaTime</c> as the body's linear velocity and let their
        /// integrator advance the pose, so contacts solve against the same surface
        /// velocity the body is actually moving at. Do not also write the pose:
        /// Bepu integrates kinematic velocities, and a pose write would double-move
        /// the body.
        /// <para>
        /// Use this for moving platforms, planets and other scripted kinematic
        /// bodies. Use <see cref="MoveKinematic(IPhysicsBody, Vector3)"/> for a
        /// teleport or a collide-and-slide move (XR player, grabbed props).
        /// </para>
        /// <para>
        /// The default forwards to <see cref="MoveKinematic(IPhysicsBody, Vector3)"/>
        /// so existing backends keep their behaviour.
        /// </para>
        /// </summary>
        Vector3 MoveKinematic(IPhysicsBody body, Vector3 delta, float deltaTime)
            => MoveKinematic(body, delta);
    }
}
