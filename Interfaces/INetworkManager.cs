using System;
using System.Collections.Generic;
using System.Text;

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
