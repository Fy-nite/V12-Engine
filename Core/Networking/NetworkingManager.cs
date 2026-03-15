using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using V12.Core.Networking;
using V12.Core.Core.Interfaces;
namespace V12.Core.Networking
{
    /// <summary>
    /// the networking manager or something.
    /// i am gonna hate this one really much - charlie-san
    /// </summary>
    public class NetworkingManager : INetworkManager
    {
        public static NetworkingManager Instance { get; private set; }
        private LiteNetManager? liteNet;
        public NetworkingManager() {
            Instance = this;
            // UDPConnections removed; prefer LiteNetLib-based implementation when available.
            // prefer LiteNetLib-based implementation when available
            try
            {
                liteNet = new LiteNetManager();
            }
            catch
            {
                liteNet = null;
            }
        }
        
        public void Connect(Uri address)
        {
            if (liteNet != null)
            {
                liteNet.Connect(address);
                return;
            }

            throw new InvalidOperationException("No networking backend available: LiteNetLib not initialized.");
        }
        public void Disconnect(Uri address)
        {
            throw new NotImplementedException();
        }

        // Synchronous interface implementations - keep original behavior until a higher-level
        // API is agreed on. These satisfy INetworkManager.
        public void SendMessage(MessageDTO message)
        {
            throw new NotImplementedException();
        }

        public MessageDTO ReceiveMessage()
        {
            throw new NotImplementedException();
        }
        public CancellationTokenSource ListenCts { get; private set; }
        public void Listen(int port)
        {
            if (liteNet != null)
            {
                liteNet.Listen(port);
                return;
            }

            throw new InvalidOperationException("No networking backend available: LiteNetLib not initialized.");
        }

        // Expose liteNet helpers
        public System.Collections.Generic.IEnumerable<int> GetConnectedPeerIds()
        {
            if (liteNet == null) return System.Linq.Enumerable.Empty<int>();
            return liteNet.GetConnectedPeerIds();
        }

        public void MapClientToPeer(string clientId, int peerId)
        {
            if (liteNet == null) throw new InvalidOperationException("No networking backend available");
            liteNet.MapClientToPeer(clientId, peerId);
        }

        public bool SendToClient(string clientId, MessageDTO message)
        {
            if (liteNet == null) throw new InvalidOperationException("No networking backend available");
            return liteNet.SendToClient(clientId, message);
        }

        public System.Threading.Tasks.Task<LiteNetManager.RemoteMessage> ReceiveRemoteMessageAsync(System.Threading.CancellationToken ct = default)
        {
            if (liteNet == null) throw new InvalidOperationException("No networking backend available");
            return liteNet.ReceiveRemoteMessageAsync(ct);
        }

        public async Task SendMessageAsync(MessageDTO message, string connectionId)
        {
            if (liteNet != null)
            {
                await Task.Run(() => liteNet.SendMessage(message));
                return;
            }

            throw new InvalidOperationException("No networking backend available: LiteNetLib not initialized.");
        }

        public async Task<MessageDTO?> ReceiveMessageAsync(UdpClient udp, CancellationToken ct = default)
        {
            if (liteNet != null)
            {
                // Wrap the blocking ReceiveMessage in a Task to avoid blocking callers.
                return await Task.Run(() => liteNet.ReceiveMessage());
            }

            throw new InvalidOperationException("No networking backend available: LiteNetLib not initialized.");
        }


    }
}
