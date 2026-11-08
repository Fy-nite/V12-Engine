using System;
using V12.Core.Core.Interfaces;
using V12.Core;
using V12.Core.UI;
namespace V12.Components
{
    public enum BackgroundMode { SolidColor, Skybox }

    /// <summary>
    /// Scene-wide environment settings attached to an element.
    /// Controls sky/background and ambient lighting hints.
    /// </summary>
    [ComponentEntry("LIGHTING", "Environment", SingleInstance = true, SortOrder = 4, Icon = "mountain-sun")]
    public class EnvironmentComponent : ComponentBase
    {
        private BackgroundMode _mode = BackgroundMode.SolidColor;
        private float _skyR = 0.5f, _skyG = 0.6f, _skyB = 0.8f;
        private float _ambientR = 0.2f, _ambientG = 0.2f, _ambientB = 0.25f;
        private float _ambientEnergy = 1f;
        private string _skyboxPath = string.Empty;

        public override string Name => "Environment";
        public override string Description => "Scene environment (sky, ambient light)";

        public BackgroundMode Mode
        {
            get => _mode;
            set { if (_mode != value) { _mode = value; MarkDirty(); } }
        }

        public float SkyR { get => _skyR; set { if (Math.Abs(_skyR - value) > 0.0001f) { _skyR = Clamp01(value); MarkDirty(); } } }
        public float SkyG { get => _skyG; set { if (Math.Abs(_skyG - value) > 0.0001f) { _skyG = Clamp01(value); MarkDirty(); } } }
        public float SkyB { get => _skyB; set { if (Math.Abs(_skyB - value) > 0.0001f) { _skyB = Clamp01(value); MarkDirty(); } } }

        public float AmbientR { get => _ambientR; set { if (Math.Abs(_ambientR - value) > 0.0001f) { _ambientR = Clamp01(value); MarkDirty(); } } }
        public float AmbientG { get => _ambientG; set { if (Math.Abs(_ambientG - value) > 0.0001f) { _ambientG = Clamp01(value); MarkDirty(); } } }
        public float AmbientB { get => _ambientB; set { if (Math.Abs(_ambientB - value) > 0.0001f) { _ambientB = Clamp01(value); MarkDirty(); } } }

        public float AmbientEnergy { get => _ambientEnergy; set { if (Math.Abs(_ambientEnergy - value) > 0.0001f) { _ambientEnergy = MathF.Max(0f, value); MarkDirty(); } } }

        /// <summary>Optional path to a skybox resource (Panorama or CubeMap).</summary>
        public string SkyboxPath { get => _skyboxPath; set { if (_skyboxPath != value) { _skyboxPath = value ?? string.Empty; MarkDirty(); } } }

        // ── Shadows ─────────────────────────────────────────────────────────

        private bool _shadowsEnabled = true;
        private float _shadowDistance = 100f;
        private float _shadowBias = 2f;
        private float _shadowNormalBias = 1f;
        private float _shadowOpacity = 1f;

        /// <summary>Master switch for realtime shadows from every light.</summary>
        public bool ShadowsEnabled
        {
            get => _shadowsEnabled;
            set { if (_shadowsEnabled != value) { _shadowsEnabled = value; MarkDirty(); } }
        }

        /// <summary>How far from the camera directional shadows are drawn. Smaller
        /// values spend the shadow map on the near scene and look sharper.</summary>
        public float ShadowDistance
        {
            get => _shadowDistance;
            set { if (Math.Abs(_shadowDistance - value) > 0.0001f) { _shadowDistance = MathF.Max(1f, value); MarkDirty(); } }
        }

        /// <summary>Depth offset used when comparing against the shadow map. Raise
        /// it when a large flat surface (a floor) shadows itself.</summary>
        public float ShadowBias
        {
            get => _shadowBias;
            set { if (Math.Abs(_shadowBias - value) > 0.0001f) { _shadowBias = MathF.Max(0f, value); MarkDirty(); } }
        }

        /// <summary>Extra offset along the surface normal, for grazing angles.</summary>
        public float ShadowNormalBias
        {
            get => _shadowNormalBias;
            set { if (Math.Abs(_shadowNormalBias - value) > 0.0001f) { _shadowNormalBias = MathF.Max(0f, value); MarkDirty(); } }
        }

        /// <summary>How dark a shadow is. 1 = fully occluded, 0 = no visible shadow.</summary>
        public float ShadowOpacity
        {
            get => _shadowOpacity;
            set { if (Math.Abs(_shadowOpacity - value) > 0.0001f) { _shadowOpacity = Clamp01(value); MarkDirty(); } }
        }

        public EnvironmentComponent() { }

        public EnvironmentComponent(BackgroundMode mode, float skyR, float skyG, float skyB)
        {
            _mode = mode; _skyR = Clamp01(skyR); _skyG = Clamp01(skyG); _skyB = Clamp01(skyB);
        }
        /// <summary>Generate editable environment fields for the inspector.</summary>
        public override void BuildInspector(IInspector inspector)
        {
            inspector.Section("Environment");
            inspector.Enum("Mode", () => Mode, v => Mode = v);
            inspector.Float("Sky Red", () => SkyR, v => SkyR = v);
            inspector.Float("Sky Green", () => SkyG, v => SkyG = v);
            inspector.Float("Sky Blue", () => SkyB, v => SkyB = v);
            inspector.Float("Ambient Red", () => AmbientR, v => AmbientR = v);
            inspector.Float("Ambient Green", () => AmbientG, v => AmbientG = v);
            inspector.Float("Ambient Blue", () => AmbientB, v => AmbientB = v);
            inspector.Float("Ambient Energy", () => AmbientEnergy, v => AmbientEnergy = v);
            inspector.String("Skybox Path", () => SkyboxPath, v => SkyboxPath = v);
            inspector.Section("Shadows");
            inspector.Bool("Enabled", () => ShadowsEnabled, v => ShadowsEnabled = v);
            inspector.Float("Distance", () => ShadowDistance, v => ShadowDistance = v);
            inspector.Float("Bias", () => ShadowBias, v => ShadowBias = v);
            inspector.Float("Normal Bias", () => ShadowNormalBias, v => ShadowNormalBias = v);
            inspector.Float("Opacity", () => ShadowOpacity, v => ShadowOpacity = v);
        }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        private static float Clamp01(float v) => Math.Max(0f, Math.Min(1f, v));
    }
}
