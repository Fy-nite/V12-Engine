using LiteNetLib;
using V12.Core.Networking;

namespace V12.Core.NetworkCable
{
    /// <summary>
    /// Shared wire helpers for the LiteNetLib (UDP)-based transport: message serialization
    /// and per-message-type delivery selection.
    /// </summary>
    internal static class LiteNetWire
    {
        /// <summary>Handshake key required on every connection.</summary>
        public const string ConnectKey = "v12";

        /// <summary>Channel 0: reliable, ordered - lifecycle and imperative messages.</summary>
        public const byte ReliableChannel = 0;

        /// <summary>Channel 1: unreliable - continuous, self-correcting state updates.</summary>
        public const byte UnreliableChannel = 1;

        /// <summary>
        /// Pick the LiteNetLib delivery method and channel for a message type. Lifecycle
        /// messages (join/leave/archive/sync/delta/removal/rpc) must arrive exactly once
        /// and in order. Continuous state (WorldUpdate, element updates, PlayerSync,
        /// Heartbeat) is self-correcting at the sync rate, so it goes unreliable to keep
        /// it from ever head-of-line blocking the reliable channel.
        /// </summary>
        public static (DeliveryMethod Method, byte Channel) DeliveryFor(MessageDTO message)
        {
            switch (message.MessageType)
            {
                case MessageType.WorldSync:
                case MessageType.WorldArchive:
                case MessageType.WorldDelta:
                case MessageType.ComponentRemoved:
                case MessageType.RpcCall:
                case MessageType.PlayerJoin:
                case MessageType.PlayerLeave:
                case MessageType.PlayerBan:
                case MessageType.PlayerKick:
                case MessageType.PlayerAction:
                case MessageType.SyncBatch:
                case MessageType.VNodeFileRequest:
                case MessageType.Error:
                case MessageType.Text:
                case MessageType.Binary:
                case MessageType.Command:
                case MessageType.Event:
                    return (DeliveryMethod.ReliableOrdered, ReliableChannel);

                case MessageType.WorldUpdate:
                case MessageType.WorldElementUpdate:
                case MessageType.PlayerSync:
                case MessageType.Heartbeat:
                default:
                    return (DeliveryMethod.Unreliable, UnreliableChannel);
            }
        }

        /// <summary>
        /// Delivery selection aware of the serialized payload size. LiteNetLib
        /// fragments only reliable delivery methods; Unreliable and
        /// ReliableSequenced packets over the MTU (<see cref="NetConstants.MaxUnreliableDataSize"/>)
        /// throw <see cref="TooBigPacketException"/> instead of being sent. Any
        /// payload that would exceed that cap is escalated to
        /// <see cref="DeliveryMethod.ReliableOrdered"/> on the reliable channel,
        /// where LiteNetLib splits it into MTU-sized fragments and reassembles on
        /// the receiver. Continuous state is normally small and stays Unreliable;
        /// the escalation is a safety net so a single large element or component
        /// (e.g. a script source) can never wedge the state stream.
        /// </summary>
        public static (DeliveryMethod Method, byte Channel) DeliveryFor(MessageDTO message, int payloadSize)
        {
            var (method, channel) = DeliveryFor(message);
            if (payloadSize > NetConstants.MaxUnreliableDataSize &&
                (method == DeliveryMethod.Unreliable || method == DeliveryMethod.ReliableSequenced))
                return (DeliveryMethod.ReliableOrdered, ReliableChannel);
            return (method, channel);
        }

        public static byte[] Serialize(MessageDTO message)
            => AncientCompressor.Compress(message);

        /// <summary>
        /// Deserialize a BSON MessageDTO from the wire. Returns null on malformed data.
        /// </summary>
        public static MessageDTO? Deserialize(byte[] data)
        {
            try { return AncientCompressor.Decompress<MessageDTO>(data); }
            catch { return null; }
        }
    }
}
