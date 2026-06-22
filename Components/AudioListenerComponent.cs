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
                if (Owner == null) return Vector3.Zero;
                var m = Owner.WorldTransform;
                return new Vector3(m.M41, m.M42, m.M43);
            }
        }

        public Vector3 Forward
        {
            get
            {
                if (Owner == null) return -Vector3.UnitZ;
                var m = Owner.WorldTransform;
                var fwd = new Vector3(m.M31, m.M32, m.M33);
                return fwd.LengthSquared() > 0.0001f ? Vector3.Normalize(fwd) : -Vector3.UnitZ;
            }
        }

        public Vector3 Up
        {
            get
            {
                if (Owner == null) return Vector3.UnitY;
                var m = Owner.WorldTransform;
                var up = new Vector3(m.M21, m.M22, m.M23);
                return up.LengthSquared() > 0.0001f ? Vector3.Normalize(up) : Vector3.UnitY;
            }
        }

        public AudioListenerComponent() { }

        public override IWorldElement BuildUI() => new Element();

        public override string ToString() =>
            $"AudioListener(Pos:{Position})";
    }
}
