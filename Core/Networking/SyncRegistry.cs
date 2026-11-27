namespace V12.Core.Networking
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    /// <summary>
    /// Thread-safe key to <see cref="ISyncValue"/> map. The default instance keeps
    /// the historical static API working; a <see cref="SyncChannel"/> owns a
    /// separate instance so two worlds cannot collide on the same key.
    /// </summary>
    public sealed class SyncRegistry
    {
        private readonly ConcurrentDictionary<string, ISyncValue> _map =
            new(StringComparer.Ordinal);

        /// <summary>Process-wide registry used by values that are not bound to a session.</summary>
        public static SyncRegistry Default { get; } = new SyncRegistry();

        public int Count => _map.Count;

        public void Register(ISyncValue value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            _map[value.Key] = value;
        }

        public bool Unregister(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return _map.TryRemove(key, out _);
        }

        /// <summary>
        /// Remove <paramref name="value"/> only when it is still the instance
        /// registered under its key. Registering a second value with the same key
        /// deliberately wins, so removing by key alone could delete a live
        /// replacement; teardown must not do that.
        /// </summary>
        public bool Unregister(ISyncValue value)
        {
            if (value == null) return false;
            return ((ICollection<KeyValuePair<string, ISyncValue>>)_map)
                .Remove(new KeyValuePair<string, ISyncValue>(value.Key, value));
        }

        public void Clear() => _map.Clear();

        public bool TryGet(string key, out ISyncValue value)
            => _map.TryGetValue(key, out value!);

        public IEnumerable<ISyncValue> AllValues() => _map.Values;

        public IEnumerable<ISyncValue> DirtyValues()
        {
            foreach (var value in _map.Values)
                if (value.IsDirty) yield return value;
        }

    }
}
