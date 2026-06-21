using System;
using System.Collections.Generic;
using System.Linq;
using V12.Core.Core.Interfaces;
namespace V12.Core
{
    public class World 
    {

        public List<IWorldElement> Root { get; set; }
        public string WorldName { get; set; }
        public  Dictionary<long, IWorldElement> _elementsById = new();     

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
        public IWorldElement? FindElementWithComponent<T>()
    where T : IComponent
        {
            return Root.FirstOrDefault(e => e.GetComponent<T>() != null);
        }

        public IWorldElement? FindElementWithComponentRecursive<T>() where T : IComponent
        {
            foreach (var element in Root)
            {
                if (element.GetComponent<T>() != null) return element;
                var found = FindInChildren<T>(element);
                if (found != null) return found;
            }
            return null;
        }

        private IWorldElement? FindInChildren<T>(IWorldElement parent) where T : IComponent
        {
            foreach (var child in parent.Children)
            {
                if (child.GetComponent<T>() != null) return child;
                var found = FindInChildren<T>(child);
                if (found != null) return found;
            }
            return null;
        }
        public void AddElement(IWorldElement element)
        {
            Root.Add(element);
            _elementsById[element.Id] = element;
            ElementAdded?.Invoke(element);
        }

        /// <summary>Remove an element from the world and fire <see cref="ElementRemoved"/>.</summary>
        public void RemoveElement(IWorldElement element)
        {
            if (Root.Remove(element))
            {
                _elementsById.Remove(element.Id);
                ElementRemoved?.Invoke(element);
            }
        }

        public void GenerateWorld()
        {
            // This method can be overridden in derived classes to create specific world content.
            // For example, you could create a HomeWorld class that inherits from World and overrides this method to populate the world with specific elements.
        }
        public void Update(float deltaTime)
        {
            Console.WriteLine($"World.Update: {Root.Count} elements");
            foreach (var element in Root)
            {
                Console.WriteLine($"  Element: {element.Name} ({element.Components.Count} components)");
                try
                {
                    element.Components.ForEach(component => component.Update(deltaTime));
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error updating element {element.Name}: {e.Message}"); Console.WriteLine(e);
                }
        }
    }
}
}
