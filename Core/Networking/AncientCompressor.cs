using System;
using System.Linq;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using V12.Core.Core.Interfaces;

namespace V12.Core.Networking
{
    public static class AncientCompressor
    {
        public static byte[] Compress(object obj)
        {
            // World uses a flat snapshot DTO because its Root is List<IWorldElement>
            // (interface-typed), which BSON cannot serialize without discriminator class maps.
            if (obj is V12.Core.World w)
            {
                var snap = new WorldSyncDTO { WorldName = w.WorldName };
                foreach (var el in w.Root)
                {
                    var esDto = new ElementSyncDTO { Name = el?.Name, Description = el?.Description };
                    if (el?.Components != null)
                    {
                        foreach (var comp in el.Components)
                        {
                            try
                            {
                                var csDto = CompressComponent(comp);
                                if (csDto != null) esDto.Components.Add(csDto);
                            }
                            catch { /* skip components that can't be serialized */ }
                        }
                    }
                    snap.Elements.Add(esDto);
                }
                return snap.ToBson();
            }

            return obj.ToBson();
        }

        public static T Decompress<T>(byte[] data)
        {
            if (typeof(T) == typeof(V12.Core.World))
            {
                var snap = BsonSerializer.Deserialize<WorldSyncDTO>(data);
                var world = new V12.Core.World(snap.WorldName ?? "World");
                foreach (var es in snap.Elements)
                {
                    var element = new V12.Core.Element(es.Name, es.Description);
                    foreach (var cs in es.Components)
                    {
                        try
                        {
                            var comp = DecompressComponent(cs);
                            if (comp != null) element.Components.Add(comp);
                        }
                        catch { /* skip components that can't be deserialized */ }
                    }
                    world.Root.Add(element);
                }
                return (T)(object)world;
            }

            return BsonSerializer.Deserialize<T>(data);
        }

        // ── Component serialization helpers ───────────────────────────────────

        /// <summary>
        /// Serialize a single component to a <see cref="ComponentSyncDTO"/> suitable for wire transport.
        /// Returns null if the component cannot be serialized.
        /// </summary>
        public static ComponentSyncDTO? CompressComponent(IComponent comp)
        {
            if (comp == null) return null;
            var type = comp.GetType();
            // Use a short name that still uniquely identifies the type across matching assemblies:
            // "FullTypeName, AssemblyShortName" (no version/culture so it round-trips cleanly).
            var typeName = $"{type.FullName}, {type.Assembly.GetName().Name}";
            var data = ((object)comp).ToBson(type);
            return new ComponentSyncDTO { TypeName = typeName, Data = data };
        }

        /// <summary>
        /// Reconstruct a component from a <see cref="ComponentSyncDTO"/> received over the wire.
        /// Returns null if the type cannot be resolved or the data cannot be deserialized.
        /// </summary>
        public static IComponent? DecompressComponent(ComponentSyncDTO dto)
        {
            if (dto == null || string.IsNullOrEmpty(dto.TypeName) || dto.Data == null) return null;

            // Resolve type – try exact name then short name fallback.
            var type = Type.GetType(dto.TypeName)
                    ?? AppDomain.CurrentDomain.GetAssemblies()
                           .Select(a => { try { return a.GetType(dto.TypeName.Split(',')[0].Trim()); } catch { return null; } })
                           .FirstOrDefault(t => t != null);

            if (type == null) return null;

            // Deserialize via BsonDocument so we don't need a generic type parameter at compile time.
            var doc = BsonSerializer.Deserialize<BsonDocument>(dto.Data);
            var serializer = BsonSerializer.LookupSerializer(type);
            using var reader = new BsonDocumentReader(doc);
            var ctx  = BsonDeserializationContext.CreateRoot(reader);
            var args = new BsonDeserializationArgs { NominalType = type };
            return serializer.Deserialize(ctx, args) as IComponent;
        }
    }
}
