namespace V12.Core.Networking
{
    using System;
    using V12.Core.NetworkCable;

    /// <summary>
    /// Wires one <see cref="SyncChannel"/> to a <see cref="NetworkCables"/>
    /// transport. The parameterless channel overload is the process-wide legacy
    /// path; session-aware code passes the channel owned by its world session.
    /// </summary>
    public class SyncNetworkBridge : IDisposable
    {
        private readonly NetworkCables _cables;
        private readonly SyncChannel _channel;
        private readonly Uri _senderUri;
        private readonly Action<byte[]> _send;
        private bool _disposed;

        public SyncNetworkBridge(NetworkCables? cables = null, string? senderUri = null)
            : this(SyncManager.Default, cables, senderUri)
        {
        }

        public SyncNetworkBridge(SyncChannel channel, NetworkCables? cables = null, string? senderUri = null)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _cables = cables ?? NetworkCables.Default;
            _senderUri = new Uri(senderUri ?? "networkcables://syncbridge");

            _send = bytes =>
            {
                if (bytes == null) return;
                var dto = new MessageDTO
                {
                    Sender = _senderUri,
                    MessageType = MessageType.SyncBatch,
                    Message = bytes
                };
                _cables.SendData(dto);
            };

            _channel.SendBytes = _send;
            _cables.OnMessageReceived += OnMessageReceived;
        }

        private void OnMessageReceived(MessageDTO message)
        {
            if (message == null || message.Message == null) return;

            // Only process SyncBatch messages. DirtyTracker owns the component
            // and element-level paths; this bridge is the value-level channel.
            if (message.MessageType != MessageType.SyncBatch) return;

            try
            {
                _channel.ApplyIncoming(new ReadOnlyMemory<byte>(message.Message), message.Sender);
            }
            catch (Exception)
            {
                // Never let a malformed batch take down the network thread.
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _cables.OnMessageReceived -= OnMessageReceived; } catch { }
            if (ReferenceEquals(_channel.SendBytes, _send))
                _channel.SendBytes = null;
        }
    }
}
