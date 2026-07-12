using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace V12.Core.GamePak
{
    /// <summary>
    /// Discovers, instantiates, and manages the lifecycle of <see cref="IV12Gamepack"/>
    /// implementations. Scans a directory for .NET assemblies, finds types that
    /// implement the interface, and drives the two-phase initialization.
    /// </summary>
    public class GamepackLoader
    {
        private readonly List<IV12Gamepack> _gamepaks = new();

        /// <summary>All loaded game paks, in load order.</summary>
        public IReadOnlyList<IV12Gamepack> Gamepaks => _gamepaks;

        /// <summary>
        /// Find a loaded game pak by its <see cref="IV12Gamepack.Name"/> (case-insensitive).
        /// Returns null if not found.
        /// </summary>
        public IV12Gamepack? FindByName(string name)
        {
            return _gamepaks.FirstOrDefault(
                p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Scan a directory for .dll assemblies and load any <see cref="IV12Gamepack"/>
        /// implementations found. Skips assemblies that cannot be loaded (e.g. native).
        /// </summary>
        public void LoadFromDirectory(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Console.WriteLine($"[GamepackLoader] Directory not found: {directory}");
                return;
            }

            // Ensure game pak assemblies can resolve dependencies (V12.dll etc.)
            // to the SAME assembly instance already loaded by the app, avoiding
            // type identity mismatches between LoadFrom context and default context.
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var name = new AssemblyName(args.Name).Name;
                return AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == name);
            };

            foreach (var dll in Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories))
            {
                try
                {
                    Console.WriteLine($"[GamepackLoader] Loading '{dll}'...");
                    var assembly = Assembly.LoadFrom(dll);
                    LoadFromAssembly(assembly);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GamepackLoader] Skipping '{Path.GetFileName(dll)}': {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Load an already-loaded assembly and register any <see cref="IV12Gamepack"/>
        /// implementations found within it.
        /// </summary>
        public void LoadFromAssembly(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                Console.WriteLine($"[GamepackLoader] Type load errors in '{assembly.GetName().Name}':");
                foreach (var loaderEx in ex.LoaderExceptions ?? Array.Empty<Exception>())
                    Console.WriteLine($"  → {loaderEx?.Message}");
                types = ex.Types.Where(t => t != null).ToArray()!;
            }

            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (!typeof(IV12Gamepack).IsAssignableFrom(type)) continue;

                try
                {
                    var instance = (IV12Gamepack)Activator.CreateInstance(type)!;
                    _gamepaks.Add(instance);
                    Console.WriteLine($"[GamepackLoader] Loaded gamepak: {instance.Name} ({type.FullName})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GamepackLoader] Failed to instantiate {type.FullName}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (!types.Any(t => !t.IsAbstract && !t.IsInterface && typeof(IV12Gamepack).IsAssignableFrom(t)))
            {
                Console.WriteLine($"[GamepackLoader] No IV12Gamepack implementations found in '{assembly.GetName().Name}'");
            }
        }

        /// <summary>
        /// Call <see cref="IV12Gamepack.Initialize"/> on every loaded game pak,
        /// in load order.
        /// </summary>
        public void InitializeAll()
        {
            foreach (var pak in _gamepaks)
            {
                try
                {
                    Console.WriteLine($"[GamepackLoader] Initializing '{pak.Name}'...");
                    pak.Initialize();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GamepackLoader] Failed to initialize '{pak.Name}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Call <see cref="IV12Gamepack.OnStart"/> on every loaded game pak,
        /// in load order.
        /// </summary>
        public void StartAll()
        {
            foreach (var pak in _gamepaks)
            {
                try
                {
                    Console.WriteLine($"[GamepackLoader] Starting '{pak.Name}'...");
                    pak.OnStart();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GamepackLoader] Failed to start '{pak.Name}': {ex.Message}");
                }
            }
        }
    }
}
