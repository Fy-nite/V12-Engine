using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using V12.Rendering;

namespace V12.Core.Rendering;

/// <summary>
/// Registry of V12 shader assets. Built-ins (for example the Paper UI shaders)
/// are registered by their owning library; mounted .v12shader files can be
/// registered from their manifest JSON. The renderer receives a plain
/// <see cref="ShaderProgram"/> with source and reflection.
/// </summary>
public sealed class ShaderAssetRegistry
{
    private sealed class Entry
    {
        public ShaderProgram Program;
        public long Version;
    }

    public static ShaderAssetRegistry Instance { get; } = new();

    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string uri, in ShaderProgram program, long version = 1)
    {
        if (string.IsNullOrWhiteSpace(uri))
            throw new ArgumentException("Shader URI cannot be empty.", nameof(uri));

        lock (_lock)
        {
            _entries[uri] = new Entry { Program = program, Version = version };
        }
    }

    public void RegisterJson(string uri, string json)
    {
        if (string.IsNullOrWhiteSpace(uri))
            throw new ArgumentException("Shader URI cannot be empty.", nameof(uri));
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Shader manifest cannot be empty.", nameof(json));

        var manifest = JsonSerializer.Deserialize<Manifest>(json, JsonOptions)
                       ?? throw new InvalidDataException($"Shader manifest '{uri}' is empty or invalid.");

        var parameters = new List<ShaderParameter>(manifest.Parameters.Count);
        foreach (var p in manifest.Parameters)
        {
            parameters.Add(new ShaderParameter(
                p.Name ?? string.Empty,
                p.Index,
                ParseKind(p.Kind)));
        }

        var textures = new List<ShaderTextureSlot>(manifest.Textures.Count);
        foreach (var t in manifest.Textures)
            textures.Add(new ShaderTextureSlot(t.Name ?? string.Empty, t.Slot));

        string? source = null;
        ShaderSourceLanguage language = ShaderSourceLanguage.None;

        if (manifest.Backends is not null)
        {
            if (!string.IsNullOrWhiteSpace(manifest.Backends.GodotSpatial))
            {
                source = manifest.Backends.GodotSpatial;
                language = ShaderSourceLanguage.GodotSpatial;
            }
            else if (!string.IsNullOrWhiteSpace(manifest.Backends.GodotCanvasItem))
            {
                source = manifest.Backends.GodotCanvasItem;
                language = ShaderSourceLanguage.GodotCanvasItem;
            }
        }

        if (string.IsNullOrWhiteSpace(source))
            throw new InvalidDataException($"Shader manifest '{uri}' has no supported backend source.");

        var reflection = new ShaderReflection(
            parameters,
            textures,
            Array.Empty<VertexAttribute>());

        var program = new ShaderProgram(
            BuiltIn: BuiltInShader.None,
            Reflection: reflection,
            AssetUri: uri,
            Source: source,
            SourceLanguage: language);

        Register(uri, program, manifest.Version <= 0 ? 1 : manifest.Version);
    }

    public bool TryResolve(string uri, out ShaderProgram program, out long version)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(uri, out var entry))
            {
                program = entry.Program;
                version = entry.Version;
                return true;
            }
        }

        program = default;
        version = 0;
        return false;
    }

    public ShaderProgram Resolve(string uri)
    {
        if (TryResolve(uri, out var program, out _))
            return program;
        throw new InvalidOperationException($"Shader asset '{uri}' is not registered.");
    }

    private static MaterialValueKind ParseKind(string? kind)
    {
        return Enum.TryParse<MaterialValueKind>(kind, true, out var value)
            ? value
            : MaterialValueKind.Float;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed class Manifest
    {
        public string? Name { get; set; }
        public int Version { get; set; } = 1;
        public List<ParameterManifest> Parameters { get; set; } = new();
        public List<TextureManifest> Textures { get; set; } = new();
        public BackendManifest? Backends { get; set; }
    }

    private sealed class ParameterManifest
    {
        public string? Name { get; set; }
        public int Index { get; set; }
        public string? Kind { get; set; }
    }

    private sealed class TextureManifest
    {
        public string? Name { get; set; }
        public int Slot { get; set; }
    }

    private sealed class BackendManifest
    {
        [JsonPropertyName("godot_spatial")]
        public string? GodotSpatial { get; set; }

        [JsonPropertyName("godot_canvas_item")]
        public string? GodotCanvasItem { get; set; }
    }
}
