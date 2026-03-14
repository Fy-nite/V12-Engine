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
        private readonly ConcurrentBag<TcpClient> _clients = new ConcurrentBag<TcpClient>();
        private CancellationTokenSource? _cts;

        public bool IsRunning { get; private set; }
        public int Port => _port;
        public int ClientCount => _clients.Count;

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
        /// Start the host and begin accepting connections.
        /// </summary>
        public async Task StartAsync(CancellationToken token = default)
        {
            if (IsRunning) return;

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

        private async Task AcceptClientsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(token);
                    _clients.Add(client);
                    Console.WriteLine($"[NetworkHost] Client connected. Total clients: {_clients.Count}");

                    // Start handling this client
                    _ = Task.Run(async () => await HandleClientAsync(client, token), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NetworkHost] Error accepting client: {ex.Message}");
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
                        _cables.EnqueueIncoming(message);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[NetworkHost] Error deserializing message: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkHost] Client handler error: {ex.Message}");
            }
            finally
            {
                client.Close();
                Console.WriteLine($"[NetworkHost] Client disconnected. Total clients: {_clients.Count}");
            }
        }

        private void BroadcastMessage(MessageDTO message)
        {
            var serialized = AncientCompressor.Compress(message);
            var lengthBytes = BitConverter.GetBytes(serialized.Length);

            foreach (var client in _clients)
            {
                if (!client.Connected) continue;

                try
                {
                    var stream = client.GetStream();
                    stream.Write(lengthBytes, 0, 4);
                    stream.Write(serialized, 0, serialized.Length);
                    stream.Flush();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NetworkHost] Error sending to client: {ex.Message}");
                }
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;

            Console.WriteLine($"[NetworkHost] Stopping...");
            _cts?.Cancel();
            _listener?.Stop();

            foreach (var client in _clients)
            {
                client?.Close();
            }

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
