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
        // Backing field kept for compatibility: `ID`.
        public long ID { get; set; }
        // Preferred camel-cased property exposed via IWorldElement.Id.
        public long Id { get => ID; set => ID = value; }
        public string? Description { get; set; }
        public IWorldElement? Parent { get; set; }
        public List<IComponent> Components { get; set; } = new List<IComponent>();
        public List<IWorldElement> Children { get; } = new List<IWorldElement>();

        private static long _nextElementId = 0;

        private static long GenerateElementId() => System.Threading.Interlocked.Increment(ref _nextElementId);

        public Element(string? name = null, string? description = null, IWorldElement? parent = null)
        {
            Name = name;
            Description = description;
            Parent = parent;
            ID = GenerateElementId();
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
        }

        public void RemoveChild(IWorldElement child)
        {
            if (child == null) return;
            if (Children.Remove(child))
                child.Parent = null;
        }

        internal void AddComponent(IComponent comp)
        {
            Components.Add(comp);
        }
    }
}
