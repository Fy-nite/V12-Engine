using Assimp;
using System;
using System.Collections.Generic;
using System.Numerics;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;
using V12.Core.UI;

namespace V12.Components.Renderables
{
    /// <summary>
    /// Loads a 3D model file and rebuilds the model's own node hierarchy as V12
    /// elements: every node keeps its local transform, every mesh part becomes a
    /// child element with a <see cref="StaticMesh"/>, and every part gets a
    /// <see cref="MaterialComponent"/> filled from the material authored in the
    /// file (PBR values, texture slots, and a triplanar fallback for meshes with
    /// no UV set).
    ///
    /// Use <see cref="MeshAsset"/> instead when a single flattened mesh is
    /// wanted; this component is for models that should stay editable part by
    /// part.
    /// </summary>
    [ComponentEntry("RENDER", "Model", SingleInstance = true, SortOrder = 6, Icon = "asset")]
    public class ModelInstance : ComponentBase, IDisposable
    {
        private static readonly string[] ModelExtensions =
        {
            "glb", "gltf", "obj", "fbx", "dae", "ply", "stl", "3ds", "x", "off", "abc", "lwo", "ctm"
        };

        private readonly object _gate = new();
        private readonly List<IWorldElement> _generated = new();
        private string? _assetPath;
        private ModelDocument? _document;
        private bool _rebuilding;

        /// <summary>Path to the model: a <c>v12://</c> URI, a <c>res://</c> path,
        /// or a plain file path. Changing it rebuilds the generated elements.</summary>
        public string? AssetPath
        {
            get => _assetPath;
            set
            {
                if (string.Equals(_assetPath, value, StringComparison.Ordinal))
                    return;
                _assetPath = value;
                lock (_gate)
                    _document = null;
                Rebuild();
            }
        }

        /// <summary>When true (the default) the model's node hierarchy is
        /// rebuilt as elements. When false a single flattened mesh is created
        /// under the owner instead.</summary>
        public bool BuildHierarchy { get; set; } = true;

        /// <summary>Turn on triplanar projection for parts whose mesh carries no
        /// UV set but whose material has textures. Without it those parts would
        /// sample a single texel and read as flat colour.</summary>
        public bool TriplanarFallback { get; set; } = true;

        /// <summary>The materials discovered in the file, in file order. Exposed
        /// so hosts can list what a model offers without re-importing it.</summary>
        public IReadOnlyList<ModelMaterial> Materials =>
            (IReadOnlyList<ModelMaterial>?)Document?.Materials ?? Array.Empty<ModelMaterial>();

        /// <summary>The elements this component created for the current model.
        /// They are removed and rebuilt whenever the asset changes.</summary>
        public IReadOnlyList<IWorldElement> GeneratedElements => _generated;

        private ModelDocument? Document
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_assetPath))
                    return null;
                lock (_gate)
                    return _document ??= ModelLoader.Load(_assetPath);
            }
        }

        public ModelInstance() { }

        public ModelInstance(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new ArgumentException("Asset path cannot be null or empty.", nameof(assetPath));

            _assetPath = assetPath;
        }

        public override void OnAttach(IWorldElement worldElement)
        {
            base.OnAttach(worldElement);
            Rebuild();
        }

        /// <summary>
        /// Discards the elements built from the previous asset and rebuilds them
        /// from the current one. Safe to call while the owner is already in a
        /// world: child insertion takes the world's (recursive) write lock.
        /// </summary>
        public void Rebuild()
        {
            if (_rebuilding)
                return;

            var owner = Owner;
            if (owner is null)
                return;

            _rebuilding = true;
            try
            {
                ClearGenerated(owner);

                if (string.IsNullOrWhiteSpace(_assetPath))
                    return;

                ModelDocument document;
                try
                {
                    document = Document!;
                }
                catch (Exception ex)
                {
                    GameRoot.Log?.Error(ex, "[ModelInstance] Could not load '{Asset}'", _assetPath);
                    return;
                }

                if (document.Root.Parts.Count == 0 && document.Root.Children.Count == 0)
                    return;

                if (BuildHierarchy)
                {
                    Build(document.Root, owner, document);
                }
                else
                {
                    BuildFlat(document, owner);
                }

                // The rebuilt hierarchy is derived from the file, so it must not
                // be saved to WorldML or replicated: it is regenerated on load.
                // The marker covers descendants, so one on the root is enough.
                if (_generated.Count > 0 && _generated[0] is Element generatedRoot)
                    generatedRoot.AddComponent(new NonPersistentComponent());
            }
            finally
            {
                _rebuilding = false;
            }
        }

        private void ClearGenerated(IWorldElement owner)
        {
            foreach (var element in _generated)
            {
                if (owner is Element parent)
                    parent.RemoveChild(element);
            }

            _generated.Clear();
        }

        private void Build(ModelNode node, IWorldElement parent, ModelDocument document)
        {
            // A node with no geometry and no children contributes nothing.
            if (node.Parts.Count == 0 && node.Children.Count == 0)
                return;

            var element = new Element(string.IsNullOrWhiteSpace(node.Name) ? "Node" : node.Name);
            ApplyTransform(element, node.LocalTransform);

            if (parent is Element parentElement)
                parentElement.AddChild(element);
            else
                parent.AddChild(element);

            _generated.Add(element);

            foreach (var part in node.Parts)
            {
                var partElement = new Element(
                    string.IsNullOrWhiteSpace(part.Name) ? "Mesh" : part.Name);

                var mesh = new StaticMesh(part.Points, part.Indices)
                {
                    MeshUvs = part.Uvs,
                    MeshNormals = part.Normals,
                    MeshTangents = part.Tangents,
                };

                var material = part.MaterialIndex >= 0 && part.MaterialIndex < document.Materials.Count
                    ? document.Materials[part.MaterialIndex]
                    : null;

                partElement.AddComponent(mesh);

                if (material is not null)
                {
                    bool triplanar = TriplanarFallback && part.NeedsTriplanar;
                    var component = MaterialComponent.FromModelMaterial(material, triplanar);
                    component.MaterialName = material.Name;
                    partElement.AddComponent(component);
                }

                if (element is Element partParent)
                    partParent.AddChild(partElement);
                else
                    element.AddChild(partElement);

                _generated.Add(partElement);
            }

            foreach (var child in node.Children)
                Build(child, element, document);
        }

        private void BuildFlat(ModelDocument document, IWorldElement owner)
        {
            var element = new Element("Model Mesh");
            var mesh = new StaticMesh(document.Points, document.Indices)
            {
                MeshUvs = document.Uvs,
                MeshNormals = document.Normals,
                MeshTangents = document.Tangents,
            };

            element.AddComponent(mesh);

            if (document.Materials.Count > 0)
            {
                var source = document.Materials[
                    Math.Clamp(document.DominantMaterialIndex, 0, document.Materials.Count - 1)];
                var component = MaterialComponent.FromModelMaterial(source, false);
                component.MaterialName = source.Name;
                element.AddComponent(component);
            }

            if (owner is Element parent)
                parent.AddChild(element);
            else
                owner.AddChild(element);

            _generated.Add(element);
        }

        private static void ApplyTransform(IWorldElement element, Matrix4x4 local)
        {
            if (Matrix4x4.Decompose(local, out var scale, out var rotation, out var translation))
            {
                element.LocalTransform = new TRS(translation, rotation, scale);
                return;
            }

            // A sheared or mirrored matrix cannot be decomposed into TRS; keep
            // its translation so the part at least lands in the right place.
            element.LocalTransform = new TRS(
                new Vector3(local.M41, local.M42, local.M43),
                Quaternion.Identity,
                Vector3.One);
        }

        public override void BuildInspector(IInspector inspector)
        {
            inspector.Section("Model");
            inspector.AssetPath("Asset", () => AssetPath ?? "", v => AssetPath = v, ModelExtensions);
            inspector.Bool("Hierarchy", () => BuildHierarchy, v => BuildHierarchy = v);
            inspector.Bool("Triplanar Fallback", () => TriplanarFallback, v => TriplanarFallback = v);

            var materials = Materials;
            if (materials.Count == 0)
                return;

            inspector.Section($"Materials ({materials.Count})");
            foreach (var material in materials)
            {
                string label = string.IsNullOrWhiteSpace(material.Name) ? "(unnamed)" : material.Name;
                string detail = material.AlbedoTexture is null
                    ? $"metallic {material.Metallic:0.##}, roughness {material.Roughness:0.##}"
                    : "textured";
                inspector.ReadOnly(label, detail);
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            Dispose();
        }

        public void Dispose()
        {
            lock (_gate)
                _document = null;
            _generated.Clear();
        }
    }
}
