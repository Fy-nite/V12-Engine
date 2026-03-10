using System;
using System.Collections.Generic;
using System.Text;
using V12.Core.Networking;

namespace V12.Interfaces
{
    interface INetworkManager
    {
        void Register(INetworkManager manager);
        void Unregister(INetworkManager manager);
        void SendMessage(MessageDTO message);
        MessageDTO ReceiveMessage();
    }
}
