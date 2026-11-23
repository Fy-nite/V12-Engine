using System;
using System.Numerics;
using V12.Rendering;

namespace V12.Core.Rendering
{
    /// <summary>
    /// Engine-side raycast against world-space UI canvases. It uses only the
    /// captured camera and the canvas placement, so hit-testing works with no
    /// renderer attached (headless simulation and tests included).
    /// </summary>
    public static class WorldUiPicking
    {
        /// <summary>A canvas under the pointer and the canvas-local pixel hit.</summary>
        public readonly struct CanvasHit
        {
            public readonly long CanvasId;
            public readonly float Distance;
            public readonly float PixelX;
            public readonly float PixelY;

            public CanvasHit(long canvasId, float distance, float pixelX, float pixelY)
            {
                CanvasId = canvasId;
                Distance = distance;
                PixelX = pixelX;
                PixelY = pixelY;
            }
        }

        /// <summary>
        /// Finds the nearest interactive canvas under a screen-space point.
        /// </summary>
        public static bool TryPick(
            FrameSnapshot snapshot,
            Vector2 screenPosition,
            int screenWidth,
            int screenHeight,
            out CanvasHit hit)
        {
            hit = default;
            if (snapshot == null || snapshot.Canvases.Count == 0)
                return false;

            if (!TryBuildScreenViewport(snapshot, screenWidth, screenHeight, out var viewport))
                return false;
            if ((viewport.LayerMask & RenderLayers.Default) == 0)
                return false;
            if (!ViewportMath.TryUnprojectRay(viewport, screenPosition, out var origin, out var direction))
                return false;

            float bestDistance = float.MaxValue;
            CanvasHit best = default;

            foreach (var canvas in snapshot.Canvases)
            {
                if (!canvas.Interactive) continue;
                if (viewport.WorldId != Guid.Empty && canvas.WorldId != viewport.WorldId) continue;

                if (!TryHitCanvas(canvas, origin, direction,
                        out float distance, out float pixelX, out float pixelY))
                    continue;

                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = new CanvasHit(canvas.ElementId, distance, pixelX, pixelY);
            }

            if (best.CanvasId == 0) return false;
            hit = best;
            return true;
        }

        /// <summary>
        /// Builds the screen-space viewport used for picking: the full-screen
        /// view when there is one, otherwise the first active view, otherwise
        /// the world's current camera at full screen.
        /// </summary>
        private static bool TryBuildScreenViewport(
            FrameSnapshot snapshot,
            int screenWidth,
            int screenHeight,
            out ViewportInfo viewport)
        {
            viewport = default;
            if (screenWidth <= 0 || screenHeight <= 0)
                return false;

            RenderViewSnapshot? selected = null;
            foreach (var view in snapshot.Views)
            {
                if (!view.IsActive) continue;
                if (view.Width <= 0 || view.Height <= 0)
                {
                    selected = view;
                    break;
                }
                selected ??= view;
            }

            RenderableSnapshot? camera = selected is { } chosen
                ? FindCamera(snapshot, chosen)
                : null;
            camera ??= FindCurrentCamera(snapshot);
            if (camera is not { } active)
                return false;

            return ViewportMath.TryBuildCameraViewport(
                active, 0f, 0f, screenWidth, screenHeight, out viewport);
        }

        private static RenderableSnapshot? FindCamera(FrameSnapshot snapshot, in RenderViewSnapshot view)
        {
            if (view.CameraElementId != 0)
            {
                foreach (var renderable in snapshot.Renderables)
                {
                    if (renderable.NodeType == SnapshotNodeType.Camera &&
                        renderable.ElementId == view.CameraElementId)
                        return renderable;
                }
                return null;
            }

            foreach (var renderable in snapshot.Renderables)
            {
                if (renderable.NodeType != SnapshotNodeType.Camera) continue;
                if (view.WorldId != Guid.Empty && renderable.WorldId != view.WorldId) continue;
                if (renderable.IsCurrentCamera) return renderable;
            }
            return null;
        }

        private static RenderableSnapshot? FindCurrentCamera(FrameSnapshot snapshot)
        {
            foreach (var renderable in snapshot.Renderables)
            {
                if (renderable.NodeType == SnapshotNodeType.Camera && renderable.IsCurrentCamera)
                    return renderable;
            }
            return null;
        }

        /// <summary>
        /// Ray versus the canvas rectangle. The canvas lies on its local XY
        /// plane; hits outside the metres-sized quad are rejected.
        /// </summary>
        private static bool TryHitCanvas(
            in WorldCanvasSnapshot canvas,
            Vector3 origin,
            Vector3 direction,
            out float distance,
            out float pixelX,
            out float pixelY)
        {
            distance = 0f;
            pixelX = 0f;
            pixelY = 0f;

            var world = canvas.World;
            var center = new Vector3(world.M41, world.M42, world.M43);
            var axisX = new Vector3(world.M11, world.M21, world.M31);
            var axisY = new Vector3(world.M12, world.M22, world.M32);

            float axisXLength = axisX.Length();
            float axisYLength = axisY.Length();
            if (axisXLength < 1e-8f || axisYLength < 1e-8f)
                return false;

            axisX /= axisXLength;
            axisY /= axisYLength;

            var normal = Vector3.Cross(axisX, axisY);
            float normalLength = normal.Length();
            if (normalLength < 1e-8f)
                return false;
            normal /= normalLength;

            float denominator = Vector3.Dot(direction, normal);
            if (MathF.Abs(denominator) < 1e-9f)
                return false;

            float t = Vector3.Dot(normal, center - origin) / denominator;
            if (t <= 0f)
                return false;

            var point = origin + direction * t;
            float localX = Vector3.Dot(point - center, axisX);
            float localY = Vector3.Dot(point - center, axisY);

            float halfWidth = MathF.Max(0.01f, canvas.WidthMetres) * 0.5f;
            float halfHeight = MathF.Max(0.01f, canvas.HeightMetres) * 0.5f;
            if (MathF.Abs(localX) > halfWidth || MathF.Abs(localY) > halfHeight)
                return false;

            distance = t;
            pixelX = (localX / (halfWidth * 2f) + 0.5f) * canvas.PixelWidth;
            pixelY = (0.5f - localY / (halfHeight * 2f)) * canvas.PixelHeight;
            return true;
        }
    }
}
