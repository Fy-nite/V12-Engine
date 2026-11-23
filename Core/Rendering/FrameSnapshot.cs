using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using V12.Core.Interfaces.Renderer;
using V12.Rendering;

namespace V12.Core.Rendering
{
    public enum SnapshotNodeType
    {
        RawElement,
        LightPoint,
        LightDirectional,
        LightSpot,
        MeshBox,
        MeshSphere,
        MeshCapsule,
        MeshCylinder,
        MeshPlane,
        MeshCustom,
        Sprite,
        Svg,
        Text,
        Camera,
        Particle
    }

    public struct RenderableSnapshot
    {
        public long ElementId;
        public long ParentId;
        public string Name;
        public SnapshotNodeType NodeType;

        /// <summary>
        /// Legacy viewport ownership id. New capture leaves this 0; the clean
        /// view model routes by <see cref="WorldId"/> and <see cref="LayerMask"/>
        /// instead. Kept so the legacy snapshot renderers still compile.
        /// </summary>
        public long ViewportId;

        /// <summary>World this renderable belongs to.</summary>
        public Guid WorldId;

        /// <summary>Layers this renderable is visible on.</summary>
        public uint LayerMask;

        /// <summary>
        /// Optional view restriction. 0 renders in every view whose world and
        /// layers match; a non-zero id renders only in that view. Used for
        /// nested 3D UI, not for normal scene content.
        /// </summary>
        public long ViewId;

        public Matrix4x4 Transform;
        public Matrix4x4 LocalTransform;
        public bool HasLocalTransform;
        public bool IsWorldLocked;

        // Light properties
        public Color LightColor;
        public float LightIntensity;
        public float LightRange;
        public float LightAngle;
        public float LightSpotSoftness;

        // Mesh properties
        public float MeshWidth;
        public float MeshHeight;
        public float MeshDepth;
        public double[] MeshPoints;
        public double[]? MeshUvs;
        public double[]? MeshNormals;
        public double[]? MeshTangents;
        public uint[] MeshIndices;

        // Material properties (PBR)
        public float MatR;
        public float MatG;
        public float MatB;
        public float MatA;
        public bool MatUnlit;
        public bool MatNoDepth;
        /// <summary>Optional shader:// URI. When set the backend compiles it as a
        /// custom material instead of the built-in lit/unlit shader.</summary>
        public string MatShaderUri;
        public float MatMetallic;
        public float MatRoughness;
        public float MatEmissionR;
        public float MatEmissionG;
        public float MatEmissionB;
        public float MatEmissionEnergy;
        public float MatNormalScale;
        public float MatAlphaCutoff;
        public bool MatDoubleSided;
        public bool MatTriplanar;
        public bool MatTriplanarWorld;
        public float MatTriplanarSharpness;
        public string MatTexturePath;
        public string MatNormalTexturePath;
        public string MatMetallicTexturePath;
        public string MatRoughnessTexturePath;
        public string MatEmissionTexturePath;
        public string MatOcclusionTexturePath;
        public float MatUvOffsetX;
        public float MatUvOffsetY;
        public float MatUvScaleX;
        public float MatUvScaleY;

        // Sprite / SVG
        public string TextureSource;
        public float SizeX;
        public float SizeY;
        public Color Tint;

        // SVG
        public string SvgContent;

        // Text
        public string TextContent;
        public Color TextColor;
        public float FontSize;

        // Camera
        public float Fov;
        public float NearClip;
        public float FarClip;
        public bool IsCurrentCamera;
        public CameraProjection Projection;
        public float OrthoSize;

        // Particle emitter (NodeType == Particle)
        public int ParticleAmount;
        public float ParticleLifetime;
        public float ParticleEmissionRadius;
        public float ParticleSpeedMin;
        public float ParticleSpeedMax;
        public float ParticleDirX;
        public float ParticleDirY;
        public float ParticleDirZ;
        public float ParticleSpread;
        public float ParticleSize;
        public Color ParticleColor;
        public float ParticleGravity;
        public bool ParticleEmitting;
        public bool ParticleOneShot;

        // Whether the original V12 element has a ColliderComponent.
        // Used by renderers to decide whether to add physics shapes.
        public bool HasCollider;

        // Skinning (ISkinnedMeshRenderable). Bone indices reference the skin's
        // joint order, which is the same order the Motion skeleton uses.
        public long SkinId;
        public Matrix4x4 SkinWorld;
        public int[]? SkinBoneParents;
        public Matrix4x4[]? SkinBoneLocals;
        public Matrix4x4[]? SkinInverseBinds;
        public float[]? MeshBoneWeights;
        public int[]? MeshBoneIndices;
    }

    /// <summary>
    /// One camera view captured from a <see cref="V12.Components.RenderViewComponent"/>.
    /// The renderer owns the target texture; the UI surface owns the rectangle.
    /// </summary>
    public struct RenderViewSnapshot
    {
        /// <summary>Stable view id (the render-view element id).</summary>
        public long ViewId;

        /// <summary>UI surface element id that displays this view.</summary>
        public long ViewportId;

        /// <summary>Camera element id. 0 means the first current camera in the world.</summary>
        public long CameraElementId;

        /// <summary>World to render. Empty means the selected world.</summary>
        public Guid WorldId;

        /// <summary>Layers this view renders.</summary>
        public uint LayerMask;

        public ClearMode Clear;
        public Color ClearColor;

        /// <summary>Higher draws later when views overlap.</summary>
        public int Priority;

        public bool IsActive;

        /// <summary>Explicit target size; 0 uses the UI surface rect.</summary>
        public int Width;
        public int Height;
    }

    /// <summary>
    /// One world-space UI canvas captured for the renderer contract. The canvas
    /// is emitted as ordinary scene meshes: <see cref="World"/> places them and
    /// <see cref="WidthMetres"/>/<see cref="HeightMetres"/> size the surface.
    /// </summary>
    public struct WorldCanvasSnapshot
    {
        /// <summary>Canvas element id. Also the id used by the UI frame.</summary>
        public long ElementId;

        /// <summary>World the canvas belongs to; used by view/layer filtering.</summary>
        public Guid WorldId;

        /// <summary>Surface size in Paper pixels.</summary>
        public int PixelWidth;
        public int PixelHeight;

        /// <summary>Rotation plus translation, no scale (size lives in metres).</summary>
        public Matrix4x4 World;

        /// <summary>Physical size of the canvas in metres.</summary>
        public float WidthMetres;
        public float HeightMetres;

        /// <summary>Higher draws later; used for transparent ordering across canvases.</summary>
        public int Priority;

        /// <summary>False for display-only canvases (they never take pointer focus).</summary>
        public bool Interactive;

        /// <summary>Width used when the canvas element has no scale of its own.</summary>
        public const float DefaultWidthMetres = 2f;

        /// <summary>Surface size used when a canvas authors no pixel size.</summary>
        public const int DefaultPixelWidth = 512;
        public const int DefaultPixelHeight = 288;

        /// <summary>
        /// Resolves a canvas's authored pixel size to the size its UI surface
        /// uses. Capture and layout share this so placement and surface agree.
        /// </summary>
        public static (int Width, int Height) ResolvePixelSize(int width, int height)
        {
            if (width <= 0) width = DefaultPixelWidth;
            if (height <= 0) height = DefaultPixelHeight;
            return (Math.Clamp(width, 64, 4096), Math.Clamp(height, 64, 4096));
        }

        /// <summary>
        /// Builds the rendering placement from an element transform. The same
        /// rule the legacy hosts used: a scaled element's X/Y axis lengths are
        /// the canvas size in metres, otherwise the canvas defaults to
        /// <see cref="DefaultWidthMetres"/> wide with aspect from the pixels.
        /// </summary>
        public static WorldCanvasSnapshot Create(
            long elementId,
            Guid worldId,
            int pixelWidth,
            int pixelHeight,
            Matrix4x4 world,
            int priority = 0,
            bool interactive = true)
        {
            int width = Math.Max(1, pixelWidth);
            int height = Math.Max(1, pixelHeight);

            float scaleX = new Vector3(world.M11, world.M21, world.M31).Length();
            float scaleY = new Vector3(world.M12, world.M22, world.M32).Length();
            bool hasScale =
                (scaleX > 0.01f && MathF.Abs(scaleX - 1f) > 1e-3f) ||
                (scaleY > 0.01f && MathF.Abs(scaleY - 1f) > 1e-3f);

            if (scaleX > 0.01f)
            {
                world.M11 /= scaleX;
                world.M21 /= scaleX;
                world.M31 /= scaleX;
            }

            if (scaleY > 0.01f)
            {
                world.M12 /= scaleY;
                world.M22 /= scaleY;
                world.M32 /= scaleY;
            }

            float widthMetres = MathF.Max(0.01f, hasScale ? scaleX : DefaultWidthMetres);
            float heightMetres = hasScale
                ? MathF.Max(0.01f, scaleY)
                : widthMetres * height / width;

            return new WorldCanvasSnapshot
            {
                ElementId = elementId,
                WorldId = worldId,
                PixelWidth = width,
                PixelHeight = height,
                World = world,
                WidthMetres = widthMetres,
                HeightMetres = heightMetres,
                Priority = priority,
                Interactive = interactive,
            };
        }
    }

    public class FrameSnapshot
    {
        public List<RenderableSnapshot> Renderables = new();

        /// <summary>Camera views declared this frame, ordered by priority.</summary>
        public List<RenderViewSnapshot> Views = new();

        /// <summary>World-space UI canvases. They are drawn as scene meshes.</summary>
        public List<WorldCanvasSnapshot> Canvases = new();
    }
}
