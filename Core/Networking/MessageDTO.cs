using System;
using System.Collections.Generic;
using System.Text;
using System.Data;
using System.Text.Json;
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
        public MessageDTO() { }
        public MessageDTO(Uri sender, Byte[] message) { Sender = sender; Message = message; }

    }
}
