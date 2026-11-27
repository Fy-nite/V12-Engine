using System.Numerics;
using V12.Components;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

namespace V12.Core
{
    /// <summary>Single source of placement math: V12 determines placement,
    /// renderers only read it. Element locals come from LocalTransform with
    /// ScaleComponent folded in; worlds compose through the parent chain
    /// (<see cref="Element.WorldTransform"/>, which calls this).
    /// Meshes carry no transforms — a mesh's world is its element's world.</summary>
    public static class ElementPlacement
    {
        /// <summary>Element-LOCAL matrix (no ancestors): Scale × Rotation ×
        /// Translation from LocalTransform with the ScaleComponent folded in
        /// (both scale channels multiply).</summary>
        public static Matrix4x4 ElementLocalMatrix(IWorldElement el)
        {
            TRS pose = ElementLocalTRS(el);
            return Matrix4x4.CreateScale(pose.Scale)
                 * Matrix4x4.CreateFromQuaternion(pose.Rotation)
                 * Matrix4x4.CreateTranslation(pose.Position);
        }

        /// <summary>Element-LOCAL pose with the ScaleComponent folded in.</summary>
        public static TRS ElementLocalTRS(IWorldElement el)
        {
            var lt = el.LocalTransform;
            var sc = el.GetComponent<ScaleComponent>();
            var scale = sc != null
                ? new Vector3(lt.Scale.X * sc.ScaleX, lt.Scale.Y * sc.ScaleY, lt.Scale.Z * sc.ScaleZ)
                : lt.Scale;

            return new TRS(lt.Position, lt.Rotation, scale);
        }

        /// <summary>
        /// Render-time local matrix. Interpolated for engine <see cref="Element"/>
        /// instances when render interpolation is enabled; otherwise the exact
        /// current local. Use this in snapshot capture only.
        /// </summary>
        public static Matrix4x4 RenderLocalMatrix(IWorldElement el)
            => el is Element element ? element.RenderLocalTransform : ElementLocalMatrix(el);

        /// <summary>
        /// Render-time world matrix. Gameplay and picking must keep using
        /// <see cref="IWorldElement.WorldTransform"/>.
        /// </summary>
        public static Matrix4x4 RenderWorldMatrix(IWorldElement el)
            => el is Element element ? element.RenderWorldTransform : el.WorldTransform;
    }
}
