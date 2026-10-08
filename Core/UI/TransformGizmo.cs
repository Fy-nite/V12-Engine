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
