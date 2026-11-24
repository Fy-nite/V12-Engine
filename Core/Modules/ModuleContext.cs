using System;
using System.IO;
using System.Text;
using Serilog;

namespace V12.Core.Modules
{
    /// <summary>
    /// Everything a module needs to attach to the running game. Passed to
    /// <see cref="IV12Module.Start"/> so the module never reaches for globals to
    /// discover its own paths or arguments.
    /// </summary>
    public sealed class ModuleContext
    {
        public ModuleContext(
            GameRoot root,
            string[] args,
            string assemblyPath,
            string moduleDirectory,
            string dataDirectory)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            Args = args ?? Array.Empty<string>();
            AssemblyPath = assemblyPath ?? string.Empty;
            ModuleDirectory = moduleDirectory ?? string.Empty;
            DataDirectory = dataDirectory ?? string.Empty;
        }

        /// <summary>The live game.</summary>
        public GameRoot Root { get; }

        /// <summary>
        /// The full command line the host received (engine arguments plus user
        /// arguments after <c>--</c>). Modules parse their own switches from this.
        /// </summary>
        public string[] Args { get; }

        /// <summary>Absolute path of the module assembly, or empty when unknown.</summary>
        public string AssemblyPath { get; }

        /// <summary>Directory the module assembly was loaded from.</summary>
        public string ModuleDirectory { get; }

        /// <summary>
        /// Per-module writable directory under the user's local application data.
        /// Created before <see cref="IV12Module.Start"/> when possible; use it for
        /// settings, caches and logs instead of writing next to the game.
        /// </summary>
        public string DataDirectory { get; }

        public ILogger Log => GameRoot.Log;

        /// <summary>
        /// Stable per-module data directory. The id is sanitised so a module name
        /// cannot escape the modules root.
        /// </summary>
        public static string GetDataDirectory(string moduleName)
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
                root = Path.GetTempPath();

            return Path.Combine(root, "V12", "modules", Sanitise(moduleName));
        }

        private static string Sanitise(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "module";

            var builder = new StringBuilder(name.Length);
            foreach (char character in name)
            {
                builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_'
                    ? character
                    : '_');
            }

            return builder.ToString();
        }
    }
}
