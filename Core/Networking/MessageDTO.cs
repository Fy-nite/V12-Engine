using System;
using System.Collections.Generic;
using System.Text;
using System.Data;
using System.Text.Json;
namespace V12.Core.Networking
{
    public class MessageDTO
    {
        public Byte[] Message;
        public String Sender;
        public MessageType MessageType;

        public MessageDTO() { }
        public MessageDTO(Byte[] message) { Message = message; }
        // Base64 encoded byte array of the message content
    }
}
