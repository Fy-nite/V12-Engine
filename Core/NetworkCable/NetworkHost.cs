using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using V12.Core.Networking;

namespace V12.Core.NetworkCable
{
    /// <summary>
    /// LiteNetLib (UDP)-based network host that accepts client connections and routes
    /// messages through NetworkCables. Lifecycle messages travel on a reliable, ordered
    /// channel; continuous state updates travel on an unreliable channel so high-frequency
    /// sync never blocks lifecycle traffic. Dead peers are reaped by LiteNetLib's
    /// DisconnectTimeout (replacing the old TCP heartbeat monitor).
    /// </summary>
    public class NetworkHost : IDisposable, INetEventListener
    {
        private readonly int _port;
        private readonly NetworkCables _cables;
        private NetManager? _net;
        private readonly ConcurrentDictionary<NetPeer, bool> _peers = new ConcurrentDictionary<NetPeer, bool>();
        private readonly ConcurrentDictionary<NetPeer, long> _clientPlayerIds = new ConcurrentDictionary<NetPeer, long>();
        private long _nextPlayerId = 1000; // Start at 1000 to avoid collision with local IDs
        private CancellationTokenSource? _cts;
        private readonly object _pollLock = new object();
        private bool _pollStarted;

        /// <summary>
        /// Global debug mode flag. Set to true to enable verbose network logging.
        /// </summary>
        public static bool DebugMode { get; set; } = false;

        public bool IsRunning { get; private set; }
        public int Port => _port;

        public int ClientCount
        {
            get
            {
                int count = 0;
                foreach (var kvp in _peers)
                    if (kvp.Key.ConnectionState == ConnectionState.Connected) count++;
                return count;
            }
        }

        /// <summary>
        /// Get the server-assigned unique player ID for a given NetPeer.
        /// Returns 0 if the client is not found.
        /// </summary>
        public long GetPlayerIdForClient(NetPeer peer)
        {
            if (peer != null && _clientPlayerIds.TryGetValue(peer, out var playerId))
                return playerId;
            return 0;
        }

        /// <summary>
        /// Globally-unique player ID for the host itself, drawn from the same
        /// server-assigned namespace as clients (starts above any per-process
        /// local element ID, so it can never collide with a peer's local IDs).
        /// </summary>
        public long HostPlayerId { get; private set; }

        /// <summary>
        /// Server-assigned unique player ID for the sender of <paramref name="message"/>.
        /// Returns 0 when the sender is not a known client (e.g. host-originated messages).
        /// </summary>
        public long GetServerAssignedPlayerId(MessageDTO message)
        {
            if (message?.SenderToken is NetPeer peer && _clientPlayerIds.TryGetValue(peer, out var playerId))
                return playerId;
            return 0;
        }

        /// <summary>
        /// Raised on the poll thread when a new client connects.
        /// The argument is the server-assigned player ID for the new client.
        /// </summary>
        public event Action<long>? OnClientConnected;

        /// <summary>
        /// Raised when a client disconnects. The argument is the server-assigned player ID
        /// that was associated with the disconnected client.
        /// </summary>
        public event Action<long>? OnClientDisconnected;

        /// <summary>
        /// Raised when the host fails to bind/listen (e.g. port already in use).
        /// </summary>
        public event Action<Exception>? OnStartFailed;

        /// <summary>
        /// Create a new network host.
        /// </summary>
        /// <param name="port">Port to listen on.</param>
        /// <param name="cables">NetworkCables instance to use. If null, uses NetworkCables.Default.</param>
        public NetworkHost(int port, NetworkCables? cables = null)
        {
            _port = port;
            _cables = cables ?? NetworkCables.Default;
            // Reserve the first slot of the server namespace for the host itself so the
            // host's PlayerSync can never collide with a client's local element ID.
            HostPlayerId = Interlocked.Increment(ref _nextPlayerId);
        }

        /// <summary>
        /// Start the host and begin accepting connections.
        /// </summary>
        public Task StartAsync(CancellationToken token = default)
        {
            if (IsRunning) return Task.CompletedTask;

            try
            {
                _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                _net = new NetManager(this)
                {
                    DisconnectTimeout = 15000,
                    MaxConnectAttempts = 5
                };
                if (!_net.Start(_port))
                {
                    Console.WriteLine($"[NetworkHost] FAILED to start on port {_port}: bind failed.");
                    Console.WriteLine($"[NetworkHost] Is another process already using port {_port}? Try: netstat -ano | findstr {_port}");
                    try { OnStartFailed?.Invoke(new InvalidOperationException($"Failed to bind UDP port {_port}")); } catch { }
                    return Task.CompletedTask;
                }

                IsRunning = true;
                Console.WriteLine($"[NetworkHost] Listening (UDP/LiteNetLib) on port {_port}");

                // Subscribe to outgoing messages
                _cables.OnMessageSending += BroadcastMessage;

                // Single background poll loop; LiteNetLib dispatches all events from here.
                StartPolling();
            }
            catch (Exception ex)
            {
                IsRunning = false;
                Console.WriteLine($"[NetworkHost] FAILED to start on port {_port}: {ex.GetType().Name}: {ex.Message}");
                try { OnStartFailed?.Invoke(ex); } catch { }
            }

            return Task.CompletedTask;
        }

        private void StartPolling()
        {
            lock (_pollLock)
            {
                if (_pollStarted) return;
                _pollStarted = true;
                _ = Task.Run(() =>
                {
                    while (_cts != null && !_cts.IsCancellationRequested)
                    {
                        try { _net?.PollEvents(); }
                        catch (Exception ex)
                        {
                            if (DebugMode) Console.WriteLine($"[NetworkHost] PollEvents error: {ex.GetType().Name}: {ex.Message}");
                        }
                        Thread.Sleep(5);
                    }
                });
            }
        }

        private void BroadcastMessage(MessageDTO message)
        {
            // No clients connected → nothing to broadcast. Bail out before paying the
            // serialization cost; this keeps the send path quiet when the server is idle
            // or nobody is connected.
            if (_peers.IsEmpty || _net == null)
            {
                if (DebugMode)
                    Console.WriteLine($"[NetworkHost] ⚠ BroadcastMessage called but no clients connected. Message type: {message.MessageType}");
                return;
            }

            var bytes = LiteNetWire.Serialize(message);
            var (method, channel) = LiteNetWire.DeliveryFor(message, bytes.Length);

            if (DebugMode)
                Console.WriteLine($"[NetworkHost] 📡 Broadcasting {message.MessageType} to {_peers.Count} client(s) ({bytes.Length} bytes, {method})");

            int sentCount = 0;
            int skippedCount = 0;

            foreach (var kvp in _peers)
            {
                var peer = kvp.Key;
                if (peer.ConnectionState != ConnectionState.Connected)
                {
                    if (DebugMode)
                        Console.WriteLine($"[NetworkHost]   ⚠ Client not connected, skipping");
                    continue;
                }

                // Don't echo the message back to the client that sent it.
                if (message.SenderToken != null && ReferenceEquals(peer, message.SenderToken))
                {
                    if (DebugMode)
                        Console.WriteLine($"[NetworkHost]   ⏭ Skipping sender client");
                    skippedCount++;
                    continue;
                }

                try
                {
                    peer.Send(bytes, channel, method);
                    sentCount++;
                    if (DebugMode)
                        Console.WriteLine($"[NetworkHost]   ✅ Sent to {peer.EndPoint}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NetworkHost]   ❌ Error sending to client {peer.EndPoint}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (DebugMode)
                Console.WriteLine($"[NetworkHost] 📊 Broadcast complete: sent={sentCount}, skipped={skippedCount}");
        }

        // ── INetEventListener ──────────────────────────────────────────────────
        public void OnPeerConnected(NetPeer peer)
        {
            _peers[peer] = true;

            // Assign a unique player ID to this client
            var playerId = Interlocked.Increment(ref _nextPlayerId);
            _clientPlayerIds[peer] = playerId;

            Console.WriteLine($"[NetworkHost] Client connected from {peer.EndPoint}. Total clients: {_peers.Count}, assigned PlayerId: {playerId}");
            try { OnClientConnected?.Invoke(playerId); } catch { }
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            long playerId = 0;
            _clientPlayerIds.TryRemove(peer, out playerId);
            _peers.TryRemove(peer, out _);

            Console.WriteLine($"[NetworkHost] Client disconnected from {peer.EndPoint} (PlayerId: {playerId}, reason: {disconnectInfo.Reason}). Total clients: {ClientCount}");
            try { OnClientDisconnected?.Invoke(playerId); } catch { }
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            Console.WriteLine($"[NetworkHost] Network error on {endPoint}: {socketError}");
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            try
            {
                var message = LiteNetWire.Deserialize(reader.GetRemainingBytes());
                if (message != null)
                {
                    // Tag the message with the originating NetPeer so BroadcastMessage
                    // can skip echoing it back to the sender.
                    message.SenderToken = peer;
                    _cables.EnqueueIncoming(message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkHost] Error deserializing message: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                reader.Recycle();
            }
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }

        public void OnConnectionRequest(ConnectionRequest request)
        {
            request.AcceptIfKey(LiteNetWire.ConnectKey);
        }

        public void Stop()
        {
            if (!IsRunning && _net == null) return;

            Console.WriteLine($"[NetworkHost] Stopping...");
            _cts?.Cancel();
            _cables.OnMessageSending -= BroadcastMessage;

            _net?.Stop();
            _peers.Clear();
            _clientPlayerIds.Clear();
            IsRunning = false;
            Console.WriteLine($"[NetworkHost] Stopped.");
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
            _cts = null;
            _net = null;
        }
    }
}
