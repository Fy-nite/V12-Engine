using System;
using System.Collections.Generic;
using System.Numerics;
using DrawingColor = System.Drawing.Color;

namespace V12.Core.UI
{
    /// <summary>
    /// The canonical transform gizmo — one renderer-independent model every
    /// host draws, so the editor looks and grabs identically in MonoGame,
    /// Godot or anything else. Pure geometry: world-space segments tagged with
    /// the handle they belong to, sized to hold a constant fraction of the
    /// viewport while dollying.
    ///
    /// Handle ids: 0 / 1 / 2 = X / Y / Z axis (arrow for translate, ring for
    /// rotate, shaft + tip cube for scale), <see cref="CenterHandle"/> =
    /// uniform-scale centre cube (scale mode only). Every drawn segment is
    /// grabbable — what you see is what you can drag.
    ///
    /// The interaction (grab tests, drag math) lives in
    /// <see cref="ViewportInteractionService"/>; hosts only project and draw
    /// the segments this model returns.
    /// </summary>
    public static class TransformGizmo
    {
        /// <summary>Handle id of the uniform-scale centre cube.</summary>
        public const int CenterHandle = 6;

        /// <summary>Shaft length as a fraction of the viewport half-height at
        /// the target's distance — constant on screen while dollying.</summary>
        public const float ScreenFraction = 0.30f;

        /// <summary>Segments per rotate-mode ring (draw and grab test).</summary>
        public const int RingSegments = 48;

        /// <summary>Screen-space grab radius for a handle, in viewport pixels.</summary>
        public const float GrabThresholdPx = 8f;

        /// <summary>World axes in draw order (X = red, Y = green, Z = blue).</summary>
        public static readonly Vector3[] AxisDirs =
        {
            Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ,
        };

        /// <summary>Axis palette (nova SceneGizmo parity — all hosts share it).</summary>
        public static readonly DrawingColor[] AxisColors =
        {
            DrawingColor.FromArgb(242, 71, 71, 242),
            DrawingColor.FromArgb(242, 89, 217, 102),
            DrawingColor.FromArgb(242, 89, 153, 255),
        };

        /// <summary>Neutral colour of the uniform-scale centre cube.</summary>
        public static readonly DrawingColor CenterColor =
            DrawingColor.FromArgb(217, 217, 230);

        /// <summary>Mesh- spec sizing as fractions of the gizmo
        /// <c>size</c>. The cone / cube / ring-radius fractions reproduce the
        /// line overlay exactly (cone = barb headLen/headHalf, cubes = wire
        /// cube half 0.05, ring radius = size); shaft and ring-tube diameters
        /// are new (lines had no width) — tune these to thicken the gizmo.</summary>
        public const float ShaftDiameterFrac = 0.04f;
        public const float TipConeDiameterFrac = 0.18f;
        public const float TipConeLengthFrac = 0.18f;
        public const float TipCubeEdgeFrac = 0.10f;
        public const float CenterCubeEdgeFrac = 0.10f;
        public const float RingTubeDiameterFrac = 0.035f;

        /// <summary>Fat analytic pick radii as fractions of <c>size</c> — the
        /// invisible grab shapes are deliberately larger than the visuals.</summary>
        public const float PickCapsuleRadiusFrac = 0.07f;
        public const float PickRingTubeFrac = 0.07f;
        public const float PickBoxHalfFrac = 0.075f;

        /// <summary>One drawable — and grabbable — gizmo segment in world
        /// space. <see cref="Handle"/> is the handle id it belongs to, or -1
        /// for a decorative segment (drawn but not grabbable).</summary>
        public readonly struct Segment
        {
            public readonly Vector3 A;
            public readonly Vector3 B;
            public readonly DrawingColor Color;
            public readonly int Handle;

            public Segment(Vector3 a, Vector3 b, DrawingColor color, int handle)
            {
                A = a;
                B = b;
                Color = color;
                Handle = handle;
            }
        }

        /// <summary>Handle families: one spec per handle. An arrow is two
        /// meshes (shaft + tip) sharing one id, constraint and pick shape.</summary>
        public enum GizmoHandleKind { Arrow, Ring, Center }

        /// <summary>Which procedural primitive a mesh placement instantiates
        /// (see V12.Components.Renderables).</summary>
        public enum GizmoMeshKind { None, Cylinder, Cone, Cube, Torus }

        /// <summary>Drag constraint the handle drives. Plane is reserved for
        /// future plane/square handles.</summary>
        public enum GizmoConstraint { Axis, Plane, Free }

        /// <summary>One visual mesh of a handle, in world space. The
        /// primitive's local +Y is the handle axis (<see cref="Rotation"/>
        /// maps +Y onto it); <see cref="Size"/> holds full extents:
        /// cylinder/cone = (diameter, length, diameter), cube = edge × 3,
        /// torus = (ring diameter, tube diameter, tube diameter).</summary>
        public readonly struct GizmoMeshPlacement
        {
            public readonly GizmoMeshKind Mesh;
            public readonly Vector3 Center;
            public readonly Quaternion Rotation;
            public readonly Vector3 Size;

            public GizmoMeshPlacement(GizmoMeshKind mesh, Vector3 center,
                Quaternion rotation, Vector3 size)
            {
                Mesh = mesh;
                Center = center;
                Rotation = rotation;
                Size = size;
            }
        }

        /// <summary>Analytic pick-shape kinds: capsule (arrow shafts + tips),
        /// torus (rotate rings), box (cubes).</summary>
        public enum GizmoPickKind { Capsule, Torus, Box }

        /// <summary>Invisible fat grab shape of a handle, in world space.
        /// Capsule = segment A→B with radius; torus = ring around
        /// <see cref="A"/> (centre) with normal <see cref="Axis"/>, ring
        /// radius <see cref="RingRadius"/>, tube radius <see cref="Radius"/>;
        /// box = cube around <see cref="A"/> with half-extent
        /// <see cref="Radius"/> oriented by <see cref="Rotation"/>.</summary>
        public readonly struct GizmoPickShape
        {
            public readonly GizmoPickKind Kind;
            public readonly Vector3 A;
            public readonly Vector3 B;
            public readonly Vector3 Axis;
            public readonly Quaternion Rotation;
            public readonly float Radius;
            public readonly float RingRadius;

            public GizmoPickShape(GizmoPickKind kind, Vector3 a, Vector3 b,
                Vector3 axis, Quaternion rotation, float radius, float ringRadius)
            {
                Kind = kind;
                A = a;
                B = b;
                Axis = axis;
                Rotation = rotation;
                Radius = radius;
                RingRadius = ringRadius;
            }
        }

        /// <summary>One grabbable handle: visual mesh placement(s) plus its
        /// analytic pick shape. The spawner parents both meshes of an arrow
        /// under one handle node sharing id, constraint and colour; the hit
        /// test only looks at <see cref="Pick"/> (never the visual mesh).</summary>
        public readonly struct GizmoHandleSpec
        {
            public readonly int HandleId;
            public readonly GizmoHandleKind Kind;
            public readonly int Axis;
            public readonly GizmoConstraint Constraint;
            public readonly DrawingColor Color;
            public readonly GizmoMeshPlacement Mesh0;
            public readonly GizmoMeshPlacement Mesh1;
            public readonly GizmoPickShape Pick;

            public GizmoHandleSpec(int handleId, GizmoHandleKind kind, int axis,
                GizmoConstraint constraint, DrawingColor color,
                GizmoMeshPlacement mesh0, GizmoMeshPlacement mesh1,
                GizmoPickShape pick)
            {
                HandleId = handleId;
                Kind = kind;
                Axis = axis;
                Constraint = constraint;
                Color = color;
                Mesh0 = mesh0;
                Mesh1 = mesh1;
                Pick = pick;
            }
        }

        /// <summary>Build the gizmo for <paramref name="mode"/> around
        /// <paramref name="origin"/>, sized <paramref name="size"/>, into
        /// <paramref name="into"/> (cleared first).
        /// <paramref name="cameraForward"/> orients the translate arrowheads
        /// so their barbs read as a V on screen from any angle.</summary>
        public static void Build(GizmoMode mode, Vector3 origin, float size,
            Vector3 cameraForward, List<Segment> into)
        {
            into.Clear();
            if (size <= 0f) return;

            float headLen = size * 0.18f;
            float headHalf = headLen * 0.5f;

            if (mode == GizmoMode.Rotate)
            {
                // One ring per axis, in the plane perpendicular to that axis;
                // the grab test measures against these same segments.
                for (int a = 0; a < 3; a++)
                {
                    var col = AxisColors[a];
                    var prev = RingPoint(origin, a, size, 0f);
                    for (int i = 1; i <= RingSegments; i++)
                    {
                        var p = RingPoint(origin, a, size, (float)i / RingSegments);
                        into.Add(new Segment(prev, p, col, a));
                        prev = p;
                    }
                }
                return;
            }

            for (int a = 0; a < 3; a++)
            {
                var axis = AxisDirs[a];
                var tip = origin + axis * size;
                var col = AxisColors[a];

                into.Add(new Segment(origin, tip, col, a));

                if (mode == GizmoMode.Scale)
                {
                    // Small wire cube at the tip as the scale handle.
                    AddWireCube(into, tip, size * 0.05f, col, a);
                }
                else
                {
                    // Arrowhead: two barbs from the tip, splayed in the plane
                    // perpendicular to the axis, oriented by the camera so the
                    // V reads on screen from any angle.
                    var perp = Vector3.Cross(axis, cameraForward);
                    if (perp.LengthSquared() < 1e-8f)
                        perp = Vector3.Cross(axis,
                            MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX);
                    perp = Vector3.Normalize(perp);

                    var back = tip - axis * headLen;
                    into.Add(new Segment(tip, back + perp * headHalf, col, a));
                    into.Add(new Segment(tip, back - perp * headHalf, col, a));
                }
            }

            if (mode == GizmoMode.Scale)
            {
                // Centre cube: uniform scale along all three axes.
                AddWireCube(into, origin, size * 0.05f, CenterColor, CenterHandle);
            }
        }

        /// <summary>Build one handle spec per gizmo handle for
        /// <paramref name="mode"/> around <paramref name="origin"/>, sized
        /// <paramref name="size"/>, into <paramref name="into"/> (cleared
        /// first), in world space. Proportions match <see cref="Build"/>
        /// exactly; mesh visuals are sized by the *Frac constants above.
        /// No camera needed — cones and cubes read from any angle.</summary>
        public static void BuildHandleSpecs(GizmoMode mode, Vector3 origin,
            float size, List<GizmoHandleSpec> into)
        {
            BuildHandleSpecs(mode, origin, size, Quaternion.Identity, into);
        }

        /// <summary>Same as <see cref="BuildHandleSpecs(GizmoMode, Vector3,
        /// float, List{GizmoHandleSpec})"/> but built from
        /// <paramref name="frame"/> (the local-vs-world toggle: identity =
        /// world axes, target orientation = local axes).</summary>
        public static void BuildHandleSpecs(GizmoMode mode, Vector3 origin,
            float size, Quaternion frame, List<GizmoHandleSpec> into)
        {
            into.Clear();
            if (size <= 0f) return;
            if (frame.LengthSquared() < 1e-8f) frame = Quaternion.Identity;
            frame = Quaternion.Normalize(frame);

            if (mode == GizmoMode.Rotate)
            {
                float tube = size * RingTubeDiameterFrac;
                float pickTube = size * PickRingTubeFrac;
                for (int a = 0; a < 3; a++)
                {
                    var axis = Vector3.Transform(AxisDirs[a], frame);
                    var rot = AlignYTo(axis);
                    into.Add(new GizmoHandleSpec(
                        a, GizmoHandleKind.Ring, a, GizmoConstraint.Axis,
                        AxisColors[a],
                        new GizmoMeshPlacement(GizmoMeshKind.Torus, origin, rot,
                            new Vector3(size * 2f, tube, tube)),
                        new GizmoMeshPlacement(GizmoMeshKind.None, origin,
                            Quaternion.Identity, Vector3.Zero),
                        new GizmoPickShape(GizmoPickKind.Torus, origin, origin,
                            axis, Quaternion.Identity, pickTube, size)));
                }
                return;
            }

            float shaftDia = size * ShaftDiameterFrac;
            float pickR = size * PickCapsuleRadiusFrac;
            for (int a = 0; a < 3; a++)
            {
                var axis = Vector3.Transform(AxisDirs[a], frame);
                var col = AxisColors[a];
                var tip = origin + axis * size;
                var rot = AlignYTo(axis);
                var shaftCenter = origin + axis * (size * 0.5f);

                if (mode == GizmoMode.Scale)
                {
                    float edge = size * TipCubeEdgeFrac;
                    var cubeCenter = tip;
                    var pickTip = tip + axis * (edge * 0.5f);
                    into.Add(new GizmoHandleSpec(
                        a, GizmoHandleKind.Arrow, a, GizmoConstraint.Axis, col,
                        new GizmoMeshPlacement(GizmoMeshKind.Cylinder,
                            shaftCenter, rot,
                            new Vector3(shaftDia, size, shaftDia)),
                        new GizmoMeshPlacement(GizmoMeshKind.Cube, cubeCenter,
                            rot, new Vector3(edge, edge, edge)),
                        new GizmoPickShape(GizmoPickKind.Capsule, origin,
                            pickTip, axis, Quaternion.Identity, pickR, 0f)));
                }
                else
                {
                    float coneLen = size * TipConeLengthFrac;
                    float coneDia = size * TipConeDiameterFrac;
                    var coneCenter = tip - axis * (coneLen * 0.5f);
                    // Shaft ends exactly at the cone base: a full-length shaft
                    // would poke out through the cone flanks near the apex
                    // (the cone interior is narrower than the shaft there).
                    float shaftLen = size - coneLen;
                    var arrowShaftCenter = origin + axis * (shaftLen * 0.5f);
                    into.Add(new GizmoHandleSpec(
                        a, GizmoHandleKind.Arrow, a, GizmoConstraint.Axis, col,
                        new GizmoMeshPlacement(GizmoMeshKind.Cylinder,
                            arrowShaftCenter, rot,
                            new Vector3(shaftDia, shaftLen, shaftDia)),
                        new GizmoMeshPlacement(GizmoMeshKind.Cone, coneCenter,
                            rot, new Vector3(coneDia, coneLen, coneDia)),
                        new GizmoPickShape(GizmoPickKind.Capsule, origin, tip,
                            axis, Quaternion.Identity, pickR, 0f)));
                }
            }

            if (mode == GizmoMode.Scale)
            {
                float edge = size * CenterCubeEdgeFrac;
                float half = size * PickBoxHalfFrac;
                into.Add(new GizmoHandleSpec(
                    CenterHandle, GizmoHandleKind.Center, -1,
                    GizmoConstraint.Free, CenterColor,
                    new GizmoMeshPlacement(GizmoMeshKind.Cube, origin, frame,
                        new Vector3(edge, edge, edge)),
                    new GizmoMeshPlacement(GizmoMeshKind.None, origin,
                        Quaternion.Identity, Vector3.Zero),
                    new GizmoPickShape(GizmoPickKind.Box, origin, origin,
                        Vector3.Zero, frame, half, 0f)));
            }
        }

        /// <summary>Hit test in V12, not the renderer: ray
        /// (<paramref name="rayOrigin"/>, unit <paramref name="rayDirection"/>)
        /// against each spec's analytic pick shape, nearest wins. Returns the
        /// winning handle id and ray distance, or false when nothing is hit.
        /// The visual mesh is never raycast.</summary>
        public static bool TryHitHandles(Vector3 rayOrigin, Vector3 rayDirection,
            List<GizmoHandleSpec> specs, out int handleId, out float distance)
        {
            handleId = -1;
            distance = float.MaxValue;
            for (int i = 0; i < specs.Count; i++)
            {
                var pick = specs[i].Pick;
                float t = 0f;
                bool hit = pick.Kind switch
                {
                    GizmoPickKind.Capsule => RayCapsuleT(rayOrigin, rayDirection,
                        pick.A, pick.B, pick.Radius, out t),
                    GizmoPickKind.Torus => RayTorusT(rayOrigin, rayDirection,
                        pick.A, pick.Axis, pick.RingRadius, pick.Radius, out t),
                    GizmoPickKind.Box => RayObbT(rayOrigin, rayDirection,
                        pick.A, pick.Rotation, pick.Radius, out t),
                    _ => false,
                };
                if (hit && t < distance)
                {
                    distance = t;
                    handleId = specs[i].HandleId;
                }
            }
            return handleId >= 0;
        }

        /// <summary>Shortest-arc rotation mapping +Y onto <paramref name="dir"/>
        /// (unit). Primitives are built with their length along +Y.</summary>
        private static Quaternion AlignYTo(Vector3 dir)
        {
            float dot = Vector3.Dot(Vector3.UnitY, dir);
            if (dot > 0.9999f) return Quaternion.Identity;
            if (dot < -0.9999f)
                return Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
            var axis = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, dir));
            return Quaternion.CreateFromAxisAngle(axis,
                MathF.Acos(Math.Clamp(dot, -1f, 1f)));
        }

        /// <summary>Ray vs capsule (segment A→B, radius): closest approach,
        /// ray parameter clamped ≥ 0. Segment-interior case solves the
        /// line/segment minimiser; endpoint and parallel cases anchor on the
        /// segment endpoint closest to the ray.</summary>
        private static bool RayCapsuleT(Vector3 ro, Vector3 rd, Vector3 a,
            Vector3 b, float radius, out float t)
        {
            t = 0f;
            var d2 = b - a;
            float e = d2.LengthSquared();
            if (e < 1e-12f) return RaySphereT(ro, rd, a, radius, out t);

            var r = ro - a;
            float bDot = Vector3.Dot(rd, d2);
            float f = Vector3.Dot(d2, r);
            float c = Vector3.Dot(rd, r);
            float denom = e - bDot * bDot; // rd is unit, so a (=rd·rd) is 1

            float s; // ray parameter
            Vector3 c2; // closest point on the segment
            if (denom > 1e-9f)
            {
                float sStar = (bDot * f - c * e) / denom;
                float tcStar = (bDot * sStar + f) / e;
                float tc = Math.Clamp(tcStar, 0f, 1f);
                if (tc == tcStar)
                {
                    s = sStar;
                    if (s < 0f)
                    {
                        // Closest line point is behind the ray: anchor at the
                        // origin and take the nearest segment point to it.
                        s = 0f;
                        tc = Math.Clamp(f / e, 0f, 1f);
                    }
                    c2 = a + d2 * tc;
                }
                else
                {
                    c2 = a + d2 * tc;
                    s = MathF.Max(0f, Vector3.Dot(c2 - ro, rd));
                }
            }
            else
            {
                // Parallel: nearest segment point to the ray origin, then the
                // nearest ray point to that.
                float tc = Math.Clamp(f / e, 0f, 1f);
                c2 = a + d2 * tc;
                s = MathF.Max(0f, Vector3.Dot(c2 - ro, rd));
            }
            var c1 = ro + rd * s;
            t = s;
            return (c1 - c2).LengthSquared() <= radius * radius;
        }

        /// <summary>Ray vs sphere: entry parameter, or false. An origin
        /// inside the sphere counts as a hit at 0; a sphere fully behind the
        /// ray is a miss.</summary>
        private static bool RaySphereT(Vector3 ro, Vector3 rd, Vector3 c,
            float radius, out float t)
        {
            t = 0f;
            var oc = ro - c;
            float halfB = Vector3.Dot(oc, rd);
            float cTerm = oc.LengthSquared() - radius * radius;
            float disc = halfB * halfB - cTerm;
            if (disc < 0f) return false;
            t = -halfB - MathF.Sqrt(disc);
            if (t < 0f)
            {
                if (cTerm > 0f) return false;
                t = 0f;
            }
            return true;
        }

        /// <summary>Ray vs torus (centre, unit normal, ring radius, tube
        /// radius): a few fixed iterations of closest-point refinement between
        /// the ray and the ring circle. Good to the fat pick tube.</summary>
        private static bool RayTorusT(Vector3 ro, Vector3 rd, Vector3 center,
            Vector3 axis, float ringRadius, float tubeRadius, out float t)
        {
            t = MathF.Max(0f, Vector3.Dot(center - ro, rd));
            var q = ro + rd * t;
            var fallback = MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            var u = Vector3.Normalize(fallback - axis * Vector3.Dot(fallback, axis));
            for (int i = 0; i < 3; i++)
            {
                var w = q - center;
                var planar = w - axis * Vector3.Dot(w, axis);
                if (planar.LengthSquared() < 1e-10f) planar = u;
                var p = center + Vector3.Normalize(planar) * ringRadius;
                t = Vector3.Dot(p - ro, rd);
                if (t < 0f) { t = 0f; q = ro; continue; }
                q = ro + rd * t;
            }
            var wFinal = q - center;
            var planarFinal = wFinal - axis * Vector3.Dot(wFinal, axis);
            if (planarFinal.LengthSquared() < 1e-10f) planarFinal = u;
            var pFinal = center + Vector3.Normalize(planarFinal) * ringRadius;
            return (q - pFinal).LengthSquared() <= tubeRadius * tubeRadius;
        }

        /// <summary>Ray vs oriented box (centre, rotation, half-extent): slab
        /// test in the box frame. Origin inside counts as a hit at 0.</summary>
        private static bool RayObbT(Vector3 ro, Vector3 rd, Vector3 center,
            Quaternion rotation, float half, out float t)
        {
            t = 0f;
            var inv = Quaternion.Conjugate(Quaternion.Normalize(rotation));
            var p = Vector3.Transform(ro - center, inv);
            var d = Vector3.Transform(rd, inv);
            float tMin = 0f, tMax = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                float pi = i == 0 ? p.X : i == 1 ? p.Y : p.Z;
                float di = i == 0 ? d.X : i == 1 ? d.Y : d.Z;
                if (MathF.Abs(di) < 1e-9f)
                {
                    if (MathF.Abs(pi) > half) return false;
                }
                else
                {
                    float t1 = (-half - pi) / di;
                    float t2 = (half - pi) / di;
                    if (t1 > t2) (t1, t2) = (t2, t1);
                    tMin = MathF.Max(tMin, t1);
                    tMax = MathF.Min(tMax, t2);
                    if (tMin > tMax) return false;
                }
            }
            t = tMin;
            return true;
        }

        /// <summary>Point on the rotate ring around <paramref name="axis"/>
        /// (0 = X, 1 = Y, 2 = Z) at parameter t ∈ [0,1] (t = 1 closes it).</summary>
        public static Vector3 RingPoint(Vector3 origin, int axis, float radius, float t)
        {
            float ang = t * MathF.PI * 2f;
            var u = AxisDirs[(axis + 1) % 3];
            var v = AxisDirs[(axis + 2) % 3];
            return origin + radius * (MathF.Cos(ang) * u + MathF.Sin(ang) * v);
        }

        /// <summary>Shaft length so the gizmo keeps a constant fraction of the
        /// viewport height at the target's distance
        /// (proj.M22 = cot(fov/2), absolute — sign flips never reach here).</summary>
        public static float ScreenSize(Vector3 camPos, Vector3 origin, Matrix4x4 proj)
        {
            float cotHalfFov = MathF.Abs(proj.M22) < 1e-6f ? 1f : MathF.Abs(proj.M22);
            return ScreenSize(Vector3.Distance(camPos, origin), cotHalfFov);
        }

        /// <summary>Same sizing for hosts without a System.Numerics projection
        /// matrix (Godot passes its vertical FOV in degrees).</summary>
        public static float ScreenSizeFromFov(float distance, float verticalFovDegrees)
        {
            float halfFovRad = verticalFovDegrees * MathF.PI / 360f;
            float cotHalfFov = 1f / MathF.Tan(halfFovRad);
            return ScreenSize(distance, cotHalfFov);
        }

        /// <summary>Shaft length from a camera distance and cot(fov/2).</summary>
        public static float ScreenSize(float distance, float cotHalfFov)
        {
            float halfHeight = distance / cotHalfFov;
            return MathF.Max(0.05f, halfHeight * ScreenFraction);
        }

        /// <summary>12 edges of the axis-aligned wire cube with half-extent
        /// <paramref name="half"/> centred on <paramref name="center"/>.</summary>
        private static void AddWireCube(List<Segment> into, Vector3 center,
            float half, DrawingColor color, int handle)
        {
            Span<Vector3> c = stackalloc Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                c[i] = center + new Vector3(
                    (i & 1) == 0 ? -half : half,
                    (i & 2) == 0 ? -half : half,
                    (i & 4) == 0 ? -half : half);
            }
            ReadOnlySpan<int> E = stackalloc int[]
            {
                0, 1, 2, 3, 0, 2, 1, 3,
                4, 5, 6, 7, 4, 6, 5, 7,
                0, 4, 1, 5, 2, 6, 3, 7,
            };
            for (int i = 0; i < E.Length; i += 2)
                into.Add(new Segment(c[E[i]], c[E[i + 1]], color, handle));
        }
    }
}
