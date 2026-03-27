namespace V12.Core.Networking
{
    using System;

    public interface ISyncValue
    {
        string Key { get; }
        Type ValueType { get; }
        bool IsDirty { get; }
        ulong Version { get; }

        // Serialize into byte[] containing version+payload
        byte[] ToByteArray();

        // Apply a remote payload (format produced by ToByteArray). Returns true if applied.
        bool ApplyRemotePayload(ReadOnlySpan<byte> payload, Uri sender = null);

        void ClearDirty();
    }
}

