namespace V12.Core.Networking
{
    using System;

    /// <summary>
    /// Compatibility facade for the process-wide sync channel. New code should
    /// use the <see cref="SyncChannel"/> owned by a <see cref="WorldSession"/> so
    /// keys are scoped per world.
    /// </summary>
    public static class SyncManager
    {
        /// <summary>The process-wide channel used by code that has no session.</summary>
        public static SyncChannel Default { get; } = new SyncChannel(null, SyncRegistry.Default);

        /// <summary>Hook to send bytes to remote peers for the default channel.</summary>
        public static Action<byte[]>? SendBytes
        {
            get => Default.SendBytes;
            set => Default.SendBytes = value;
        }

        public static void FlushDirty() => Default.FlushDirty();

        public static void ApplyIncoming(ReadOnlyMemory<byte> batch, Uri? sender = null)
            => Default.ApplyIncoming(batch, sender);
    }
}
