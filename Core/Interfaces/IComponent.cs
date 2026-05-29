using System;
using System.Collections.Generic;
using System.Text;

namespace V12.Core.Core.Interfaces
{
    /// <summary>
    /// Defines the contract for a component that exposes a name and description, and provides methods for updating and
    /// destroying its state.
    /// </summary>
    /// <remarks>Implementations of this interface should provide specific behavior for the update and destroy
    /// methods to manage the component's lifecycle. The Name and Description properties allow identification and
    /// documentation of the component within a system.</remarks>
    public interface IComponent 
    {
        /// <summary>
        /// Event raised when the component's state has changed and should be synchronized over the network.
        /// </summary>
        event Action<IComponent>? OnDirty;
        /// <summary>
        /// The ID used to describe the component, which is used to identify the component in the system. This ID should be unique across all components.
        /// </summary>
        long Id { get; }

        /// <summary>
        /// The Entity ID that this component is attached to. This is used to identify which entity this component belongs to in the system. This ID should be unique across all entities.
        /// May be Unused
        /// </summary>
        long? EntityId { get; set; }

        /// <summary>
        /// The name that the world element has assigned.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// the slots current description, usualy refered to as a "tag".
        /// </summary>
        string Description { get; } 

        /// <summary>
        /// Updates the state of the object to reflect changes in its environment or context.
        /// </summary>
        /// <remarks>Call this method periodically to ensure the object remains synchronized with external
        /// conditions. The method may trigger internal state changes or refresh data as required.</remarks>
        void Update(float deltaTime);

        /// <summary>
        /// Attaches the current component to the specified world element, enabling interaction and integration within
        /// the world context.
        /// </summary>
        /// <remarks>This method is typically called during the initialization phase of the component's
        /// lifecycle. Ensure that the world element is properly configured before invoking this method.</remarks>
        /// <param name="worldElement">The world element to which the component is being attached. This parameter must not be null and should
        /// represent a valid element within the world.</param>
        void OnAttach(IWorldElement worldElement);

        /// <summary>
        /// Detaches the specified world element from the current context, allowing for cleanup and resource management.
        /// </summary>
        /// <remarks>This method is typically called when the world element is no longer needed, ensuring
        /// that any associated resources are released properly.</remarks>
        /// <param name="worldElement">The world element to detach from the current context. This element must not be null and should be a valid
        /// instance of IWorldElement.</param>
        void OnDetach(IWorldElement worldElement);

        /// <summary>
        /// On every frame, this method gets updated. This is where the component should perform any necessary updates to its state or behavior based on the current game state or environment. This method is called periodically, typically once per frame, to ensure that the component remains responsive and up-to-date with changes in the game world.
        /// </summary>
        void OnUpdate();
        /// <summary>
        /// When prompted to, having this function called will BuildUI for a component.
        /// </summary>
        IWorldElement BuildUI();

        /// <summary>
        /// Describes the inspector UI for this component using the provided inspector interface.
        /// </summary>
        void BuildInspector(V12.Core.UI.IInspector inspector) { }

        /// <summary>
        /// Copy the serializable state from <paramref name="other"/> into this component.
        /// Called by the networking layer after deserialising an incoming component snapshot
        /// so the live component reflects the latest server values.
        /// Implement this in each concrete component to copy its own data fields.
        /// </summary>
        void CopyFrom(IComponent other) { }

        /// <summary>
        /// Releases all resources used by the current instance and performs cleanup operations.
        /// </summary>
        /// <remarks>This method should be called when the instance is no longer needed to ensure proper
        /// resource management. It is important to call this method to avoid memory leaks and other resource-related
        /// issues.</remarks>
        void OnDestroy();
    }
}
