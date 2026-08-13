using System;
using System.Linq;
using MongoDB.Bson;
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

                RegisterDelegateMemberMaps();

                _initialized = true;
            }
        }

        /// <summary>
        /// Safety net: delegate-typed members (closures, event handlers like
        /// ButtonComponent.OnPressed) can never cross the wire, so any V12 type that exposes
        /// one has it auto-ignored in its class map. Game code does not have to remember to
        /// slap [BsonIgnore] on a handler property to keep world sync from crashing.
        /// </summary>
        private static void RegisterDelegateMemberMaps()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var type in types)
                {
                    if (type.IsAbstract || type.IsInterface || type.IsEnum || type.IsGenericTypeDefinition) continue;
                    if (type.Namespace?.StartsWith("V12") != true) continue;
                    try
                    {
                        var classMap = new BsonClassMap(type);
                        classMap.AutoMap();
                        var delegateMembers = classMap.AllMemberMaps
                            .Where(m => typeof(Delegate).IsAssignableFrom(m.MemberType))
                            .ToList();
                        if (delegateMembers.Count == 0) continue;
                        foreach (var m in delegateMembers)
                            classMap.UnmapMember(m.MemberInfo);
                        if (classMap.AllMemberMaps.Count == 0) continue;
                        if (!BsonClassMap.IsClassMapRegistered(type))
                            BsonClassMap.RegisterClassMap(classMap);
                    }
                    catch { /* never let the safety net break startup */ }
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
