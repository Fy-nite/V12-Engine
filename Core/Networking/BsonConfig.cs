using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
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
                        type.Namespace?.StartsWith("V12") == true));

                // System.Uri is not handled by MongoDB out of the box.
                // Serialize it as its AbsoluteUri string so MessageDTO.Sender round-trips cleanly.
                BsonSerializer.RegisterSerializer(new UriAsBsonStringSerializer());

                // Register serializers for types that are not handled by default in MongoDB C# driver.
                // uint / uint[] are used by MeshComponent.CustomIndices.
                try { BsonSerializer.RegisterSerializer(typeof(uint), new UInt32Serializer(BsonType.Int64)); } catch { }
                try { BsonSerializer.RegisterSerializer(typeof(uint[]), new ArraySerializer<uint>()); } catch { }
                // Also register int[] and double[] explicitly for mesh/vertex data arrays.
                try { BsonSerializer.RegisterSerializer(typeof(int[]), new ArraySerializer<int>()); } catch { }
                try { BsonSerializer.RegisterSerializer(typeof(double[]), new ArraySerializer<double>()); } catch { }
                // Register common nullable types that components may carry.
                try { BsonSerializer.RegisterSerializer(typeof(long?), new NullableSerializer<long>()); } catch { }

                RegisterTransientMemberConventions();

                _initialized = true;
            }
        }

        /// <summary>
        /// Safety net for members that can never cross the BSON component path:
        /// delegates (closures, event handlers like ButtonComponent.OnPressed) and
        /// <see cref="ISyncValue"/> handles (SyncValue&lt;T&gt;). A convention makes
        /// them non-serializable so game code does not have to remember [BsonIgnore]
        /// on every handler or sync-value property.
        /// </summary>
        private static void RegisterTransientMemberConventions()
        {
            var pack = new ConventionPack
            {
                new IgnoreTransientMemberConvention()
            };

            ConventionRegistry.Register(
                "V12 transient members",
                pack,
                type => type.Namespace?.StartsWith("V12") == true);
        }

        private sealed class IgnoreTransientMemberConvention : IMemberMapConvention
        {
            public string Name => "V12: ignore transient members";

            public void Apply(BsonMemberMap memberMap)
            {
                var memberType = memberMap.MemberType;
                if (typeof(Delegate).IsAssignableFrom(memberType)
                    || typeof(ISyncValue).IsAssignableFrom(memberType))
                {
                    memberMap.SetShouldSerializeMethod(_ => false);
                }
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
