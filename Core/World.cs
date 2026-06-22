using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using V12.Core.Core.Interfaces;
namespace V12.Core
{
    public class World 
    {

        public List<IWorldElement> Root { get; set; }
        public string WorldName { get; set; }
        internal Dictionary<long, IWorldElement> _elementsById = new();

        /// <summary>Filesystem path where this world's V12World archive was extracted.</summary>
        public string ExtractPath { get; set; }

        /// <summary>The v12:// mount point for this world's assets.</summary>
        public string MountPoint { get; set; }

        /// <summary>Global lock for all world and element tree mutations.</summary>
        public ReaderWriterLockSlim Lock { get; } = new();

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
            Lock.EnterReadLock();
            try
            {
                return Root.FirstOrDefault(e => e.GetComponent<T>() != null);
            }
            finally { Lock.ExitReadLock(); }
        }

        public IWorldElement? FindElementWithComponentRecursive<T>() where T : IComponent
        {
            Lock.EnterReadLock();
            try
            {
                foreach (var element in Root.ToArray())
                {
                    if (element.GetComponent<T>() != null) return element;
                    var found = FindInChildren<T>(element);
                    if (found != null) return found;
                }
                return null;
            }
            finally { Lock.ExitReadLock(); }
        }

        private IWorldElement? FindInChildren<T>(IWorldElement parent) where T : IComponent
        {
            foreach (var child in parent.Children.ToArray())
            {
                if (child.GetComponent<T>() != null) return child;
                var found = FindInChildren<T>(child);
                if (found != null) return found;
            }
            return null;
        }
        public void AddElement(IWorldElement element)
        {
            Lock.EnterWriteLock();
            try
            {
                Root.Add(element);
                _elementsById[element.Id] = element;
            }
            finally { Lock.ExitWriteLock(); }
            ElementAdded?.Invoke(element);
        }

        /// <summary>Remove an element from the world and fire <see cref="ElementRemoved"/>.</summary>
        public void RemoveElement(IWorldElement element)
        {
            bool removed;
            Lock.EnterWriteLock();
            try
            {
                removed = Root.Remove(element);
                if (removed)
                    _elementsById.Remove(element.Id);
            }
            finally { Lock.ExitWriteLock(); }
            if (removed)
                ElementRemoved?.Invoke(element);
        }

        public void GenerateWorld()
        {
            // This method can be overridden in derived classes to create specific world content.
            // For example, you could create a HomeWorld class that inherits from World and overrides this method to populate the world with specific elements.
        }
        public void Update(float deltaTime)
        {
            Lock.EnterReadLock();
            try
            {
                foreach (var element in Root.ToArray())
                {
                    try
                    {
                        foreach (var component in element.Components.ToArray())
                            component.Update(deltaTime);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Error updating element {element.Name}: {e.Message}"); Console.WriteLine(e);
                    }
                }
            }
            finally { Lock.ExitReadLock(); }
        }
    }
}
