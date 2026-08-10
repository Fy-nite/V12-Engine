using System.Numerics;

namespace V12.Core.Interfaces.Physics
{
    public interface IPhysicsBackend
    {
        IPhysicsBody CreateBody(in PhysicsBodyDesc desc);
        void DestroyBody(IPhysicsBody body);
        bool Raycast(in Ray ray, out RaycastHit hit);
        void Step(float deltaTime);

        /// <summary>
        /// Move a kinematic body by <paramref name="delta"/> (world units),
        /// sliding along any surfaces it would collide with. The body's position
        /// is updated; the actual translation applied is returned (usually smaller
        /// than <paramref name="delta"/> when a wall/floor stops it).
        /// </summary>
        Vector3 MoveKinematic(IPhysicsBody body, Vector3 delta);
    }
}
