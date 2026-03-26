using V12.Core.Core.Interfaces;
using System.Collections.Generic;
using System;
namespace V12.Core
{
    public class World 
    {

        public List<IWorldElement> Root { get; set; }
        public string WorldName { get; set; }

        /// <summary>Raised on the calling thread when an element is added via <see cref="AddElement"/>.</summary>
        public event Action<IWorldElement>? ElementAdded;

        /// <summary>Raised on the calling thread when an element is removed via <see cref="RemoveElement"/>.</summary>
        public event Action<IWorldElement>? ElementRemoved;

        public World() { 
            WorldName = "DefaultWorld";
            Root = new List<IWorldElement>();
        }

        public World(string Name)
        {
            WorldName = Name;
            Root = new List<IWorldElement>();
        }

        public void AddElement(IWorldElement element)
        {
            Root.Add(element);
            ElementAdded?.Invoke(element);
        }

        /// <summary>Remove an element from the world and fire <see cref="ElementRemoved"/>.</summary>
        public void RemoveElement(IWorldElement element)
        {
            if (Root.Remove(element))
                ElementRemoved?.Invoke(element);
        }
        public void GenerateWorld()
        {
            // This method can be overridden in derived classes to create specific world content.
            // For example, you could create a HomeWorld class that inherits from World and overrides this method to populate the world with specific elements.
        }
        public void Update(float deltaTime)
        {
            foreach (var element in Root)
            {
                try
                {
                    element.Components.ForEach(component => component.Update(deltaTime));
                }
                catch { }
            }
        }
    }
}
