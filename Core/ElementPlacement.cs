using System.Numerics;
using V12.Components;
using V12.Core.Core.Interfaces;

namespace V12.Core
{
    /// <summary>Single source of placement math: V12 determines placement,
    /// renderers only read it. Element locals come from the TransformComponent
    /// when present, else the element's LocalTransform; worlds compose
    /// through the parent chain (<see cref="Element.WorldTransform"/>).
    /// Meshes carry no transforms — a mesh's world is its element's world.</summary>
    public static class ElementPlacement
    {
        /// <summary>Element-LOCAL matrix (no ancestors): the TransformComponent
        /// when present, else Scale × Rotation × Translation from LocalTransform.</summary>
        public static Matrix4x4 ElementLocalMatrix(IWorldElement el)
        {
            var tc = el.GetComponent<TransformComponent>();
            if (tc != null) return tc.Transform;
            var lt = el.LocalTransform;
            return Matrix4x4.CreateScale(lt.Scale)
                 * Matrix4x4.CreateFromQuaternion(lt.Rotation)
                 * Matrix4x4.CreateTranslation(lt.Position);
        }
    }
}
