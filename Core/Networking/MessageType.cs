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
        PlayerJoin,
        PlayerLeave,
        PlayerBan,
        PlayerKick,
        PlayerAction,
        Error
    }
}
