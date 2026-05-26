using System;
using System;
using System.Numerics;
using V12.Core;

namespace V12.Components
{
    public class LocomotionComponent : ComponentBase
    {
        public override string Name => "Locomotion";
        public override string Description => "State for player locomotion";

        public Vector3 Velocity { get; set; } = Vector3.Zero;
        public bool IsGrounded { get; set; } = true;

        public float MoveSpeed { get; set; } = 2.0f;
        public float JumpStrength { get; set; } = 3.0f;
        public float Gravity { get; set; } = 9.8f;
    }
}
