using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
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

        public TRS LocalTransform => throw new NotImplementedException();

        public Matrix4x4 WorldTransform => throw new NotImplementedException();

        private static long _nextElementId = 0;

        private long _nextId = 1;

        private long NextElementID()
        {
            while (GameRoot.Instance.SelectedWorld._elementsById.ContainsKey(_nextId))
                _nextId++;

            return _nextId++;
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

        //return System.Threading.Interlocked.Increment(ref _nextElementId); 


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
            child.Parent = this;
            if (!Children.Contains(child))
                Children.Add(child);
            GameRoot.Instance.SelectedWorld._elementsById[child.Id] = child;
        }

        public void RemoveChild(IWorldElement child)
        {
            if (child == null) return;
            if (Children.Remove(child))
                child.Parent = null;
            GameRoot.Instance.SelectedWorld._elementsById.Remove(child.Id);
        }

        public IComponent AddComponent(IComponent component)
        {
            component.OnAttach(this);
            Components.Add(component);
            component.MarkDirty();
            return component;
        }
        public void RemoveComponent(IComponent component)
        {
            Components.Remove(component);
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
