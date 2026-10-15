using System.Collections.Generic;
using V12.Core.Core.Interfaces;

namespace V12.Core
{
    /// <summary>
    /// Engine global for the current scene. A shortcut to the active world
    /// (<see cref="GameRoot.SelectedWorld"/>) and its root elements, so host code
    /// and Contract scripts can write <c>Scene.Add(element)</c> instead of
    /// reaching through <c>GameRoot.Instance.SelectedWorld</c>.
    /// </summary>
    public static class Scene
    {
        /// <summary>The current (selected) world, or null when none is active.</summary>
        public static World? Current => GameRoot.Instance?.SelectedWorld;

        /// <summary>The current world's root elements, or null when no world is active.</summary>
        public static List<IWorldElement>? Root => GameRoot.Instance?.SelectedWorld?.Root;

        /// <summary>Name of the current world, or an empty string when none is active.</summary>
        public static string Name => GameRoot.Instance?.SelectedWorld?.WorldName ?? "";

        /// <summary>Add an element to the current world. No-op when no world is active.</summary>
        public static void Add(IWorldElement element)
            => GameRoot.Instance?.SelectedWorld?.AddElement(element);

        /// <summary>Create a named element and add it to the current world.
        /// Returns the element, or null when no world is active.</summary>
        public static IWorldElement? Spawn(string name)
        {
            var world = GameRoot.Instance?.SelectedWorld;
            if (world == null) return null;
            var element = new Element(name);
            world.AddElement(element);
            return element;
        }
    }
}
