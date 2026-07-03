using System;
using System.Collections.Generic;
using System.Text;
using System.Data;
using System.Text.Json;
using MongoDB.Bson.Serialization.Attributes;
namespace V12.Core.Networking
{
    public class MessageDTO
    {
        /// <summary>
        /// BSON encoded message.
        /// </summary>
        public Byte[] Message;
        public Uri Sender;
        public MessageType MessageType;

        /// <summary>
        /// Transient tag used by the transport layer to track the originating client
        /// (e.g. the TcpClient that sent this message). Not serialized — used only
        /// so the server can avoid echoing a message back to its sender.
        /// </summary>
        [BsonIgnore]
        public object? SenderToken { get; set; }

        public MessageDTO() { }
        public MessageDTO(Uri sender, Byte[] message) { Sender = sender; Message = message; }

    }
}
