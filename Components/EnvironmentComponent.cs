using System;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    public enum BackgroundMode { SolidColor, Skybox }

    /// <summary>
    /// Scene-wide environment settings attached to an element.
    /// Controls sky/background and ambient lighting hints.
    /// </summary>
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

        public EnvironmentComponent() { }

        public EnvironmentComponent(BackgroundMode mode, float skyR, float skyG, float skyB)
        {
            _mode = mode; _skyR = Clamp01(skyR); _skyG = Clamp01(skyG); _skyB = Clamp01(skyB);
        }
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        private static float Clamp01(float v) => Math.Max(0f, Math.Min(1f, v));
    }
}
