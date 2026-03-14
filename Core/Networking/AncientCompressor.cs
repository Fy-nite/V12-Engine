using System;
using System.Collections.Generic;
using System.Text;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
namespace V12.Core.Networking
{
    public static class AncientCompressor
    {
        public static Byte[] Compress(Object obj)
        {
            return obj.ToBson();
        }
        public static T Decompress<T>(Byte[] data)
        {
            return BsonSerializer.Deserialize<T>(data);
        }
    }
}
