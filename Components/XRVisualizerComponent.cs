using System;
using System.Numerics;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Input;
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

        private TRS _prevTransform;

        public override void Update(float deltaTime)
        {
            Console.WriteLine("Updating");
            if (!Active || Owner == null) return;

            var root = GameRoot.Instance;
            if (root == null) return;

            var provider = root.Registry.Get<IVRInputProvider>("IVRInputProvider");
            if (provider == null) return;

            Vector3 pos;
            Quaternion rot;

            switch (Target)
            {
                case XRPoseTarget.LeftHand:
                    pos = provider.LeftHandPosition;
                    rot = provider.LeftHandOrientation;
                    break;
                case XRPoseTarget.RightHand:
                    pos = provider.RightHandPosition;
                    rot = provider.RightHandOrientation;
                    break;
                case XRPoseTarget.Head:
                default:
                    pos = provider.HeadPosition;
                    rot = provider.HeadOrientation;
                    break;
            }
            Console.WriteLine($"POS {pos}, ROT {rot}");
            var lt = new TRS { Position = pos, Rotation = rot, Scale = Scale };
            if (lt.Position != _prevTransform.Position || lt.Rotation != _prevTransform.Rotation)
            {
                Owner.LocalTransform = lt;
                _prevTransform = lt;
            }
        }

        public override IWorldElement BuildUI()
        {
            return Procedurals.GenBox("XRVisUI", Vector3.One);
        }
    }
}
