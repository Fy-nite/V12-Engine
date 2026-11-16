using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace V12.Core.Rendering;

/// <summary>
/// Loads <c>.v12shader</c> manifests into <see cref="ShaderAssetRegistry"/> so a
/// <c>MaterialComponent.ShaderUri</c> resolves at render time.
///
/// A manifest's <c>name</c> becomes the URI (<c>shader://&lt;name&gt;</c>). When
/// no name is present the file name without its extension is used, so
/// <c>Shaders/toon.v12shader</c> is <c>shader://toon</c>.
///
/// Hosts call this during startup, before the first frame; a gamepak can also
/// call <see cref="ShaderAssetRegistry.RegisterJson"/> directly for shaders it
/// generates at runtime.
/// </summary>
public static class ShaderAssetLoader
{
    public const string Scheme = "shader://";
    public const string Extension = ".v12shader";

    /// <summary>Scans each existing directory (recursively) for shader manifests.</summary>
    public static int LoadDirectories(IEnumerable<string?>? directories)
    {
        if (directories is null)
            return 0;

        int loaded = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? directory in directories)
        {
            if (string.IsNullOrWhiteSpace(directory) || !seen.Add(Path.GetFullPath(directory)))
                continue;
            loaded += LoadDirectory(directory);
        }

        return loaded;
    }

    /// <summary>Scans one directory (recursively) for shader manifests.</summary>
    public static int LoadDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return 0;

        var files = Directory
            .EnumerateFiles(directory, "*" + Extension, SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        return LoadFiles(files);
    }

    /// <summary>Loads a fixed list of manifest paths.</summary>
    public static int LoadFiles(IEnumerable<string>? files)
    {
        if (files is null)
            return 0;

        int loaded = 0;
        foreach (string path in files)
        {
            if (LoadFile(path))
                loaded++;
        }

        return loaded;
    }

    /// <summary>Loads one manifest. Returns false and logs when it cannot be used.</summary>
    public static bool LoadFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            string json = File.ReadAllText(path);
            string uri = Scheme + ResolveName(json, path);
            ShaderAssetRegistry.Instance.RegisterJson(uri, json);
            return true;
        }
        catch (Exception ex)
        {
            GameRoot.Log?.Warning(ex, "[Shader] Could not load '{Path}'", path);
            return false;
        }
    }

    private static string ResolveName(string json, string path)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("name", out var nameElement) &&
                nameElement.ValueKind == JsonValueKind.String)
            {
                string? name = nameElement.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                    return Normalize(name!);
            }
        }
        catch (JsonException)
        {
            // The registry reports the parse error with the manifest URI; the
            // file name is still a usable fallback.
        }

        return Normalize(Path.GetFileNameWithoutExtension(path));
    }

    private static string Normalize(string name)
    {
        string trimmed = name.Trim();
        return trimmed.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)
            ? trimmed.Substring(Scheme.Length)
            : trimmed.Replace('\\', '/');
    }
}
