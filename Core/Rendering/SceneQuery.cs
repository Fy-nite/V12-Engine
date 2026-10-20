using System;
using System.Numerics;
using V12.Components;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

namespace V12.Core.Rendering
{
    /// <summary>Result of a scene raycast: the nearest hit element, the
    /// world-space hit point, and the distance from the ray origin.</summary>
    public readonly struct SceneRaycastHit
    {
        public readonly IWorldElement Element;
        public readonly float Distance;
        public readonly Vector3 Point;

        public SceneRaycastHit(IWorldElement element, float distance, Vector3 point)
        {
            Element = element;
            Distance = distance;
            Point = point;
        }
    }

    /// <summary>
    /// Engine-side scene picking: casts a world-space ray against the meshes
    /// registered to a viewport and returns the nearest hit element. Geometry
    /// comes from <see cref="IMeshRenderable.MeshPoints"/>/<c>Indices</c> plus the
    /// owning element's <c>WorldTransform</c> — no renderer readback, so it works
    /// with any host. Editor-gizmo (transient) subtrees are never picked; callers
    /// can exclude further elements (e.g. the element currently direct-driven).
    /// </summary>
    public static class SceneQuery
    {
        /// <summary>
        /// Cast <paramref name="direction"/> (need not be normalized) from
        /// <paramref name="origin"/> and return the nearest mesh-owning element in
        /// <paramref name="viewportId"/> (or any viewport when 0), or null for
        /// empty space.
        /// </summary>
        public static SceneRaycastHit? Raycast(
            GameRoot root,
            long viewportId,
            Vector3 origin,
            Vector3 direction,
            Func<IWorldElement, bool>? exclude = null)
        {
            if (root == null) return null;

            var dir = direction;
            float dirLen = dir.Length();
            if (dirLen < 1e-8f) return null;
            dir /= dirLen;

            float bestDist = float.MaxValue;
            IWorldElement? best = null;
            Vector3 bestPoint = default;

            foreach (var (element, renderable, vpId) in root.GetAllRenderablesWithViewports())
            {
                if (viewportId != 0 && vpId != viewportId) continue;
                if (element == null || renderable is not IMeshRenderable mesh) continue;
                // Editor-gizmo (transient) subtrees render but are never selectable.
                if (NonPersistentComponent.IsTransient(element)) continue;
                if (exclude != null && exclude(element)) continue;

                var pts = mesh.MeshPoints;
                var idx = mesh.Indices;
                if (pts == null || idx == null || pts.Length < 9 || idx.Length < 3) continue;

                // Work in local space (meshes are small), then convert the hit
                // back to world for a distance that is comparable across meshes
                // (handles non-uniform parent scale exactly).
                var world = element.WorldTransform;
                if (!Matrix4x4.Invert(world, out var invW)) continue;
                var lo = Vector3.Transform(origin, invW);
                var ld = Vector3.TransformNormal(dir, invW);
                float ldLen = ld.Length();
                if (ldLen < 1e-8f) continue;
                ld /= ldLen;

                for (int t = 0; t + 2 < idx.Length; t += 3)
                {
                    int i0 = (int)idx[t] * 3, i1 = (int)idx[t + 1] * 3, i2 = (int)idx[t + 2] * 3;
                    if (i0 < 0 || i1 < 0 || i2 < 0 || i0 + 2 >= pts.Length || i1 + 2 >= pts.Length || i2 + 2 >= pts.Length)
                        continue;

                    var v0 = new Vector3((float)pts[i0], (float)pts[i0 + 1], (float)pts[i0 + 2]);
                    var v1 = new Vector3((float)pts[i1], (float)pts[i1 + 1], (float)pts[i1 + 2]);
                    var v2 = new Vector3((float)pts[i2], (float)pts[i2 + 1], (float)pts[i2 + 2]);

                    if (!RayTriangle(lo, ld, v0, v1, v2, out float hitT)) continue;

                    var worldHit = Vector3.Transform(lo + ld * hitT, world);
                    float d = Vector3.Distance(worldHit, origin);
                    if (d > 1e-4f && d < bestDist)
                    {
                        bestDist = d;
                        best = element;
                        bestPoint = worldHit;
                    }
                }
            }

            return best == null ? null : new SceneRaycastHit(best, bestDist, bestPoint);
        }

        /// <summary>Möller–Trumbore, two-sided (editor meshes have no winding
        /// guarantee once parents rotate).</summary>
        private static bool RayTriangle(Vector3 o, Vector3 d, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
        {
            t = 0f;
            var e1 = v1 - v0;
            var e2 = v2 - v0;
            var p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (MathF.Abs(det) < 1e-8f) return false;
            float invDet = 1f / det;
            var tv = o - v0;
            float u = Vector3.Dot(tv, p) * invDet;
            if (u < 0f || u > 1f) return false;
            var q = Vector3.Cross(tv, e1);
            float v = Vector3.Dot(d, q) * invDet;
            if (v < 0f || u + v > 1f) return false;
            float hit = Vector3.Dot(e2, q) * invDet;
            if (hit < 1e-5f) return false;
            t = hit;
            return true;
        }
    }
}
