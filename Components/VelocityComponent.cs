using System;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.UI;

namespace V12.Components
{
    /// <summary>
    /// Linear and angular velocity of an element.
    /// Used by physics / movement systems; read by the Godot renderer to
    /// drive RigidBody3D velocities.
    /// </summary>
    public class VelocityComponent : ComponentBase
    {
        private float _velX = 0f, _velY = 0f, _velZ = 0f;
        private float _angX = 0f, _angY = 0f, _angZ = 0f;

        public override string Name        => "Velocity";
        public override string Description => "Linear and angular velocity";

        public float VelX { get => _velX; set { if (Math.Abs(_velX - value) > 0.0001f) { _velX = value; MarkDirty(); } } }
        public float VelY { get => _velY; set { if (Math.Abs(_velY - value) > 0.0001f) { _velY = value; MarkDirty(); } } }
        public float VelZ { get => _velZ; set { if (Math.Abs(_velZ - value) > 0.0001f) { _velZ = value; MarkDirty(); } } }

        /// <summary>Angular velocity in degrees/sec around each axis.</summary>
        public float AngX { get => _angX; set { if (Math.Abs(_angX - value) > 0.0001f) { _angX = value; MarkDirty(); } } }
        public float AngY { get => _angY; set { if (Math.Abs(_angY - value) > 0.0001f) { _angY = value; MarkDirty(); } } }
        public float AngZ { get => _angZ; set { if (Math.Abs(_angZ - value) > 0.0001f) { _angZ = value; MarkDirty(); } } }

        public VelocityComponent() { }
        public VelocityComponent(float vx, float vy, float vz, float ax = 0f, float ay = 0f, float az = 0f)
        {
            _velX = vx; _velY = vy; _velZ = vz;
            _angX = ax; _angY = ay; _angZ = az;
        }

        /// <summary>Generate editable velocity fields for the inspector.</summary>
        public override void BuildInspector(IInspector inspector)
        {
            inspector.Section("Velocity");
            inspector.Float("Linear X", () => VelX, v => VelX = v);
            inspector.Float("Linear Y", () => VelY, v => VelY = v);
            inspector.Float("Linear Z", () => VelZ, v => VelZ = v);
            inspector.Float("Angular X", () => AngX, v => AngX = v);
            inspector.Float("Angular Y", () => AngY, v => AngY = v);
            inspector.Float("Angular Z", () => AngZ, v => AngZ = v);
        }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        public override string ToString() =>
            $"Velocity(Lin:{VelX:F2},{VelY:F2},{VelZ:F2} Ang:{AngX:F2},{AngY:F2},{AngZ:F2})";
    }
}
