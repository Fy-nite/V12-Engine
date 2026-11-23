using System;
using System.Numerics;

namespace V12.Core.Interfaces.Physics
{
    /// <summary>
    /// A single point-mass gravity source. Position is world space and <see cref="Mu"/>
    /// is the standard gravitational parameter (G * mass). <see cref="SurfaceRadius"/>
    /// is only used to keep the field finite inside the body; it is not a collision shape.
    /// </summary>
    public readonly struct GravitySource
    {
        public readonly Vector3 Position;
        public readonly float Mu;
        public readonly float SurfaceRadius;

        public GravitySource(Vector3 position, float mu, float surfaceRadius = 0f)
        {
            Position = position;
            Mu = mu;
            SurfaceRadius = surfaceRadius;
        }

        /// <summary>Acceleration this source produces at a world position.</summary>
        public Vector3 AccelerationAt(Vector3 position)
        {
            var delta = Position - position;
            float actualRadius = delta.Length();
            if (actualRadius <= 1e-6f) return Vector3.Zero;

            // Outside the body this is the usual mu/r^2. Inside, clamp to the
            // surface value instead of letting the singularity explode, while
            // keeping the direction toward the centre.
            float radius = MathF.Max(actualRadius, SurfaceRadius > 0f ? SurfaceRadius : 1e-4f);
            var direction = delta / actualRadius;
            return direction * (Mu / (radius * radius));
        }
    }

    /// <summary>
    /// A sampled gravity field. Implementations are read on the physics thread
    /// during integration, so they must be immutable for the duration of a step.
    /// </summary>
    public interface IGravityField
    {
        /// <summary>Number of sources currently in the field.</summary>
        int SourceCount { get; }

        /// <summary>
        /// Copies the current sources into <paramref name="destination"/>.
        /// Returns the number copied. The destination may be larger than the field.
        /// </summary>
        int CopySources(Span<GravitySource> destination);

        /// <summary>Acceleration at a world position, summing every source.</summary>
        Vector3 SampleAcceleration(Vector3 position);
    }

    /// <summary>
    /// Optional physics-backend capability: a backend that can apply a point
    /// gravity field in addition to its uniform <see cref="IPhysicsBackend.Gravity"/>.
    /// Bepu implements this; a host fallback may not.
    /// </summary>
    public interface IGravityFieldBackend
    {
        IGravityField? GravityField { get; set; }
    }
}
