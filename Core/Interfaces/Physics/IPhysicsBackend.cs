using System.Numerics;

namespace V12.Core.Interfaces.Physics
{
    public interface IPhysicsBackend
    {
        IPhysicsBody CreateBody(in PhysicsBodyDesc desc);
        void DestroyBody(IPhysicsBody body);
        bool Raycast(in Ray ray, out RaycastHit hit);
        void Step(float deltaTime);
    }
}
