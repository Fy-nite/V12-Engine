using System;
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

        public TransformComponent()
        {
        }

        public TransformComponent(float x, float y, float z, float rotation = 0f)
        {
            Id = GenerateId();
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

        public override void Update(float deltaTime)
        {
            // Example: could apply physics, interpolation, etc.
        }

        public override void OnAttach(IWorldElement worldElement)
        {
            Console.WriteLine($"[TransformComponent] Attached to {worldElement.Name}");
        }

        public override void OnDetach(IWorldElement worldElement)
        {
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

        /// <summary>
        /// Mark this component as dirty to trigger network synchronization.
        /// </summary>
        protected new void MarkDirty()
        {
            base.MarkDirty();
        }

        private static long _nextId = 1;
        private static long GenerateId() => System.Threading.Interlocked.Increment(ref _nextId);

        public override string ToString()
        {
            return $"Transform(X:{X:F2}, Y:{Y:F2}, Z:{Z:F2}, Rot:{Rotation:F2})";
        }
    }
}
