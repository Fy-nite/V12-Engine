namespace V12.Core.Networking;

using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

public class SyncValue<T> : ISyncValue
{
    // lightweight thread-safety
    private readonly object _lock = new object();
    private T _value;
    private ulong _version;
    private bool _isDirty;

    public string Key { get; }
    // alias for external code that expects a "Path" property
    public string Path => Key;

    public Type ValueType => typeof(T);

    // Events
    public event Action<T, T> OnValueChanged; // (old, new)
    public event Action<SyncValue<T>> OnDirty; // fired when becomes dirty
    public event Action<T, ulong> OnRemoteApplied; // (value, version)

    public SyncValue(string key, T initial = default, bool markDirtyInitially = true)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        _value = initial;
        _version = 0ul;
        _isDirty = markDirtyInitially;
        // register in global registry for discovery
        try { SyncRegistry.Register(this); } catch { }
    }

    public T Value
    {
        get
        {
            lock (_lock) return _value;
        }
    }

    public ulong Version
    {
        get
        {
            lock (_lock) return _version;
        }
    }

    public bool IsDirty
    {
        get
        {
            lock (_lock) return _isDirty;
        }
    }

    public void Set(T newValue)
    {
        T old;
        bool changed = false;
        lock (_lock)
        {
            old = _value;
            if (!EqualityComparer<T>.Default.Equals(old, newValue))
            {
                _value = newValue;
                // increment version (wrap is fine)
                unchecked { _version++; }
                _isDirty = true;
                changed = true;
            }
        }

        if (changed)
        {
            try { OnValueChanged?.Invoke(old, newValue); } catch { }
            try { OnDirty?.Invoke(this); } catch { }
        }
    }

    /// <summary>
    /// Try to apply a remote update. Returns true if applied (remote version was newer and value changed).
    /// </summary>
    public bool TryApplyRemote(T newValue, ulong version, Uri sender = null)
    {
        T old;
        bool applied;
        lock (_lock)
        {
            if (version <= _version)
                return false;
            old = _value;
            _value = newValue;
            _version = version;
            _isDirty = false; // remote authoritative application clears dirty
            applied = !EqualityComparer<T>.Default.Equals(old, newValue);
        }
        if (applied)
        {
            try { OnValueChanged?.Invoke(old, newValue); } catch (Exception) { }
        }

        try { OnRemoteApplied?.Invoke(newValue, version); } catch (Exception) { }
        return true;
    }

    public void MarkDirty()
    {
        bool became = false;
        lock (_lock)
        {
            if (!_isDirty)
            {
                _isDirty = true;
                became = true;
            }
        }

        if (became) try { OnDirty?.Invoke(this); } catch (Exception) { }
    }

    public void ClearDirty()
    {
        lock (_lock) { _isDirty = false; }
    }

    // Serialize (version + payload) into a byte[] suitable for batching.
    public byte[] ToByteArray()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);
        // write version
        writer.Write(_version);
        WriteValue(writer, _value);
        writer.Flush();
        return ms.ToArray();
    }

    // ISyncValue implementation: apply a payload produced by ToByteArray
    public bool ApplyRemotePayload(ReadOnlySpan<byte> payload, Uri sender = null)
    {
        if (!TryDeserialize(payload, out var val, out var ver)) return false;
        return TryApplyRemote(val, ver, sender);
    }

    // Try to deserialize a payload produced by ToByteArray. Returns true on success.
    public static bool TryDeserialize(ReadOnlySpan<byte> payload, out T value, out ulong version)
    {
        value = default;
        version = 0ul;
        try
        {
            using var ms = new MemoryStream(payload.ToArray());
            using var reader = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            version = reader.ReadUInt64();
            value = ReadValue(reader);
            return true;
        }
        catch
        {
            value = default;
            version = 0ul;
            return false;
        }
    }

    private static void WriteValue(BinaryWriter writer, T value)
    {
        var t = typeof(T);
        if (t == typeof(int)) writer.Write((int)(object)value);
        else if (t == typeof(long)) writer.Write((long)(object)value);
        else if (t == typeof(uint)) writer.Write((uint)(object)value);
        else if (t == typeof(short)) writer.Write((short)(object)value);
        else if (t == typeof(byte)) writer.Write((byte)(object)value);
        else if (t == typeof(bool)) writer.Write((bool)(object)value);
        else if (t == typeof(float)) writer.Write((float)(object)value);
        else if (t == typeof(double)) writer.Write((double)(object)value);
        else if (t == typeof(string))
        {
            var s = (string)(object)value;
            if (s is null)
            {
                writer.Write(-1);
            }
            else
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(s);
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
        }
        else
        {
            // fallback to JSON for arbitrary types (MVP)
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
    }

    private static T ReadValue(BinaryReader reader)
    {
        var t = typeof(T);
        if (t == typeof(int)) return (T)(object)reader.ReadInt32();
        if (t == typeof(long)) return (T)(object)reader.ReadInt64();
        if (t == typeof(uint)) return (T)(object)reader.ReadUInt32();
        if (t == typeof(short)) return (T)(object)reader.ReadInt16();
        if (t == typeof(byte)) return (T)(object)reader.ReadByte();
        if (t == typeof(bool)) return (T)(object)reader.ReadBoolean();
        if (t == typeof(float)) return (T)(object)reader.ReadSingle();
        if (t == typeof(double)) return (T)(object)reader.ReadDouble();
        if (t == typeof(string))
        {
            var len = reader.ReadInt32();
            if (len < 0) return default;
            var bytes = reader.ReadBytes(len);
            return (T)(object)System.Text.Encoding.UTF8.GetString(bytes);
        }

        // fallback JSON
        var bLen = reader.ReadInt32();
        var b = reader.ReadBytes(bLen);
        return JsonSerializer.Deserialize<T>(b);
    }
}