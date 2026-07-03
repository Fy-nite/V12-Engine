using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using V12.Core.Networking;

namespace V12.Core.NetworkCable
{
    /// <summary>
    /// TCP-based network host that accepts client connections and routes messages through NetworkCables.
    /// </summary>
    public class NetworkHost : IDisposable
    {
        private readonly int _port;
        private readonly NetworkCables _cables;
        private TcpListener? _listener;
        // Use ConcurrentDictionary instead of ConcurrentBag so we can remove disconnected clients.
        // The value is just 'true' as a placeholder — we only care about the keys.
        private readonly ConcurrentDictionary<TcpClient, bool> _clients = new ConcurrentDictionary<TcpClient, bool>();
        private CancellationTokenSource? _cts;

        // Server-assigned unique player IDs to avoid collisions when multiple clients
        // have locally-assigned element IDs that happen to match.
        private readonly ConcurrentDictionary<TcpClient, long> _clientPlayerIds = new ConcurrentDictionary<TcpClient, long>();
        private long _nextPlayerId = 1000; // Start at 1000 to avoid collision with local IDs

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
                foreach (var kvp in _clients)
                    if (kvp.Key.Connected) count++;
                return count;
            }
        }

        /// <summary>
        /// Get the server-assigned unique player ID for a given TcpClient.
        /// Returns 0 if the client is not found.
        /// </summary>
        public long GetPlayerIdForClient(TcpClient client)
        {
            if (client != null && _clientPlayerIds.TryGetValue(client, out var playerId))
                return playerId;
            return 0;
        }

        /// <summary>
        /// Raised on the accept loop thread when a new client connects.
        /// The argument is the server-assigned player ID for the new client.
        /// </summary>
        public event Action<long>? OnClientConnected;

        /// <summary>
        /// Raised when a client disconnects. The argument is the server-assigned player ID
        /// that was associated with the disconnected client.
        /// </summary>
        public event Action<long>? OnClientDisconnected;

        /// <summary>
        /// Create a new network host.
        /// </summary>
        /// <param name="port">Port to listen on.</param>
        /// <param name="cables">NetworkCables instance to use. If null, uses NetworkCables.Default.</param>
        public NetworkHost(int port, NetworkCables? cables = null)
        {
            _port = port;
            _cables = cables ?? NetworkCables.Default;
        }

        /// <summary>
        /// Raised when the host fails to bind/listen (e.g. port already in use).
        /// </summary>
        public event Action<Exception>? OnStartFailed;

        /// <summary>
        /// Start the host and begin accepting connections.
        /// </summary>
        public async Task StartAsync(CancellationToken token = default)
        {
            if (IsRunning) return;

            try
            {
                _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                _listener = new TcpListener(IPAddress.Any, _port);
                _listener.Start();
                IsRunning = true;

                Console.WriteLine($"[NetworkHost] Listening on port {_port}");

                // Subscribe to outgoing messages
                _cables.OnMessageSending += BroadcastMessage;

                // Accept clients loop
                _ = Task.Run(async () => await AcceptClientsAsync(_cts.Token), _cts.Token);
            }
            catch (Exception ex)
            {
                IsRunning = false;
                Console.WriteLine($"[NetworkHost] FAILED to start on port {_port}: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"[NetworkHost] Is another process already using port {_port}? Try: ss -tlnp | grep {_port}");
                try { OnStartFailed?.Invoke(ex); } catch { }
            }

            await System.Threading.Tasks.Task.CompletedTask; // keep async signature
        }

        private async Task AcceptClientsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var acceptTask = _listener.AcceptTcpClientAsync();
                    var cancelTask = System.Threading.Tasks.Task.Delay(-1, token);
                    var finished = await System.Threading.Tasks.Task.WhenAny(acceptTask, cancelTask);
                    if (finished == cancelTask)
                        throw new OperationCanceledException(token);
                    var client = await acceptTask;
                    if (client == null) continue;
                    // Capture remote endpoint (may be null on some platforms)
                    var remoteEndpoint = client.Client?.RemoteEndPoint?.ToString() ?? "<unknown>";
                    _clients[client] = true;

                    // Assign a unique player ID to this client
                    var playerId = System.Threading.Interlocked.Increment(ref _nextPlayerId);
                    _clientPlayerIds[client] = playerId;

                    Console.WriteLine($"[NetworkHost] Client connected from {remoteEndpoint}. Total clients: {_clients.Count}, assigned PlayerId: {playerId}");
                    try { OnClientConnected?.Invoke(playerId); } catch { }

                    // Start handling this client
                    _ = Task.Run(async () => await HandleClientAsync(client, token), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NetworkHost] Error accepting client: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            try
            {
                using var stream = client.GetStream();
                var buffer = new byte[4096];

                while (!token.IsCancellationRequested && client.Connected)
                {
                    // Read length prefix (4 bytes)
                    var lengthBytes = new byte[4];
                    int bytesRead = await stream.ReadAsync(lengthBytes, 0, 4, token);
                    if (bytesRead == 0) break;

                    int messageLength = BitConverter.ToInt32(lengthBytes, 0);
                    if (messageLength <= 0 || messageLength > 10_000_000) // 10MB sanity check
                    {
                        Console.WriteLine($"[NetworkHost] Invalid message length: {messageLength}");
                        break;
                    }

                    // Read message payload
                    var messageBytes = new byte[messageLength];
                    int totalRead = 0;
                    while (totalRead < messageLength)
                    {
                        bytesRead = await stream.ReadAsync(messageBytes, totalRead, messageLength - totalRead, token);
                        if (bytesRead == 0) break;
                        totalRead += bytesRead;
                    }

                    if (totalRead < messageLength)
                    {
                        Console.WriteLine($"[NetworkHost] Incomplete message received");
                        break;
                    }

                    // Deserialize and enqueue
                    try
                    {
                        var message = AncientCompressor.Decompress<MessageDTO>(messageBytes);
                        // Tag the message with the originating TcpClient so BroadcastMessage
                        // can skip echoing it back to the sender.
                        message.SenderToken = client;
                        _cables.EnqueueIncoming(message);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[NetworkHost] Error deserializing message: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkHost] Client handler error: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                var remote = client.Client?.RemoteEndPoint?.ToString() ?? "<unknown>";
                var playerId = 0L;
                _clientPlayerIds.TryRemove(client, out playerId);
                _clients.TryRemove(client, out _);
                try { client.Close(); } catch { }
                Console.WriteLine($"[NetworkHost] Client disconnected from {remote} (PlayerId: {playerId}). Total clients: {ClientCount}");
                try { OnClientDisconnected?.Invoke(playerId); } catch { }
            }
        }

        private void BroadcastMessage(MessageDTO message)
        {
            // No clients connected → nothing to broadcast. Bail out before paying the
            // serialization cost; this keeps the send path quiet when the server is idle
            // or nobody is connected.
            if (_clients.IsEmpty)
            {
                if (DebugMode)
                    Console.WriteLine($"[NetworkHost] ⚠ BroadcastMessage called but no clients connected. Message type: {message.MessageType}");
                return;
            }

            if (DebugMode)
                Console.WriteLine($"[NetworkHost] 📡 Broadcasting {message.MessageType} to {_clients.Count} client(s)...");

            var serialized = AncientCompressor.Compress(message);
            var lengthBytes = BitConverter.GetBytes(serialized.Length);

            if (DebugMode)
                Console.WriteLine($"[NetworkHost]   Serialized size: {serialized.Length} bytes");

            int sentCount = 0;
            int skippedCount = 0;

            foreach (var kvp in _clients)
            {
                var client = kvp.Key;
                // Don't echo the message back to the client that sent it.
                if (message.SenderToken != null && ReferenceEquals(client, message.SenderToken))
                {
                    if (DebugMode)
                        Console.WriteLine($"[NetworkHost]   ⏭ Skipping sender client");
                    skippedCount++;
                    continue;
                }

                if (!client.Connected)
                {
                    if (DebugMode)
                        Console.WriteLine($"[NetworkHost]   ⚠ Client not connected, skipping");
                    continue;
                }

                try
                {
                    var stream = client.GetStream();
                    stream.Write(lengthBytes, 0, 4);
                    stream.Write(serialized, 0, serialized.Length);
                    stream.Flush();
                    sentCount++;
                    if (DebugMode)
                    {
                        var rem = client?.Client?.RemoteEndPoint?.ToString() ?? "<unknown>";
                        Console.WriteLine($"[NetworkHost]   ✅ Sent to {rem}");
                    }
                }
                catch (Exception ex)
                {
                    var rem = client?.Client?.RemoteEndPoint?.ToString() ?? "<unknown>";
                    Console.WriteLine($"[NetworkHost]   ❌ Error sending to client {rem}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (DebugMode)
                Console.WriteLine($"[NetworkHost] 📊 Broadcast complete: sent={sentCount}, skipped={skippedCount}");
        }

        public void Stop()
        {
            if (!IsRunning) return;

            Console.WriteLine($"[NetworkHost] Stopping...");
            _cts?.Cancel();
            _listener?.Stop();

            foreach (var kvp in _clients)
            {
                kvp.Key?.Close();
            }
            _clients.Clear();

            _cables.OnMessageSending -= BroadcastMessage;
            IsRunning = false;
            Console.WriteLine($"[NetworkHost] Stopped.");
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
            _listener = null;
        }
    }
}
