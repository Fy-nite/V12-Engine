using System;
using System.Collections.Generic;
using System.Numerics;

namespace V12.Components.Renderables
{
    /// <summary>
    /// Renderer-agnostic material data.
    /// Each renderer implementation interprets this data according to its own pipeline.
    /// </summary>
    public class MaterialData
    {
        public string Name { get; set; } = "Default";

        // PBR Properties
        public Vector3 Albedo { get; set; } = Vector3.One;
        public float Metallic { get; set; } = 0f;
        public float Roughness { get; set; } = 0.5f;
        public float AmbientOcclusion { get; set; } = 1f;

        // Texture paths (renderer resolves these relative to asset root or absolute)
        public string AlbedoTexturePath { get; set; }
        public string NormalMapPath { get; set; }
        public string MetallicMapPath { get; set; }
        public string RoughnessMapPath { get; set; }
        public string AmbientOcclusionMapPath { get; set; }
        public string EmissiveMapPath { get; set; }

        // Emissive
        public Vector3 EmissiveColor { get; set; } = Vector3.Zero;
        public float EmissiveIntensity { get; set; } = 0f;

        // Blending
        public BlendMode BlendMode { get; set; } = BlendMode.Opaque;
        public float Opacity { get; set; } = 1f;

        // Flags
        public bool DoubleSided { get; set; } = false;
        public bool ReceiveShadows { get; set; } = true;

        // Custom properties for renderer-specific features
        public Dictionary<string, object> CustomProperties { get; set; } = new();
    }

    public enum BlendMode
    {
        Opaque,
        Transparent,
        Additive,
        Multiplicative
    }
}