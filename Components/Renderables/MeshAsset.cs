using Assimp;
using System;
using System.Numerics;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

namespace V12.Components.Renderables
{
    /// <summary>
    /// Loads a 3D model file (GLB, GLTF, OBJ, FBX, …) and exposes the WHOLE file
    /// as one flattened triangle soup with the node hierarchy baked into the
    /// vertices. Use this when an element should be a single mesh; use
    /// <see cref="ModelInstance"/> when the model's own hierarchy and per-part
    /// materials should be rebuilt as elements.
    ///
    /// An element's own <c>MaterialComponent</c> still wins; this component only
    /// supplies a material for elements that don't carry one.
    /// </summary>
    [ComponentEntry("RENDER", "Mesh Asset", SingleInstance = true, SortOrder = 5, Icon = "asset")]
    public class MeshAsset : ComponentBase, IMeshRenderable, IModelMaterialSource, IDisposable
    {
        private static readonly string[] ModelExtensions =
        {
            "glb", "gltf", "obj", "fbx", "dae", "ply", "stl", "3ds", "x", "off", "abc", "lwo", "ctm"
        };

        private readonly object _gate = new();
        private string? _assetPath;
        private ModelDocument? _model;
        private Material? _material;

        /// <summary>Path to the model: a <c>v12://</c> URI, a <c>res://</c> path,
        /// or a plain file path. Setting it discards everything already loaded.</summary>
        public string? AssetPath
        {
            get => _assetPath;
            set
            {
                if (string.Equals(_assetPath, value, StringComparison.Ordinal))
                    return;
                _assetPath = value;
                lock (_gate)
                {
                    _model = null;
                    _material = null;
                }
            }
        }

        private ModelDocument Model
        {
            get
            {
                lock (_gate)
                    return _model ??= ModelLoader.Load(
                        _assetPath ?? throw new InvalidOperationException("MeshAsset has no AssetPath."));
            }
        }

        public RenderType RenderType => RenderType.Mesh;

        /// <summary>Vertices as a flat array [x0, y0, z0, x1, y1, z1, …], with
        /// every node transform already applied.</summary>
        public double[] MeshPoints => Model.Points;

        /// <summary>Flat UV0 array parallel to <see cref="MeshPoints"/>, or null
        /// when the model has no texture coordinates at all.</summary>
        public double[]? MeshUvs => Model.Uvs;

        /// <summary>Flat normals parallel to <see cref="MeshPoints"/>, or null
        /// when the file carries none (the producer then derives them).</summary>
        public double[]? MeshNormals => Model.Normals;

        /// <summary>Per-vertex tangents packed xyzw, or null when there is no
        /// normal/UV data to derive them from.</summary>
        public double[]? MeshTangents => Model.Tangents;

        /// <summary>Triangle indices into <see cref="MeshPoints"/>.</summary>
        public uint[] Indices => Model.Indices;

        /// <summary>The dominant material rebuilt as a plain Assimp object, for
        /// callers that want the legacy <see cref="IMeshRenderable"/> shape. The
        /// render path reads the structured values below instead.</summary>
        public Material Material
        {
            get
            {
                lock (_gate)
                {
                    if (_material is not null)
                        return _material;

                    var material = new Material { Name = "Dominant" };
                    if (_assetPath is null)
                        return _material = material;

                    var model = _model ??= ModelLoader.Load(_assetPath);
                    if (model.Materials.Count > 0)
                    {
                        var source = model.Materials[
                            Math.Clamp(model.DominantMaterialIndex, 0, model.Materials.Count - 1)];
                        material.Name = source.Name;
                        material.ColorDiffuse = source.Albedo;
                        material.Opacity = source.Albedo.W;
                    }

                    return _material = material;
                }
            }
        }

        // ── IModelMaterialSource ────────────────────────────────────────────

        public Vector4 AlbedoColor => Dominant.Albedo;
        public float Metallic => Dominant.Metallic;
        public float Roughness => Dominant.Roughness;
        public string? AlbedoTexture => Dominant.AlbedoTexture;
        public string? NormalTexture => Dominant.NormalTexture;
        public string? MetallicTexture => Dominant.MetallicTexture;
        public string? RoughnessTexture => Dominant.RoughnessTexture;
        public string? EmissionTexture => Dominant.EmissionTexture;

        private ModelMaterial Dominant
        {
            get
            {
                var model = Model;
                if (model.Materials.Count == 0)
                    return new ModelMaterial();
                return model.Materials[Math.Clamp(model.DominantMaterialIndex, 0, model.Materials.Count - 1)];
            }
        }

        public bool IsWorldLocked => true;

        /// <summary>Meshes carry no transform of their own: placement lives on
        /// the element (<see cref="ISpatial"/>), so this is always identity.</summary>
        public TRS LocalTransform => default;

        public Matrix4x4 WorldTransform => Owner?.WorldTransform ?? Matrix4x4.Identity;

        public MeshAsset() { }

        public MeshAsset(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new ArgumentException("Asset path cannot be null or empty.", nameof(assetPath));

            _assetPath = assetPath;
        }

        public override void BuildInspector(V12.Core.UI.IInspector inspector)
        {
            inspector.Section("Model");
            // AssetPath (not String) so hosts can offer a project-asset picker;
            // values may be v12:// URIs, res:// paths or relative file paths.
            inspector.AssetPath("Asset", () => AssetPath ?? "", v => AssetPath = v, ModelExtensions);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            Dispose();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _model = null;
                _material = null;
            }
        }
    }
}
