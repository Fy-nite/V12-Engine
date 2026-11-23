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
        private static readonly ConcurrentDictionary<Type, MemberInfo[]> SyncMembers = new();

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

        /// <summary>Apply an incoming batch produced by another channel.</summary>
        public int ApplyIncoming(ReadOnlyMemory<byte> batch, Uri? sender = null)
        {
            int applied = 0;
            foreach (var (key, payload) in SyncBatchBuilder.EnumerateBatch(batch))
            {
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

                foreach (var value in EnumerateSyncValues(component))
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

        private static IEnumerable<ISyncValue> EnumerateSyncValues(object target)
        {
            Type type = target.GetType();
            var members = SyncMembers.GetOrAdd(type, static t =>
                t.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.MemberType is MemberTypes.Property or MemberTypes.Field)
                    .Where(m => m switch
                    {
                        PropertyInfo p => p.GetIndexParameters().Length == 0,
                        FieldInfo f => !f.IsStatic,
                        _ => false,
                    })
                    .Where(m => m switch
                    {
                        PropertyInfo p => typeof(ISyncValue).IsAssignableFrom(p.PropertyType),
                        FieldInfo f => typeof(ISyncValue).IsAssignableFrom(f.FieldType),
                        _ => false,
                    })
                    .ToArray());

            foreach (var member in members)
            {
                object? value;
                try
                {
                    value = member switch
                    {
                        PropertyInfo p => p.GetValue(target),
                        FieldInfo f => f.GetValue(target),
                        _ => null,
                    };
                }
                catch
                {
                    continue;
                }

                if (value is ISyncValue sync) yield return sync;
            }
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

            public byte[] ToByteArray() => _inner.ToByteArray();
            public bool ApplyRemotePayload(ReadOnlySpan<byte> payload, Uri? sender = null)
                => _inner.ApplyRemotePayload(payload, sender);
            public void ClearDirty() => _inner.ClearDirty();
        }
    }
}
