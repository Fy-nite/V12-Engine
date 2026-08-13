using System;
using System.Collections.Generic;
using System.Text;

namespace V12.Core.Networking
{
    public enum MessageType
    {
        Text,
        Binary,
        Command,
        Event,
        WorldSync,
        WorldUpdate,
        WorldElementUpdate,
        ComponentRemoved,
        WorldArchive,
        PlayerJoin,
        PlayerLeave,
        PlayerBan,
        PlayerKick,
        PlayerAction,
        PlayerSync,
        RpcCall,     // Remote code execution: invoke a handler on the element identified by RpcCallDTO
        SyncBatch,   // SyncManager batch (SyncValue-level updates, separate from DirtyTracker)
        WorldDelta,  // Element lifecycle batch: creates + deletes since the last delta
        Heartbeat,
        Error,
        VNodeFileRequest
    }
}
