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

        internal void AddComponent(IComponent comp)
        {
            Components.Add(comp);
        }
    }
}
