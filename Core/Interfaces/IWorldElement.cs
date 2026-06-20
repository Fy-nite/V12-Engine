using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using V12.Core.NetworkCable;
using V12.Core.Interfaces.Renderer;
namespace V12.Core.Core.Interfaces
{
    public interface IWorldElement : ISpatial
    {
        /// <summary>
        /// Event raised when this world element or its components have changed and should be synchronized over the network.
        /// </summary>
        event Action<IWorldElement>? OnDirty;

        /// <summary>
        /// A stable numeric identifier for this element. Useful for networking
        /// and referencing elements without relying on mutable names.
        /// </summary>
        long Id { get; set; }

        /// <summary>
        /// Gets or sets the name associated with the entity.
        /// </summary>
        /// <remarks>The name can be null, indicating that no name has been assigned. It is recommended to
        /// provide a meaningful name for better identification.</remarks>
        string? Name { get; set; }

        /// <summary>
        /// Gets or sets the Active state on a component
        /// </summary>
        /// <remarks>This MUST be not null, if the bool is false. the component will be skipped over during rendering.</remarks>
        bool Active { get; set; }

        /// <summary>
        /// The description provides additional information about the entity. It can be used to give context, details, or any relevant information that helps to understand the purpose or characteristics of the entity. Like the name, the description can also be null if no additional information is provided.
        /// </summary>
        string? Description { get; set; } // tis is funny

        /// <summary>
        /// Gets or sets the parent element of the current world element. If this property is null, the element
        /// represents the root of the world hierarchy.
        /// </summary>
        /// <remarks>Use this property to navigate or modify the hierarchical structure of world elements.
        /// Setting the parent correctly is essential for maintaining the integrity of the world hierarchy.</remarks>
        IWorldElement? Parent { get; set; } // if Parent == null, then this is the world itself

        /// <summary>
        /// Gets or sets the list of components associated with this instance.
        /// </summary>
        /// <remarks>This property allows for the management of components, enabling addition, removal,
        /// and iteration over the components. It is important to ensure that the list is not modified directly while
        /// iterating to avoid exceptions.</remarks>
        List<IComponent> Components { get; set; }

        /// <summary>
        /// Adds the specified component to the collection and attaches it to the current instance.
        /// </summary>
        /// <remarks>The method invokes the OnAttach method of the component, allowing it to perform any
        /// necessary initialization with the current instance before being added to the collection.</remarks>
        /// <param name="component">The component to be added to the collection. This component must not be null.</param>
        public void AddComponent(IComponent component)
        {

            component.OnAttach(this);
            Components.Add(component);
            component.MarkDirty(); // mark the component as dirty after attaching to ensure it is updated in the next cycle
        }

        /// <summary>
        /// Removes the specified component from the collection and ensures that it is properly detached before removal.
        /// </summary>
        /// <remarks>This method calls the component's OnDetach method prior to removal to ensure any
        /// necessary cleanup is performed.</remarks>
        /// <param name="component">The component to remove from the collection. Cannot be null.</param>
        public void RemoveComponent(IComponent component)
        {
            component.OnDetach(this); // always call detach before removing, to ensure proper cleanup
            Components.Remove(component);
            component.MarkDirty(); // mark the component as dirty after detaching to ensure it is updated in the next cycle
        }

        /// <summary>
        /// Retrieves a component from the collection that matches the specified name.
        /// </summary>
        /// <remarks>The search for the component name is case-sensitive. Ensure that the provided name
        /// matches exactly with the component's name in the collection.</remarks>
        /// <param name="name">The name of the component to retrieve. This parameter cannot be null or empty.</param>
        /// <returns>The component that has the specified name, or null if no such component exists.</returns>
        public IComponent GetComponent(string name) {
            return Components.Find(c => c.Name == name);
        }



        /// <summary>
        /// Retrieves the first component of the specified type from the collection of components.
        /// </summary>
        /// <remarks>This method searches through the collection of components and returns the first
        /// instance that matches the specified type. If no matching component is found, the method returns null. Ensure
        /// that the type parameter T is a valid component type that implements IComponent.</remarks>
        /// <typeparam name="T">The type of the component to retrieve. This type must implement the IComponent interface.</typeparam>
        /// <returns>The component of type T if found; otherwise, null.</returns>
        public T GetComponent<T>() where T : IComponent
        {
            return (T)Components.Find(c => c is T) ?? default(T);
        }

        public T GetComponent<T>(string name) where T : IComponent
        {
            return (T)Components.Find(c => c is T && c.Name == name) ?? default(T);
        }

        /// <summary>
        /// The direct children of this element in the world hierarchy.
        /// </summary>
        List<IWorldElement> Children { get; }

        /// <summary>
        /// Adds a child element, setting its <see cref="Parent"/> to this element.
        /// </summary>
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

        /// <summary>
        /// Finds the first direct child whose name matches (case-sensitive).
        /// </summary>
        public IWorldElement FindChildByName(string name)
            => Children.Find(c => c.Name == name);

        /// <summary>
        /// Recursively searches this element and all descendants for a child
        /// matching the given name. Returns null if not found.
        /// </summary>
        public IWorldElement FindChildByNameRecursive(string name)
        {
            foreach (var child in Children)
            {
                if (child.Name == name) return child;
                var found = child.FindChildByNameRecursive(name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Walks up the parent chain and returns the root element
        /// (the element whose Parent is null).
        /// </summary>
        public IWorldElement GetRoot()
        {
            IWorldElement current = this;
            while (current.Parent != null)
                current = current.Parent;
            return current;
        }
    }
}
