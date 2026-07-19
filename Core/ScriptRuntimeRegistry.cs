using System;
using System.Collections.Generic;
using V12.Core.Interfaces;

namespace V12.Core
{
    /// <summary>
    /// Registry of available script runtimes. Maps file extensions to runtime factories.
    /// ScriptComponent uses this to select the correct runtime for a given script file.
    /// </summary>
    public class ScriptRuntimeRegistry
    {
        private readonly List<(string[] Extensions, Func<IScriptRuntime> Factory)> _runtimes = new();
        private IScriptRuntime? _default;

        /// <summary>
        /// Register a script runtime for the extensions it handles.
        /// The first registered runtime for a given extension wins.
        /// </summary>
        public ScriptRuntimeRegistry Register(Func<IScriptRuntime> factory)
        {
            using var rt = factory();
            _runtimes.Add((rt.SupportedExtensions, factory));

            // The first registered runtime becomes the default
            _default ??= factory();
            return this;
        }

        /// <summary>
        /// Create a new runtime instance for the given file extension.
        /// Returns null if no runtime supports this extension.
        /// </summary>
        public IScriptRuntime? CreateForExtension(string extension)
        {
            string ext = extension.StartsWith(".") ? extension : "." + extension;

            foreach (var (extensions, factory) in _runtimes)
            {
                foreach (string e in extensions)
                {
                    if (string.Equals(e, ext, StringComparison.OrdinalIgnoreCase))
                        return factory();
                }
            }

            return null;
        }

        /// <summary>
        /// Create a new default runtime instance (first registered).
        /// </summary>
        public IScriptRuntime? CreateDefault()
        {
            return _default != null ? _default : null;
        }

        /// <summary>
        /// Create a runtime based on the script file path, inferring extension.
        /// Falls back to the default runtime if extension is not recognized.
        /// </summary>
        public IScriptRuntime CreateForScript(string scriptPath)
        {
            if (!string.IsNullOrEmpty(scriptPath))
            {
                string ext = System.IO.Path.GetExtension(scriptPath);
                var runtime = CreateForExtension(ext);
                if (runtime != null) return runtime;
            }

            if (_default != null) return _default;

            throw new InvalidOperationException("No script runtimes registered.");
        }
    }
}
