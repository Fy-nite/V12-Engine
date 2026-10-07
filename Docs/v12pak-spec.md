# V12 Pak Loading System

## Summary

Replace `.v12world` ZIP archives with a unified `.v12pak` tar.gz format that bundles worlds, assets, scripts, and optionally C# DLL gamepaks into a single distributable file. The pak system lives in a separate `V12.Pak` library so it can be consumed optionally. A security options layer controls what gets loaded from user-generated paks.

## Motivation

V12 currently loads game content via two disconnected mechanisms:

1. **`.v12world` ZIP archives** — contain a single world's XML, templates, and loose asset files. No scripting, no DLLs, no multi-world support.
2. **`GamepackLoader`** — scans directories for C# DLLs implementing `IV12Gamepack`. No packaging, no distribution format.

Neither supports a self-contained game distribution. There is no way to ship a game as a single file that includes worlds, meshes, textures, scripts, and game logic. The pak system solves this.

### Why not extend `.v12world`?

- ZIP format lacks built-in streaming (must extract to disk before reading).
- Single-world-only design doesn't support multi-world games.
- No asset manifest, no DLL bundling, no security controls.
- A clean break allows a better format (tar.gz) and a proper asset index.

## File Format

A `.v12pak` file is a **tar.gz** archive (gzip-compressed POSIX tar). This uses only .NET BCL types (`System.Formats.Tar` + `System.IO.Compression.GZipStream`), requires no external packages, and allows streaming reads without full extraction.

### Archive Layout

```
game.v12pak
├── manifest.json              # Index: worlds, asset map, DLL list, metadata
├── worlds/
│   └── {WorldName}/
│       ├── world.xml          # WorldML scene definition
│       └── templates/         # XML element templates
├── assets/
│   ├── meshes/                # GLB, GLTF, OBJ model files
│   ├── textures/              # PNG, JPG image files
│   └── scripts/               # Lua (.lua), or any IScriptRuntime-supported file
└── paks/                      # C# DLL gamepaks (optional)
    └── *.dll
```

### manifest.json

```json
{
  "version": 1,
  "engine": "v12",
  "worlds": [
    {
      "name": "MyWorld",
      "entry": "worlds/MyWorld/world.xml",
      "templatesDir": "worlds/MyWorld/templates/"
    }
  ],
  "assets": {
    "textures/crate.png": "assets/textures/crate.png",
    "models/hero.glb": "assets/meshes/models/hero.glb",
    "scripts/player.lua": "assets/scripts/player.lua"
  },
  "paks": ["paks/mygame.dll"]
}
```

| Field | Type | Description |
|-------|------|-------------|
| `version` | `int` | Manifest format version. Currently `1`. |
| `engine` | `string` | Engine identifier. Always `"v12"`. |
| `worlds` | `WorldEntry[]` | Worlds contained in this pak. A pak can hold multiple worlds. |
| `worlds[].name` | `string` | Human-readable world name. Used as the `v12://` mount point. |
| `worlds[].entry` | `string` | Path inside the tar archive to `world.xml`. |
| `worlds[].templatesDir` | `string` | Path inside the tar archive to the templates directory. |
| `assets` | `{string: string}` | Virtual path to archive path mapping. Keys are `v12://` resolvable paths, values are tar entry paths. |
| `paks` | `string[]` | Archive paths to C# DLL gamepaks. Loaded via `GamepackLoader`. |

## Architecture

### Separate Library

The pak system is a new project: `libs/V12.Pak/V12.Pak.csproj`.

- Targets `net10.0`.
- References `V12.csproj` (the engine library).
- V12.Host or any consuming project references it optionally.
- V12-engine itself has **no dependency** on V12.Pak.

```
V12-engine (core library, no pak awareness)
    ^
    |
V12.Pak (optional pak loading library)
    ^
    |
V12.Host (application, references both)
```

### New Files

| File | Purpose |
|------|---------|
| `V12.Pak/V12.Pak.csproj` | Library project file. |
| `V12.Pak/V12PakManifest.cs` | Data model for `manifest.json`. |
| `V12.Pak/V12PakReader.cs` | Opens `.v12pak` tar.gz, reads manifest, provides stream access to entries. |
| `V12.Pak/V12PakWriter.cs` | Builds `.v12pak` tar.gz from a directory of game content. |
| `V12.Pak/V12PakAssetResolver.cs` | `IAssetResolver` implementation that reads from pak entries. |
| `V12.Pak/V12PakLoader.cs` | High-level orchestrator: open, mount, load worlds, load DLLs. |
| `V12.Pak/V12PakOptions.cs` | Security/loading options for controlling what gets imported. |
| `V12.Pak/README.md` | User-facing documentation. |

### Modified Files

| File | Change |
|------|--------|
| `V12-engine/Components/ScriptComponent.cs` | Replace hardcoded `new MoonSharpScriptRuntime()` with `ScriptRuntimeRegistry` lookup by file extension. |
| `V12-engine/Core/Interfaces/IScriptRuntime.cs` | Add `string[] SupportedExtensions` property. |
| `V12-engine/Core/WorldLoader.cs` | Mark `LoadFromArchive()` as `[Obsolete]`. Add `LoadFromPak()` static method that takes a `V12PakReader`. |
| `V12-engine/Core/GameRoot.cs` | Add `LoadPak(string path, V12PakOptions?)` and `BuildPak(string inputDir, string outputPath)` methods. |
| `V12.Host/V12.Host.csproj` | Add `<ProjectReference>` to `V12.Pak.csproj`. |

## API Design

### Loading a Pak

```csharp
// Full trust: load everything including DLLs
root.LoadPak("official_content.v12pak");

// Restricted: no DLLs, only allowed asset types
root.LoadPak("user_world.v12pak", new V12PakOptions
{
    LoadDlls = false,
    AllowedAssetExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".xml", ".glb", ".gltf", ".obj", ".png", ".jpg", ".lua"
    }
});
```

### Building a Pak

```csharp
// Scan a directory and pack everything into a .v12pak
GameRoot.BuildPak("./mygame_build", "game.v12pak");
```

### Low-Level Reader/Writer

```csharp
// Reading
using var reader = new V12PakReader("game.v12pak");
var manifest = reader.Manifest;
Stream meshStream = reader.OpenEntry("assets/meshes/hero.glb");

// Writing
using var writer = new V12PakWriter("output.v12pak");
writer.AddFile("worlds/MyWorld/world.xml", worldXmlStream);
writer.AddFile("assets/textures/crate.png", textureStream);
writer.WriteManifest(manifest);
```

## Security: V12PakOptions

When loading paks from untrusted sources (user-generated content, community mods), the game developer controls what the loader imports.

```csharp
public class V12PakOptions
{
    /// <summary>
    /// Whether to load DLL gamepaks from the pak. Default: false.
    /// When false, any DLLs in the pak's paks/ directory are skipped entirely.
    /// </summary>
    public bool LoadDlls { get; set; } = false;

    /// <summary>
    /// File extensions allowed to be imported as assets.
    /// Null = allow all extensions. Empty set = block all assets.
    /// Checked against the archive path of each asset in the manifest.
    /// </summary>
    public HashSet<string>? AllowedAssetExtensions { get; set; } = null;

    /// <summary>
    /// Specific asset paths to block (exact match against manifest asset keys).
    /// Checked after the extension whitelist.
    /// </summary>
    public HashSet<string>? BlockedAssetKeys { get; set; } = null;

    /// <summary>
    /// Whether to allow the pak's manifest to declare worlds for loading. Default: true.
    /// Set to false to only import assets without loading any worlds.
    /// </summary>
    public bool AllowWorldLoading { get; set; } = true;
}
```

### Loading Flow

```
V12PakLoader.Load(pakPath, options):

  1. Open pak → V12PakReader
  2. Read manifest.json
  3. Create V12PakAssetResolver wrapping the reader
  4. For each world in manifest.worlds:
     a. If !options.AllowWorldLoading → skip
     b. Parse world.xml via WorldMLParser
     c. Load templates from templatesDir
     d. Mount resolver into registry as "AssetResolver"
     e. Add world to GameRoot.Worlds
  5. For each asset in manifest.assets:
     a. Extract file extension from the archive path
     b. If AllowedAssetExtensions is set and extension not in set → skip, log
     c. If BlockedAssetKeys contains the key → skip, log
     d. Asset is now readable through the resolver
  6. For each DLL in manifest.paks:
     a. If !options.LoadDlls → skip, log warning
     b. Extract DLL to temp dir (%TEMP%/V12Paks/{pakName}/)
     c. Load via GamepackLoader.LoadFromDirectory()
  7. Return loaded world(s)
```

### Default Trust Levels

| Scenario | LoadDlls | AllowedAssetExtensions |
|----------|----------|----------------------|
| First-party game | `true` | `null` (all) |
| Official DLC/expansion | `true` | `null` (all) |
| User-generated world | `false` | `.xml`, `.glb`, `.gltf`, `.obj`, `.png`, `.jpg`, `.lua` |
| Minimal/import-only | `false` | `.xml` only |

## Scripting Generality

### Problem

`ScriptComponent` currently hardcodes `new MoonSharpScriptRuntime()`. This prevents loading paks with scripts in other languages (C#, Python, etc.) and tightly couples the engine to Lua.

### Solution

1. **`IScriptRuntime` gets a `SupportedExtensions` property** so runtimes declare what file types they handle.

2. **A `ScriptRuntimeRegistry`** maps file extensions to runtime factories. Registered at startup or by gamepaks.

3. **`ScriptComponent.Initialize()`** looks up the correct runtime by the script file's extension.

```csharp
// In IScriptRuntime
public interface IScriptRuntime : IDisposable
{
    string[] SupportedExtensions { get; }
    void Load(string source, string scriptName);
    void Call(string functionName, params object[] args);
    void SetGlobal(string name, object value);
    object? GetGlobal(string name);
    event Action<string> OnPrint;
    bool SupportsHotReload { get; }
}

// In ScriptRuntimeRegistry (registered as a service on GameRoot)
public class ScriptRuntimeRegistry
{
    public ScriptRuntimeRegistry Register(Func<IScriptRuntime> factory);
    public IScriptRuntime? CreateForExtension(string extension);
    public IScriptRuntime CreateForScript(string scriptPath);
}

// In ScriptComponent.Initialize()
var registry = GameRoot.Instance?.Registry.Get<ScriptRuntimeRegistry>();
Runtime = registry?.CreateForScript(Source) ?? new MoonSharpScriptRuntime();
string ext = Path.GetExtension(Source);
Runtime = ScriptRuntimeRegistry.CreateForExtension(ext)
    ?? throw new InvalidOperationException($"No script runtime registered for '{ext}'");
```

4. **MoonSharp registers itself** at startup:
   ```csharp
   ScriptRuntimeRegistry.Register(".lua", () => new MoonSharpScriptRuntime());
   ```

5. **C# scripts ship as pre-compiled DLLs** in the pak's `paks/` directory. They implement `IV12Gamepack` and are loaded by `GamepackLoader`, not by `ScriptComponent`. This avoids runtime compilation.

## tar.gz Implementation

Uses only .NET BCL types. No external NuGet packages.

### Reading

```csharp
public class V12PakReader : IDisposable
{
    private readonly FileStream _fileStream;
    private readonly GZipStream _gzipStream;
    private readonly TarReader _tarReader;
    private readonly Dictionary<string, TarEntry> _index = new();

    public V12PakManifest Manifest { get; }

    public V12PakReader(string pakPath)
    {
        _fileStream = File.OpenRead(pakPath);
        _gzipStream = new GZipStream(_fileStream, CompressionMode.Decompress);
        _tarReader = new TarReader(_gzipStream);

        // Index all entries
        TarEntry? entry;
        while ((entry = _tarReader.GetNextEntry()) != null)
        {
            _index[entry.Name] = entry;
            if (entry.Name == "manifest.json")
                Manifest = JsonSerializer.Deserialize<V12PakManifest>(entry.DataStream);
        }
    }

    public Stream? OpenEntry(string pakPath)
    {
        if (_index.TryGetValue(pakPath, out var entry))
            return entry.DataStream;
        return null;
    }

    public bool HasEntry(string pakPath) => _index.ContainsKey(pakPath);

    public void Dispose()
    {
        _tarReader?.Dispose();
        _gzipStream?.Dispose();
        _fileStream?.Dispose();
    }
}
```

### Writing

```csharp
public class V12PakWriter : IDisposable
{
    private readonly TarWriter _tarWriter;
    private readonly GZipStream _gzipStream;
    private readonly FileStream _fileStream;

    public V12PakWriter(string outputPath)
    {
        _fileStream = File.Create(outputPath);
        _gzipStream = new GZipStream(_fileStream, CompressionLevel.Optimal);
        _tarWriter = new TarWriter(_gzipStream);
    }

    public void AddFile(string archivePath, Stream data);
    public void AddFile(string archivePath, byte[] data);
    public void AddDirectory(string archivePath);
    public void WriteManifest(V12PakManifest manifest);
    public void Dispose();
}
```

### Build API

```csharp
// On GameRoot
public static void BuildPak(string inputDirectory, string outputPath)
{
    var writer = new V12PakWriter(outputPath);
    var manifest = new V12PakManifest { Version = 1, Engine = "v12" };

    // Scan inputDirectory for worlds, assets, DLLs
    // Write manifest.json first
    // Then write all files
    writer.WriteManifest(manifest);
    writer.Dispose();
}
```

## Backwards Compatibility

- `WorldLoader.LoadFromArchive()` is marked `[Obsolete("Use V12PakLoader instead")]` but continues to work.
- Existing `.v12world` ZIP files can be converted to `.v12pak` by packing them into the tar.gz format.
- The pak loader can optionally also check for loose files on disk as a fallback (useful during development).

## Testing Plan

1. **Unit tests for V12PakReader/Writer** — create a pak, read it back, verify manifest and entries.
2. **Unit tests for V12PakOptions** — verify extension whitelist, blocklist, and DLL toggle work correctly.
3. **Integration test** — build a pak from SampleGame assets, load it, verify the world renders.
4. **Security test** — load a pak with DLLs but `LoadDlls = false`, verify DLLs are not executed.
5. **Scripting test** — register a mock `IScriptRuntime`, load a pak with `.test` scripts, verify the correct runtime is instantiated.
