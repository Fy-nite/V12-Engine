using System.Collections.Generic;
using System.Numerics;
using V12.Core.Core.Interfaces;

namespace V12.Core.UI
{
    /// <summary>
    /// Renderer-side primitives the viewport interaction needs: viewport
    /// hit-testing, camera matrices, screen-space projection and pickable
    /// meshes. Implemented by the host renderer (the MonoGame host ships
    /// <c>MonogameViewportHost</c>) so the editor logic in
    /// <see cref="ViewportInteractionService"/> stays renderer-agnostic.
    ///
    /// (Distinct from <c>V12.UI.IViewportHost</c>, the viewport *widget*.)
    /// </summary>
    public interface IViewportInteractionHost
    {
        /// <summary>Viewport under a screen-space point (smallest wins when
        /// nested). False when over no viewport.</summary>
        bool TryGetViewportAt(Vector2 screenPos, out long viewportId);

        /// <summary>Camera matrices the renderer draws <paramref name="viewportId"/>
        /// with (same source of truth as drawing, so pick rays line up with pixels).</summary>
        bool TryGetViewportCamera(long viewportId, out Matrix4x4 view, out Matrix4x4 projection);

        /// <summary>Pick ray through a screen-space point: origin at the near
        /// plane, unit direction towards the far plane.</summary>
        bool TryUnprojectRay(long viewportId, Vector2 screenPos, out Vector3 origin, out Vector3 direction);

        /// <summary>Project a world-space point to screen-space window
        /// coordinates (for screen-space tests such as gizmo axis grabs).</summary>
        bool TryProjectToScreen(long viewportId, Vector3 worldPosition, out Vector2 screenPosition);

        /// <summary>Fill <paramref name="into"/> with the meshes drawn in
        /// <paramref name="viewportId"/> this frame (cleared first).</summary>
        void CollectPickMeshes(long viewportId, List<ViewportPickMesh> into);
    }

    /// <summary>One mesh eligible for editor picking: world transform plus raw
    /// geometry and the owning element.</summary>
    public readonly struct ViewportPickMesh
    {
        public readonly Matrix4x4 World;
        public readonly double[] Points;
        public readonly uint[] Indices;
        public readonly IWorldElement? Owner;

        public ViewportPickMesh(Matrix4x4 world, double[] points, uint[] indices, IWorldElement? owner)
        {
            World = world;
            Points = points;
            Indices = indices;
            Owner = owner;
        }
    }
}
