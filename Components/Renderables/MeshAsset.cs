//using Assimp;
//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Linq;
//using System.Numerics;
//using V12.Core.Core.Interfaces;
//using V12.Core.Interfaces.Renderer;

//namespace V12.Components.Renderables
//{
//    /// <summary>
//    /// Loads 3D mesh assets (GLB, GLTF, OBJ) and exposes them as IMeshRenderable.
//    /// Material data is extracted from the asset and made available to renderers.
//    /// </summary>
//    public class MeshAsset : ComponentBase, IMeshRenderable
//    {
//        private readonly string _assetPath;
//        private readonly AssimpContext _assimpContext;

//        // Cached mesh data
//        private double[] _meshPoints;
//        private int[] _indices;
//        private MaterialData _material;

//        // Transform state
//        private Vector3 _position = Vector3.Zero;
//        private Quaternion _rotation = Quaternion.Identity;
//        private Vector3 _scale = Vector3.One;
//        private Matrix4x4 _transform;
//        private bool _transformDirty = true;

//        public RenderType RenderType => RenderType.Mesh;

//        /// <summary>
//        /// Mesh vertices as flat array [x0, y0, z0, x1, y1, z1, ...]
//        /// Lazily loaded on first access.
//        /// </summary>
//        public double[] MeshPoints => _meshPoints ??= LoadMeshData();

//        /// <summary>
//        /// Triangle indices into MeshPoints.
//        /// Lazily loaded on first access.
//        /// </summary>
//        public int[] Indices => _indices ??= LoadIndices();

//        /// <summary>
//        /// Material data extracted from the asset.
//        /// Lazily loaded on first access.
//        /// </summary>
//        public MaterialData Material => _material ??= ExtractMaterialData();

//        public Matrix4x4 Transform
//        {
//            get
//            {
//                if (_transformDirty)
//                {
//                    _transform = Matrix4x4.CreateScale(_scale)
//                        * Matrix4x4.CreateFromQuaternion(_rotation)
//                        * Matrix4x4.CreateTranslation(_position);
//                    _transformDirty = false;
//                }
//                return _transform;
//            }
//        }

//        public bool IsWorldLocked => true;

//        public Vector3 Position
//        {
//            get => _position;
//            set
//            {
//                _position = value;
//                _transformDirty = true;
//            }
//        }

//        public Quaternion Rotation
//        {
//            get => _rotation;
//            set
//            {
//                _rotation = value;
//                _transformDirty = true;
//            }
//        }

//        public Vector3 Scale
//        {
//            get => _scale;
//            set
//            {
//                _scale = value;
//                _transformDirty = true;
//            }
//        }

//        public MeshAsset(string assetPath)
//        {
//            if (string.IsNullOrWhiteSpace(assetPath))
//                throw new ArgumentException("Asset path cannot be null or empty.", nameof(assetPath));

//            _assetPath = assetPath;
//            _assimpContext = new AssimpContext();
//        }

//        /// <summary>
//        /// Loads the scene once and caches it.
//        /// </summary>
//        private Scene LoadScene()
//        {
//            try
//            {
//                var postProcess = PostProcessSteps.Triangulate
//                    | PostProcessSteps.FlipUVs
//                    | PostProcessSteps.CalculateTangentSpace
//                    | PostProcessSteps.JoinIdenticalVertices;

//                var scene = _assimpContext.ImportFile(_assetPath, postProcess);

//                if (scene == null || scene.MeshCount == 0)
//                    throw new InvalidOperationException($"No meshes found in asset: {_assetPath}");

//                return scene;
//            }
//            catch (Exception ex)
//            {
//                throw new InvalidOperationException($"Failed to load mesh asset '{_assetPath}'", ex);
//            }
//        }

//        /// <summary>
//        /// Extracts all mesh vertices into a flat double array.
//        /// </summary>
//        private double[] LoadMeshData()
//        {
//            var scene = LoadScene();
//            var vertices = new List<double>();

//            foreach (var mesh in scene.Meshes)
//            {
//                foreach (var vertex in mesh.Vertices)
//                {
//                    vertices.Add(vertex.X);
//                    vertices.Add(vertex.Y);
//                    vertices.Add(vertex.Z);
//                }
//            }

//            if (vertices.Count == 0)
//                throw new InvalidOperationException($"Asset '{_assetPath}' contains no vertices.");

//            return vertices.ToArray();
//        }

//        /// <summary>
//        /// Extracts all mesh indices, accounting for vertex offsets between meshes.
//        /// </summary>
//        private int[] LoadIndices()
//        {
//            var scene = LoadScene();
//            var indices = new List<int>();
//            int vertexOffset = 0;

//            foreach (var mesh in scene.Meshes)
//            {
//                foreach (var face in mesh.Faces)
//                {
//                    if (face.IndexCount != 3)
//                        continue; // Skip non-triangles (should be none after triangulation)

//                    foreach (var index in face.Indices)
//                    {
//                        indices.Add(index + vertexOffset);
//                    }
//                }
//                vertexOffset += mesh.VertexCount;
//            }

//            if (indices.Count == 0)
//                throw new InvalidOperationException($"Asset '{_assetPath}' contains no face indices.");

//            return indices.ToArray();
//        }

//        /// <summary>
//        /// Extracts material data from the first material in the asset.
//        /// </summary>
//        private MaterialData ExtractMaterialData()
//        {
//            var scene = LoadScene();

//            if (scene.MaterialCount == 0)
//                return CreateDefaultMaterial();

//            var assimpMat = scene.Materials[0];
//            var material = new MaterialData
//            {
//                Name = assimpMat.Name ?? "Material"
//            };

//            // Extract color properties
//            if (assimpMat.HasColorDiffuse)
//            {
//                var color = assimpMat.ColorDiffuse;
//                material.Albedo = new Vector3(color.R, color.G, color.B);
//            }

//            if (assimpMat.HasColorEmissive)
//            {
//                var color = assimpMat.ColorEmissive;
//                material.EmissiveColor = new Vector3(color.R, color.G, color.B);
//            }

//            // Extract numeric properties
//            if (assimpMat.HasShininessStrength)
//                material.Metallic = Math.Clamp(assimpMat.ShininessStrength, 0, 1);

//            if (assimpMat.HasShininess)
//                material.Roughness = Math.Clamp(1f - (assimpMat.Shininess / 100f), 0, 1);

//            // Extract texture paths
//            material.AlbedoTexturePath = ExtractTexturePath(assimpMat, TextureType.Diffuse);
//            material.NormalMapPath = ExtractTexturePath(assimpMat, TextureType.Normals);
//            material.MetallicMapPath = ExtractTexturePath(assimpMat, TextureType.Metallic);
//            material.RoughnessMapPath = ExtractTexturePath(assimpMat, TextureType.Roughness);
//            material.AmbientOcclusionMapPath = ExtractTexturePath(assimpMat, TextureType.Ambient);
//            material.EmissiveMapPath = ExtractTexturePath(assimpMat, TextureType.Emissive);

//            // Blend mode
//            if (assimpMat.HasBlendFunc)
//            {
//                material.BlendMode = assimpMat.BlendFunc == BlendMode.Default
//                    ? BlendMode.Opaque
//                    : BlendMode.Transparent;
//            }

//            // Opacity
//            if (assimpMat.HasOpacity)
//                material.Opacity = Math.Clamp(assimpMat.Opacity, 0, 1);

//            // Double-sided
//            if (assimpMat.HasWireFrame)
//                material.DoubleSided = assimpMat.WireFrame;

//            return material;
//        }

//        /// <summary>
//        /// Extracts texture path from material, resolving relative to asset directory.
//        /// </summary>
//        private string ExtractTexturePath(Material material, TextureType type)
//        {
//            if (material.GetMaterialTextureCount(type) == 0)
//                return null;

//            var textureSlot = material.GetMaterialTexture(type, 0);
//            var texturePath = textureSlot.FilePath;

//            if (string.IsNullOrEmpty(texturePath))
//                return null;

//            // If it's a relative path, resolve it relative to the asset directory
//            if (!Path.IsPathRooted(texturePath))
//            {
//                var assetDir = Path.GetDirectoryName(_assetPath);
//                texturePath = Path.Combine(assetDir, texturePath);
//            }

//            return texturePath;
//        }

//        private MaterialData CreateDefaultMaterial()
//        {
//            return new MaterialData
//            {
//                Name = "Default",
//                Albedo = Vector3.One,
//                Metallic = 0f,
//                Roughness = 0.5f
//            };
//        }

//        public override IWorldElement BuildUI()
//        {
//            // TODO: Build UI controls for position, rotation, scale
//            throw new NotImplementedException();
//        }

//        public void Dispose()
//        {
//            _assimpContext?.Dispose();
//        }
//    }
//}