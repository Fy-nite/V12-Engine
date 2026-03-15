using System;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using LiteNetLib;
using LiteNetLib.Utils;
using V12.Core.Core.Interfaces;

namespace V12.Core.Networking
{
    // Minimal LiteNetLib-based implementation of INetworkManager.
    // This is a lightweight wrapper: it starts a NetManager, accepts/creates a single peer
    // and enqueues received MessageDTO instances for synchronous retrieval.
    public class LiteNetManager : INetworkManager, INetEventListener
    {
        private NetManager net;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, NetPeer> peers = new System.Collections.Concurrent.ConcurrentDictionary<int, NetPeer>();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> clientMap = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        public class RemoteMessage
        {
            public int PeerId { get; }
            public MessageDTO Message { get; }
            public IPEndPoint EndPoint { get; }
            public RemoteMessage(int peerId, MessageDTO message, System.Net.IPEndPoint endPoint)
            {
                PeerId = peerId;
                Message = message;
                EndPoint = endPoint;
            }
        }
        private readonly BlockingCollection<RemoteMessage> receiveQueue = new BlockingCollection<RemoteMessage>();
        private readonly string connectKey = "v12";

        public LiteNetManager()
        {
            net = new NetManager(this);
            net.Start();

            // Polling loop required by LiteNetLib to dispatch events.
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                while (true)
                {
                    net.PollEvents();
                    System.Threading.Thread.Sleep(5);
                }
            });
        }

        public void Connect(Uri address)
        {
            var host = address.Host;
            var port = address.Port;
            // Connect returns a NetPeer; it will be added to peers when OnPeerConnected fires.
            _ = net.Connect(host, port, connectKey);
        }

        // Return the list of connected NetPeer Ids
        public System.Collections.Generic.IEnumerable<int> GetConnectedPeerIds()
        {
            return peers.Keys;
        }

        // Map an application-level client id (string) to a NetPeer.Id
        public void MapClientToPeer(string clientId, int peerId)
        {
            clientMap[clientId] = peerId;
        }

        public bool TryGetPeerIdForClient(string clientId, out int peerId)
        {
            return clientMap.TryGetValue(clientId, out peerId);
        }

        public bool SendToClient(string clientId, MessageDTO message)
        {
            if (!TryGetPeerIdForClient(clientId, out var pid))
                return false;
            return SendTo(pid, message);
        }

        public void Listen(int port)
        {
            net.Start(port);
        }

        // Send to all connected peers.
        public void SendMessage(MessageDTO message)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
            foreach (var p in peers.Values)
            {
                try
                {
                    p.Send(bytes, DeliveryMethod.Unreliable);
                }
                catch
                {
                    // ignore send errors per-peer
                }
            }
        }

        // Send to a specific peer by its NetPeer.Id
        public bool SendTo(int peerId, MessageDTO message)
        {
            if (!peers.TryGetValue(peerId, out var p))
                return false;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
            p.Send(bytes, DeliveryMethod.Unreliable);
            return true;
        }

        // Broadcast alias
        public void Broadcast(MessageDTO message) => SendMessage(message);

        // Blocking receive that returns remote metadata
        public RemoteMessage ReceiveRemoteMessage()
        {
            return receiveQueue.Take();
        }

        public System.Threading.Tasks.Task<RemoteMessage> ReceiveRemoteMessageAsync(System.Threading.CancellationToken ct = default)
        {
            return System.Threading.Tasks.Task.Run(() => receiveQueue.Take(ct), ct);
        }

        // Non-blocking try-receive
        public bool TryReceiveRemote(out RemoteMessage? msg)
        {
            return receiveQueue.TryTake(out msg!);
        }

        // Backwards-compatible ReceiveMessage from INetworkManager: returns only the MessageDTO
        public MessageDTO ReceiveMessage()
        {
            var rm = receiveQueue.Take();
            return rm.Message;
        }

        // INetEventListener implementations
        public void OnPeerConnected(NetPeer peer)
        {
            peers[peer.Id] = peer;
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            peers.TryRemove(peer.Id, out _);
            // remove any clientId mappings that referenced this peer
            foreach (var kv in clientMap)
            {
                if (kv.Value == peer.Id)
                    clientMap.TryRemove(kv.Key, out _);
            }
        }

        public void OnNetworkError(System.Net.IPEndPoint endPoint, System.Net.Sockets.SocketError socketError)
        {
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            try
            {
                var data = reader.GetRemainingBytes();
                var msg = JsonSerializer.Deserialize<MessageDTO>(data);
                if (msg != null)
                {
                    var remote = new RemoteMessage(peer.Id, msg, peer.EndPoint);
                    receiveQueue.Add(remote);
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                reader.Recycle();
            }
        }

        public void OnNetworkReceiveUnconnected(System.Net.IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
        }

        public void OnConnectionRequest(ConnectionRequest request)
        {
            // accept all incoming connections
            request.AcceptIfKey(connectKey);
        }
    }
}
