using System;

namespace V12.Core.Networking
{
    /// <summary>
    /// Parses the scoped value keys written by <c>SyncChannel.ScopeFor</c>:
    /// <code>
    /// world/{worldId:N}/element/{elementUid:N}/component/{componentUid:N}/{field}
    /// </code>
    /// A client-authored value write is only accepted when its element can be
    /// resolved, so this is what turns a batch key into an authority check.
    /// </summary>
    public static class SyncKey
    {
        private const int FieldIndex = 6;

        /// <summary>
        /// True when the key is scoped and targets an element with a stable
        /// <see cref="Guid"/> uid. An element without one cannot be delegated, so
        /// a client write to it is always rejected.
        /// </summary>
        public static bool TryParseElement(string? key, out Guid worldId, out Guid elementUid)
        {
            worldId = Guid.Empty;
            elementUid = Guid.Empty;
            if (string.IsNullOrEmpty(key)) return false;

            var parts = key!.Split('/', FieldIndex + 1);
            if (parts.Length <= FieldIndex) return false;
            if (parts[0] != "world" || parts[2] != "element" || parts[4] != "component")
                return false;

            return Guid.TryParseExact(parts[1], "N", out worldId)
                && Guid.TryParseExact(parts[3], "N", out elementUid);
        }
    }
}
