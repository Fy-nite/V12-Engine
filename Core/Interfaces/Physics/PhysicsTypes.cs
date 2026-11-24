using System.Numerics;
using V12.Components;
using V12.Core.Interfaces.Renderer;

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
        /// <summary>
        /// Collider shape. <see cref="MeshShape.Custom"/> uses
        /// <see cref="Mesh"/> when the caller supplies one; without a payload the
        /// Bepu backend falls back to the <see cref="Size"/> box dimensions.
        /// </summary>
        public MeshShape Shape;
        public Vector3 Size;
        /// <summary>
        /// Vertex/index payload for <see cref="MeshShape.Custom"/>. Runtime only;
        /// the element's mesh component is supplied by
        /// <c>PhysicsLocomotionSystem</c>, and raw Bepu callers can pass any
        /// <see cref="IMeshRenderable"/> directly.
        /// </summary>
        public IMeshRenderable? Mesh;
        public float Mass;
        public PhysicsBodyType Type;
        /// <summary>
        /// Overlap-only intent. The Bepu backend raises
        /// <see cref="IPhysicsTriggerEvents"/> enter/exit events and suppresses
        /// contact response for trigger bodies. Overlap is tested against the
        /// collider's broadphase bounds, so a trigger should be a volume, not a
        /// precise contact shape.
        /// </summary>
        public bool IsTrigger;
        /// <summary>Multiplier for uniform gravity and point gravity fields. 0 is weightless.</summary>
        public float GravityScale;
        public float LinearDamping;
        public float AngularDamping;
        /// <summary>When false the body is never allowed to sleep.</summary>
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
        /// <summary>
        /// The hit V12 body, or null when the ray hit a backend-native static
        /// with no V12 body. The Bepu backend represents V12 "static" bodies as
        /// kinematic bodies, so a hit on one reports a body here.
        /// </summary>
        public IPhysicsBody? Body;
    }
}
