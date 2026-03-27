namespace V12.Core.Networking
{
    using System.IO;

    public interface ISyncSerializer
    {
        // Non-generic base for registry lookups
        byte TypeId { get; }
        void Write(object value, BinaryWriter writer);
        object Read(BinaryReader reader);
    }

    public interface ISyncSerializer<T> : ISyncSerializer
    {
        new void Write(object value, BinaryWriter writer);
        new T Read(BinaryReader reader);
    }
}

