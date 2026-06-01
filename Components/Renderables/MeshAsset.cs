using Assimp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

namespace V12.Components.Renderables
{
    /// <summary>
    /// Loads 3D mesh assets (GLB, GLTF, OBJ) and exposes them as IMeshRenderable.
    /// Material data is extracted from the asset and made available to renderers.
    /// </summary>
    public class MeshAsset : ComponentBase, IMeshRenderable
    {
        private string _assetPath;
        private readonly AssimpContext _assimpContext;

        public string AssetPath
        {
            get => _assetPath;
            set
            {
                if (_assetPath != value)
                {
                    _assetPath = value;
                    _meshPoints = null; // Invalidate cache
                    _indices = null;
                    _material = null;
                }
            }
        }

        // Cached mesh data
        private double[] _meshPoints;
        private uint[] _indices;
        private Material _material;

        // Transform state
        private Vector3 _position = Vector3.Zero;
        private Quaternion _rotation = Quaternion.Identity;
        private Vector3 _scale = Vector3.One;
        private Matrix4x4 _transform;
        private bool _transformDirty = true;

        public RenderType RenderType => RenderType.Mesh;

        /// <summary>
        /// Mesh vertices as flat array [x0, y0, z0, x1, y1, z1, ...]
        /// Lazily loaded on first access.
        /// </summary>
        public double[] MeshPoints => _meshPoints ??= LoadMeshData();

        /// <summary>
        /// Triangle indices into MeshPoints.
        /// Lazily loaded on first access.
        /// </summary>
        public uint[] Indices => _indices ??= LoadIndices();

        /// <summary>
        /// Material data extracted from the asset.
        /// Lazily loaded on first access.
        /// </summary>
        public Material Material => _material ??= LoadScene().Materials.FirstOrDefault() ?? new Material();

        public Matrix4x4 Transform
        {
            get
            {
                if (_transformDirty)
                {
                    _transform = Matrix4x4.CreateScale(_scale)
                        * Matrix4x4.CreateFromQuaternion(_rotation)
                        * Matrix4x4.CreateTranslation(_position);
                    _transformDirty = false;
                }
                return _transform;
            }
        }

        public bool IsWorldLocked => true;

        public Vector3 Position
        {
            get => _position;
            set
            {
                _position = value;
                _transformDirty = true;
            }
        }

        public Quaternion Rotation
        {
            get => _rotation;
            set
            {
                _rotation = value;
                _transformDirty = true;
            }
        }

        public Vector3 Scale
        {
            get => _scale;
            set
            {
                _scale = value;
                _transformDirty = true;
            }
        }

        public MeshAsset()
        {
            _assimpContext = new AssimpContext();
        }

        public MeshAsset(string assetPath) : this()
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new ArgumentException("Asset path cannot be null or empty.", nameof(assetPath));

            _assetPath = assetPath;
        }

        /// <summary>
        /// Loads the scene once and caches it.
        /// </summary>
        private Scene LoadScene()
        {
            try
            {
                var postProcess = PostProcessSteps.Triangulate
                    | PostProcessSteps.FlipUVs
                    | PostProcessSteps.CalculateTangentSpace
                    | PostProcessSteps.JoinIdenticalVertices;

                var scene = _assimpContext.ImportFile(_assetPath, postProcess);

                if (scene == null || scene.MeshCount == 0)
                    throw new InvalidOperationException($"No meshes found in asset: {_assetPath}");

                return scene;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load mesh asset '{_assetPath}'", ex);
            }
        }

        /// <summary>
        /// Extracts all mesh vertices into a flat double array.
        /// </summary>
        private double[] LoadMeshData()
        {
            var scene = LoadScene();
            var vertices = new List<double>();

            foreach (var mesh in scene.Meshes)
            {
                foreach (var vertex in mesh.Vertices)
                {
                    vertices.Add(vertex.X);
                    vertices.Add(vertex.Y);
                    vertices.Add(vertex.Z);
                }
            }

            if (vertices.Count == 0)
                throw new InvalidOperationException($"Asset '{_assetPath}' contains no vertices.");

            return vertices.ToArray();
        }

        /// <summary>
        /// Extracts all mesh indices, accounting for vertex offsets between meshes.
        /// </summary>
        private uint[] LoadIndices()
        {
            var scene = LoadScene();
            var indices = new List<uint>();
            uint vertexOffset = 0;

            foreach (var mesh in scene.Meshes)
            {
                foreach (var face in mesh.Faces)
                {
                    if (face.IndexCount != 3)
                        continue; // Skip non-triangles (should be none after triangulation)

                    foreach (var index in face.Indices)
                    {
                        indices.Add((uint)(index + vertexOffset));
                    }
                }
                vertexOffset += (uint)mesh.VertexCount;
            }

            if (indices.Count == 0)
                throw new InvalidOperationException($"Asset '{_assetPath}' contains no face indices.");

            return indices.ToArray();
        }

        public override IWorldElement BuildUI()
        {
            // TODO: Build UI controls for position, rotation, scale
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            _assimpContext?.Dispose();
        }
    }
}