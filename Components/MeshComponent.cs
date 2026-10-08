using Assimp;
using System;
using System.Numerics;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;
using V12.Core.Networking;
using V12.Core.NetworkCable;
using V12.Core.UI;
using MongoDB.Bson.Serialization.Attributes;

namespace V12.Components
{
    /// <summary>Defines what primitive shape an element's mesh should use.</summary>
    public enum MeshShape
    {
        Box,
        Sphere,
        Capsule,
        Cylinder,
        Plane,
        Custom
    }

    /// <summary>
    /// Controls the rendered shape of an element.
    /// Width / Height / Depth map to the three axes of the primitive.
    /// For Sphere and Capsule they are diameters (radius = Width/2); the
    /// capsule's total height is Height, caps are spherical.
    /// </summary>
    public class MeshComponent : ComponentBase, IMeshRenderable
    {
        private MeshShape _shape  = MeshShape.Box;
        private float     _width  = 1f;
        private float     _height = 1f;
        private float     _depth  = 1f;

        // Cached mesh data
        private double[] _meshPoints;
        private uint[] _indices;

        // Optional custom vertex/index data (used when Shape == Custom)
        private double[] _customMeshPoints;
        private uint[] _customIndices;
        public double[] CustomMeshPoints
        {
            get => _customMeshPoints;
            set { _customMeshPoints = value; InvalidateCache(); MarkDirty(); PublishMeshToSyncAuthoritative(); }
        }
        public uint[] CustomIndices
        {
            get => _customIndices;
            set { _customIndices = value; InvalidateCache(); MarkDirty(); PublishMeshToSyncAuthoritative(); }
        }

        // ── SyncValue-based network sync for mesh data ───────────────────────
        // These SyncValues are registered in SyncRegistry and synced via the
        // SyncManager path (separate from DirtyTracker's component-level BSON sync).
        // They provide fine-grained value-level sync for mesh vertex/index data.
        /// <summary>SyncValue wrapper for <see cref="MeshPoints"/> — syncs the computed/generated mesh vertex data.</summary>
        [Sync, BsonIgnore]
        public SyncValue<double[]>? MeshPointsValue { get; set; }

        /// <summary>SyncValue wrapper for <see cref="Indices"/> — syncs the computed/generated mesh index data.</summary>
        [Sync, BsonIgnore]
        public SyncValue<uint[]>? IndicesValue { get; set; }

        /// <summary>Sync key prefix, derived from element ID and component ID.</summary>
        private string Key => $"mesh/{Owner?.Id ?? 0}/{Id}";

		public override string Name { get; set; } = "Mesh";
        public override string Description => "Primitive mesh shape";

        /// <summary>Generate editable mesh fields for the inspector.</summary>
        public override void BuildInspector(IInspector inspector)
        {
            inspector.Section("Mesh");
            inspector.Enum("Shape", () => Shape, v => Shape = v);
            inspector.Float("Width", () => Width, v => Width = v);
            inspector.Float("Height", () => Height, v => Height = v);
            inspector.Float("Depth", () => Depth, v => Depth = v);
        }

        public MeshShape Shape
        {
            get => _shape;
            set { if (_shape != value) { _shape = value; InvalidateCache(); MarkDirty(); PublishMeshToSyncAuthoritative(); } }
        }

        public float Width
        {
            get => _width;
            set { if (Math.Abs(_width - value) > 0.0001f) { _width = value; InvalidateCache(); MarkDirty(); PublishMeshToSyncAuthoritative(); } }
        }

        public float Height
        {
            get => _height;
            set { if (Math.Abs(_height - value) > 0.0001f) { _height = value; InvalidateCache(); MarkDirty(); PublishMeshToSyncAuthoritative(); } }
        }

        public float Depth
        {
            get => _depth;
            set { if (Math.Abs(_depth - value) > 0.0001f) { _depth = value; InvalidateCache(); MarkDirty(); PublishMeshToSyncAuthoritative(); } }
        }

        public override void OnAttach(IWorldElement element)
        {
            base.OnAttach(element);
            var collider = element.GetComponent<ColliderComponent>();
            if (collider != null)
            {
                _width = collider.Width;
                _height = collider.Height;
                _depth = collider.Depth;
            }

            // Auto-create SyncValue<T> wrappers for [Sync]-marked properties
            SyncAutoCreator.EnsureSyncValues(this, Key);

            // Publish current mesh data into SyncValues (only on authoritative side;
            // the internal check in PublishMeshToSyncAuthoritative handles this safely).
            PublishMeshToSyncAuthoritative();
        }

        /// <summary>
        /// Publish the current mesh data into SyncValues so remote peers receive it.
        /// Call this explicitly on the authoritative instance (e.g. server) when mesh data changes.
        /// SyncValues are registered in SyncRegistry and synced via SyncManager batches,
        /// separate from the DirtyTracker's component-level BSON sync.
        /// On client (non-authoritative) instances this should NOT be called to avoid echo loops.
        /// </summary>
        public void PublishMeshToSyncAuthoritative()
        {
            // Only publish when on server or standalone (not a network client)
            var root = GameRoot.Instance;
            if (root == null) return;
            var host = root.Registry?.Get<NetworkHost>("NetworkHost");
            var client = root.Registry?.Get<NetworkClient>("NetworkClient");
            if (client != null && host == null) return; // pure client, don't push back

            if (MeshPoints != null)
            {
                if (MeshPointsValue == null)
                    MeshPointsValue = new SyncValue<double[]>($"{Key}/MeshPoints", MeshPoints, true);
                else
                    MeshPointsValue.Set(MeshPoints);
            }

            if (Indices != null)
            {
                if (IndicesValue == null)
                    IndicesValue = new SyncValue<uint[]>($"{Key}/Indices", Indices, true);
                else
                    IndicesValue.Set(Indices);
            }
        }

        public RenderType RenderType => RenderType.Mesh;

        public double[] MeshPoints => Shape == MeshShape.Custom && CustomMeshPoints != null ? CustomMeshPoints : (_meshPoints ??= GenerateMeshPoints());

        public uint[] Indices => Shape == MeshShape.Custom && CustomIndices != null ? CustomIndices : (_indices ??= GenerateIndices());

        public Material Material => null; // Use MaterialComponent from element

        public Matrix4x4 Transform
        {
            get
            {
                if (Owner != null)
                {
                    var t = Owner.GetComponent<TransformComponent>();
                    var s = Owner.GetComponent<ScaleComponent>();
                    
                    float scaleX = Width * (s?.ScaleX ?? 1f);
                    float scaleY = Height * (s?.ScaleY ?? 1f);
                    float scaleZ = Depth * (s?.ScaleZ ?? 1f);

                    if (t != null)
                    {
                        return Matrix4x4.CreateScale(scaleX, scaleY, scaleZ)
                             * Matrix4x4.CreateFromYawPitchRoll(t.RY, t.RX, t.RZ)
                             * Matrix4x4.CreateTranslation(t.X, t.Y, t.Z);
                    }
                    // Fallback: use Element's LocalTransform
                    var lt = Owner.LocalTransform;
                    return Matrix4x4.CreateScale(scaleX, scaleY, scaleZ)
                         * Matrix4x4.CreateFromQuaternion(lt.Rotation)
                         * Matrix4x4.CreateTranslation(lt.Position);
                }
                return Matrix4x4.Identity;
            }
        }

        public bool IsWorldLocked => true;

        [BsonIgnore]
        public TRS LocalTransform => throw new NotImplementedException();

        [BsonIgnore]
        public Matrix4x4 WorldTransform => throw new NotImplementedException();

        public MeshComponent() { }
        public MeshComponent(MeshShape shape, float width = 1f, float height = 1f, float depth = 1f)
        {
            _shape  = shape;
            _width  = width;
            _height = height;
            _depth  = depth;
        }

        private void InvalidateCache()
        {
            _meshPoints = null;
            _indices = null;
        }

        private double[] GenerateMeshPoints()
        {
            switch (Shape)
            {
                case MeshShape.Box:
                    return GenerateBoxPoints();
                case MeshShape.Plane:
                    return GeneratePlanePoints();
                case MeshShape.Sphere:
                    // StereoKit handles sphere generation better, but we provide points for generic renderers
                    return GenerateSpherePoints(16, 8);
                case MeshShape.Capsule:
                    return GenerateCapsulePoints(6, 16);
                case MeshShape.Custom:
                    // Custom shape without vertex data — return empty so nothing renders
                    // rather than silently falling back to a box.
                    return Array.Empty<double>();
                default:
                    return GenerateBoxPoints();
            }
        }

        private uint[] GenerateIndices()
        {
            switch (Shape)
            {
                case MeshShape.Box:
                    return GenerateBoxIndices();
                case MeshShape.Plane:
                    return GeneratePlaneIndices();
                case MeshShape.Sphere:
                    return GenerateSphereIndices(16, 8);
                case MeshShape.Capsule:
                    return GenerateCapsuleIndices(6, 16);
                case MeshShape.Custom:
                    // Custom shape without index data — return empty so nothing renders
                    // rather than silently falling back to box indices.
                    return Array.Empty<uint>();
                default:
                    return GenerateBoxIndices();
            }
        }

        private double[] GenerateBoxPoints()
        {
            // Unit box points (transform handles scaling)
            return new double[] {
                -0.5, -0.5,  0.5,  0.5, -0.5,  0.5,  0.5,  0.5,  0.5, -0.5,  0.5,  0.5, // Front
                -0.5, -0.5, -0.5, -0.5,  0.5, -0.5,  0.5,  0.5, -0.5,  0.5, -0.5, -0.5, // Back
                -0.5,  0.5, -0.5, -0.5,  0.5,  0.5,  0.5,  0.5,  0.5,  0.5,  0.5, -0.5, // Top
                -0.5, -0.5, -0.5,  0.5, -0.5, -0.5,  0.5, -0.5,  0.5, -0.5, -0.5,  0.5, // Bottom
                 0.5, -0.5, -0.5,  0.5,  0.5, -0.5,  0.5,  0.5,  0.5,  0.5, -0.5,  0.5, // Right
                -0.5, -0.5, -0.5, -0.5, -0.5,  0.5, -0.5,  0.5,  0.5, -0.5,  0.5, -0.5  // Left
            };
        }

        private uint[] GenerateBoxIndices()
        {
            return new uint[] {
                0,1,2, 0,2,3, 4,5,6, 4,6,7, 8,9,10, 8,10,11,
                12,13,14, 12,14,15, 16,17,18, 16,18,19, 20,21,22, 20,22,23
            };
        }

        private double[] GeneratePlanePoints()
        {
            return new double[] {
                -0.5, 0, -0.5,
                 0.5, 0, -0.5,
                 0.5, 0,  0.5,
                -0.5, 0,  0.5
            };
        }

        private uint[] GeneratePlaneIndices()
        {
            return new uint[] { 0, 1, 2, 0, 2, 3 };
        }

        private double[] GenerateSpherePoints(int stacks, int slices)
        {
            var points = new System.Collections.Generic.List<double>();
            for (int i = 0; i <= stacks; i++)
            {
                double phi = Math.PI * i / stacks;
                for (int j = 0; j <= slices; j++)
                {
                    double theta = 2 * Math.PI * j / slices;
                    points.Add(0.5 * Math.Sin(phi) * Math.Cos(theta));
                    points.Add(0.5 * Math.Cos(phi));
                    points.Add(0.5 * Math.Sin(phi) * Math.Sin(theta));
                }
            }
            return points.ToArray();
        }

        private uint[] GenerateSphereIndices(int stacks, int slices)
        {
            var indices = new System.Collections.Generic.List<uint>();
            for (int i = 0; i < stacks; i++)
            {
                for (int j = 0; j < slices; j++)
                {
                    uint first = (uint)(i * (slices + 1) + j);
                    uint second = (uint)(first + slices + 1);
                    indices.Add(first); indices.Add(second); indices.Add(first + 1);
                    indices.Add(second); indices.Add(second + 1); indices.Add(first + 1);
                }
            }
            return indices.ToArray();
        }

        /// <summary>Unit-space capsule: top cap rows (pole→equator), then
        /// bottom cap rows (equator→pole) — the band between the two equator
        /// rows is the cylinder wall, so the sphere strip indexing applies to
        /// the whole thing. The cap-radius/height ratio (rho) is baked into the
        /// unit Y coordinates because the renderer scales the three axes
        /// independently (Width×Height×Depth); a plain unit sphere stretched
        /// vertically would grow ellipsoid caps instead of keeping them round.
        /// rho is derived from the CURRENT dims and the cache is invalidated
        /// when they change, so the shape stays exact.</summary>
        private double[] GenerateCapsulePoints(int capStacks, int slices)
        {
            double r = Math.Min(_width, _depth) * 0.5;
            double h = Math.Max(_height, 1e-4);
            if (r > h * 0.5) r = h * 0.5;
            double rho = r / h;

            var points = new System.Collections.Generic.List<double>();
            for (int i = 0; i <= capStacks; i++)
            {
                double phi = (Math.PI * 0.5) * i / capStacks;
                AddCapsuleRing(points, 0.5 - rho + rho * Math.Cos(phi), 0.5 * Math.Sin(phi), slices);
            }
            for (int i = 0; i <= capStacks; i++)
            {
                double phi = (Math.PI * 0.5) + (Math.PI * 0.5) * i / capStacks;
                AddCapsuleRing(points, -0.5 + rho + rho * Math.Cos(phi), 0.5 * Math.Sin(phi), slices);
            }
            return points.ToArray();
        }

        private static void AddCapsuleRing(System.Collections.Generic.List<double> points, double y, double radial, int slices)
        {
            for (int j = 0; j <= slices; j++)
            {
                double theta = 2 * Math.PI * j / slices;
                points.Add(radial * Math.Cos(theta));
                points.Add(y);
                points.Add(radial * Math.Sin(theta));
            }
        }

        private uint[] GenerateCapsuleIndices(int capStacks, int slices)
            => GenerateSphereIndices(capStacks * 2 + 1, slices);

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        public override string ToString() =>
            $"Mesh(Shape:{Shape}, {Width:F2}x{Height:F2}x{Depth:F2})";
    }
}
