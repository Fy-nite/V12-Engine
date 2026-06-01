using System;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>
    /// Example component demonstrating automatic dirty tracking for network synchronization.
    /// </summary>
    public class TransformComponent : ComponentBase
    {
        private float _x;
        private float _y;
        private float _z;
        private float _rotation;
        // Optional per-axis rotation (degrees). Nullable so parser can leave them
        // unset when only legacy `rotation` is present.
        private float? _rotationX;
        private float? _rotationY;
        private float? _rotationZ;

        public override string Name => "Transform";
        public override string Description => "Position and rotation in 3D space";

        public float X
        {
            get => _x;
            set
            {
                if (Math.Abs(_x - value) > 0.001f)
                {
                    _x = value;
                    MarkDirty();
                }
            }
        }

        public float Y
        {
            get => _y;
            set
            {
                if (Math.Abs(_y - value) > 0.001f)
                {
                    _y = value;
                    MarkDirty();
                }
            }
        }

        public float Z
        {
            get => _z;
            set
            {
                if (Math.Abs(_z - value) > 0.001f)
                {
                    _z = value;
                    MarkDirty();
                }
            }
        }

        public float Rotation
        {
            get => _rotation;
            set
            {
                if (Math.Abs(_rotation - value) > 0.001f)
                {
                    _rotation = value;
                    MarkDirty();
                }
            }
        }

        /// <summary>Optional X (pitch) rotation in degrees. Null when not provided by XML.</summary>
        public float? RotationX
        {
            get => _rotationX;
            set
            {
                if ((_rotationX.HasValue != value.HasValue) || (value.HasValue && Math.Abs(_rotationX!.Value - value.Value) > 0.001f))
                {
                    _rotationX = value;
                    MarkDirty();
                }
            }
        }

        /// <summary>Optional Y (yaw) rotation in degrees. Null when not provided by XML.</summary>
        public float? RotationY
        {
            get => _rotationY;
            set
            {
                if ((_rotationY.HasValue != value.HasValue) || (value.HasValue && Math.Abs(_rotationY!.Value - value.Value) > 0.001f))
                {
                    _rotationY = value;
                    MarkDirty();
                }
            }
        }

        /// <summary>Optional Z (roll) rotation in degrees. Null when not provided by XML.</summary>
        public float? RotationZ
        {
            get => _rotationZ;
            set
            {
                if ((_rotationZ.HasValue != value.HasValue) || (value.HasValue && Math.Abs(_rotationZ!.Value - value.Value) > 0.001f))
                {
                    _rotationZ = value;
                    MarkDirty();
                }
            }
        }

        /// <summary>X rotation in radians.</summary>
        public float RX
        {
            get => (RotationX ?? 0) * (MathF.PI / 180f);
            set => RotationX = value * (180f / MathF.PI);
        }

        /// <summary>Y rotation in radians.</summary>
        public float RY
        {
            get => (RotationY ?? Rotation) * (MathF.PI / 180f);
            set => RotationY = value * (180f / MathF.PI);
        }

        /// <summary>Z rotation in radians.</summary>
        public float RZ
        {
            get => (RotationZ ?? 0) * (MathF.PI / 180f);
            set => RotationZ = value * (180f / MathF.PI);
        }

        public TransformComponent()
        {
        }

        public TransformComponent(float x, float y, float z, float rotation = 0f)
        {
            _x = x;
            _y = y;
            _z = z;
            _rotation = rotation;
        }

        public void SetPosition(float x, float y, float z)
        {
            bool changed = false;
            
            if (Math.Abs(_x - x) > 0.001f) { _x = x; changed = true; }
            if (Math.Abs(_y - y) > 0.001f) { _y = y; changed = true; }
            if (Math.Abs(_z - z) > 0.001f) { _z = z; changed = true; }

            if (changed)
            {
                MarkDirty();
            }
        }
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        public override void Update(float deltaTime)
        {
            // Example: could apply physics, interpolation, etc.
        }

        public override void OnAttach(IWorldElement worldElement)
        {
            base.OnAttach(worldElement);
            Console.WriteLine($"[TransformComponent] Attached to {worldElement.Name}");
        }

        public override void OnDetach(IWorldElement worldElement)
        {
            base.OnDetach(worldElement);
            Console.WriteLine($"[TransformComponent] Detached from {worldElement.Name}");
        }

        public override void OnUpdate()
        {
            // Per-frame update logic
        }

        public override void OnDestroy()
        {
            Console.WriteLine($"[TransformComponent] Destroyed");
        }

        public override string ToString()
        {
            return $"Transform(X:{X:F2}, Y:{Y:F2}, Z:{Z:F2}, Rot:{Rotation:F2})";
        }
    }
}
