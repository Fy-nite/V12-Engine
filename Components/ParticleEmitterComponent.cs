using System;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>
    /// GPU particle emitter settings.
    /// Direction X/Y/Z define the emission direction vector (normalised on the Godot side).
    /// SpreadAngle (degrees) adds randomness around that direction.
    /// </summary>
    public class ParticleEmitterComponent : ComponentBase
    {
        private int   _amount        = 32;
        private float _lifetime      = 2f;
        private float _emissionRadius = 0f;
        private float _speedMin      = 1f;
        private float _speedMax      = 3f;
        private float _dirX          = 0f;
        private float _dirY          = 1f;
        private float _dirZ          = 0f;
        private float _spreadAngle   = 45f;
        private bool  _emitting      = true;
        private bool  _oneShot       = false;

        public override string Name        => "ParticleEmitter";
        public override string Description => "GPU particle emitter";

        public int   Amount         { get => _amount;        set { if (_amount != value)                              { _amount = Math.Max(1, value); MarkDirty(); } } }
        public float Lifetime       { get => _lifetime;      set { if (Math.Abs(_lifetime - value) > 0.001f)         { _lifetime = MathF.Max(0.01f, value); MarkDirty(); } } }
        public float EmissionRadius { get => _emissionRadius;set { if (Math.Abs(_emissionRadius - value) > 0.0001f)  { _emissionRadius = MathF.Max(0f, value); MarkDirty(); } } }
        public float SpeedMin       { get => _speedMin;      set { if (Math.Abs(_speedMin - value) > 0.001f)         { _speedMin = MathF.Max(0f, value); MarkDirty(); } } }
        public float SpeedMax       { get => _speedMax;      set { if (Math.Abs(_speedMax - value) > 0.001f)         { _speedMax = MathF.Max(_speedMin, value); MarkDirty(); } } }
        public float DirX           { get => _dirX;          set { if (Math.Abs(_dirX - value) > 0.0001f)            { _dirX = value; MarkDirty(); } } }
        public float DirY           { get => _dirY;          set { if (Math.Abs(_dirY - value) > 0.0001f)            { _dirY = value; MarkDirty(); } } }
        public float DirZ           { get => _dirZ;          set { if (Math.Abs(_dirZ - value) > 0.0001f)            { _dirZ = value; MarkDirty(); } } }
        /// <summary>Random spread angle in degrees around the direction vector.</summary>
        public float SpreadAngle    { get => _spreadAngle;   set { if (Math.Abs(_spreadAngle - value) > 0.001f)      { _spreadAngle = Math.Clamp(value, 0f, 180f); MarkDirty(); } } }
        public bool  Emitting       { get => _emitting;      set { if (_emitting != value)                           { _emitting = value; MarkDirty(); } } }
        /// <summary>If true, emits one burst then stops.</summary>
        public bool  OneShot        { get => _oneShot;       set { if (_oneShot != value)                            { _oneShot = value; MarkDirty(); } } }

        public ParticleEmitterComponent() { }
        public ParticleEmitterComponent(int amount, float lifetime, float speedMin = 1f, float speedMax = 3f)
        {
            _amount = amount; _lifetime = lifetime; _speedMin = speedMin; _speedMax = speedMax;
        }
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        public override string ToString() =>
            $"Particles(Amount:{Amount} Life:{Lifetime:F2}s Speed:{SpeedMin:F1}–{SpeedMax:F1} Emitting:{Emitting})";
    }
}
