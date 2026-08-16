using System;
using System.Collections.Generic;
using System.Numerics;

namespace V12.Core.Networking
{
    /// <summary>
    /// Wire-safe DTO for a full world snapshot. Uses only primitive/concrete types
    /// so BSON can serialize it without needing polymorphic class maps.
    /// </summary>
    public class WorldSyncDTO
    {
        public string? WorldName { get; set; }
        public List<ElementSyncDTO> Elements { get; set; } = new();
    }

    public class ElementSyncDTO
    {
        public long Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        /// <summary>Element transform. Carried explicitly because Element.LocalTransform is a
        /// plain property (not a component), so BSON component serialization alone would drop it,
        /// collapsing every synced element to the world origin on the receiving side.</summary>
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; }
        public Vector3 Scale { get; set; }
        /// <summary>Serialized component payloads. Each entry stores the component's
        /// assembly-qualified type name and its BSON-serialized data so the receiver
        /// can fully reconstruct the component.</summary>
        public List<ComponentSyncDTO> Components { get; set; } = new();
        /// <summary>Child elements (recursive, for full tree serialization).</summary>
        public List<ElementSyncDTO> Children { get; set; } = new();
    }

    /// <summary>Carries a single serialized component for wire transport.</summary>
    public class ComponentSyncDTO
    {
        /// <summary>Assembly-qualified type name used to reconstruct the component on the receiver.</summary>
        public string TypeName { get; set; } = "";
        /// <summary>BSON-serialized bytes of the concrete component instance.</summary>
        public byte[] Data { get; set; } = System.Array.Empty<byte>();
    }

    /// <summary>
    /// Wire-safe DTO for an incremental element lifecycle batch: elements that were
    /// created (spawned) or removed (despawned) since the last delta. Host-authoritative:
    /// only the host broadcasts these, and the ids carried are the host's sequential
    /// element ids, so every peer gives the same element the same id.
    /// </summary>
    public class WorldDeltaDTO
    {
        public List<ElementCreateDTO> Creates { get; set; } = new();
        public List<long> Deletes { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Wire-safe DTO for an incremental element state update: transform, name,
    /// description and parent, without component payloads or children. Components
    /// flow through <see cref="ComponentBatchDTO"/> and lifecycle through
    /// <see cref="WorldDeltaDTO"/>, so a dirty element only needs its own state here.
    /// </summary>
    public class ElementUpdateDTO
    {
        public long Id { get; set; }
        /// <summary>Id of the parent element. 0 means a world root.</summary>
        public long ParentId { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; }
        public Vector3 Scale { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>Batch of element state updates for a single <see cref="MessageType.WorldElementUpdate"/>.</summary>
    public class ElementUpdateBatchDTO
    {
        public List<ElementUpdateDTO> Elements { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Wire-safe DTO for component removals: components removed from existing
    /// elements since the last batch. Host-authoritative, like WorldDelta.
    /// </summary>
    public class ComponentRemovalDTO
    {
        public long ElementId { get; set; }
        public List<long> ComponentIds { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }

    /// <summary>A single element create inside a <see cref="WorldDeltaDTO"/>.</summary>
    public class ElementCreateDTO
    {
        /// <summary>Id of the parent element the new element was attached to. 0 means a world root.</summary>
        public long ParentId { get; set; }
        /// <summary>Name of the sender's world the element was added to (root creates only).</summary>
        public string? WorldName { get; set; }
        /// <summary>Full element subtree (id, transform, components, children).</summary>
        public ElementSyncDTO? Element { get; set; }
    }
}
