using System;
using System.Collections.Generic;

namespace V12.Core.Mods
{
    /// <summary>Version of the `module.json` contract understood by this engine.</summary>
    public static class ModuleApi
    {
        /// <summary>Bump when a manifest field changes meaning or is removed.</summary>
        public const int Version = 1;

        /// <summary>Entry name of the manifest inside a mod pak.</summary>
        public const string ManifestFile = "module.json";
    }

    /// <summary>
    /// `module.json` inside a mod pak. A mod is an ordinary <c>.v12pak</c> whose
    /// root contains this file; the rest of the archive uses the normal pak layout
    /// (worlds, assets, paks, bin, scripts).
    /// </summary>
    public sealed class ModuleManifest
    {
        /// <summary>Manifest contract version. A mismatch refuses to load.</summary>
        public int ApiVersion { get; set; } = ModuleApi.Version;

        /// <summary>Unique mod id. Stable across versions; dependency targets use this.</summary>
        public string Id { get; set; } = "";

        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Author { get; set; }
        public string? Description { get; set; }

        /// <summary>Mod ids or module ids that must be loaded/started first.</summary>
        public List<string> Dependencies { get; set; } = new();
    }

    /// <summary>Diagnostic record for one mod pak the host considered.</summary>
    public sealed class ModInfo
    {
        public string Id { get; init; } = "";
        public string? Name { get; init; }
        public string? Version { get; init; }
        public string? Author { get; init; }
        public string? Description { get; init; }
        public string? PakPath { get; init; }
        public int ApiVersion { get; init; }
        public bool Enabled { get; init; }
        public bool Loaded { get; init; }
        /// <summary>Why it did not load: disabled, API mismatch, missing dependency, or an exception.</summary>
        public string? Error { get; init; }
        public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Loads mod paks into a running game. Implemented by <c>V12.Pak</c> so the
    /// engine never references the archive reader; hosts register the
    /// implementation in the registry under <c>"Mods"</c>, which is how the
    /// introspection probe and the editor discover it.
    /// </summary>
    public interface IModHost
    {
        IReadOnlyList<ModInfo> Mods { get; }

        /// <summary>
        /// Load from the command line: <c>--mods [dir]</c>, <c>--mod &lt;pak&gt;</c>,
        /// <c>--disable-mod &lt;id&gt;</c>, <c>--no-mods</c>. Returns mods loaded.
        /// </summary>
        int EnableFromArgs(string[] args, string? baseDirectory = null);

        /// <summary>Load every <c>.v12pak</c> in a directory that contains a `module.json`.</summary>
        int LoadDirectory(string directory, string[]? args = null);

        /// <summary>Load one mod pak. Returns false for non-mod paks and failures.</summary>
        bool LoadPak(string pakPath, string[]? args = null);

        /// <summary>Enable or disable a mod id and persist the choice.</summary>
        bool SetEnabled(string id, bool enabled);

        /// <summary>Human-readable status, one line per considered mod.</summary>
        string Describe();
    }
}
