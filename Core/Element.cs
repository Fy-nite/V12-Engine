using System;
using System.Collections.Generic;
using System.Text;
using V12.Core.Core.Interfaces;

namespace V12.Core
{
    public class Element : IWorldElement
    {
        public event Action<IWorldElement>? OnDirty;

        public string? Name { get; set; }
        public string? Description { get; set; }
        public IWorldElement? Parent { get; set; }
        public List<IComponent> Components { get; set; } = new List<IComponent>();

        public Element(string? name = null, string? description = null, IWorldElement? parent = null)
        {
            Name = name;
            Description = description;
            Parent = parent;
        }

        public void MarkDirty()
        {
            OnDirty?.Invoke(this);
        }

        public void AddComponent(IComponent comp)
        {
            Components.Add(comp);
        }
        public IComponent GetComponent(string name)
        {
            return Components.Find(c => c.Name == name) ?? throw new Exception($"Component with name {name} not found.");
        }
        public T GetComponent<T>() where T : IComponent
        {
            return (T)(Components.Find(c => c is T) ?? throw new Exception($"Component of type {typeof(T).Name} not found."));
        }
        public bool TryGetComponent<T>(out T comp) where T : IComponent
        {
            var found = Components.Find(c => c is T);
            if (found != null)
            {
                comp = (T)found;
                return true;
            }
            comp = default(T)!;
            return false;
        }
        public void RemoveComponent(IComponent comp)
        {
            Components.Remove(comp);
        }
    }
}
