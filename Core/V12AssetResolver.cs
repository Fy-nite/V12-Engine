using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using V12.Core.Interfaces;

namespace V12.Core
{
    public class V12AssetResolver : IAssetResolver
    {
        private readonly ConcurrentDictionary<string, string> _mounts = new(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<string> GetMountPaths() => _mounts.Values;

        /// <summary>Resolve a v12:// path via the global asset resolver, or return the path unchanged.</summary>
        public static string ResolveGlobal(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("v12://", StringComparison.OrdinalIgnoreCase))
                return path;
            var resolver = GameRoot.Instance?.Registry?.Get<IAssetResolver>();
            return resolver != null ? resolver.Resolve(path) : path;
        }
        private string _defaultMount;

        public void Mount(string mountPoint, string physicalPath)
        {
            _mounts[mountPoint] = physicalPath;
            _defaultMount ??= mountPoint;
        }

        public void Unmount(string mountPoint)
        {
            _mounts.TryRemove(mountPoint, out _);
            if (_defaultMount == mountPoint)
                _defaultMount = null;
        }

        public string Resolve(string uri)
        {
            if (string.IsNullOrEmpty(uri))
                return uri;

            if (!uri.StartsWith("v12://", System.StringComparison.OrdinalIgnoreCase))
                return uri;

            string path = uri.Substring(6).TrimStart('/');

            int slashIndex = path.IndexOf('/');
            if (slashIndex > 0)
            {
                string mount = path.Substring(0, slashIndex);
                string local = path.Substring(slashIndex + 1);
                if (_mounts.TryGetValue(mount, out string basePath))
                    return Path.Combine(basePath, local);
            }
            else if (_defaultMount != null && _mounts.TryGetValue(_defaultMount, out string defaultPath))
            {
                return Path.Combine(defaultPath, path);
            }

            return uri;
        }

        public Stream Open(string uri)
        {
            string resolved = Resolve(uri);
            if (resolved == uri && !File.Exists(resolved))
                return null;
            return File.OpenRead(resolved);
        }
    }
}
