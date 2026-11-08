using Assimp;
using Assimp.Unmanaged;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Text;
using V12.Core.Core.Interfaces;
using static System.Net.Mime.MediaTypeNames;

namespace V12.Core.Interfaces.Renderer
{


    public struct TRS
    {
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.Identity;
        public Vector3 Scale = Vector3.One;
        
        public TRS(Vector3? position = null, Quaternion? rotation = null, Vector3? scale = null)
        {
            Position = position ?? Vector3.Zero;
            Rotation = rotation ?? Quaternion.Identity;
            Scale = scale ?? Vector3.One;
        }
    }
    public enum RenderType
    {
        PrimitiveModel,
        RawElement,
        Mesh,
        Sprite,
        Text,
        Svg,
        ParticleSystem,
        Light,
        Custom
    }

    public interface IRenderable : IComponent, ISpatial
    {
        public RenderType RenderType { get; }
    }
    public enum LightType
    {
        Point,
        Directional,
        Spot,
        AreaLight
    }
    public enum CameraProjection
    {
        Perspective,
        Orthographic
    }

    public interface ICameraRenderable : ITransformRenderable
    {
        public float FieldOfView { get; }
        public float AspectRatio { get; }
        public float NearClip { get; }
        public float FarClip { get; }
        public bool IsCurrent { get; }
        public CameraProjection Projection { get; }
        public float OrthoSize { get; }
    }
    public interface ILightRenderable : ITransformRenderable
    {
        public Color Color { get; }
        public float Intensity { get; }
        public float Range { get; }
        public LightType Type { get; }
        public float Angle { get; }
        public float SpotSoftness { get; }
    }

    public interface ITransformRenderable : IRenderable
    {
        /// <summary>Mesh-LOCAL matrix: dimensions plus the component's offset
        /// relative to its element. Never contains element or world placement —
        /// V12 composes the world matrix (mesh-local × element
        /// <c>WorldTransform</c>, inner-to-outer row-vector order, like
        /// <c>local × parent</c> everywhere else) and renderers consume that.
        /// See <see cref="ISpatial"/>.</summary>
        public Matrix4x4 Transform { get; }
        public bool IsWorldLocked { get; }  // true = world space, false = body/hand-locked for XR
    }

    /// <summary>Meshes never carry transforms: placement lives only on
    /// elements (<see cref="ISpatial"/> / element <c>WorldTransform</c>).
    /// A mesh is pure geometry (verts/indices) parameterized by dims.</summary>
    public interface IMeshRenderable : IRenderable
    {
        public string Name { get; }
        public double[] MeshPoints { get; }
        public uint[] Indices { get; }
        public Material Material { get; }

        /// <summary>
        /// Optional flat UV0 array [u0, v0, u1, v1, ...] parallel to
        /// <see cref="MeshPoints"/>. Null means the producer generates a
        /// fallback planar projection.
        /// </summary>
        public double[]? MeshUvs => null;

        /// <summary>
        /// Optional flat normals [x0, y0, z0, ...] parallel to
        /// <see cref="MeshPoints"/>. Null means the producer derives smooth
        /// normals from the triangle winding, so authored shading (the file's
        /// own normals) is only kept when a source actually publishes them.
        /// </summary>
        public double[]? MeshNormals => null;

        /// <summary>
        /// Optional per-vertex tangents packed xyzw [x0, y0, z0, w0, ...]
        /// parallel to <see cref="MeshPoints"/>; w is the binormal sign, the
        /// usual per-vertex tangent layout. Null means the mesh has no tangents
        /// and normal maps are skipped rather than guessed.
        /// </summary>
        public double[]? MeshTangents => null;
    }

    /// <summary>
    /// Material authored inside a 3D model file (GLB/GLTF/OBJ/…). The engine
    /// reads this when the element carries no <c>MaterialComponent</c>, so a
    /// model keeps the surface its author baked in. A <c>MaterialComponent</c>
    /// always wins; this is the fallback, never an override.
    /// </summary>
    public interface IModelMaterialSource : IComponent
    {
        /// <summary>Base colour multiplied over the albedo texture.</summary>
        public Vector4 AlbedoColor { get; }
        public float Metallic { get; }
        public float Roughness { get; }

        // Absolute paths (embedded textures are extracted to a temp cache first),
        // or null when the model does not use that slot.
        public string? AlbedoTexture { get; }
        public string? NormalTexture { get; }
        public string? MetallicTexture { get; }
        public string? RoughnessTexture { get; }
        public string? EmissionTexture { get; }
    }

    public interface ISpriteRenderable : ITransformRenderable
    {
        public ITexture Texture { get; }
        public Vector2 Size { get; }
        public Color Tint { get; }
    }

    public interface ITextRenderable : ITransformRenderable
    {
        public string Text { get; }
        public Font Font { get; }
        public Color Color { get; }
        public float FontSize { get; }
    }

    public interface ISvgRenderable : ITransformRenderable
    {
        public string SvgContent { get; }
        public Vector2 Size { get; }
        public Color Tint { get; }
    }
}
