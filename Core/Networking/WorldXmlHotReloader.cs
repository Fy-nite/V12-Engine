using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Interfaces;
using V12.WorldML;

namespace V12.Core.Networking
{
    /// <summary>
    /// Thread-safe XML hot reloader for world elements.
    /// 
    /// Architecture:
    /// FileSystemWatcher (threadpool)
    ///     → ConcurrentQueue (events)
    ///         → Main-thread Update()
    ///             → Parse + Apply world mutation safely
    /// </summary>
    public class WorldXmlHotReloader : IDisposable
    {
        private readonly World _world;
        private readonly WorldMLParser _parser;

        // Watchers
        public readonly Dictionary<string, FileSystemWatcher> _watchers = new();

        // Thread-safe reload queue
        private readonly ConcurrentQueue<ReloadRequest> _queue = new();

        // Debounce tracking
        private readonly Dictionary<string, DateTime> _debounce = new();

        // ----------------------------
        // Reload request structure
        // ----------------------------
        private struct ReloadRequest
        {
            public string Path;
            public string FullPath;
            public IWorldElement Target;
            public DateTime Time;
        }

        public WorldXmlHotReloader(World world)
        {
            _world = world;
            _parser = new WorldMLParser();
        }

        // ----------------------------
        // Public API
        // ----------------------------
        public void Initialize()
        {
            foreach (var element in _world.Root)
                HookElement(element);
        }

        public void Update()
        {
            // MUST be called on main thread
            while (_queue.TryDequeue(out var req))
            {
                Console.WriteLine($"Processing reload for {req.Path} at {req.Time}");
                ProcessReload(req);
            }
        }

        public void Dispose()
        {
            foreach (var watcher in _watchers.Values)
                watcher.Dispose();

            _watchers.Clear();
        }

        // ----------------------------
        // World scanning
        // ----------------------------
        private void HookElement(IWorldElement element)
        {
            var source = element.GetComponent<WorldXmlSourceComponent>();

            if (source != null && !string.IsNullOrEmpty(source.FilePath))
            {
                WatchFile(source.FilePath, element);
                Console.WriteLine($"Watching {source.FilePath} for {element.Name}");
            }

            foreach (var child in element.Children)
                HookElement(child);
        }

        // ----------------------------
        // File watcher setup
        // ----------------------------
        private void WatchFile(string path, IWorldElement target)
        {
            var fullPath = NormalizePath(path);

            if (_watchers.ContainsKey(fullPath))
                return;

            if (!File.Exists(fullPath))
                return;

            var dir = Path.GetDirectoryName(fullPath);
            var file = Path.GetFileName(fullPath);

            if (string.IsNullOrEmpty(dir))
                return;

             var watcher= new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };

            watcher.Changed += (_, __) =>
                EnqueueReload(path, fullPath, target);

            _watchers[fullPath] = watcher;

            Console.WriteLine($"Watcher active: {fullPath}");
        }

        // ----------------------------
        // Event ingestion (thread-safe)
        // ----------------------------
        private void EnqueueReload(string path, string fullPath, IWorldElement target)
        {
            var key = NormalizePath(fullPath);

            // debounce
            if (_debounce.TryGetValue(key, out var last))
            {
                if ((DateTime.Now - last).TotalMilliseconds < 200)
                    return;
            }

            _debounce[key] = DateTime.Now;

            _queue.Enqueue(new ReloadRequest
            {
                Path = path,
                FullPath = key,
                Target = target,
                Time = DateTime.Now
            });
        }

        // ----------------------------
        // Main-thread processing
        // ----------------------------
        private void ProcessReload(ReloadRequest req)
        {
            var xml = ReadFileSafe(req.FullPath);
            if (xml == null)
                return;

            var newRoot = BuildTree(xml);
            ApplyReplacement(req.Target, newRoot);
        }

        // ----------------------------
        // File reading (retry safe)
        // ----------------------------
        private string ReadFileSafe(string path)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    return File.ReadAllText(path);
                }
                catch (IOException)
                {
                    Thread.Sleep(50);
                }
            }

            return null;
        }

        // ----------------------------
        // Parsing
        // ----------------------------
        private IWorldElement BuildTree(string xml)
        {
            var tempWorld = _parser.Parse(xml);
            return tempWorld;
        }

        // ----------------------------
        // Safe world mutation
        // ----------------------------
        private void ApplyReplacement(IWorldElement target, IWorldElement newRoot)
        {
            // Remove old children safely
            var oldChildren = target.Children.ToList();
            foreach (var child in oldChildren)
            {
                target.RemoveChild(child);
            }

            // Add new children
            foreach (var child in newRoot.Children)
            {
                target.AddChild(child);
                Console.WriteLine($"Reloaded child: {child.Name}");
            }
            Console.WriteLine(target.Name);
        
        }

        // ----------------------------
        // Helpers
        // ----------------------------
        private string NormalizePath(string path)
        {
            var fullPath = Path.IsPathRooted(path)
                ? path
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);

            return Path.GetFullPath(fullPath);
        }
    }
}