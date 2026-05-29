using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;
using V12.Core.Core.Interfaces;

namespace V12.Core.Systems
{
    public class PhysicsService : IGameService
    {
        public Simulation Simulation { get; private set; }
        private BufferPool _bufferPool;
        private ThreadDispatcher _threadDispatcher;

        public void Initialize(GameRoot g)
        {
            _bufferPool = new BufferPool();
            _threadDispatcher = new ThreadDispatcher(Environment.ProcessorCount); 
            
            // Simulation.Create now requires a SolveDescription
            Simulation = Simulation.Create(_bufferPool, new NarrowPhaseCallbacks(), new PoseIntegratorCallbacks(new Vector3(0, -9.81f, 0)), new SolveDescription(8, 1));
        }

        public void Update(float deltaTime)
        {
            if (deltaTime <= 0) return;
            Simulation.Timestep(deltaTime, _threadDispatcher);
        }

        public BodyHandle CreateBodyForElement(V12.Core.Core.Interfaces.IWorldElement element)
        {
            var shape = new Box(1f, 2f, 1f); // Default size
            var shapeIndex = Simulation.Shapes.Add(shape);
            
            // Get transform if available, default to 1.5 height
            var transform = element.GetComponent<V12.Components.TransformComponent>();
            var pos = transform != null ? new System.Numerics.Vector3(transform.X, transform.Y, transform.Z) : new System.Numerics.Vector3(0, 1.5f, 0);

            var physicsComp = element.GetComponent<V12.Components.PhysicsBodyComponent>();
            var description = physicsComp != null && physicsComp.IsKinematic 
                ? BodyDescription.CreateKinematic(new RigidPose(pos), new CollidableDescription(shapeIndex, 0.1f), new BodyActivityDescription(0.01f))
                : BodyDescription.CreateDynamic(new RigidPose(pos), new BodyInertia { InverseMass = 1f }, new CollidableDescription(shapeIndex, 0.1f), new BodyActivityDescription(0.01f));
            
            return Simulation.Bodies.Add(description);
        }
    }

    public struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
    {
        public void Initialize(Simulation simulation) { }
        public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin) => true;
        public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) => true;

        public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial) where TManifold : unmanaged, IContactManifold<TManifold>
        {
            pairMaterial = new PairMaterialProperties(1f, 0.1f, new SpringSettings(30, 1));
            return true;
        }

        public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold)
        {
            return true;
        }
        public void Dispose() { }
    }

    public struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
    {
        public Vector3 Gravity;
        Vector3Wide gravityWide;

        // Use DontIntegrate instead of Nonintegrated
        public readonly AngularIntegrationMode AngularIntegrationMode => (AngularIntegrationMode)0;
        public readonly bool AllowSubsteppingForFocusBodies => false;
        public bool AllowSubstepsForUnconstrainedBodies => false;
        public readonly bool IntegrateVelocityForKinematics => false;

        public PoseIntegratorCallbacks(Vector3 gravity)
        {
            Gravity = gravity;
            gravityWide = default;
        }

        public void Initialize(Simulation simulation)
        {
            Vector3Wide.Broadcast(Gravity, out gravityWide);
        }

        public void PrepareForMultithreadedExecution(int workerCount) { }

        public void PrepareForIntegration(float dt) { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation, BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity)
        {
            Vector3Wide.Scale(gravityWide, dt, out var gravityDelta);
            Vector3Wide.Add(velocity.Linear, gravityDelta, out velocity.Linear);
        }
    }
}
