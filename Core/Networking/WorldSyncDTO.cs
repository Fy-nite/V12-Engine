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
    }
}
