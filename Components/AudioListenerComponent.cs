using System;
using System.Numerics;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    public class AudioListenerComponent : ComponentBase, IAudioListener
    {
        public override string Name => "AudioListener";
        public override string Description => "3D audio listener position and orientation";

        public Vector3 Position
        {
            get
            {
                var t = Owner?.GetComponent<TransformComponent>();
                return t != null ? new Vector3(t.X, t.Y, t.Z) : Vector3.Zero;
            }
        }

        public Vector3 Forward
        {
            get
            {
                float yaw = GetEffectiveYaw();
                float pitch = GetEffectivePitch();
                return Vector3.Normalize(new Vector3(
                    MathF.Cos(pitch) * MathF.Sin(yaw),
                    MathF.Sin(pitch),
                    MathF.Cos(pitch) * MathF.Cos(yaw)
                ));
            }
        }

        public Vector3 Up
        {
            get
            {
                float yaw = GetEffectiveYaw();
                float pitch = GetEffectivePitch();
                var fwd = new Vector3(
                    MathF.Cos(pitch) * MathF.Sin(yaw),
                    MathF.Sin(pitch),
                    MathF.Cos(pitch) * MathF.Cos(yaw)
                );
                var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
                return Vector3.Normalize(Vector3.Cross(right, fwd));
            }
        }

        private float GetEffectiveYaw()
        {
            var t = Owner?.GetComponent<TransformComponent>();
            float yaw = t?.RY ?? 0f;
            if (Owner?.Parent != null)
            {
                var parentT = Owner.Parent.GetComponent<TransformComponent>();
                if (parentT != null)
                    yaw += parentT.RY;
            }
            return yaw;
        }

        private float GetEffectivePitch()
        {
            return Owner?.GetComponent<TransformComponent>()?.RX ?? 0f;
        }

        public AudioListenerComponent() { }

        public override IWorldElement BuildUI() => new Element();

        public override string ToString() =>
            $"AudioListener(Pos:{Position})";
    }
}
