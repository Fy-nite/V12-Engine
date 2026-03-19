using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

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
                    snap.Elements.Add(new ElementSyncDTO { Name = el?.Name, Description = el?.Description });
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
                    world.Root.Add(new V12.Core.Element(es.Name, es.Description));
                return (T)(object)world;
            }

            return BsonSerializer.Deserialize<T>(data);
        }
    }
}
