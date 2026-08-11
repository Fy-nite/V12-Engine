using System;
using System.Collections.Generic;
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

        public string? Name { get; set; }
        // Backing field kept for compatibility: `ID`.
        public long ID { get; set; }
        // Preferred camel-cased property exposed via IWorldElement.Id.
        public long Id { get => ID; set => ID = value; }
        public string? Description { get; set; }
        public IWorldElement? Parent { get; set; }
        public List<IComponent> Components { get; set; } = new List<IComponent>();
        public List<IWorldElement> Children { get; } = new List<IWorldElement>();
        public bool Active { get; set; } = true;

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
                // Avoid signalling dirty (and re-syncing the TransformComponent) when the
                // new transform is effectively identical to the current one. This is the
                // single biggest source of spurious dirty traffic: many systems assign
                // LocalTransform every tick even when nothing actually moved.
                if (TRSApproximatelyEqual(_localTransform, value))
                    return;

                _localTransform = value;
                var tc = GetComponent<TransformComponent>();
                if (tc != null)
                {
                    tc.X = value.Position.X;
                    tc.Y = value.Position.Y;
                    tc.Z = value.Position.Z;
                    var (yaw, pitch, roll) = ToEulerAngles(value.Rotation);
                    tc.RY = yaw;
                    tc.RX = pitch;
                    tc.RZ = roll;
                }
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

        private static (float yaw, float pitch, float roll) ToEulerAngles(Quaternion q)
        {
            float siny_cosp = 2 * (q.W * q.Y + q.Z * q.X);
            float cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
            float yaw = MathF.Atan2(siny_cosp, cosy_cosp);
            float sinp = 2 * (q.W * q.X - q.Y * q.Z);
            float pitch = Math.Abs(sinp) >= 1 ? MathF.CopySign(MathF.PI / 2, sinp) : MathF.Asin(sinp);
            float sinr_cosp = 2 * (q.W * q.Z + q.X * q.Y);
            float cosr_cosp = 1 - 2 * (q.X * q.X + q.Z * q.Z);
            float roll = MathF.Atan2(sinr_cosp, cosr_cosp);
            return (yaw, pitch, roll);
        }

        public Matrix4x4 WorldTransform
        {
            get
            {
                var tc = GetComponent<TransformComponent>();
                if (tc != null)
                {
                    var local = tc.Transform;
                    if (Parent != null)
                        return local * Parent.WorldTransform;
                    return local;
                }
                var lt = LocalTransform;
                var local2 = Matrix4x4.CreateScale(lt.Scale)
                          * Matrix4x4.CreateFromQuaternion(lt.Rotation)
                          * Matrix4x4.CreateTranslation(lt.Position);
                if (Parent != null)
                    return local2 * Parent.WorldTransform;
                return local2;
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
            if (child == null) return;
            var world = FindWorldForElement();
            world?.Lock.EnterWriteLock();
            try
            {
                child.Parent = this;
                if (!Children.Contains(child))
                    Children.Add(child);
                if (world != null)
                    world._elementsById[child.Id] = child;
            }
            finally { world?.Lock.ExitWriteLock(); }
            MarkDirty();
        }

        private World? FindWorldForElement()
        {
            return GameRoot.Instance?.GetWorldForElement(this);
        }

        public void RemoveChild(IWorldElement child)
        {
            if (child == null) return;
            var world = FindWorldForElement();
            world?.Lock.EnterWriteLock();
            try
            {
                if (Children.Remove(child))
                    child.Parent = null;
                if (world != null)
                    world._elementsById.Remove(child.Id);
            }
            finally { world?.Lock.ExitWriteLock(); }
            MarkDirty();
        }

        public IComponent AddComponent(IComponent component)
        {
            component.OnAttach(this);
            var world = GameRoot.Instance?.SelectedWorld;
            world?.Lock.EnterWriteLock();
            try
            {
                Components.Add(component);
                // Route through the DirtyTracker so the component reaches remote
                // peers via the batched path (safe for non-serializable components).
                // The raw network MarkDirty extension BSON-serialises the whole
                // component and throws on delegate-bearing types (e.g. UIButton).
                var tracker = GameRoot.Instance?.Registry.Get<DirtyTracker>("DirtyTracker");
                tracker?.TrackComponent(component);
                component.RaiseDirty();
            }
            finally { world?.Lock.ExitWriteLock(); }
            return component;
        }
        public void RemoveComponent(IComponent component)
        {
            var world = GameRoot.Instance?.SelectedWorld;
            world?.Lock.EnterWriteLock();
            try
            {
                Components.Remove(component);
            }
            finally { world?.Lock.ExitWriteLock(); }
            var tracker = GameRoot.Instance?.Registry.Get<DirtyTracker>("DirtyTracker");
            tracker?.UntrackComponent(component);
            component.OnDetach(this);
        }
        public IComponent GetComponent(string name)
        {
            foreach (var component in Components)
            {
                if (component.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
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
