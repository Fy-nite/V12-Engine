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

        // ── XR rig sync ──────────────────────────────────────────────────────
        // Head + hand poses, all LOCAL to the player element (the remote side
        // re-parents them under the remote player). HasRig is false for
        // desktop players, which keeps the payload small.

        /// <summary>True when the sender is in XR mode and the rig data below is valid.</summary>
        public bool HasRig { get; set; }

        public Vector3 HeadPosition { get; set; }
        public Quaternion HeadRotation { get; set; }

        public Vector3 LeftHandPosition { get; set; }
        public Quaternion LeftHandRotation { get; set; }

        public Vector3 RightHandPosition { get; set; }
        public Quaternion RightHandRotation { get; set; }

        // ── VR controller analog state (local input broadcast to peers) ─────
        public float LeftTrigger { get; set; }
        public float LeftGrip { get; set; }
        public float RightTrigger { get; set; }
        public float RightGrip { get; set; }
    }

    /// <summary>
    /// DTO sent when a player disconnects so clients can clean up remote player entities.
    /// </summary>
    public class PlayerLeaveDTO
    {
        public long PlayerId { get; set; }
    }
}
