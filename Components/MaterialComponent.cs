using System;
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
    public class MaterialComponent : ComponentBase
    {
        private float _r        = 1f;
        private float _g        = 1f;
        private float _b        = 1f;
        private float _a        = 1f;
        private float _metallic = 0f;
        private float _roughness = 0.5f;
        private bool _unlit;
        private bool _noDepthTest;
        private string? _textureUrl;
        private string? _albedoTexture;
        private string? _normalTexture;
        private string? _metallicTexture;
        private string? _roughnessTexture;
        private string? _emissionTexture;
        private float _uv1OffsetX;
        private float _uv1OffsetY;
        private float _uv1ScaleX = 1f;
        private float _uv1ScaleY = 1f;

        public override string Name        => "Material";
        public override string Description => "Surface colour and PBR properties";

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

        /// <summary>Generate editable material fields for the inspector.</summary>
        public override void BuildInspector(IInspector inspector)
        {
            inspector.Section("Colour");
            inspector.Float("Red", () => R, v => R = v);
            inspector.Float("Green", () => G, v => G = v);
            inspector.Float("Blue", () => B, v => B = v);
            inspector.Float("Alpha", () => A, v => A = v);
            inspector.Float("Metallic", () => Metallic, v => Metallic = v);
            inspector.Float("Roughness", () => Roughness, v => Roughness = v);
            inspector.Section("UV");
            inspector.Float("UV Offset X", () => Uv1OffsetX, v => Uv1OffsetX = v);
            inspector.Float("UV Offset Y", () => Uv1OffsetY, v => Uv1OffsetY = v);
            inspector.Float("UV Scale X", () => Uv1ScaleX, v => Uv1ScaleX = v);
            inspector.Float("UV Scale Y", () => Uv1ScaleY, v => Uv1ScaleY = v);
            inspector.Section("Textures");
            inspector.String("Albedo", () => AlbedoTexture ?? "", v => AlbedoTexture = v);
            inspector.String("Normal", () => NormalTexture ?? "", v => NormalTexture = v);
            inspector.String("Metallic Map", () => MetallicTexture ?? "", v => MetallicTexture = v);
            inspector.String("Roughness Map", () => RoughnessTexture ?? "", v => RoughnessTexture = v);
            inspector.String("Emission Map", () => EmissionTexture ?? "", v => EmissionTexture = v);
        }

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
