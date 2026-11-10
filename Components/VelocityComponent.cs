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
    [ComponentEntry("PHYSICS", "Velocity", SingleInstance = true, SortOrder = 2, Icon = "gauge")]
    public class VelocityComponent : ComponentBase
    {
        private float _velX = 0f, _velY = 0f, _velZ = 0f;
        private float _angX = 0f, _angY = 0f, _angZ = 0f;

        public override string Name        => "Velocity";
        public override string Description => "Linear and angular velocity";

        public float VelX { get => _velX; set => SetSync(ref _velX, value, 0.0001f); }
        public float VelY { get => _velY; set => SetSync(ref _velY, value, 0.0001f); }
        public float VelZ { get => _velZ; set => SetSync(ref _velZ, value, 0.0001f); }

        /// <summary>Angular velocity in degrees/sec around each axis.</summary>
        public float AngX { get => _angX; set => SetSync(ref _angX, value, 0.0001f); }
        public float AngY { get => _angY; set => SetSync(ref _angY, value, 0.0001f); }
        public float AngZ { get => _angZ; set => SetSync(ref _angZ, value, 0.0001f); }

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
