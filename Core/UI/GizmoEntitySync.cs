using System;
using System.Collections.Generic;
using System.Numerics;
using V12.Components;
using V12.Components.Renderables;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;

namespace V12.Core.UI
{
    /// <summary>
    /// Shared transient gizmo spawner: keeps a "_EditorGizmo" root in the
    /// target's world with one child element per handle mesh (cylinders,
    /// cones, cubes, torus rings from the canonical
    /// <see cref="TransformGizmo"/> specs). Both editor hosts drive one of
    /// these per frame (the MonoGame viewport service and nova's SceneGizmo);
    /// the tree renders and picks through the normal pipeline in every host.
    ///
    /// Meshes carry no transforms: the element TRS carries position/rotation,
    /// components only size their dims. The transient marker keeps the tree
    /// out of saves and network sync; grabs use the analytic pick shapes,
    /// never the visual mesh.
    /// </summary>
    public sealed class GizmoEntitySync
    {
        private IWorldElement? _gizmoRoot;
        private World? _gizmoWorld;
        private readonly List<GizmoPart> _gizmoParts = new();
        private readonly List<TransformGizmo.GizmoHandleSpec> _specs = new();
        private IWorldElement? _builtTarget;
        private GizmoMode _builtMode = GizmoMode.Translate;
        private Vector3 _lastOrigin;
        private float _lastSize;

        private sealed class GizmoPart
        {
            public IWorldElement Element;
            public ComponentBase Mesh;
            public TransformGizmo.GizmoMeshKind Kind;
        }

        /// <summary>Specs from the last sync (for analytic hit tests).</summary>
        public IReadOnlyList<TransformGizmo.GizmoHandleSpec> Specs => _specs;

        /// <summary>Per-frame upkeep: ensure the tree exists, rebuild it on
        /// target/mode/world change, refresh part transforms so the gizmo
        /// holds constant screen size. Static frames skip untouched. A null
        /// world/target or non-positive size destroys the tree.</summary>
        public void Sync(World? world, IWorldElement? target, GizmoMode mode, Vector3 origin, float size)
        {
            if (world == null || target == null || size <= 0f) { Destroy(); return; }

            bool needRebuild = _gizmoRoot == null
                || !ReferenceEquals(_gizmoWorld, world)
                || !world.Root.Contains(_gizmoRoot)
                || !ReferenceEquals(_builtTarget, target)
                || _builtMode != mode;
            if (needRebuild)
            {
                Rebuild(world, target, mode, origin, size);
                return;
            }
            if (_gizmoParts.Count == 0) return;
            if ((origin - _lastOrigin).LengthSquared() < 1e-10f
                && MathF.Abs(size - _lastSize) < 1e-6f)
                return; // static frame: nothing moved
            _lastOrigin = origin;
            _lastSize = size;

            TransformGizmo.BuildHandleSpecs(mode, origin, size, _specs);
            int expected = 0;
            foreach (var s in _specs)
            {
                if (s.Mesh0.Mesh != TransformGizmo.GizmoMeshKind.None) expected++;
                if (s.Mesh1.Mesh != TransformGizmo.GizmoMeshKind.None) expected++;
            }
            if (_gizmoParts.Count != expected)
            {
                Rebuild(world, target, mode, origin, size);
                return;
            }
            int pi = 0;
            foreach (var s in _specs)
            {
                pi = SyncPartMesh(pi, s.Mesh0, s);
                if (pi < 0) { Rebuild(world, target, mode, origin, size); return; }
                pi = SyncPartMesh(pi, s.Mesh1, s);
                if (pi < 0) { Rebuild(world, target, mode, origin, size); return; }
            }
        }

        /// <summary>Remove the tree from its world, if any.</summary>
        public void Destroy()
        {
            if (_gizmoRoot == null)
            {
                _gizmoParts.Clear();
                return;
            }
            var world = _gizmoWorld ?? GameRoot.Instance?.GetWorldForElement(_gizmoRoot);
            if (world != null)
            {
                foreach (var part in _gizmoParts)
                    _gizmoRoot.RemoveChild(part.Element);
                world.RemoveElement(_gizmoRoot);
            }
            _gizmoRoot = null;
            _gizmoWorld = null;
            _gizmoParts.Clear();
            _builtTarget = null;
        }

        /// <summary>True when <paramref name="el"/> is the gizmo root or one
        /// of its parts.</summary>
        public bool IsGizmoElement(IWorldElement? el)
        {
            if (el == null || _gizmoRoot == null) return false;
            for (var cur = el; cur != null; cur = cur.Parent)
                if (ReferenceEquals(cur, _gizmoRoot)) return true;
            return false;
        }

        /// <summary>True when <paramref name="elementId"/> belongs to a live
        /// gizmo part (for hosts that pick by id).</summary>
        public bool IsGizmoPart(long elementId)
        {
            foreach (var part in _gizmoParts)
                if (part.Element != null && part.Element.Id == elementId) return true;
            return false;
        }

        private void Rebuild(World world, IWorldElement target, GizmoMode mode, Vector3 origin, float size)
        {
            Destroy();

            var root = new Element("_EditorGizmo");
            root.AddComponent(new EditorTransientComponent());
            world.AddElement(root);
            _gizmoRoot = root;
            _gizmoWorld = world;

            TransformGizmo.BuildHandleSpecs(mode, origin, size, _specs);
            foreach (var spec in _specs)
            {
                AddPartMesh(root, spec.Mesh0, spec);
                AddPartMesh(root, spec.Mesh1, spec);
            }
            _lastOrigin = origin;
            _lastSize = size;
            _builtTarget = target;
            _builtMode = mode;
            Console.WriteLine($"[Gizmo] spawned {_gizmoParts.Count} parts mode={mode} target='{target.Name}' size={size:F2}");
        }

        private void AddPartMesh(IWorldElement root, TransformGizmo.GizmoMeshPlacement placement,
            TransformGizmo.GizmoHandleSpec spec)
        {
            if (placement.Mesh == TransformGizmo.GizmoMeshKind.None) return;
            var el = new Element($"GizmoHandle_{spec.HandleId}");
            var mesh = CreatePartMesh(placement);
            el.AddComponent(mesh);
            var col = spec.Color;
            el.AddComponent(new MaterialComponent(col.R / 255f, col.G / 255f, col.B / 255f,
                col.A / 255f, 0f, 1f, unlit: true, noDepthTest: true));
            el.AddComponent(new GizmoHandleComponent(spec.HandleId));
            PlacePart(el, mesh, placement);
            root.AddChild(el);
            _gizmoParts.Add(new GizmoPart { Element = el, Mesh = mesh, Kind = placement.Mesh });
        }

        /// <summary>Refresh part <paramref name="pi"/> from a placement.
        /// Returns the next part index, or -1 when the part's mesh kind no
        /// longer matches (caller rebuilds).</summary>
        private int SyncPartMesh(int pi, TransformGizmo.GizmoMeshPlacement placement,
            TransformGizmo.GizmoHandleSpec spec)
        {
            if (placement.Mesh == TransformGizmo.GizmoMeshKind.None) return pi;
            if (pi < 0 || pi >= _gizmoParts.Count) return -1;
            var part = _gizmoParts[pi];
            if (part.Kind != placement.Mesh) return -1;
            PlacePart(part.Element, part.Mesh, placement);
            var col = spec.Color;
            var mat = part.Element.GetComponent<MaterialComponent>();
            if (mat != null)
            {
                float r = col.R / 255f, g = col.G / 255f, b = col.B / 255f, a = col.A / 255f;
                if (MathF.Abs(mat.R - r) > 0.002f) mat.R = r;
                if (MathF.Abs(mat.G - g) > 0.002f) mat.G = g;
                if (MathF.Abs(mat.B - b) > 0.002f) mat.B = b;
                if (MathF.Abs(mat.A - a) > 0.002f) mat.A = a;
            }
            return pi + 1;
        }

        /// <summary>Write a placement: the ELEMENT carries position/rotation;
        /// the component only sizes its dims. The gizmo root sits at identity,
        /// so local is world here.</summary>
        private static void PlacePart(IWorldElement el, ComponentBase mesh,
            TransformGizmo.GizmoMeshPlacement p)
        {
            el.LocalTransform = new TRS { Position = p.Center, Rotation = p.Rotation, Scale = Vector3.One };
            SizePartMesh(mesh, p);
        }

        /// <summary>Write absolute dimensions (compared first: assigning
        /// regenerates the vertex cache, so static frames must not touch).</summary>
        private static void SizePartMesh(ComponentBase mesh, TransformGizmo.GizmoMeshPlacement p)
        {
            switch (mesh)
            {
                case CylinderMesh c:
                    float cr = p.Size.X * 0.5f;
                    if (MathF.Abs(c.Radius - cr) > 1e-6f) c.Radius = cr;
                    if (MathF.Abs(c.Height - p.Size.Y) > 1e-6f) c.Height = p.Size.Y;
                    break;
                case ConeMesh k:
                    float kr = p.Size.X * 0.5f;
                    if (MathF.Abs(k.Radius - kr) > 1e-6f) k.Radius = kr;
                    if (MathF.Abs(k.Height - p.Size.Y) > 1e-6f) k.Height = p.Size.Y;
                    break;
                case BoxMesh b:
                    if ((b.Size - p.Size).LengthSquared() > 1e-12f) b.Size = p.Size;
                    break;
                case TorusSegmentMesh t:
                    float rr = p.Size.X * 0.5f, tr = p.Size.Y * 0.5f;
                    if (MathF.Abs(t.RingRadius - rr) > 1e-6f) t.RingRadius = rr;
                    if (MathF.Abs(t.TubeRadius - tr) > 1e-6f) t.TubeRadius = tr;
                    break;
            }
        }

        private static ComponentBase CreatePartMesh(TransformGizmo.GizmoMeshPlacement p)
        {
            ComponentBase mesh = p.Mesh switch
            {
                TransformGizmo.GizmoMeshKind.Cylinder => new CylinderMesh(),
                TransformGizmo.GizmoMeshKind.Cone => new ConeMesh(),
                TransformGizmo.GizmoMeshKind.Cube => new BoxMesh(),
                TransformGizmo.GizmoMeshKind.Torus => new TorusSegmentMesh(),
                _ => throw new ArgumentOutOfRangeException(nameof(p), "No mesh for placement kind None"),
            };
            SizePartMesh(mesh, p);
            return mesh;
        }
    }
}
