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

        public bool IsConnected
        {
            get
            {
                try
                {
                    // _client may be null or may have been disposed concurrently; guard against exceptions
                    return _client != null && _client.Connected;
                }
                catch (Exception ex)
                {
                    try { Console.WriteLine($"[NetworkClient] IsConnected check failed: {ex.GetType().Name}: {ex.Message}"); } catch { }
                    return false;
                }
            }
        }
        public string Host => _host;
        public int Port => _port;

        // ── Connection lifecycle events ────────────────────────────────────────
        /// <summary>Raised when the TCP connection is successfully established.</summary>
        public event Action? OnConnected;
        /// <summary>Raised when the connection is closed (gracefully or by server).</summary>
        public event Action? OnDisconnected;
        /// <summary>Raised when an outbound connection attempt fails. Argument is the exception.</summary>
        public event Action<Exception>? OnConnectionFailed;

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
        /// Uses the host/port provided at construction time.
        /// Connection failures are surfaced via <see cref="OnConnectionFailed"/> instead of
        /// propagating as unobserved task exceptions.
        /// </summary>
        public async Task ConnectAsync(CancellationToken token = default)
            => await ConnectToAsync(_host, _port, token);

        /// <summary>
        /// Connect to a specific host/port and start sending/receiving messages.
        /// Connection failures are surfaced via <see cref="OnConnectionFailed"/> instead of
        /// propagating as unobserved task exceptions.
        /// </summary>
        public async Task ConnectAsync(string host, int port, CancellationToken token = default)
            => await ConnectToAsync(host, port, token);

        private async Task ConnectToAsync(string host, int port, CancellationToken token)
        {
            if (IsConnected) return;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _client = new TcpClient();

            Console.WriteLine($"[NetworkClient] Connecting to {host}:{port}...");
            try
            {
                var connectTask = _client.ConnectAsync(host, port);
                var cancelTask  = System.Threading.Tasks.Task.Delay(-1, _cts.Token);
                var finished    = await System.Threading.Tasks.Task.WhenAny(connectTask, cancelTask);

                if (finished != connectTask)
                {
                    Console.WriteLine($"[NetworkClient] Connection to {host}:{port} was cancelled.");
                    return;
                }

                await connectTask; // re-throws if the TCP connect failed

                Console.WriteLine($"[NetworkClient] Connected to {host}:{port}");
                _cables.OnMessageSending += SendMessage;
                OnConnected?.Invoke();

                _ = Task.Run(async () => await ReceiveLoopAsync(_cts.Token), _cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[NetworkClient] Connection to {host}:{port} cancelled.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkClient] Failed to connect to {host}:{port}: {ex.GetType().Name}: {ex.Message}");
                try { OnConnectionFailed?.Invoke(ex); } catch { }
            }
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
                        Console.WriteLine($"[NetworkClient] Incomplete message received ({totalRead}/{messageLength} bytes)");
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
            catch (OperationCanceledException) { /* Normal shutdown */ }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkClient] Receive error: {ex.Message}");
            }
            finally
            {
                Console.WriteLine($"[NetworkClient] Disconnected from {_host}:{_port}");
                _cables.OnMessageSending -= SendMessage;
                try { OnDisconnected?.Invoke(); } catch { }
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
            bool connected = false;
            try { connected = IsConnected; } catch { connected = false; }
            if (!connected)
            {
                // Ensure we still clean up resources even if not connected
                try { _cts?.Cancel(); } catch { }
                try { _cables.OnMessageSending -= SendMessage; } catch { }
                try { _client?.Close(); } catch { }
                return;
            }

            Console.WriteLine($"[NetworkClient] Disconnecting from {_host}:{_port}...");
            try { _cts?.Cancel(); } catch { }
            try { _cables.OnMessageSending -= SendMessage; } catch { }
            try { _client?.Close(); } catch { }
        }

        public void Dispose()
        {
            Disconnect();
            _cts?.Dispose();
            _client?.Dispose();
        }
    }
}
