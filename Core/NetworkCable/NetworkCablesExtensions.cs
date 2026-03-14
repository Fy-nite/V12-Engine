using System;
using V12.Core.Networking;

namespace V12.Core.NetworkCable
{
    /// <summary>
    /// Extension helpers for marking objects as dirty and sending them over the network.
    /// </summary>
    public static class NetworkCablesExtensions
    {
        /// <summary>
        /// Marks the object as dirty by serializing it and sending it via the provided NetworkCables instance or the global default.
        /// </summary>
        /// <param name="obj">The object to mark dirty.</param>
        /// <param name="cables">Optional NetworkCables instance to use. If null the global NetworkCables.Default is used.</param>
        /// <param name="sender">Optional sender URI. If null a default uri of "networkcables://localhost" is used.</param>
        /// <param name="messageType">Optional message type. Defaults to MessageType.Event.</param>
        public static void MarkDirty(this object obj, NetworkCables? cables = null, Uri? sender = null, MessageType messageType = MessageType.Event)
        {
            if (obj is null) throw new ArgumentNullException(nameof(obj));

            var transport = cables ?? NetworkCables.Default;
            if (transport is null) return;

            var dto = new MessageDTO
            {
                Sender = sender ?? new Uri("networkcables://localhost"),
                MessageType = messageType,
                Message = AncientCompressor.Compress(obj)
            };

            transport.SendData(dto);
        }
    }
}
