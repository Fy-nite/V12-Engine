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

        /// <summary>
        /// Radius of this source's patched-conic frame. Zero means a root source
        /// (normally the star) that applies outside every child frame. When a
        /// point is inside one or more frames, only the smallest frame's source
        /// acts; the full point-mass sum is used only by fields that author no
        /// frames at all. This keeps a body on rails from being dragged out of
        /// its local orbit by the primary's un-cancelled pull.
        /// </summary>
        public readonly float SphereOfInfluence;

        public GravitySource(
            Vector3 position,
            float mu,
            float surfaceRadius = 0f,
            float sphereOfInfluence = 0f)
        {
            Position = position;
            Mu = mu;
            SurfaceRadius = surfaceRadius;
            SphereOfInfluence = sphereOfInfluence;
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

        /// <summary>Acceleration at a world position, using the patched-conic
        /// frame selection described on <see cref="GravitySource.SphereOfInfluence"/>.</summary>
        Vector3 SampleAcceleration(Vector3 position);
    }

    /// <summary>
    /// Shared sampling for point-mass fields. A field with no authored
    /// spheres of influence keeps the old behaviour (sum every source); once a
    /// source authors one, the smallest containing frame owns the point and the
    /// rest of the field is ignored until the point leaves it.
    /// </summary>
    public static class GravitySources
    {
        public static Vector3 SampleAcceleration(
            ReadOnlySpan<GravitySource> sources,
            Vector3 position)
        {
            int patchedIndex = -1;
            float smallestSoi = float.MaxValue;
            bool hasPatchedFrame = false;

            for (int i = 0; i < sources.Length; i++)
            {
                float soi = sources[i].SphereOfInfluence;
                if (soi <= 0f || sources[i].Mu <= 0f)
                    continue;

                hasPatchedFrame = true;
                float distance = (sources[i].Position - position).Length();
                if (distance > soi || soi >= smallestSoi)
                    continue;

                smallestSoi = soi;
                patchedIndex = i;
            }

            if (patchedIndex >= 0)
            {
                // A body on rails accelerates with its primary, but the physics
                // engine only integrates the ship. Add the primary's pull at the
                // ship minus its pull at the local body centre: the uniform part
                // cancels the frame acceleration and the remainder is the real
                // third-body tidal term. Only ancestors (roots, or frames larger
                // than this one) contribute; siblings and children do not drive
                // the local frame.
                var local = sources[patchedIndex];
                var localAcceleration = local.AccelerationAt(position);
                for (int i = 0; i < sources.Length; i++)
                {
                    if (i == patchedIndex || sources[i].Mu <= 0f)
                        continue;

                    float otherSoi = sources[i].SphereOfInfluence;
                    bool ancestor = otherSoi <= 0f || otherSoi > local.SphereOfInfluence;
                    if (!ancestor)
                        continue;

                    localAcceleration += sources[i].AccelerationAt(position)
                                       - sources[i].AccelerationAt(local.Position);
                }

                return localAcceleration;
            }

            var acceleration = Vector3.Zero;
            if (hasPatchedFrame)
            {
                // Outside every child frame, only roots (SOI == 0) act. If a
                // field forgot to author a root, fall through to the old sum so
                // gameplay still gets a field instead of free fall.
                bool appliedRoot = false;
                for (int i = 0; i < sources.Length; i++)
                {
                    if (sources[i].SphereOfInfluence > 0f)
                        continue;

                    acceleration += sources[i].AccelerationAt(position);
                    appliedRoot = true;
                }

                if (appliedRoot)
                    return acceleration;
            }

            for (int i = 0; i < sources.Length; i++)
                acceleration += sources[i].AccelerationAt(position);

            return acceleration;
        }
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
