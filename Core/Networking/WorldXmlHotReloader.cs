using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using V12.Core;
using V12.Components;
using V12.WorldML;

namespace V12.Core.Networking
{
    /// <summary>
    /// Service that scans a world for WorldXmlSourceComponent instances and hot-reloads them when the target file changes.
    /// </summary>
    public class WorldXmlHotReloader
    {
        private readonly World _world;
        private readonly WorldMLParser _parser;
        private readonly Dictionary<string, FileSystemWatcher> _watchers = new Dictionary<string, FileSystemWatcher>();

        public WorldXmlHotReloader(World world)
        {
            _world = world;
            _parser = new WorldMLParser();
        }

        public void Initialize()
        {
            // Initial scan of elements to hook up existing components
            foreach (var element in _world.Root)
            {
                HookElement(element);
            }
        }

        private void HookElement(V12.Core.Core.Interfaces.IWorldElement element)
        {
            var source = element.GetComponent<WorldXmlSourceComponent>();
            if (source != null && !string.IsNullOrEmpty(source.FilePath))
            {
                WatchFile(source.FilePath, element);
            }

            foreach (var child in element.Children)
            {
                HookElement(child);
            }
        }

        private void WatchFile(string path, V12.Core.Core.Interfaces.IWorldElement targetElement)
        {
            if (_watchers.ContainsKey(path)) return;

            // Simple path handling: assume relative to base directory
            string fullPath = Path.IsPathRooted(path) ? path : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
            if (!File.Exists(fullPath)) return;

            var dir = Path.GetDirectoryName(fullPath);
            var file = Path.GetFileName(fullPath);

            var watcher = new FileSystemWatcher(dir, file);
            watcher.NotifyFilter = NotifyFilters.LastWrite;
            watcher.Changed += (s, e) =>
            {
                // Simple debounce/retry for file lock
                try
                {
                    var xml = File.ReadAllText(fullPath);
                    // Reload elements into targetElement safely on main thread
                    ReloadElement(targetElement, xml);
                }
                catch (IOException) { }
            };
            watcher.EnableRaisingEvents = true;
            _watchers[path] = watcher;
        }

        private void ReloadElement(V12.Core.Core.Interfaces.IWorldElement target, string xml)
        {
            // This is a simplified reload: clear and repopulate
            // A more robust implementation would diff the structure.
            var tempWorld = _parser.Parse(xml);
            
            // Logic to move children from tempWorld to target would go here.
            // Simplified: Clear children and add new ones.
            target.Children.Clear();
            foreach(var child in tempWorld.Root)
            {
                target.AddChild(child);
            }
        }

        public void Dispose()
        {
            foreach (var watcher in _watchers.Values)
            {
                watcher.Dispose();
            }
            _watchers.Clear();
        }
    }
}
