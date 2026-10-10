using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using V12.Core.Networking;

namespace V12.Core.NetworkCable
{
    /// <summary>
    /// LiteNetLib (UDP)-based network client that connects to a NetworkHost and routes
    /// messages through NetworkCables. Lifecycle messages travel on a reliable, ordered
    /// channel; continuous state updates travel on an unreliable channel. Dead
    /// connections are detected by LiteNetLib's DisconnectTimeout instead of an
    /// application-level heartbeat.
    /// </summary>
    public class NetworkClient : IDisposable, INetEventListener
    {
        private readonly string _host;
        private readonly int _port;
        private readonly NetworkCables _cables;
        private NetManager? _net;
        private NetPeer? _peer;
        private CancellationTokenSource? _cts;
        private readonly object _pollLock = new object();
        private bool _pollStarted;
        private bool _everConnected;

        public bool IsConnected
        {
            get
            {
                try
                {
                    // _peer may be null or may have been disposed concurrently; guard against exceptions
                    return _peer != null && _peer.ConnectionState == ConnectionState.Connected;
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
        /// <summary>Raised when the connection is successfully established.</summary>
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
            // Defend against hosts that already carry ":port" and/or trailing
            // dots (env vars / CLI typos like "localhost:7777."): without this
            // the endpoint renders doubled ("localhost:7777.:7777") and DNS
            // fails instantly on every attempt.
            _host = NormalizeHost(host);
            _port = port;
            _cables = cables ?? NetworkCables.Default;
        }

        private static string NormalizeHost(string host)
        {
            var h = (host ?? string.Empty).Trim().TrimEnd('.');
            // Strip an embedded ":port" suffix, keeping the explicit port arg.
            // Bracketed IPv6 ("[::1]:7777") and bare multi-colon hosts are left alone.
            if (!h.StartsWith("[", StringComparison.Ordinal) && h.IndexOf(':') == h.LastIndexOf(':'))
            {
                int sep = h.LastIndexOf(':');
                if (sep > 0 && int.TryParse(h.Substring(sep + 1).TrimEnd('.'), out _))
                    h = h.Substring(0, sep).Trim().TrimEnd('.');
            }
            return h;
        }

        /// <summary>
        /// Connect to the host and start sending/receiving messages.
        /// Uses the host/port provided at construction time.
        /// Connection failures are surfaced via <see cref="OnConnectionFailed"/> instead of
        /// propagating as unobserved task exceptions.
        /// </summary>
        public Task ConnectAsync(CancellationToken token = default)
            => ConnectToAsync(_host, _port, token);

        /// <summary>
        /// Connect to a specific host/port and start sending/receiving messages.
        /// Connection failures are surfaced via <see cref="OnConnectionFailed"/> instead of
        /// propagating as unobserved task exceptions.
        /// </summary>
        public Task ConnectAsync(string host, int port, CancellationToken token = default)
            => ConnectToAsync(host, port, token);

        private Task ConnectToAsync(string host, int port, CancellationToken token)
        {
            if (IsConnected) return Task.CompletedTask;

            // Tear down any previous attempt cleanly so each retry starts fresh.
            try { Disconnect(); } catch { }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _everConnected = false;

            var net = new NetManager(this)
            {
                DisconnectTimeout = 15000,
                MaxConnectAttempts = 5
            };
            net.Start();
            _net = net;

            Console.WriteLine($"[NetworkClient] Connecting (UDP/LiteNetLib) to {host}:{port}...");
            try
            {
                _peer = net.Connect(host, port, LiteNetWire.ConnectKey);
            }
            catch (Exception ex)
            {
                _peer = null;
                Console.WriteLine($"[NetworkClient] Failed to connect to {host}:{port}: {ex.GetType().Name}: {ex.Message}");
                try { OnConnectionFailed?.Invoke(ex); } catch { }
                return Task.CompletedTask;
            }

            _cables.OnMessageSending += SendMessage;
            StartPolling();
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
                            Console.WriteLine($"[NetworkClient] PollEvents error: {ex.GetType().Name}: {ex.Message}");
                        }
                        Thread.Sleep(5);
                    }
                });
            }
        }

        private void SendMessage(MessageDTO message)
        {
            var peer = _peer;
            if (_net == null || peer == null || peer.ConnectionState != ConnectionState.Connected) return;

            try
            {
                var bytes = LiteNetWire.Serialize(message);
                var (method, channel) = LiteNetWire.DeliveryFor(message, bytes.Length);
                peer.Send(bytes, channel, method);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkClient] Error sending message: {ex.Message}");
            }
        }

        // ── INetEventListener ──────────────────────────────────────────────────
        public void OnPeerConnected(NetPeer peer)
        {
            _peer = peer;
            _everConnected = true;
            Console.WriteLine($"[NetworkClient] Connected to {peer.EndPoint}");
            try { OnConnected?.Invoke(); } catch { }
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            Console.WriteLine($"[NetworkClient] Disconnected from {_host}:{_port} (reason: {disconnectInfo.Reason})");
            _peer = null;
            _cables.OnMessageSending -= SendMessage;

            if (!_everConnected)
            {
                try { OnConnectionFailed?.Invoke(new Exception($"Connection failed: {disconnectInfo.Reason}")); } catch { }
            }
            else
            {
                try { OnDisconnected?.Invoke(); } catch { }
            }
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            Console.WriteLine($"[NetworkClient] Network error on {endPoint}: {socketError}");
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            try
            {
                var message = LiteNetWire.Deserialize(reader.GetRemainingBytes());
                if (message != null)
                    _cables.EnqueueIncoming(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkClient] Error deserializing message: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                reader.Recycle();
            }
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }

        public void OnConnectionRequest(ConnectionRequest request) { }

        public void Disconnect()
        {
            Console.WriteLine($"[NetworkClient] Disconnecting from {_host}:{_port}...");
            try { _cts?.Cancel(); } catch { }
            try { _cables.OnMessageSending -= SendMessage; } catch { }
            try { _net?.Stop(); } catch { }
            _peer = null;
        }

        public void Dispose()
        {
            Disconnect();
            _cts?.Dispose();
            _cts = null;
            _net = null;
        }
    }
}
