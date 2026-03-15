using System;
using System.Collections.Generic;
using System.Text;
using V12.Core.Networking;

namespace V12.Core.Core.Interfaces
{
    interface INetworkManager
    {
        void SendMessage(MessageDTO message);
        MessageDTO ReceiveMessage();
    }
}
