using System.Numerics;
using V12.Components;

namespace V12.Core.Interfaces.Physics
{
    /// <summary>
    /// How the physics backend should treat a body. These are generic physics
    /// primitives; gameplay behavior (input, jumping, WASD movement) belongs in
    /// game code that writes <see cref="IPhysicsBody.LinearVelocity"/> or calls
    /// <see cref="IPhysicsBody.AddForce"/>.
    /// </summary>
    public enum PhysicsBodyType
    {
        /// <summary>Immovable level geometry.</summary>
        Static,
        /// <summary>Moved by code through its transform or velocity, ignores forces.</summary>
        Kinematic,
        /// <summary>Full rigid-body simulation: gravity, forces, collision response.</summary>
        Dynamic,
        /// <summary>Character controller: collision-and-slide movement driven by velocity.</summary>
        Character,
    }

    public struct PhysicsBodyDesc
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public MeshShape Shape;
        public Vector3 Size;
        public float Mass;
        public PhysicsBodyType Type;
        public bool IsTrigger;
        public float GravityScale;
        public float LinearDamping;
        public float AngularDamping;
        public bool CanSleep;
        /// <summary>Keep the body upright by ignoring angular motion (character bodies).</summary>
        public bool LockRotation;

        /// <summary>True for every type except <see cref="PhysicsBodyType.Dynamic"/>.</summary>
        public bool IsKinematic => Type != PhysicsBodyType.Dynamic;
        public bool IsCharacterController => Type == PhysicsBodyType.Character;

        public PhysicsBodyDesc(Vector3 position, Quaternion rotation, MeshShape shape, Vector3 size,
            float mass, PhysicsBodyType type, bool isTrigger = false, float gravityScale = 1f,
            float linearDamping = 0f, float angularDamping = 0f, bool canSleep = true,
            bool lockRotation = false)
        {
            Position = position;
            Rotation = rotation;
            Shape = shape;
            Size = size;
            Mass = mass;
            Type = type;
            IsTrigger = isTrigger;
            GravityScale = gravityScale;
            LinearDamping = linearDamping;
            AngularDamping = angularDamping;
            CanSleep = canSleep;
            LockRotation = lockRotation;
        }

        /// <summary>
        /// Legacy constructor kept for existing callers and scenes. New code
        /// should pass a <see cref="PhysicsBodyType"/> instead.
        /// </summary>
        public PhysicsBodyDesc(Vector3 position, Quaternion rotation, MeshShape shape, Vector3 size,
            float mass = 1f, bool isKinematic = false, bool isTrigger = false, float gravityScale = 1f,
            bool isCharacterController = false)
            : this(position, rotation, shape, size, mass,
                  isCharacterController ? PhysicsBodyType.Character
                  : isKinematic ? PhysicsBodyType.Kinematic
                  : PhysicsBodyType.Dynamic,
                  isTrigger, gravityScale)
        {
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
