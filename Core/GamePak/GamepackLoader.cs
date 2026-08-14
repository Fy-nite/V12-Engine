using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using V12.Core;

namespace V12.Core.GamePak
{
    /// <summary>
    /// Discovers, instantiates, and manages the lifecycle of <see cref="IV12Gamepack"/>
    /// implementations. Scans a directory or assembly for .NET types that implement
    /// the interface, and drives the two-phase initialization (Initialize then OnStart).
    /// </summary>
    public class GamepackLoader
    {
        private readonly List<IV12Gamepack> _gamepaks = new();

        private static Serilog.ILogger Log => GameRoot.Log;

        /// <summary>All loaded game paks, in load order.</summary>
        public IReadOnlyList<IV12Gamepack> Gamepaks => _gamepaks;

        /// <summary>
        /// Register a gamepak instance directly (bypasses assembly scanning).
        /// Used for gamepaks that are constructed at runtime rather than
        /// discovered via reflection, e.g. a <c>.ct</c> script compiled into an
        /// <see cref="IV12Gamepack"/> adapter. Appends to the load-order list.
        /// </summary>
        public void Add(IV12Gamepack pak)
        {
            if (pak == null) throw new ArgumentNullException(nameof(pak));
            _gamepaks.Add(pak);
            Log.Debug("Registered gamepak directly: '{Name}' (index {Index})", pak.Name, _gamepaks.Count - 1);
        }

        /// <summary>
        /// Find a loaded game pak by its <see cref="IV12Gamepack.Name"/> (case-insensitive).
        /// Returns null if not found.
        /// </summary>
        public IV12Gamepack? FindByName(string name)
        {
            Log.Debug("Looking up gamepak by name: '{Name}'", name);
            var pak = _gamepaks.FirstOrDefault(
                p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (pak != null)
                Log.Debug("Found gamepak '{Name}' at index {Index}", pak.Name, _gamepaks.IndexOf(pak));
            else
                Log.Warning("Gamepak '{Name}' not found among {Count} loaded pak(s)", name, _gamepaks.Count);
            return pak;
        }

        /// <summary>
        /// Scan a directory for .dll assemblies and load any <see cref="IV12Gamepack"/>
        /// implementations found. Skips assemblies that cannot be loaded (e.g. native).
        /// </summary>
        public void LoadFromDirectory(string directory)
        {
            var sw = Stopwatch.StartNew();
            Log.Information("Scanning directory for gamepaks: '{Directory}'", directory);

            if (!Directory.Exists(directory))
            {
                Log.Warning("Gamepak directory not found: '{Directory}' — no paks loaded", directory);
                return;
            }

            // Ensure game pak assemblies can resolve dependencies (V12.dll etc.)
            // to the SAME assembly instance already loaded by the app, avoiding
            // type identity mismatches between LoadFrom context and default context.
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var resolveName = new AssemblyName(args.Name).Name;
                Log.Debug("Assembly resolve request for '{Name}', searching loaded assemblies", resolveName);
                return AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == resolveName);
            };

            var dlls = Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories).ToArray();
            Log.Information("Found {Count} DLL(s) in '{Directory}'", dlls.Length, directory);

            int loaded = 0, skipped = 0;
            foreach (var dll in dlls)
            {
                try
                {
                    Log.Debug("Loading assembly: '{Path}'", dll);
                    var assembly = Assembly.LoadFrom(dll);
                    var count = LoadFromAssembly(assembly);
                    if (count > 0)
                    {
                        loaded += count;
                        Log.Information("Assembly '{Name}' contributed {Count} gamepak(s)", assembly.GetName().Name, count);
                    }
                }
                catch (Exception ex)
                {
                    skipped++;
                    Log.Warning(ex, "Skipping '{FileName}' — failed to load assembly", Path.GetFileName(dll));
                }
            }

            sw.Stop();
            Log.Information("Directory scan complete: {Loaded} gamepak(s) loaded, {Skipped} DLL(s) skipped in {Elapsed}ms",
                loaded, skipped, sw.ElapsedMilliseconds);
        }

        /// <summary>
        /// Load an already-loaded assembly and register any <see cref="IV12Gamepack"/>
        /// implementations found within it. Returns the number of gamepaks discovered.
        /// </summary>
        public int LoadFromAssembly(Assembly assembly)
        {
            var assemblyName = assembly.GetName().Name ?? "<unknown>";
            var sw = Stopwatch.StartNew();
            Log.Information("Scanning assembly '{Assembly}' for IV12Gamepack implementations", assemblyName);

            Type[] types;
            try
            {
                types = assembly.GetTypes();
                Log.Debug("Assembly '{Assembly}' contains {Count} type(s)", assemblyName, types.Length);
            }
            catch (ReflectionTypeLoadException ex)
            {
                var validTypes = ex.Types.Where(t => t != null).ToArray()!;
                Log.Warning(ex, "Assembly '{Assembly}' has {ErrorCount} type load error(s), recovered {RecoveredCount} type(s)",
                    assemblyName, ex.LoaderExceptions?.Length ?? 0, validTypes.Length);

                foreach (var loaderEx in ex.LoaderExceptions ?? Array.Empty<Exception>())
                    Log.Debug("Type load error: {Message}", loaderEx?.Message);

                types = validTypes;
            }

            int found = 0;
            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (!typeof(IV12Gamepack).IsAssignableFrom(type)) continue;

                try
                {
                    Log.Debug("Instantiating gamepak type: {TypeName} from '{Assembly}'", type.FullName, assemblyName);
                    var instance = (IV12Gamepack)Activator.CreateInstance(type)!;
                    _gamepaks.Add(instance);
                    found++;
                    Log.Information("Loaded gamepak: '{Name}' ({Type}) from assembly '{Assembly}'",
                        instance.Name, type.FullName, assemblyName);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to instantiate gamepak type {TypeName} from '{Assembly}'",
                        type.FullName, assemblyName);
                }
            }

            if (found == 0)
            {
                Log.Debug("No IV12Gamepack implementations found in assembly '{Assembly}'", assemblyName);
            }

            sw.Stop();
            Log.Information("Assembly '{Assembly}' scan complete: {Found} gamepak(s) found in {Elapsed}ms",
                assemblyName, found, sw.ElapsedMilliseconds);

            return found;
        }

        /// <summary>
        /// Load, initialize, and start all game paks from a single assembly in one call.
        /// Convenience method that combines <see cref="LoadFromAssembly"/>,
        /// <see cref="InitializeAll"/>, and <see cref="StartAll"/>.
        /// </summary>
        public int LoadAndStart(Assembly assembly)
        {
            var assemblyName = assembly.GetName().Name ?? "<unknown>";
            var sw = Stopwatch.StartNew();
            var countBefore = _gamepaks.Count;

            Log.Information("LoadAndStart: loading gamepaks from assembly '{Assembly}'", assemblyName);
            var discovered = LoadFromAssembly(assembly);

            if (discovered > 0)
            {
                Log.Information("LoadAndStart: initializing {Count} newly discovered gamepak(s) from '{Assembly}'",
                    discovered, assemblyName);
                InitializeNew(countBefore);

                Log.Information("LoadAndStart: starting {Count} newly discovered gamepak(s) from '{Assembly}'",
                    discovered, assemblyName);
                StartNew(countBefore);
            }

            sw.Stop();
            Log.Information("LoadAndStart from '{Assembly}' complete: {Discovered} gamepak(s) in {Elapsed}ms",
                assemblyName, discovered, sw.ElapsedMilliseconds);

            return discovered;
        }

        /// <summary>
        /// Load, initialize, and start all game paks from a directory in one call.
        /// Convenience method that combines <see cref="LoadFromDirectory"/>,
        /// <see cref="InitializeAll"/>, and <see cref="StartAll"/>.
        /// </summary>
        public void LoadAndStartFromDirectory(string directory)
        {
            var sw = Stopwatch.StartNew();
            var countBefore = _gamepaks.Count;

            Log.Information("LoadAndStartFromDirectory: scanning '{Directory}'", directory);
            LoadFromDirectory(directory);

            var discovered = _gamepaks.Count - countBefore;
            if (discovered > 0)
            {
                Log.Information("LoadAndStartFromDirectory: initializing and starting {Count} new gamepak(s)", discovered);
                InitializeNew(countBefore);
                StartNew(countBefore);
            }
            else
            {
                Log.Information("LoadAndStartFromDirectory: no new gamepaks found in '{Directory}'", directory);
            }

            sw.Stop();
            Log.Information("LoadAndStartFromDirectory complete in {Elapsed}ms", sw.ElapsedMilliseconds);
        }

        /// <summary>
        /// Call <see cref="IV12Gamepack.Initialize"/> on every loaded game pak,
        /// in load order.
        /// </summary>
        public void InitializeAll()
        {
            Log.Information("Initializing all {Count} gamepak(s)", _gamepaks.Count);
            var sw = Stopwatch.StartNew();
            int success = 0, failed = 0;

            foreach (var pak in _gamepaks)
            {
                try
                {
                    Log.Information("Initializing gamepak '{Name}'...", pak.Name);
                    pak.Initialize();
                    success++;
                    Log.Information("Gamepak '{Name}' initialized successfully", pak.Name);
                }
                catch (Exception ex)
                {
                    failed++;
                    Log.Error(ex, "Failed to initialize gamepak '{Name}'", pak.Name);
                }
            }

            sw.Stop();
            Log.Information("InitializeAll complete: {Success} succeeded, {Failed} failed in {Elapsed}ms",
                success, failed, sw.ElapsedMilliseconds);
        }

        /// <summary>
        /// Call <see cref="IV12Gamepack.OnStart"/> on every loaded game pak,
        /// in load order.
        /// </summary>
        public void StartAll()
        {
            Log.Information("Starting all {Count} gamepak(s)", _gamepaks.Count);
            var sw = Stopwatch.StartNew();
            int success = 0, failed = 0;

            foreach (var pak in _gamepaks)
            {
                try
                {
                    Log.Information("Starting gamepak '{Name}'...", pak.Name);
                    pak.OnStart();
                    success++;
                    Log.Information("Gamepak '{Name}' started successfully", pak.Name);
                }
                catch (Exception ex)
                {
                    failed++;
                    Log.Error(ex, "Failed to start gamepak '{Name}'", pak.Name);
                }
            }

            sw.Stop();
            Log.Information("StartAll complete: {Success} succeeded, {Failed} failed in {Elapsed}ms",
                success, failed, sw.ElapsedMilliseconds);
        }

        /// <summary>
        /// Initialize only the game paks added after the given index (new paks).
        /// Used by <see cref="LoadAndStart"/> to avoid double-initializing paks
        /// that were already loaded.
        /// </summary>
        private void InitializeNew(int startIndex)
        {
            for (int i = startIndex; i < _gamepaks.Count; i++)
            {
                var pak = _gamepaks[i];
                try
                {
                    Log.Information("Initializing new gamepak '{Name}' (index {Index})...", pak.Name, i);
                    pak.Initialize();
                    Log.Information("Gamepak '{Name}' initialized", pak.Name);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to initialize new gamepak '{Name}'", pak.Name);
                }
            }
        }

        /// <summary>
        /// Start only the game paks added after the given index (new paks).
        /// Used by <see cref="LoadAndStart"/> to avoid double-starting paks
        /// that were already loaded.
        /// </summary>
        private void StartNew(int startIndex)
        {
            for (int i = startIndex; i < _gamepaks.Count; i++)
            {
                var pak = _gamepaks[i];
                try
                {
                    Log.Information("Starting new gamepak '{Name}' (index {Index})...", pak.Name, i);
                    pak.OnStart();
                    Log.Information("Gamepak '{Name}' started", pak.Name);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to start new gamepak '{Name}'", pak.Name);
                }
            }
        }

        /// <summary>
        /// Initialize and start a single game pak by name. Returns true if found and started.
        /// </summary>
        public bool InitializeAndStartByName(string name)
        {
            Log.Information("Looking up gamepak '{Name}' for initialize+start", name);
            var pak = FindByName(name);
            if (pak == null)
            {
                Log.Warning("Cannot initialize+start: gamepak '{Name}' not found", name);
                return false;
            }

            try
            {
                Log.Information("Initializing gamepak '{Name}'...", pak.Name);
                pak.Initialize();
                Log.Information("Gamepak '{Name}' initialized", pak.Name);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize gamepak '{Name}'", pak.Name);
                return false;
            }

            try
            {
                Log.Information("Starting gamepak '{Name}'...", pak.Name);
                pak.OnStart();
                Log.Information("Gamepak '{Name}' started", pak.Name);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start gamepak '{Name}'", pak.Name);
                return false;
            }
        }

        /// <summary>
        /// Remove all loaded game paks and reset state.
        /// </summary>
        public void Clear()
        {
            var count = _gamepaks.Count;
            _gamepaks.Clear();
            Log.Information("Cleared {Count} gamepak(s) from loader", count);
        }
    }
}
