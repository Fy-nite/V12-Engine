using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using V12.Core.Core.Interfaces;

namespace V12.Core.Networking
{
    /// <summary>
    /// One replication channel for one world session. It owns a private
    /// <see cref="SyncRegistry"/> and scopes every <see cref="ISyncValue"/> key by
    /// world, element, and component so two sessions cannot collide.
    /// </summary>
    public sealed class SyncChannel : IDisposable
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, Registration> _registered = new(StringComparer.Ordinal);
        private readonly bool _clearRegistryOnDispose;
        private bool _refreshPending;
        private DateTime _lastRefreshUtc = DateTime.MinValue;
        private bool _disposed;

        private readonly struct Registration
        {
            public readonly ISyncValue Inner;
            public readonly ISyncValue Wrapped;

            public Registration(ISyncValue inner, ISyncValue wrapped)
            {
                Inner = inner;
                Wrapped = wrapped;
            }
        }

        /// <summary>World this channel scopes keys for. Null for the process-wide default channel.</summary>
        public World? World { get; }

        /// <summary>The registry this channel reads and writes.</summary>
        public SyncRegistry Registry { get; }

        /// <summary>Sends a serialized batch. A <see cref="SyncNetworkBridge"/> sets this.</summary>
        public Action<byte[]>? SendBytes { get; set; }

        public bool HasSender => SendBytes != null;

        private ushort _writerId = NewWriterId();

        /// <summary>
        /// 16-bit identity stamped into every value this channel sends. It only
        /// needs to be unique inside one session; it is the last-write-wins
        /// tie-break, not a permission check. The host overrides it with its
        /// server-assigned player id so host writes have a stable, known stamp.
        /// </summary>
        public ushort WriterId
        {
            get => _writerId;
            set
            {
                _writerId = value;
                lock (_gate)
                {
                    foreach (var registration in _registered.Values)
                        registration.Inner.WriterId = value;
                }
            }
        }

        private static ushort NewWriterId()
        {
            // Non-zero so an unconfigured peer never looks like "no writer".
            ushort id = (ushort)Random.Shared.Next(1, ushort.MaxValue + 1);
            return id == 0 ? (ushort)1 : id;
        }

        /// <summary>How often to rescan the world for newly attached SyncValues.</summary>
        public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMilliseconds(250);

        public SyncChannel(World? world = null, SyncRegistry? registry = null, bool subscribeToWorld = true)
        {
            World = world;
            _clearRegistryOnDispose = world != null;
            Registry = registry ?? (world == null ? SyncRegistry.Default : new SyncRegistry());

            if (world != null && subscribeToWorld)
            {
                world.ElementAdded += OnWorldChanged;
                world.ElementRemoved += OnWorldChanged;
                Refresh();
            }
        }

        /// <summary>Re-scan the world now and register every SyncValue it contains.</summary>
        public void Refresh()
        {
            if (World == null)
            {
                _refreshPending = false;
                _lastRefreshUtc = DateTime.UtcNow;
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);

            World.Lock.EnterReadLock();
            try
            {
                foreach (var root in World.Root.ToArray())
                    RegisterElement(root, seen);
            }
            finally
            {
                World.Lock.ExitReadLock();
            }

            lock (_gate)
            {
                foreach (var stale in _registered.Keys.Where(k => !seen.Contains(k)).ToArray())
                {
                    Registry.Unregister(stale);
                    _registered.Remove(stale);
                }
            }

            _refreshPending = false;
            _lastRefreshUtc = DateTime.UtcNow;
        }

        /// <summary>Flush dirty values through <see cref="SendBytes"/>.</summary>
        public void FlushDirty()
        {
            ThrowIfDisposed();
            RefreshIfDue();

            var dirty = Registry.DirtyValues().ToList();
            if (dirty.Count == 0) return;

            // No transport yet: keep the dirty values so the first real sender
            // does not silently lose the initial snapshot.
            if (SendBytes == null) return;

            // Stamp our identity before serializing. Values registered outside a
            // world (the process-wide legacy registry) never pass through
            // RegisterKey, so this is their only chance to get a writer id.
            foreach (var value in dirty)
                value.WriterId = _writerId;

            var batch = SyncBatchBuilder.BuildBatch(dirty);
            foreach (var value in dirty)
                value.ClearDirty();

            SendBytes?.Invoke(batch);
        }

        /// <summary>Refresh if needed, then flush. Safe to call once per tick.</summary>
        public void Pump()
        {
            ThrowIfDisposed();
            FlushDirty();
        }

        /// <summary>
        /// Mark every registered value dirty so the next flush is a full keyframe.
        /// Called when a client joins: the component snapshot carries most state,
        /// but values that are not in the snapshot (<c>[Sync][BsonIgnore]</c>, or
        /// an explicit <see cref="SyncValue{T}"/> the snapshot never sees) still
        /// need one send.
        /// </summary>
        public void MarkAllDirty()
        {
            ThrowIfDisposed();
            lock (_gate)
            {
                foreach (var registration in _registered.Values)
                    registration.Wrapped.MarkDirty();
            }
        }

        /// <summary>
        /// Apply an incoming batch produced by another channel.
        /// <paramref name="writerPlayerId"/> is the player the host assigned to the
        /// sending peer (0 for host-originated or untrusted context); when
        /// <see cref="AcceptIncomingKey"/> is set it filters client-authored writes
        /// so a peer cannot change state it does not simulate.
        /// </summary>
        public int ApplyIncoming(ReadOnlyMemory<byte> batch, Uri? sender = null, long writerPlayerId = 0)
        {
            int applied = 0;
            foreach (var (key, payload) in SyncBatchBuilder.EnumerateBatch(batch))
            {
                if (AcceptIncomingKey is not null && !AcceptIncomingKey(key, writerPlayerId))
                    continue;

                if (!Registry.TryGet(key, out var value)) continue;

                try
                {
                    if (value.ApplyRemotePayload(payload.Span, sender)) applied++;
                }
                catch
                {
                    // A malformed or stale value must not take down the network thread.
                }
            }
            return applied;
        }

        /// <summary>
        /// Optional per-key acceptance check. The host sets this to reject a
        /// client's value writes for elements that client does not simulate; a
        /// client leaves it null and accepts everything the host sends.
        /// </summary>
        public Func<string, long, bool>? AcceptIncomingKey { get; set; }

        /// <summary>Register one value under a scope prefix. Useful for non-world channels.</summary>
        public string Register(ISyncValue value, string scope)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (string.IsNullOrWhiteSpace(scope)) throw new ArgumentException("scope is required", nameof(scope));

            string key = scope.TrimEnd('/') + "/" + value.Key;
            RegisterKey(key, value);
            return key;
        }

        private void RegisterElement(IWorldElement element, HashSet<string> seen)
        {
            if (element == null) return;

            foreach (var component in element.Components)
            {
                if (component == null) continue;
                string scope = ScopeFor(element, component);

                foreach (var value in SyncValueScanner.Enumerate(component))
                {
                    string key = scope + "/" + value.Key;
                    seen.Add(key);
                    RegisterKey(key, value);
                }
            }

            foreach (var child in element.Children)
                RegisterElement(child, seen);
        }

        private void RegisterKey(string key, ISyncValue value)
        {
            lock (_gate)
            {
                if (_registered.TryGetValue(key, out var existing) &&
                    ReferenceEquals(existing.Inner, value))
                    return;

                if (_registered.ContainsKey(key))
                    Registry.Unregister(key);

                value.WriterId = _writerId;
                var wrapped = World == null ? value : new ScopedSyncValue(value, key);
                _registered[key] = new Registration(value, wrapped);
                Registry.Register(wrapped);
            }
        }

        private string ScopeFor(IWorldElement element, IComponent component)
        {
            string elementId = element.Uid != Guid.Empty
                ? element.Uid.ToString("N")
                : element.Id.ToString();
            string componentId = component.Uid != Guid.Empty
                ? component.Uid.ToString("N")
                : component.Id.ToString();
            return $"world/{World!.Id:N}/element/{elementId}/component/{componentId}";
        }

        private void OnWorldChanged(IWorldElement _) => _refreshPending = true;

        private void RefreshIfDue()
        {
            if (!_refreshPending && DateTime.UtcNow - _lastRefreshUtc < RefreshInterval)
                return;
            Refresh();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SyncChannel));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (World != null)
            {
                World.ElementAdded -= OnWorldChanged;
                World.ElementRemoved -= OnWorldChanged;
            }

            lock (_gate)
            {
                if (_clearRegistryOnDispose)
                {
                    foreach (var key in _registered.Keys)
                        Registry.Unregister(key);
                }
                _registered.Clear();
            }

            SendBytes = null;
        }

        /// <summary>Wrap a value with a scoped key without changing the value itself.</summary>
        private sealed class ScopedSyncValue : ISyncValue
        {
            private readonly ISyncValue _inner;

            public ScopedSyncValue(ISyncValue inner, string key)
            {
                _inner = inner;
                Key = key;
            }

            public string Key { get; }
            public Type ValueType => _inner.ValueType;
            public bool IsDirty => _inner.IsDirty;
            public ulong Version => _inner.Version;

            public ushort WriterId
            {
                get => _inner.WriterId;
                set => _inner.WriterId = value;
            }

            public byte[] ToByteArray() => _inner.ToByteArray();
            public bool ApplyRemotePayload(ReadOnlySpan<byte> payload, Uri? sender = null)
                => _inner.ApplyRemotePayload(payload, sender);
            public void ClearDirty() => _inner.ClearDirty();
            public void MarkDirty() => _inner.MarkDirty();
            public void Unregister() => _inner.Unregister();
        }
    }
}
