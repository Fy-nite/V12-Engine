namespace V12.Core.Networking
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    public static class SyncRegistry
    {
        private static readonly ConcurrentDictionary<string, ISyncValue> _map = new ConcurrentDictionary<string, ISyncValue>();

        public static void Register(ISyncValue v)
        {
            if (v == null) throw new ArgumentNullException(nameof(v));
            _map[v.Key] = v;
        }

        public static bool TryGet(string key, out ISyncValue value) => _map.TryGetValue(key, out value);

        public static IEnumerable<ISyncValue> AllValues() => _map.Values;

        public static IEnumerable<ISyncValue> DirtyValues()
        {
            foreach (var v in _map.Values)
                if (v.IsDirty) yield return v;
        }
    }
}

