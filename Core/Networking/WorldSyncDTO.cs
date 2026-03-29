using System.Collections.Generic;

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
        public string? Name { get; set; }
        public string? Description { get; set; }
        /// <summary>Serialized component payloads. Each entry stores the component's
        /// assembly-qualified type name and its BSON-serialized data so the receiver
        /// can fully reconstruct the component.</summary>
        public List<ComponentSyncDTO> Components { get; set; } = new();
    }

    /// <summary>Carries a single serialized component for wire transport.</summary>
    public class ComponentSyncDTO
    {
        /// <summary>Assembly-qualified type name used to reconstruct the component on the receiver.</summary>
        public string TypeName { get; set; } = "";
        /// <summary>BSON-serialized bytes of the concrete component instance.</summary>
        public byte[] Data { get; set; } = System.Array.Empty<byte>();
    }
}
