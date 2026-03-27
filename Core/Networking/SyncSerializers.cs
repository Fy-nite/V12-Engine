namespace V12.Core.Networking
{
    using System;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Text.Json;

    // Minimal serializer registry for SyncValue system (extendable).
    public static class SyncSerializers
    {
        private static readonly ConcurrentDictionary<Type, ISyncSerializer> _map = new ConcurrentDictionary<Type, ISyncSerializer>();

        static SyncSerializers()
        {
            // register primitive serializers with small TypeId values
            Register<int>(new PrimitiveSerializer<int>(1, (w, v) => w.Write(v), r => r.ReadInt32()));
            Register<long>(new PrimitiveSerializer<long>(2, (w, v) => w.Write(v), r => r.ReadInt64()));
            Register<float>(new PrimitiveSerializer<float>(3, (w, v) => w.Write(v), r => r.ReadSingle()));
            Register<double>(new PrimitiveSerializer<double>(4, (w, v) => w.Write(v), r => r.ReadDouble()));
            Register<bool>(new PrimitiveSerializer<bool>(5, (w, v) => w.Write(v), r => r.ReadBoolean()));
            Register<string>(new StringSerializer(10));
        }

        public static void Register<T>(ISyncSerializer<T> serializer)
        {
            _map[typeof(T)] = serializer;
        }

        public static ISyncSerializer? Get(Type t)
        {
            if (_map.TryGetValue(t, out var s)) return s;
            return null;
        }

        public static ISyncSerializer<T>? Get<T>()
        {
            if (_map.TryGetValue(typeof(T), out var s)) return s as ISyncSerializer<T>;
            return null;
        }

        private class PrimitiveSerializer<T> : ISyncSerializer<T>
        {
            private readonly Action<BinaryWriter, T> _writer;
            private readonly Func<BinaryReader, T> _reader;
            public byte TypeId { get; }

            public PrimitiveSerializer(byte id, Action<BinaryWriter, T> writer, Func<BinaryReader, T> reader)
            {
                TypeId = id;
                _writer = writer;
                _reader = reader;
            }

            public void Write(object value, BinaryWriter writer) => _writer(writer, (T)value);
            object ISyncSerializer.Read(BinaryReader reader) => _reader(reader);
            public T Read(BinaryReader reader) => _reader(reader);
        }

        private class StringSerializer : ISyncSerializer<string>
        {
            public byte TypeId { get; }
            public StringSerializer(byte id) { TypeId = id; }
            public void Write(object value, BinaryWriter writer)
            {
                var s = (string)value;
                if (s is null) writer.Write(-1);
                else
                {
                    var b = System.Text.Encoding.UTF8.GetBytes(s);
                    writer.Write(b.Length);
                    writer.Write(b);
                }
            }

            public string Read(BinaryReader reader)
            {
                var len = reader.ReadInt32();
                if (len < 0) return null;
                var b = reader.ReadBytes(len);
                return System.Text.Encoding.UTF8.GetString(b);
            }

            object ISyncSerializer.Read(BinaryReader reader) => Read(reader);
        }

        // JSON fallback used by caller when no serializer registered
        public static void WriteUsingJson(object value, BinaryWriter writer)
        {
            var b = JsonSerializer.SerializeToUtf8Bytes(value);
            writer.Write(b.Length);
            writer.Write(b);
        }

        public static object ReadUsingJson(Type t, BinaryReader reader)
        {
            var len = reader.ReadInt32();
            var b = reader.ReadBytes(len);
            return JsonSerializer.Deserialize(b, t);
        }
    }
}
