using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using V12.Core.Networking;

namespace V12.Core.NetworkCable
{
    /// <summary>
    /// TCP-based network client that connects to a NetworkHost and routes messages through NetworkCables.
    /// </summary>
    public class NetworkClient : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private readonly NetworkCables _cables;
        private TcpClient? _client;
        private CancellationTokenSource? _cts;

        public bool IsConnected => _client?.Connected ?? false;
        public string Host => _host;
        public int Port => _port;

        /// <summary>
        /// Create a new network client.
        /// </summary>
        /// <param name="host">Host address to connect to.</param>
        /// <param name="port">Port to connect to.</param>
        /// <param name="cables">NetworkCables instance to use. If null, uses NetworkCables.Default.</param>
        public NetworkClient(string host, int port, NetworkCables? cables = null)
        {
            _host = host;
            _port = port;
            _cables = cables ?? NetworkCables.Default;
        }

        /// <summary>
        /// Connect to the host and start sending/receiving messages.
        /// </summary>
        public async Task ConnectAsync(CancellationToken token = default)
        {
            if (IsConnected) return;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _client = new TcpClient();

            Console.WriteLine($"[NetworkClient] Connecting to {_host}:{_port}...");
            await _client.ConnectAsync(_host, _port, _cts.Token);
            Console.WriteLine($"[NetworkClient] Connected to {_host}:{_port}");

            // Subscribe to outgoing messages
            _cables.OnMessageSending += SendMessage;

            // Start receive loop
            _ = Task.Run(async () => await ReceiveLoopAsync(_cts.Token), _cts.Token);
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            if (_client == null) return;

            try
            {
                using var stream = _client.GetStream();

                while (!token.IsCancellationRequested && _client.Connected)
                {
                    // Read length prefix (4 bytes)
                    var lengthBytes = new byte[4];
                    int bytesRead = await stream.ReadAsync(lengthBytes, 0, 4, token);
                    if (bytesRead == 0) break;

                    int messageLength = BitConverter.ToInt32(lengthBytes, 0);
                    if (messageLength <= 0 || messageLength > 10_000_000) // 10MB sanity check
                    {
                        Console.WriteLine($"[NetworkClient] Invalid message length: {messageLength}");
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
                        Console.WriteLine($"[NetworkClient] Incomplete message received");
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
                        Console.WriteLine($"[NetworkClient] Error deserializing message: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkClient] Receive error: {ex.Message}");
            }
            finally
            {
                Console.WriteLine($"[NetworkClient] Disconnected from {_host}:{_port}");
            }
        }

        private void SendMessage(MessageDTO message)
        {
            if (_client == null || !_client.Connected) return;

            try
            {
                var serialized = AncientCompressor.Compress(message);
                var lengthBytes = BitConverter.GetBytes(serialized.Length);

                var stream = _client.GetStream();
                stream.Write(lengthBytes, 0, 4);
                stream.Write(serialized, 0, serialized.Length);
                stream.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkClient] Error sending message: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            if (!IsConnected) return;

            Console.WriteLine($"[NetworkClient] Disconnecting from {_host}:{_port}...");
            _cts?.Cancel();
            _cables.OnMessageSending -= SendMessage;
            _client?.Close();
            Console.WriteLine($"[NetworkClient] Disconnected.");
        }

        public void Dispose()
        {
            Disconnect();
            _cts?.Dispose();
            _client?.Dispose();
        }
    }
}
