namespace V12.Core.Networking
{
    using System;
    using System.IO;
    using System.Text;
    using System.Collections.Generic;

    public static class SyncBatchBuilder
    {
        public static byte[] BuildBatch(IEnumerable<ISyncValue> items)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
            // write count placeholder
            var list = new List<ISyncValue>(items);
            w.Write(list.Count);
            foreach (var it in list)
            {
                var keyBytes = Encoding.UTF8.GetBytes(it.Key);
                w.Write(keyBytes.Length);
                w.Write(keyBytes);
                // include serialized payload (which already contains version)
                var payload = it.ToByteArray();
                w.Write(payload.Length);
                w.Write(payload);
            }
            w.Flush();
            return ms.ToArray();
        }

        public static IEnumerable<(string key, ReadOnlyMemory<byte> payload)> EnumerateBatch(ReadOnlyMemory<byte> batch)
        {
            // Convert to array early because iterator methods cannot capture spans.
            var arr = batch.ToArray();
            using var ms = new MemoryStream(arr);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);
            var count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                var klen = r.ReadInt32();
                var kbytes = r.ReadBytes(klen);
                var key = Encoding.UTF8.GetString(kbytes);
                var payloadLen = r.ReadInt32();
                var payload = r.ReadBytes(payloadLen);
                yield return (key, new ReadOnlyMemory<byte>(payload));
            }
        }
    }
}

