namespace V12.Core.Networking
{
    using System;
    using V12.Core.NetworkCable;

    /// <summary>
    /// Bridge that wires the SyncManager send/receive pipeline to a NetworkCables transport.
    /// Sets SyncManager.SendBytes to emit a MessageDTO on the provided NetworkCables instance
    /// and listens for incoming WorldSync/WorldUpdate messages to apply via SyncManager.ApplyIncoming.
    /// </summary>
    public class SyncNetworkBridge : IDisposable
    {
        private readonly NetworkCables _cables;
        private readonly Uri _senderUri;

        public SyncNetworkBridge(NetworkCables? cables = null, string? senderUri = null)
        {
            _cables = cables ?? NetworkCables.Default;
            _senderUri = new Uri(senderUri ?? "networkcables://syncbridge");

            // Hook send path: when SyncManager.FlushDirty calls SendBytes it will enqueue a MessageDTO
            SyncManager.SendBytes = (bytes) =>
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

            // Listen for incoming messages from the transport and apply sync batches
            _cables.OnMessageReceived += OnMessageReceived;
        }

        private void OnMessageReceived(MessageDTO message)
        {
            if (message == null || message.Message == null) return;

            // Only process SyncBatch messages (dedicated MessageType for SyncManager value-level sync).
            // This avoids conflicting with DirtyTracker's WorldUpdate/WorldSync (component-level BSON sync).
            if (message.MessageType == MessageType.SyncBatch)
            {
                try
                {
                    SyncManager.ApplyIncoming(new ReadOnlyMemory<byte>(message.Message), message.Sender);
                }
                catch (Exception)
                {
                    // swallow to avoid crashing network thread
                }
            }
        }

        public void Dispose()
        {
            try { _cables.OnMessageReceived -= OnMessageReceived; } catch { }
            // Clear the send hook if it points to this bridge
            try
            {
                if (SyncManager.SendBytes != null)
                    SyncManager.SendBytes = null;
            }
            catch { }
        }
    }
}

