using System.Numerics;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

namespace V12.Components
{
    public enum XRPoseTarget
    {
        Head,
        LeftHand,
        RightHand
    }

    public class XRVisualizerComponent : ComponentBase
    {
        public XRPoseTarget Target { get; set; } = XRPoseTarget.Head;
        public Vector3 Scale { get; set; } = new Vector3(0.1f, 0.1f, 0.1f);

        public override string Name => "XRVisualizer";

        public override void Update(float deltaTime)
        {
            if (!Active || Owner == null) return;

            var lt = Owner.LocalTransform;
            lt.Scale = Scale;
            Owner.LocalTransform = lt;
        }

        public override IWorldElement BuildUI()
        {
            return Procedurals.GenBox("XRVisUI", Vector3.One);
        }
    }
}
