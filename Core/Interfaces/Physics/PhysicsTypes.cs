using System.Numerics;
using V12.Components;

namespace V12.Core.Interfaces.Physics
{
    public struct PhysicsBodyDesc
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public MeshShape Shape;
        public Vector3 Size;
        public float Mass;
        public bool IsKinematic;
        public bool IsTrigger;
        public float GravityScale;

        public PhysicsBodyDesc(Vector3 position, Quaternion rotation, MeshShape shape, Vector3 size,
            float mass = 1f, bool isKinematic = false, bool isTrigger = false, float gravityScale = 1f)
        {
            Position = position;
            Rotation = rotation;
            Shape = shape;
            Size = size;
            Mass = mass;
            IsKinematic = isKinematic;
            IsTrigger = isTrigger;
            GravityScale = gravityScale;
        }
    }

    public struct Ray
    {
        public Vector3 Origin;
        public Vector3 Direction;
        public float MaxDistance;
    }

    public struct RaycastHit
    {
        public Vector3 Point;
        public Vector3 Normal;
        public float Distance;
        public IPhysicsBody Body;
    }
}
