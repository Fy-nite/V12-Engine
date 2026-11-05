using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Text;
using V12.Core.Core.Interfaces;

namespace V12.Core.Interfaces.Renderer
{
    public class RenderPacket
    {
        public List<MeshDraw> Meshes = new();
        /// <summary>Hierarchy mirror data: every element in the rendered
        /// worlds (mesh owners and group ancestors alike). Renderers mirror
        /// the V12 tree from this and compose world matrices themselves —
        /// placement decisions live in V12, never in renderer code.</summary>
        public List<NodePlacement> Nodes = new();
        public List<SpriteDraw> Sprites = new();
        public List<TextDraw> Texts = new();
        public List<LightData> Lights = new();
        public List<CameraData> Cameras = new();
        public EnvironmentData Environment = EnvironmentData.Default;
    }
    /// <summary>One element's place in the hierarchy: element-local matrix
    /// (no ancestors — see <see cref="V12.Core.ElementPlacement"/>).</summary>
    public struct NodePlacement
    {
        public long ElementId;
        public long ParentId;
        public Matrix4x4 Local;
        public Matrix4x4 World;
        public bool HasWorld;
    }

    public enum MaterialBlendMode
    {
        Opaque = 0,
        Transparent = 1,
        Additive = 2,
        Multiplicative = 3
    }

    public enum EnvironmentFogMode
    {
        Disabled = 0,
        Linear = 1,
        Exponential = 2,
        ExponentialSquared = 3
    }

    public enum EnvironmentTonemapMode
    {
        Linear = 0,
        Reinhard = 1,
        Filmic = 2,
        Aces = 3,
        Agx = 4
    }

    public enum EnvironmentBloomMode
    {
        Disabled = 0,
        Mix = 1,
        Additive = 2,
        Screen = 3
    }

    public struct EnvironmentData
    {
        public bool HasValue;
        public Color SkyColor;
        public float BackgroundEnergy;
        public Color AmbientColor;
        public float AmbientIntensity;
        public bool Skybox;
        public string? SkyboxPath;
        public float SkyBlur;
        public Quaternion SkyboxRotation;
        public EnvironmentFogMode FogMode;
        public Color FogColor;
        public float FogStart;
        public float FogEnd;
        public float FogDensity;
        public float FogSkyAffect;
        public bool VolumetricFogEnabled;
        public float VolumetricFogDensity;
        public float VolumetricFogAnisotropy;
        public float VolumetricFogLength;
        public float VolumetricFogSkyAffect;
        public EnvironmentTonemapMode TonemapMode;
        public float Exposure;
        public float TonemapWhite;
        public float Brightness;
        public float Contrast;
        public float Saturation;
        public EnvironmentBloomMode BloomMode;
        public float BloomIntensity;
        public float BloomThreshold;
        public float BloomSoftThreshold;
        public bool AutoExposureEnabled;
        public float AutoExposureMinEV;
        public float AutoExposureMaxEV;
        public float AutoExposureCompensation;
        public bool SsaoEnabled;
        public float SsaoIntensity;
        public float SsaoRadius;
        public float SsaoPower;
        public bool SsilEnabled;
        public float SsilGiIntensity;
        public float SsilAoIntensity;
        public float SsilRadius;
        public bool SsgiEnabled;
        public float SsgiIntensity;
        public bool SsrEnabled;
        public bool DofEnabled;
        public float DofFocusDistance;
        public float DofMaxBlurSize;

        public static EnvironmentData Default => new EnvironmentData
        {
            HasValue = false,
            SkyColor = Color.FromArgb(77, 140, 230),
            BackgroundEnergy = 1f,
            AmbientColor = Color.FromArgb(96, 102, 122),
            AmbientIntensity = 0.9f,
            Skybox = false,
            SkyboxPath = null,
            SkyBlur = 0f,
            SkyboxRotation = Quaternion.Identity,
            FogMode = EnvironmentFogMode.Disabled,
            FogColor = Color.FromArgb(128, 128, 128),
            FogStart = 0f,
            FogEnd = 100f,
            FogDensity = 0f,
            FogSkyAffect = 0f,
            VolumetricFogEnabled = false,
            VolumetricFogDensity = 0f,
            VolumetricFogAnisotropy = 0f,
            VolumetricFogLength = 64f,
            VolumetricFogSkyAffect = 0f,
            TonemapMode = EnvironmentTonemapMode.Filmic,
            Exposure = 1f,
            TonemapWhite = 1f,
            Brightness = 1f,
            Contrast = 1f,
            Saturation = 1f,
            BloomMode = EnvironmentBloomMode.Disabled,
            BloomIntensity = 0f,
            BloomThreshold = 1f,
            BloomSoftThreshold = 0f,
            AutoExposureEnabled = false,
            AutoExposureMinEV = -8f,
            AutoExposureMaxEV = 8f,
            AutoExposureCompensation = 0f,
            SsaoEnabled = false,
            SsaoIntensity = 1f,
            SsaoRadius = 0.5f,
            SsaoPower = 1f,
            SsilEnabled = false,
            SsilGiIntensity = 1f,
            SsilAoIntensity = 1f,
            SsilRadius = 1f,
            SsgiEnabled = false,
            SsgiIntensity = 1f,
            SsrEnabled = false,
            DofEnabled = false,
            DofFocusDistance = 10f,
            DofMaxBlurSize = 0.05f,
        };
    }

    public struct CameraData
    {
        public long ElementId;
        public bool Active;
        public bool IsCurrent;
        public Matrix4x4 World;
        public CameraProjection Projection;
        public float OrthoSize;
        public float FieldOfView;
        public float NearClip;
        public float FarClip;
    }

    public struct LightData
    {
        public long ElementId;
        public bool Active;
        public LightType Type;
        public Vector3 Position;
        public Vector3 Direction;
        public Color Color;
        public float Intensity;
        public float Range;
        public float Angle;
        public float SpotSoftness;
    }

    public struct MeshDraw
    {
        public long ElementId;
        public IMeshRenderable Mesh;
        public long ViewportId;
        public bool Active;
        public bool HasWorld;
        public Matrix4x4 World;
        public Color Albedo;
        public Color EmissiveColor;
        public float EmissiveIntensity;
        public float Metallic;
        public float Roughness;
        public float AmbientOcclusion;
        public float Specular;
        public float NormalScale;
        public float AlphaCutoff;
        public MaterialBlendMode BlendMode;
        public int RenderPriority;
        public bool Unlit;
        public bool NoDepthTest;
        public bool DoubleSided;
        public bool CastShadows;
        public bool ReceiveShadows;
        public Vector2 UvOffset;
        public Vector2 UvScale;
        public string? TexturePath;
        public string? NormalTexturePath;
        public string? MetallicTexturePath;
        public string? RoughnessTexturePath;
        public string? OcclusionTexturePath;
        public string? EmissionTexturePath;
    }
    public struct SpriteDraw
    {
        public Matrix4x4 Transform;
        public ITexture Texture;
        public Vector2 Size;
        public Color Tint;
    }
    public struct TextDraw
    {
        public Matrix4x4 Transform;
        public string Text;
        public string FontName;
        public Color Color;
        public float Size;
    }
}
