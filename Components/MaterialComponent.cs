using System;
using V12.Components.Renderables;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.UI;

namespace V12.Components
{
    /// <summary>
    /// Describes the surface appearance of an element: colour (RGBA) and PBR parameters.
    /// R/G/B/A are in the 0–1 range. Metallic and Roughness are also 0–1.
    /// Texture paths are relative (e.g. "textures/crate.png") and resolved against the
    /// owning world's V12:// mount point at render time.
    /// </summary>
    [ComponentEntry("RENDER", "Material", SingleInstance = true, SortOrder = 3, Icon = "palette")]
    public class MaterialComponent : ComponentBase
    {
        private float _r        = 1f;
        private float _g        = 1f;
        private float _b        = 1f;
        private float _a        = 1f;
        private float _metallic = 0f;
        private float _roughness = 0.5f;
        private float _emissionR;
        private float _emissionG;
        private float _emissionB;
        private float _emissionEnergy = 1f;
        private float _normalScale = 1f;
        private float _alphaCutoff = 0.5f;
        private bool _doubleSided;
        private bool _triplanar;
        private bool _triplanarWorld;
        private float _triplanarSharpness = 1f;
        private int _uvSet;
        private bool _unlit;
        private bool _noDepthTest;
        private string? _textureUrl;
        private string? _albedoTexture;
        private string? _normalTexture;
        private string? _metallicTexture;
        private string? _roughnessTexture;
        private string? _emissionTexture;
        private string? _occlusionTexture;
        private float _uv1OffsetX;
        private float _uv1OffsetY;
        private float _uv1ScaleX = 1f;
        private float _uv1ScaleY = 1f;

        public override string Name        => "Material";
        public override string Description => "Surface colour and PBR properties";

        private string? _materialName;

        /// <summary>Name of the material this component was built from, when it
        /// came from a model file. Cosmetic: it labels the surface so a rebuilt
        /// model stays recognisable part by part.</summary>
        public string? MaterialName
        {
            get => _materialName;
            set { if (_materialName != value) { _materialName = value; MarkDirty(); } }
        }

        public string? TextureUrl
        {
            get => _textureUrl;
            set { _textureUrl = value; MarkDirty(); }
        }

        public string? AlbedoTexture
        {
            get => _albedoTexture;
            set { _albedoTexture = value; MarkDirty(); }
        }

        public string? NormalTexture
        {
            get => _normalTexture;
            set { _normalTexture = value; MarkDirty(); }
        }

        public string? MetallicTexture
        {
            get => _metallicTexture;
            set { _metallicTexture = value; MarkDirty(); }
        }

        public string? RoughnessTexture
        {
            get => _roughnessTexture;
            set { _roughnessTexture = value; MarkDirty(); }
        }

        public string? EmissionTexture
        {
            get => _emissionTexture;
            set { _emissionTexture = value; MarkDirty(); }
        }

        public string? OcclusionTexture
        {
            get => _occlusionTexture;
            set { _occlusionTexture = value; MarkDirty(); }
        }

        /// <summary>Emission tint. Only visible when the energy or an emission
        /// map is non-zero.</summary>
        public float EmissionR
        {
            get => _emissionR;
            set { if (Math.Abs(_emissionR - value) > 0.001f) { _emissionR = Math.Max(0f, value); MarkDirty(); } }
        }
        public float EmissionG
        {
            get => _emissionG;
            set { if (Math.Abs(_emissionG - value) > 0.001f) { _emissionG = Math.Max(0f, value); MarkDirty(); } }
        }
        public float EmissionB
        {
            get => _emissionB;
            set { if (Math.Abs(_emissionB - value) > 0.001f) { _emissionB = Math.Max(0f, value); MarkDirty(); } }
        }
        public float EmissionEnergy
        {
            get => _emissionEnergy;
            set { if (Math.Abs(_emissionEnergy - value) > 0.001f) { _emissionEnergy = Math.Max(0f, value); MarkDirty(); } }
        }

        /// <summary>Strength of the normal map. 1 = as authored.</summary>
        public float NormalScale
        {
            get => _normalScale;
            set { if (Math.Abs(_normalScale - value) > 0.001f) { _normalScale = value; MarkDirty(); } }
        }

        /// <summary>Alpha below this is discarded when the surface is not
        /// blended (cutout foliage, punched metal).</summary>
        public float AlphaCutoff
        {
            get => _alphaCutoff;
            set { if (Math.Abs(_alphaCutoff - value) > 0.001f) { _alphaCutoff = Clamp01(value); MarkDirty(); } }
        }

        /// <summary>Draw both faces. Models frequently author single-sided
        /// planes that need to be visible from behind.</summary>
        public bool DoubleSided
        {
            get => _doubleSided;
            set { if (_doubleSided != value) { _doubleSided = value; MarkDirty(); } }
        }

        /// <summary>Project textures from world space instead of sampling the
        /// mesh UVs. This is the fallback for models whose meshes carry no UV
        /// set at all: without it every texel collapses to one point.</summary>
        public bool Triplanar
        {
            get => _triplanar;
            set { if (_triplanar != value) { _triplanar = value; MarkDirty(); } }
        }

        /// <summary>Triplanar in world space rather than object space, so the
        /// projection does not swim when the object moves or scales.</summary>
        public bool TriplanarWorld
        {
            get => _triplanarWorld;
            set { if (_triplanarWorld != value) { _triplanarWorld = value; MarkDirty(); } }
        }

        /// <summary>Blend tightness between the three triplanar projections.
        /// Higher is sharper at the seams.</summary>
        public float TriplanarSharpness
        {
            get => _triplanarSharpness;
            set { if (Math.Abs(_triplanarSharpness - value) > 0.001f) { _triplanarSharpness = Math.Max(0.01f, value); MarkDirty(); } }
        }

        /// <summary>UV channel the textures address (0 or 1 today).</summary>
        public int UvSet
        {
            get => _uvSet;
            set { if (_uvSet != value) { _uvSet = Math.Max(0, value); MarkDirty(); } }
        }

        public float R
        {
            get => _r;
            set { if (Math.Abs(_r - value) > 0.001f) { _r = Clamp01(value); MarkDirty(); } }
        }
        public float G
        {
            get => _g;
            set { if (Math.Abs(_g - value) > 0.001f) { _g = Clamp01(value); MarkDirty(); } }
        }
        public float B
        {
            get => _b;
            set { if (Math.Abs(_b - value) > 0.001f) { _b = Clamp01(value); MarkDirty(); } }
        }
        public float A
        {
            get => _a;
            set { if (Math.Abs(_a - value) > 0.001f) { _a = Clamp01(value); MarkDirty(); } }
        }
        public float Metallic
        {
            get => _metallic;
            set { if (Math.Abs(_metallic - value) > 0.001f) { _metallic = Clamp01(value); MarkDirty(); } }
        }
        public float Roughness
        {
            get => _roughness;
            set { if (Math.Abs(_roughness - value) > 0.001f) { _roughness = Clamp01(value); MarkDirty(); } }
        }

        /// <summary>Skip scene lighting: render the flat albedo colour
        /// (editor gizmos, overlays).</summary>
        public bool Unlit
        {
            get => _unlit;
            set { if (_unlit != value) { _unlit = value; MarkDirty(); } }
        }

        /// <summary>Skip depth testing: always draw on top (editor gizmos).</summary>
        public bool NoDepthTest
        {
            get => _noDepthTest;
            set { if (_noDepthTest != value) { _noDepthTest = value; MarkDirty(); } }
        }

        public float Uv1OffsetX
        {
            get => _uv1OffsetX;
            set { if (Math.Abs(_uv1OffsetX - value) > 0.001f) { _uv1OffsetX = value; MarkDirty(); } }
        }
        public float Uv1OffsetY
        {
            get => _uv1OffsetY;
            set { if (Math.Abs(_uv1OffsetY - value) > 0.001f) { _uv1OffsetY = value; MarkDirty(); } }
        }
        public float Uv1ScaleX
        {
            get => _uv1ScaleX;
            set { if (Math.Abs(_uv1ScaleX - value) > 0.001f) { _uv1ScaleX = value; MarkDirty(); } }
        }
        public float Uv1ScaleY
        {
            get => _uv1ScaleY;
            set { if (Math.Abs(_uv1ScaleY - value) > 0.001f) { _uv1ScaleY = value; MarkDirty(); } }
        }

        public MaterialComponent() { }
        public MaterialComponent(float r, float g, float b, float a = 1f, float metallic = 0f, float roughness = 0.5f, bool unlit = false, bool noDepthTest = false)
        {
            _r = Clamp01(r); _g = Clamp01(g); _b = Clamp01(b); _a = Clamp01(a);
            _metallic = Clamp01(metallic); _roughness = Clamp01(roughness);
            _unlit = unlit; _noDepthTest = noDepthTest;
        }

        /// <summary>
        /// Fills a component from a material authored in a model file. Used when
        /// a model's hierarchy is rebuilt as elements: each part gets the surface
        /// its file declared instead of a flat default.
        /// <paramref name="needsTriplanar"/> switches projection on for meshes
        /// that carry no UV set, where sampling would otherwise collapse.
        /// </summary>
        public static MaterialComponent FromModelMaterial(ModelMaterial source, bool needsTriplanar)
        {
            var material = new MaterialComponent
            {
                _r = Clamp01(source.Albedo.X),
                _g = Clamp01(source.Albedo.Y),
                _b = Clamp01(source.Albedo.Z),
                _a = Clamp01(source.Albedo.W),
                _metallic = Clamp01(source.Metallic),
                _roughness = Clamp01(source.Roughness),
                _emissionR = Math.Max(0f, source.Emission.X),
                _emissionG = Math.Max(0f, source.Emission.Y),
                _emissionB = Math.Max(0f, source.Emission.Z),
                _emissionEnergy = Math.Max(0f, source.EmissionStrength),
                _normalScale = source.NormalScale,
                _alphaCutoff = Clamp01(source.AlphaCutoff),
                _doubleSided = source.DoubleSided,
                _unlit = source.Unlit,
                _uvSet = Math.Max(0, source.UvChannel),
                _triplanar = needsTriplanar,
                _albedoTexture = source.AlbedoTexture,
                _normalTexture = source.NormalTexture,
                _metallicTexture = source.MetallicTexture,
                _roughnessTexture = source.RoughnessTexture,
                _emissionTexture = source.EmissionTexture,
                _occlusionTexture = source.OcclusionTexture,
                _textureUrl = source.AlbedoTexture,
            };

            if (source.AlphaBlend)
                material._a = Clamp01(source.Albedo.W);

            return material;
        }

        /// <summary>Generate editable material fields for the inspector.</summary>
        public override void BuildInspector(IInspector inspector)
        {
            inspector.Section("Colour");
            inspector.ReadOnly("Source", string.IsNullOrWhiteSpace(MaterialName) ? "(authored)" : MaterialName);
            inspector.Float("Red", () => R, v => R = v);
            inspector.Float("Green", () => G, v => G = v);
            inspector.Float("Blue", () => B, v => B = v);
            inspector.Float("Alpha", () => A, v => A = v);
            inspector.Float("Metallic", () => Metallic, v => Metallic = v);
            inspector.Float("Roughness", () => Roughness, v => Roughness = v);
            inspector.Float("Normal Scale", () => NormalScale, v => NormalScale = v);
            inspector.Float("Alpha Cutoff", () => AlphaCutoff, v => AlphaCutoff = v);
            inspector.Bool("Double Sided", () => DoubleSided, v => DoubleSided = v);
            inspector.Bool("Unlit", () => Unlit, v => Unlit = v);
            inspector.Section("Emission");
            inspector.Float("Emission R", () => EmissionR, v => EmissionR = v);
            inspector.Float("Emission G", () => EmissionG, v => EmissionG = v);
            inspector.Float("Emission B", () => EmissionB, v => EmissionB = v);
            inspector.Float("Emission Energy", () => EmissionEnergy, v => EmissionEnergy = v);
            inspector.Section("Projection");
            inspector.Bool("Triplanar", () => Triplanar, v => Triplanar = v);
            inspector.Bool("Triplanar World", () => TriplanarWorld, v => TriplanarWorld = v);
            inspector.Float("Triplanar Sharpness", () => TriplanarSharpness, v => TriplanarSharpness = v);
            inspector.Int("UV Set", () => UvSet, v => UvSet = v);
            inspector.Section("UV");
            inspector.Float("UV Offset X", () => Uv1OffsetX, v => Uv1OffsetX = v);
            inspector.Float("UV Offset Y", () => Uv1OffsetY, v => Uv1OffsetY = v);
            inspector.Float("UV Scale X", () => Uv1ScaleX, v => Uv1ScaleX = v);
            inspector.Float("UV Scale Y", () => Uv1ScaleY, v => Uv1ScaleY = v);
            inspector.Section("Textures");
            // AssetPath (not String) so hosts can offer a project-asset
            // picker; values may be relative paths or v12:// URIs.
            inspector.AssetPath("Albedo", () => AlbedoTexture ?? "", v => AlbedoTexture = v, TextureExtensions);
            inspector.AssetPath("Normal", () => NormalTexture ?? "", v => NormalTexture = v, TextureExtensions);
            inspector.AssetPath("Metallic Map", () => MetallicTexture ?? "", v => MetallicTexture = v, TextureExtensions);
            inspector.AssetPath("Roughness Map", () => RoughnessTexture ?? "", v => RoughnessTexture = v, TextureExtensions);
            inspector.AssetPath("Emission Map", () => EmissionTexture ?? "", v => EmissionTexture = v, TextureExtensions);
            inspector.AssetPath("Occlusion Map", () => OcclusionTexture ?? "", v => OcclusionTexture = v, TextureExtensions);
        }

        private static readonly string[] TextureExtensions =
        {
            "png", "jpg", "jpeg", "webp", "bmp", "tga", "ktx", "ktx2", "exr", "hdr"
        };

        public string? PrimaryTexture =>
            AlbedoTexture ?? NormalTexture ?? MetallicTexture ?? RoughnessTexture ?? EmissionTexture ?? TextureUrl;

        private static float Clamp01(float v) => Math.Max(0f, Math.Min(1f, v));
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        public override string ToString() =>
            $"Material(RGBA:{R:F2},{G:F2},{B:F2},{A:F2} M:{Metallic:F2} R:{Roughness:F2} Tex:{PrimaryTexture})";
    }
}
