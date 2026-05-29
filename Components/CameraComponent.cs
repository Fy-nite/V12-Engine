using System;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    public enum CameraProjection { Perspective, Orthographic }

    /// <summary>
    /// Defines a camera view attached to an element.
    /// IsCurrent = true makes this the active scene camera when synced to Godot.
    /// </summary>
    public class CameraComponent : ComponentBase
    {
        private CameraProjection _projection  = CameraProjection.Perspective;
        private float            _fov         = 75f;
        private float            _nearClip    = 0.05f;
        private float            _farClip     = 1000f;
        private float            _orthoSize   = 5f;
        private bool             _isCurrent   = false;

        public override string Name        => "Camera";
        public override string Description => "Scene camera";

        public CameraProjection Projection
        {
            get => _projection;
            set { if (_projection != value) { _projection = value; MarkDirty(); } }
        }
        /// <summary>Vertical field-of-view in degrees (perspective mode only).</summary>
        public float Fov
        {
            get => _fov;
            set { if (Math.Abs(_fov - value) > 0.001f) { _fov = Math.Clamp(value, 1f, 179f); MarkDirty(); } }
        }
        public float NearClip
        {
            get => _nearClip;
            set { if (Math.Abs(_nearClip - value) > 0.0001f) { _nearClip = MathF.Max(0.001f, value); MarkDirty(); } }
        }
        public float FarClip
        {
            get => _farClip;
            set { if (Math.Abs(_farClip - value) > 0.001f) { _farClip = MathF.Max(_nearClip + 0.1f, value); MarkDirty(); } }
        }
        /// <summary>Half-size in world units for orthographic projection.</summary>
        public float OrthoSize
        {
            get => _orthoSize;
            set { if (Math.Abs(_orthoSize - value) > 0.001f) { _orthoSize = MathF.Max(0.01f, value); MarkDirty(); } }
        }
        /// <summary>If true, this camera becomes the active camera when the element enters the scene.</summary>
        public bool IsCurrent
        {
            get => _isCurrent;
            set { if (_isCurrent != value) { _isCurrent = value; MarkDirty(); } }
        }

        public CameraComponent() { }
        public CameraComponent(float fov, float nearClip = 0.05f, float farClip = 1000f, bool isCurrent = false)
        {
            _fov = fov; _nearClip = nearClip; _farClip = farClip; _isCurrent = isCurrent;
        }
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        public override string ToString() =>
            $"Camera(Proj:{Projection} FOV:{Fov:F1} Near:{NearClip:F3} Far:{FarClip:F1} Current:{IsCurrent})";
    }
}
