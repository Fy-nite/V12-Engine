using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using V12.Components;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;
using V12.Core.NetworkCable;
using V12.Core.Networking;

namespace V12.Core
{
    public class Element : IWorldElement
    {
        public event Action<IWorldElement>? OnDirty;

        /// <summary>Raised (outside the world lock) when a child is added via <see cref="AddChild"/>.
        /// The argument is the added child. Used by the DirtyTracker to replicate element spawns.</summary>
        public event Action<IWorldElement>? ChildAdded;

        /// <summary>Raised (outside the world lock) when a child is removed via <see cref="RemoveChild"/>.
        /// The argument is the removed child. Used by the DirtyTracker to replicate element despawns.</summary>
        public event Action<IWorldElement>? ChildRemoved;

        private string? _name;
        public string? Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                MarkDirty();
            }
        }
        // Backing field kept for compatibility: `ID`.
        public long ID { get; set; }
        // Preferred camel-cased property exposed via IWorldElement.Id.
        public long Id { get => ID; set => ID = value; }

        private Guid _uid = Guid.Empty;

        /// <summary>
        /// Optional stable authored id. Empty unless the author pinned it with
        /// a <c>uid</c> attribute (or the editor's Pin action). Selector refs
        /// do not require it. Changes are dirty-tracked so runtime pinning
        /// reaches peers through <c>WorldElementUpdate</c>.
        /// </summary>
        public Guid Uid
        {
            get => _uid;
            set
            {
                if (_uid == value) return;
                _uid = value;
                MarkDirty();
            }
        }
        private string? _description;
        public string? Description
        {
            get => _description;
            set
            {
                if (_description == value) return;
                _description = value;
                MarkDirty();
            }
        }
        public IWorldElement? Parent { get; set; }
        public List<IComponent> Components { get; set; } = new List<IComponent>();
        public List<IWorldElement> Children { get; } = new List<IWorldElement>();

        private bool _active = true;

        public bool Active
        {
            get => _active;
            set
            {
                if (_active == value) return;
                _active = value;
                MarkDirty();
            }
        }

        private TRS _localTransform = new TRS
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
            Scale = Vector3.One
        };

        public TRS LocalTransform
        {
            get => _localTransform;
            set
            {
                // Avoid signalling dirty when the new transform is effectively
                // identical to the current one. This is the single biggest source
                // of spurious dirty traffic: many systems assign LocalTransform
                // every tick even when nothing actually moved.
                if (TRSApproximatelyEqual(_localTransform, value))
                    return;

                _localTransform = value;
                MarkDirty();
            }
        }

        private const float TransformEpsilon = 1e-5f;

        private static bool QuaternionApproximatelyEqual(Quaternion a, Quaternion b)
        {
            // Quaternions q and -q represent the same rotation, so compare both signs.
            return MathF.Abs(a.X - b.X) < TransformEpsilon
                && MathF.Abs(a.Y - b.Y) < TransformEpsilon
                && MathF.Abs(a.Z - b.Z) < TransformEpsilon
                && MathF.Abs(a.W - b.W) < TransformEpsilon
                || MathF.Abs(a.X + b.X) < TransformEpsilon
                && MathF.Abs(a.Y + b.Y) < TransformEpsilon
                && MathF.Abs(a.Z + b.Z) < TransformEpsilon
                && MathF.Abs(a.W + b.W) < TransformEpsilon;
        }

        private static bool TRSApproximatelyEqual(TRS a, TRS b)
        {
            return MathF.Abs(a.Position.X - b.Position.X) < TransformEpsilon
                && MathF.Abs(a.Position.Y - b.Position.Y) < TransformEpsilon
                && MathF.Abs(a.Position.Z - b.Position.Z) < TransformEpsilon
                && QuaternionApproximatelyEqual(a.Rotation, b.Rotation)
                && MathF.Abs(a.Scale.X - b.Scale.X) < TransformEpsilon
                && MathF.Abs(a.Scale.Y - b.Scale.Y) < TransformEpsilon
                && MathF.Abs(a.Scale.Z - b.Scale.Z) < TransformEpsilon;
        }

        public Matrix4x4 WorldTransform
        {
            get
            {
                // Single definition of local placement lives in
                // ElementPlacement (LocalTransform × ScaleComponent);
                // ancestors compose here.
                var local = ElementPlacement.ElementLocalMatrix(this);
                if (Parent != null)
                    return local * Parent.WorldTransform;
                return local;
            }
        }

        // -- Render-pose interpolation ---------------------------------------
        // Gameplay reads WorldTransform (the exact fixed-step pose). Rendering
        // reads RenderWorldTransform, which interpolates the local pose
        // (position/rotation/scale) between the ends of the last two fixed steps
        // using the clock's alpha. This is what removes 60 Hz judder on a
        // 144/240 Hz display without adding a simulation step or touching physics.

        private TRS _renderPoseFrom;
        private TRS _renderPoseTo;
        private bool _hasRenderPose;

        /// <summary>Snapshot the pose at the start of a fixed step.</summary>
        internal void BeginRenderPose()
        {
            TRS local = ElementPlacement.ElementLocalTRS(this);

            if (!_hasRenderPose)
            {
                _renderPoseFrom = local;
                _renderPoseTo = local;
                _hasRenderPose = true;
                return;
            }

            _renderPoseFrom = _renderPoseTo;
        }

        /// <summary>Snapshot the pose at the end of a fixed step.</summary>
        internal void EndRenderPose()
        {
            if (!_hasRenderPose)
            {
                _renderPoseFrom = ElementPlacement.ElementLocalTRS(this);
                _hasRenderPose = true;
            }

            _renderPoseTo = ElementPlacement.ElementLocalTRS(this);
        }

        /// <summary>
        /// Element-local matrix for rendering: interpolated when the host opted
        /// into render interpolation, otherwise exactly the current local.
        /// </summary>
        public Matrix4x4 RenderLocalTransform
        {
            get
            {
                GameRoot? root = GameRoot.Instance;
                if (!_hasRenderPose || root?.RenderInterpolation != true)
                    return ElementPlacement.ElementLocalMatrix(this);

                float alpha = root.InterpolationAlpha;
                Vector3 position = Vector3.Lerp(_renderPoseFrom.Position, _renderPoseTo.Position, alpha);
                Quaternion rotation = Quaternion.Slerp(_renderPoseFrom.Rotation, _renderPoseTo.Rotation, alpha);
                Vector3 scale = Vector3.Lerp(_renderPoseFrom.Scale, _renderPoseTo.Scale, alpha);

                return Matrix4x4.CreateScale(scale)
                     * Matrix4x4.CreateFromQuaternion(rotation)
                     * Matrix4x4.CreateTranslation(position);
            }
        }

        /// <summary>Interpolated world matrix for rendering only.</summary>
        public Matrix4x4 RenderWorldTransform
        {
            get
            {
                Matrix4x4 local = RenderLocalTransform;
                if (Parent is Element parent)
                    return local * parent.RenderWorldTransform;

                // A non-Element parent cannot interpolate; compose with its
                // current world so a custom element still renders sensibly.
                return Parent != null ? local * Parent.WorldTransform : local;
            }
        }

        private static long _nextElementId = 0;

        private static long NextElementID()
        {
            return Interlocked.Increment(ref _nextElementId);
        }
        private static void Traverse(IWorldElement element, ref long maxId)
        {
            if (element == null) return;

            if (element.Id >= maxId)
                maxId = element.Id + 1;

            if (element.Children == null) return;

            foreach (var child in element.Children)
                Traverse(child, ref maxId);
        }


        public Element(string? name = null, string? description = null, IWorldElement? parent = null)
        {
            Name = name;
            Description = description;
            Parent = parent;
            ID = NextElementID();
        }

        public void MarkDirty()
        {
            OnDirty?.Invoke(this);
            GameRoot.Instance?.MarkRenderDirty();
        }

        public void AddChild(IWorldElement child)
        {
            if (child == null || ReferenceEquals(child, this)) return;

            // Refuse cycles: a parent must not sit inside the child's subtree.
            for (IWorldElement? ancestor = this; ancestor != null; ancestor = ancestor.Parent)
            {
                if (ReferenceEquals(ancestor, child))
                {
                    GameRoot.Log.Warning(
                        "Refusing to parent '{Child}' under its own descendant '{Parent}'",
                        child.Name, Name);
                    return;
                }
            }

            var world = FindWorldForElement();
            var previousParent = child.Parent;
            var previousWorld = previousParent != null
                ? GameRoot.Instance?.FindOwningWorld(previousParent)
                : GameRoot.Instance?.FindOwningWorld(child);

            if (previousWorld != null && world != null && !ReferenceEquals(previousWorld, world))
            {
                GameRoot.Log.Warning(
                    "Refusing to move '{Child}' from world '{From}' to '{To}': cross-world reparenting is not supported",
                    child.Name, previousWorld.WorldName, world.WorldName);
                return;
            }

            bool added = false;

            world?.Lock.EnterWriteLock();
            try
            {
                // A reparent must leave the old container, or the element ends up
                // in two parents' children lists.
                if (previousParent != null && !ReferenceEquals(previousParent, this))
                    previousParent.RemoveChild(child);
                else if (previousParent == null && world != null && world.Root.Contains(child))
                    world.RemoveElement(child);

                child.Parent = this;
                if (!Children.Contains(child))
                {
                    Children.Add(child);
                    added = true;
                }

                world?.IndexSubtree(child);
            }
            finally { world?.Lock.ExitWriteLock(); }

            if (added) ChildAdded?.Invoke(child);
            MarkDirty();
        }

        private World? FindWorldForElement()
        {
            // Owning world only: a detached element being assembled has no world
            // yet, and must not lock or index into the focused one.
            return GameRoot.Instance?.FindOwningWorld(this);
        }

        public void RemoveChild(IWorldElement child)
        {
            if (child == null) return;
            bool removed = false;
            var world = FindWorldForElement();
            world?.Lock.EnterWriteLock();
            try
            {
                if (Children.Remove(child))
                {
                    removed = true;
                    child.Parent = null;
                    world?.UnindexSubtree(child);
                }
            }
            finally { world?.Lock.ExitWriteLock(); }
            if (removed) ChildRemoved?.Invoke(child);
            MarkDirty();
        }

        public IComponent AddComponent(IComponent component)
        {
            component.OnAttach(this);
            var world = FindWorldForElement();
            world?.Lock.EnterWriteLock();
            try
            {
                Components.Add(component);
                // Route through the DirtyTracker so the component reaches remote
                // peers via the batched path (safe for non-serializable components).
                // The raw network MarkDirty extension BSON-serialises the whole
                // component and throws on delegate-bearing types (e.g. UIButton).
                var tracker = GameRoot.Instance?.Registry.Get<DirtyTracker>("DirtyTracker");
                tracker?.TrackComponent(component, this);
                component.RaiseDirty();
            }
            finally { world?.Lock.ExitWriteLock(); }
            MarkDirty(); // re-capture a frame so the renderer materializes the new component's node
            return component;
        }
        public void RemoveComponent(IComponent component)
        {
            var world = FindWorldForElement();
            world?.Lock.EnterWriteLock();
            try
            {
                Components.Remove(component);
            }
            finally { world?.Lock.ExitWriteLock(); }
            var tracker = GameRoot.Instance?.Registry.Get<DirtyTracker>("DirtyTracker");
            tracker?.UntrackComponent(component);

            // Snapshot values before detach: the component may clear them.
            var values = SyncValueScanner.EnumerateMembers(component).ToArray();

            // Broadcast the removal to peers (host only) so remote copies drop it too.
            tracker?.NotifyComponentRemoved(this, component);
            component.OnDetach(this);
            try { component.OnDestroy(); } catch { }

            // Release process-wide registry entries so a removed component cannot
            // leak its values (including mesh buffers).
            foreach (var value in values)
            {
                try { value.Unregister(); } catch { }
            }

            MarkDirty(); // re-capture a frame so the renderer drops the removed component's node
        }
        public IComponent GetComponent(string name)
        {
            foreach (var component in Components)
            {
                if (string.Equals(component.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return component;
                }
            }
            return null;
        }

        public IWorldElement? FindChildByName(string name)
        {
            return Children.Find(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public IWorldElement? FindChildByNameRecursive(string name)
        {
            foreach (var child in Children)
            {
                if (string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
                    return child;
                var found = child.FindChildByNameRecursive(name);
                if (found != null) return found;
            }
            return null;
        }

        public T? GetComponent<T>() where T : IComponent
        {
            foreach (var component in Components)
            {
                if (component is T t) return t;
            }
            return default;
        }

        public T? GetComponent<T>(string name) where T : IComponent
        {
            foreach (var component in Components)
            {
                if (component is T && string.Equals(component.Name, name, StringComparison.OrdinalIgnoreCase))
                    return (T)component;
            }
            return default;
        }

        public List<T> GetComponents<T>() where T : IComponent
        {
            var results = new List<T>();
            foreach (var component in Components)
            {
                if (component is T t) results.Add(t);
            }
            return results;
        }
    }
}
