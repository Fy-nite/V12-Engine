namespace V12.Core.Networking;

using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

/// <summary>
/// A single replicated value with last-write-wins semantics.
/// <para>
/// The version is not a plain counter. It packs a hybrid logical clock in the
/// high 48 bits and the writer's 16-bit identity in the low bits:
/// <c>(clock &lt;&lt; 16) | writerId</c>. Comparing two versions as a
/// <see cref="ulong"/> is a total order over <c>(clock, writerId)</c>, so two
/// peers writing the same value always agree on a winner instead of rejecting
/// each other.
/// </para>
/// <para>
/// The clock is UTC milliseconds, bumped to stay strictly greater than the last
/// stamp on this value. That means a peer that has never seen the value can
/// still write it and win, which a pure Lamport counter does not allow. Clock
/// skew only affects writes made within the skew window; the writer id breaks
/// exact ties deterministically.
/// </para>
/// <para>
/// This is conflict resolution, not permission. Which peers may write a value
/// is a policy decision owned by <see cref="SyncChannel"/> and the session
/// authority.
/// </para>
/// </summary>
public class SyncValue<T> : ISyncValue
{
    /// <summary>Bits reserved for the writer id in a packed version.</summary>
    private const int WriterIdBits = 16;

    /// <summary>Mask for the clock part of a packed version.</summary>
    private const ulong ClockMask = (1UL << (64 - WriterIdBits)) - 1UL;

    // lightweight thread-safety
    private readonly object _lock = new object();
    private T _value;
    private ulong _clock;
    private ulong _lastVersion;
    private ushort _writerId;
    private bool _isDirty;

    public string Key { get; }
    // alias for external code that expects a "Path" property
    public string Path => Key;

    public Type ValueType => typeof(T);

    // Events
    public event Action<T, T> OnValueChanged; // (old, new)
    public event Action<SyncValue<T>> OnDirty; // fired when becomes dirty
    public event Action<T, ulong> OnRemoteApplied; // (value, version)

    public SyncValue(
        string key,
        T initial = default,
        bool markDirtyInitially = true,
        bool registerInGlobalRegistry = true)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        _value = initial;
        _clock = 0UL;
        _isDirty = markDirtyInitially;

        // Register in the process-wide registry for legacy discovery. Values
        // created by the field binder opt out: the session channel finds them
        // through the component instead, and a world can have thousands.
        if (registerInGlobalRegistry)
        {
            try { SyncRegistry.Default.Register(this); } catch { }
        }
    }

    /// <summary>
    /// Pack a clock stamp and writer id into the wire version. Public so tests
    /// and tooling can reason about versions without re-deriving the layout.
    /// </summary>
    public static ulong PackVersion(ulong clock, ushort writerId)
        => ((clock & ClockMask) << WriterIdBits) | writerId;

    /// <summary>Clock part of a packed version.</summary>
    public static ulong ClockOf(ulong version) => version >> WriterIdBits;

    /// <summary>Writer part of a packed version.</summary>
    public static ushort WriterOf(ulong version) => (ushort)(version & 0xFFFFUL);

    /// <summary>
    /// 16-bit writer identity. Assigned by the owning channel; only needs to be
    /// unique within one session. It is the tie-break, never a permission check.
    /// </summary>
    public ushort WriterId
    {
        get { lock (_lock) return _writerId; }
        set { lock (_lock) _writerId = value; }
    }

    public T Value
    {
        get
        {
            lock (_lock) return _value;
        }
    }

    /// <summary>
    /// Packed <c>(clock &lt;&lt; 16) | writerId</c> of the last accepted write,
    /// local or remote. A remote version is applied only when it is strictly
    /// greater, so a late duplicate cannot roll a value back. Read
    /// <see cref="ClockOf"/> / <see cref="WriterOf"/> to inspect it.
    /// </summary>
    public ulong Version
    {
        get
        {
            lock (_lock) return _lastVersion;
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
                _clock = NextClock(_clock);
                _lastVersion = PackVersion(_clock, _writerId);
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
    /// Hybrid logical clock tick: UTC milliseconds, forced forward so it is
    /// strictly greater than the previous stamp on this value even when the
    /// system clock steps backwards.
    /// </summary>
    private static ulong NextClock(ulong previous)
    {
        ulong now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        ulong next = now > previous ? now : previous + 1UL;
        return next & ClockMask;
    }

    /// <summary>
    /// Try to apply a remote update. Returns true if applied (remote version was newer and value changed).
    /// </summary>
    /// <remarks>
    /// Last-write-wins: the packed version orders writes by <c>(clock, writerId)</c>,
    /// so two peers that wrote in the same millisecond still agree on which one
    /// wins. The accepted clock is adopted so a local clock that is behind can
    /// never produce a stamp that looks older than an update already seen.
    /// </remarks>
    public bool TryApplyRemote(T newValue, ulong version, Uri sender = null)
    {
        T old;
        bool applied;
        lock (_lock)
        {
            if (version <= _lastVersion)
                return false;
            old = _value;
            _value = newValue;
            // Remember the exact accepted stamp: recomputing it from
            // (clock, ourWriterId) would make a duplicate from a higher writer
            // look newer again on the next comparison.
            _lastVersion = version;
            ulong remoteClock = ClockOf(version);
            if (remoteClock > _clock) _clock = remoteClock;
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

    /// <summary>
    /// Release the process-wide registry entry. A value that opted out of the
    /// global registry (the <c>[Sync]</c> field binder) is simply not present, so
    /// this is a no-op for it.
    /// </summary>
    public void Unregister() => SyncRegistry.Default.Unregister(this);

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
        writer.Write(Version);
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
        // Numeric arrays used to fall through to JSON, which expands them ~10x.
        // They are a fixed-width bulk payload, so length + raw values is correct
        // and much smaller (mesh buffers, curves, sampled data).
        else if (t == typeof(byte[]))
        {
            var a = (byte[])(object)value;
            if (a is null) { writer.Write(-1); return; }
            writer.Write(a.Length);
            writer.Write(a);
        }
        else if (t == typeof(int[]))
        {
            var a = (int[])(object)value;
            if (a is null) { writer.Write(-1); return; }
            writer.Write(a.Length);
            for (int i = 0; i < a.Length; i++) writer.Write(a[i]);
        }
        else if (t == typeof(uint[]))
        {
            var a = (uint[])(object)value;
            if (a is null) { writer.Write(-1); return; }
            writer.Write(a.Length);
            for (int i = 0; i < a.Length; i++) writer.Write(a[i]);
        }
        else if (t == typeof(float[]))
        {
            var a = (float[])(object)value;
            if (a is null) { writer.Write(-1); return; }
            writer.Write(a.Length);
            for (int i = 0; i < a.Length; i++) writer.Write(a[i]);
        }
        else if (t == typeof(double[]))
        {
            var a = (double[])(object)value;
            if (a is null) { writer.Write(-1); return; }
            writer.Write(a.Length);
            for (int i = 0; i < a.Length; i++) writer.Write(a[i]);
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

        if (t == typeof(byte[]))
        {
            var len = reader.ReadInt32();
            if (len < 0) return default;
            return (T)(object)reader.ReadBytes(len);
        }
        if (t == typeof(int[]))
        {
            var len = reader.ReadInt32();
            if (len < 0) return default;
            var a = new int[len];
            for (int i = 0; i < len; i++) a[i] = reader.ReadInt32();
            return (T)(object)a;
        }
        if (t == typeof(uint[]))
        {
            var len = reader.ReadInt32();
            if (len < 0) return default;
            var a = new uint[len];
            for (int i = 0; i < len; i++) a[i] = reader.ReadUInt32();
            return (T)(object)a;
        }
        if (t == typeof(float[]))
        {
            var len = reader.ReadInt32();
            if (len < 0) return default;
            var a = new float[len];
            for (int i = 0; i < len; i++) a[i] = reader.ReadSingle();
            return (T)(object)a;
        }
        if (t == typeof(double[]))
        {
            var len = reader.ReadInt32();
            if (len < 0) return default;
            var a = new double[len];
            for (int i = 0; i < len; i++) a[i] = reader.ReadDouble();
            return (T)(object)a;
        }

        // fallback JSON
        var bLen = reader.ReadInt32();
        var b = reader.ReadBytes(bLen);
        return JsonSerializer.Deserialize<T>(b);
    }
}