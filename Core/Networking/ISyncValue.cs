namespace V12.Core.Networking
{
    using System;

    public interface ISyncValue
    {
        string Key { get; }
        Type ValueType { get; }
        bool IsDirty { get; }
        ulong Version { get; }

        /// <summary>
        /// 16-bit writer identity stamped into <see cref="Version"/>. Set by the
        /// owning <see cref="SyncChannel"/>; it is the tie-break that makes
        /// last-write-wins converge when two peers write the same value.
        /// </summary>
        ushort WriterId { get; set; }

        // Serialize into byte[] containing version+payload
        byte[] ToByteArray();

        // Apply a remote payload (format produced by ToByteArray). Returns true if applied.
        bool ApplyRemotePayload(ReadOnlySpan<byte> payload, Uri sender = null);

        void ClearDirty();

        /// <summary>
        /// Mark the value for re-send without changing it. Used for the
        /// full-state keyframe a client receives on connect, which covers values
        /// that are not part of the component snapshot (for example a
        /// <c>[Sync][BsonIgnore]</c> field).
        /// </summary>
        void MarkDirty() { }

        /// <summary>
        /// Drop the process-wide registry entry for this value. Called when its
        /// owning component is detached or destroyed; a session channel cleans its
        /// own scoped keys separately. Safe to call more than once.
        /// </summary>
        void Unregister() { }
    }
}

