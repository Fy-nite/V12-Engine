using System;
using System.Collections.Generic;
using System.Numerics;

namespace V12.Core.Networking
{
    /// <summary>
    /// DTO for synchronizing player state across the network.
    /// </summary>
    public class PlayerSyncDTO
    {
        public long PlayerId { get; set; }
        public string PlayerName { get; set; } = "";
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; }
        public long ElementId { get; set; }
        public List<ComponentSyncDTO> Components { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// DTO sent when a player disconnects so clients can clean up remote player entities.
    /// </summary>
    public class PlayerLeaveDTO
    {
        public long PlayerId { get; set; }
    }
}
