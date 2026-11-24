using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using V12.Core.GamePak;

namespace V12.Core.Modules
{
    /// <summary>Diagnostic record for one module that was considered for loading.</summary>
    public sealed class ModuleInfo
    {
        public string Name { get; init; } = "";
        public string? Version { get; init; }
        public string TypeName { get; init; } = "";
        public string AssemblyName { get; init; } = "";
        public string? AssemblyPath { get; init; }
        public bool Started { get; init; }
        /// <summary>Why it did not start: missing dependency, cycle, declined, or an exception.</summary>
        public string? Error { get; init; }
        public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Loads opt-in <see cref="IV12Module"/> assemblies into a running game, in
    /// dependency order, with diagnostics and a per-module data directory.
    ///
    /// <para>
    /// This is the trusted-code half of V12's mod support. Nothing here runs unless
    /// a host asks for it: either through <see cref="EnableFromArgs"/> (the
    /// <c>--modules</c> / <c>--module</c> switches) or by calling
    /// <see cref="LoadDirectory"/> directly. A build that never opts in behaves
    /// exactly as before.
    /// </para>
    ///
    /// <para>
    /// Assemblies are opened through <see cref="GamepackLoader.AssemblyLoader"/>
    /// when a host has installed one (Godot's plugin context, MonoGame's default
    /// context), which keeps <see cref="IV12Module"/> and every engine type on the
    /// single identity the game already uses.
    /// </para>
    /// </summary>
    public sealed class ModuleHost
    {
        private static readonly string[] EmptyArgs = Array.Empty<string>();

        private readonly GameRoot _root;
        private readonly List<IV12Module> _modules = new();
        private readonly List<ModuleInfo> _diagnostics = new();
        private readonly HashSet<string> _loadedAssemblies = new(StringComparer.OrdinalIgnoreCase);

        public ModuleHost(GameRoot root)
            => _root = root ?? throw new ArgumentNullException(nameof(root));

        /// <summary>Modules that started successfully, in start order.</summary>
        public IReadOnlyList<IV12Module> Modules => _modules;

        /// <summary>Every module that was considered, including ones that did not start.</summary>
        public IReadOnlyList<ModuleInfo> Diagnostics => _diagnostics;

        public IV12Module? Find(string name)
            => _modules.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

        // ── entry points ──────────────────────────────────────────────────

        /// <summary>
        /// Enable modules from the command line. The host calls this once after
        /// creating the root:
        /// <code>
        /// root.Modules.EnableFromArgs(args, AppContext.BaseDirectory);
        /// </code>
        /// <list type="bullet">
        /// <item><c>--modules</c> loads every module in <c>modules/</c> (or the
        /// directory given as its value).</item>
        /// <item><c>--module &lt;dll&gt;</c> loads one assembly, repeatable.</item>
        /// <item><c>--no-modules</c> disables the whole system, including any other
        /// module flag.</item>
        /// </list>
        /// Relative paths resolve against <paramref name="baseDirectory"/>.
        /// </summary>
        public int EnableFromArgs(string[] args, string? baseDirectory = null)
        {
            ArgumentNullException.ThrowIfNull(args);

            if (ModuleArgs.HasFlag(args, "--no-modules"))
            {
                GameRoot.Log.Information("Modules disabled by --no-modules");
                return 0;
            }

            string root = string.IsNullOrWhiteSpace(baseDirectory)
                ? AppContext.BaseDirectory
                : baseDirectory;

            int started = 0;

            var directories = ModuleArgs.Values(args, "--modules", out bool modulesFlag);
            if (modulesFlag && directories.Count == 0)
                directories.Add("modules");

            foreach (string directory in directories)
                started += LoadDirectory(ModuleArgs.Resolve(root, directory), args);

            foreach (string assembly in ModuleArgs.Values(args, "--module", out _))
                if (Load(ModuleArgs.Resolve(root, assembly), args))
                    started++;

            if (started == 0 && (modulesFlag || ModuleArgs.HasFlag(args, "--module")))
                GameRoot.Log.Information("Module flags were passed but no modules started");

            return started;
        }

        /// <summary>
        /// Load every <c>*.dll</c> in <paramref name="directory"/>, then start the
        /// modules they contain in dependency order. Non-module assemblies are
        /// skipped (a modules folder may also hold dependencies), as are
        /// assemblies already present in the process.
        /// </summary>
        public int LoadDirectory(string directory, string[]? args = null)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                GameRoot.Log.Warning("Module directory not found: '{Directory}'", directory);
                return 0;
            }

            var modules = new List<IV12Module>();

            foreach (string dll in Directory
                         .EnumerateFiles(directory, "*.dll")
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string simpleName = Path.GetFileNameWithoutExtension(dll);
                if (IsAssemblyLoaded(simpleName))
                {
                    GameRoot.Log.Debug("Skipping '{Name}': an assembly with that name is already loaded", simpleName);
                    continue;
                }

                try
                {
                    var assembly = LoadFile(dll);
                    if (!Claim(assembly)) continue;
                    modules.AddRange(Instantiate(assembly));
                }
                catch (Exception ex)
                {
                    GameRoot.Log.Warning(ex, "Failed to load module assembly '{Path}'", dll);
                }
            }

            if (modules.Count == 0)
            {
                GameRoot.Log.Information("No modules found in '{Directory}'", directory);
                return 0;
            }

            return RegisterRange(modules, args);
        }

        /// <summary>Load one module assembly. Returns true when a module started.</summary>
        public bool Load(string assemblyPath, string[]? args = null)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
                return false;

            string fullPath;
            try { fullPath = Path.GetFullPath(assemblyPath); }
            catch (Exception ex)
            {
                GameRoot.Log.Warning(ex, "Module path '{Path}' is not usable", assemblyPath);
                return false;
            }

            if (!File.Exists(fullPath))
            {
                GameRoot.Log.Warning("Module assembly not found: '{Path}'", fullPath);
                return false;
            }

            string simpleName = Path.GetFileNameWithoutExtension(fullPath);
            if (IsAssemblyLoaded(simpleName))
            {
                GameRoot.Log.Debug("Skipping '{Name}': an assembly with that name is already loaded", simpleName);
                return false;
            }

            try
            {
                var assembly = LoadFile(fullPath);
                return LoadAssembly(assembly, args, fullPath);
            }
            catch (Exception ex)
            {
                GameRoot.Log.Error(ex, "Failed to load module assembly '{Path}'", fullPath);
                return false;
            }
        }

        /// <summary>
        /// Start the modules in an assembly the host already loaded. Useful for
        /// built-in modules and tests; the assembly is only scanned once.
        /// </summary>
        public bool LoadAssembly(Assembly assembly, string[]? args = null, string? assemblyPath = null)
        {
            ArgumentNullException.ThrowIfNull(assembly);

            if (!Claim(assembly))
                return false;

            var modules = Instantiate(assembly);
            return modules.Count > 0 && RegisterRange(modules, args) > 0;
        }

        /// <summary>Register one already-constructed module.</summary>
        public bool Register(IV12Module module, string[]? args = null)
            => RegisterRange(new[] { module }, args) == 1;

        /// <summary>
        /// Start a batch of modules in dependency order. Modules whose dependencies
        /// are missing or cyclic are rejected and reported in
        /// <see cref="Diagnostics"/>; the rest still start.
        /// </summary>
        public int RegisterRange(IEnumerable<IV12Module> modules, string[]? args = null)
        {
            ArgumentNullException.ThrowIfNull(modules);

            string[] effectiveArgs = args ?? EmptyArgs;
            var candidates = new List<IV12Module>();

            foreach (var module in modules)
            {
                if (module is null) continue;

                if (string.IsNullOrWhiteSpace(module.Name))
                {
                    Reject(module, "module name is empty");
                    continue;
                }

                if (_modules.Any(m => Same(m.Name, module.Name)))
                {
                    Reject(module, $"a module named '{module.Name}' is already loaded");
                    continue;
                }

                if (candidates.Any(c => Same(c.Name, module.Name)))
                {
                    Reject(module, $"duplicate module name '{module.Name}' in this batch");
                    continue;
                }

                candidates.Add(module);
            }

            int started = 0;
            bool progress = true;

            while (candidates.Count > 0 && progress)
            {
                progress = false;

                // Forward order: modules without dependencies start in the order
                // they were given (directory order, host registration order).
                for (int i = 0; i < candidates.Count; )
                {
                    var module = candidates[i];

                    var missing = module.Dependencies
                        .Where(dependency => !_modules.Any(m => Same(m.Name, dependency)))
                        .ToList();

                    if (missing.Count > 0)
                    {
                        // A dependency may still be waiting in this batch.
                        if (missing.Any(dependency => candidates.Any(c => Same(c.Name, dependency))))
                        {
                            i++;
                            continue;
                        }

                        candidates.RemoveAt(i);
                        Reject(module, "missing dependency: " + string.Join(", ", missing));
                        progress = true;
                        continue;
                    }

                    candidates.RemoveAt(i);
                    if (StartOne(module, effectiveArgs)) started++;
                    progress = true;
                }
            }

            foreach (var module in candidates)
                Reject(module, "dependency cycle");

            return started;
        }

        /// <summary>Human-readable module status, one line per considered module.</summary>
        public string Describe()
        {
            if (_diagnostics.Count == 0)
                return "no modules considered";

            var builder = new StringBuilder();
            foreach (var info in _diagnostics)
            {
                builder.Append(info.Started ? "  [ok] " : "  [--] ").Append(info.Name);

                if (!string.IsNullOrWhiteSpace(info.Version))
                    builder.Append(' ').Append(info.Version);

                if (!info.Started && !string.IsNullOrWhiteSpace(info.Error))
                    builder.Append(": ").Append(info.Error);

                builder.AppendLine();
            }

            return builder.ToString();
        }

        /// <summary>Dispose every started module in reverse start order.</summary>
        public void DisposeAll()
        {
            for (int i = _modules.Count - 1; i >= 0; i--)
            {
                try { _modules[i].Dispose(); }
                catch (Exception ex) { GameRoot.Log.Warning(ex, "Module '{Name}' failed to dispose", _modules[i].Name); }
            }

            _modules.Clear();
            _loadedAssemblies.Clear();
        }

        // ── internals ─────────────────────────────────────────────────────

        private bool StartOne(IV12Module module, string[] args)
        {
            var type = module.GetType();
            string assemblyPath = SafeLocation(type.Assembly);
            string moduleDirectory = string.IsNullOrEmpty(assemblyPath)
                ? string.Empty
                : Path.GetDirectoryName(assemblyPath) ?? string.Empty;

            string dataDirectory = ModuleContext.GetDataDirectory(module.Name);
            try { Directory.CreateDirectory(dataDirectory); } catch { }

            var context = new ModuleContext(_root, args, assemblyPath, moduleDirectory, dataDirectory);

            try
            {
                if (!module.Start(context))
                {
                    _diagnostics.Add(Info(module, started: false, error: "declined to start"));
                    module.Dispose();
                    return false;
                }

                _modules.Add(module);
                _diagnostics.Add(Info(module, started: true, error: null));

                GameRoot.Log.Information("Started module '{Name}'{Version} ({Type})",
                    module.Name,
                    string.IsNullOrWhiteSpace(module.Version) ? string.Empty : " " + module.Version,
                    type.FullName);

                return true;
            }
            catch (Exception ex)
            {
                _diagnostics.Add(Info(module, started: false, error: ex.GetBaseException().Message));
                try { module.Dispose(); } catch { }

                GameRoot.Log.Error(ex, "Module '{Name}' ({Type}) failed to start", module.Name, type.FullName);
                return false;
            }
        }

        private void Reject(IV12Module module, string error)
        {
            _diagnostics.Add(Info(module, started: false, error));

            try { module.Dispose(); } catch { }

            GameRoot.Log.Warning("Module '{Name}' was not started: {Reason}", module.Name, error);
        }

        private List<IV12Module> Instantiate(Assembly assembly)
        {
            var modules = new List<IV12Module>();

            foreach (var type in GetTypes(assembly))
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition) continue;
                if (!typeof(IV12Module).IsAssignableFrom(type)) continue;

                try
                {
                    if (Activator.CreateInstance(type) is IV12Module module)
                        modules.Add(module);
                }
                catch (Exception ex)
                {
                    _diagnostics.Add(new ModuleInfo
                    {
                        Name = type.Name,
                        TypeName = type.FullName ?? type.Name,
                        AssemblyName = assembly.GetName().Name ?? string.Empty,
                        AssemblyPath = SafeLocation(assembly),
                        Started = false,
                        Error = ex.GetBaseException().Message,
                    });

                    GameRoot.Log.Error(ex, "Could not construct module type '{Type}'", type.FullName);
                }
            }

            return modules;
        }

        private static Type[] GetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                GameRoot.Log.Warning(ex, "Module assembly '{Assembly}' has type load errors", assembly.GetName().Name);
                return ex.Types.Where(t => t is not null).ToArray()!;
            }
        }

        private bool Claim(Assembly assembly)
        {
            string identity = assembly.FullName ?? SafeLocation(assembly);
            if (!_loadedAssemblies.Add(identity))
            {
                GameRoot.Log.Debug("Module assembly '{Assembly}' was already scanned", assembly.GetName().Name);
                return false;
            }

            return true;
        }

        private static bool IsAssemblyLoaded(string simpleName)
            => AppDomain.CurrentDomain.GetAssemblies().Any(a =>
                string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));

        private static Assembly LoadFile(string path)
            => GamepackLoader.AssemblyLoader?.Invoke(path) ?? Assembly.LoadFrom(path);

        private static string SafeLocation(Assembly assembly)
        {
            try { return assembly.Location ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static ModuleInfo Info(IV12Module module, bool started, string? error) => new()
        {
            Name = module.Name,
            Version = module.Version,
            TypeName = module.GetType().FullName ?? module.GetType().Name,
            AssemblyName = module.GetType().Assembly.GetName().Name ?? string.Empty,
            AssemblyPath = SafeLocation(module.GetType().Assembly),
            Started = started,
            Error = error,
            Dependencies = module.Dependencies.ToArray(),
        };

        private static bool Same(string left, string right)
            => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
