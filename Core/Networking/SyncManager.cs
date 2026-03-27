namespace V12.Core.Networking
{
    using System;
    using System.Collections.Generic;

    public static class SyncManager
    {
        // Hook to actually send bytes to remote peers. User of this library should set this.
        public static Action<byte[]> SendBytes { get; set; }

        // Build a batch from all dirty values and send it using SendBytes.
        public static void FlushDirty()
        {
            var dirty = new List<ISyncValue>();
            foreach (var v in SyncRegistry.DirtyValues()) dirty.Add(v);
            if (dirty.Count == 0) return;
            var batch = SyncBatchBuilder.BuildBatch(dirty);
            // mark cleared locally
            foreach (var v in dirty) v.ClearDirty();
            SendBytes?.Invoke(batch);
        }

        // Apply an incoming batch produced by BuildBatch
        public static void ApplyIncoming(ReadOnlyMemory<byte> batch, Uri sender = null)
        {
            foreach (var (key, payload) in SyncBatchBuilder.EnumerateBatch(batch))
            {
                if (SyncRegistry.TryGet(key, out var v))
                {
                    try { v.ApplyRemotePayload(payload.Span, sender); }
                    catch { }
                }
                else
                {
                    // unknown key: ignore or log
                }
            }
        }
    }
}

