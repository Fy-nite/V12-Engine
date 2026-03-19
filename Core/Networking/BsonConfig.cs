using System;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace V12.Core.Networking
{
    /// <summary>
    /// One-time BSON serializer registration for the V12 networking layer.
    /// Call <see cref="Initialize"/> at application startup before any BSON operation.
    /// </summary>
    public static class BsonConfig
    {
        private static volatile bool _initialized;
        private static readonly object _lock = new();

        public static void Initialize()
        {
            if (_initialized) return;
            lock (_lock)
            {
                if (_initialized) return;

                // Allow every type whose namespace starts with "V12." to be serialized through
                // ObjectSerializer. MongoDB 3.x+ restricts which types the ObjectSerializer
                // will handle by default; without this registration V12 types throw
                // BsonSerializationException at runtime.
                BsonSerializer.RegisterSerializer(
                    new ObjectSerializer(type =>
                        ObjectSerializer.DefaultAllowedTypes(type) ||
                        type.Namespace?.StartsWith("V12.") == true));

                // System.Uri is not handled by MongoDB out of the box.
                // Serialize it as its AbsoluteUri string so MessageDTO.Sender round-trips cleanly.
                BsonSerializer.RegisterSerializer(new UriAsBsonStringSerializer());

                _initialized = true;
            }
        }
    }

    internal sealed class UriAsBsonStringSerializer : SerializerBase<Uri>
    {
        public override void Serialize(BsonSerializationContext ctx, BsonSerializationArgs args, Uri value)
            => ctx.Writer.WriteString(value?.AbsoluteUri ?? string.Empty);

        public override Uri Deserialize(BsonDeserializationContext ctx, BsonDeserializationArgs args)
        {
            var s = ctx.Reader.ReadString();
            return string.IsNullOrEmpty(s) ? new Uri("networkcables://unknown") : new Uri(s);
        }
    }
}
