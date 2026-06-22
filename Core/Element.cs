using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading;
using V12.Components;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces.Renderer;
using V12.Core.NetworkCable;

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
            get
            {
                var tc = GetComponent<TransformComponent>();
                if (tc != null)
                {
                    var sc = GetComponent<ScaleComponent>();
                    return new TRS
                    {
                        Position = new Vector3(tc.X, tc.Y, tc.Z),
                        Rotation = Quaternion.CreateFromYawPitchRoll(tc.RY, tc.RX, tc.RZ),
                        Scale = sc != null ? new Vector3(sc.ScaleX, sc.ScaleY, sc.ScaleZ) : Vector3.One
                    };
                }
                return _localTransform;
            }
            set
            {
                _localTransform = value;
            }
        }

        public Matrix4x4 WorldTransform
        {
            get
            {
                var lt = LocalTransform;
                var local = Matrix4x4.CreateScale(lt.Scale)
                          * Matrix4x4.CreateFromQuaternion(lt.Rotation)
                          * Matrix4x4.CreateTranslation(lt.Position);
                if (Parent != null)
                    return local * Parent.WorldTransform;
                return local;
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
        }

        public void AddChild(IWorldElement child)
        {
            if (child == null) return;
            var world = GameRoot.Instance?.SelectedWorld;
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
        }

        public void RemoveChild(IWorldElement child)
        {
            if (child == null) return;
            var world = GameRoot.Instance?.SelectedWorld;
            world?.Lock.EnterWriteLock();
            try
            {
                if (Children.Remove(child))
                    child.Parent = null;
                if (world != null)
                    world._elementsById.Remove(child.Id);
            }
            finally { world?.Lock.ExitWriteLock(); }
        }

        public IComponent AddComponent(IComponent component)
        {
            var world = GameRoot.Instance?.SelectedWorld;
            world?.Lock.EnterWriteLock();
            try
            {
                component.OnAttach(this);
                Components.Add(component);
                component.MarkDirty();
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
                component.OnDetach(this);
            }
            finally { world?.Lock.ExitWriteLock(); }
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
