using System;
using System.IO;
using System.IO.Compression;
using V12.Core.Interfaces;
using V12.WorldML;

namespace V12.Core
{
    public static class WorldLoader
    {
        [Obsolete("Use V12PakLoader with .v12pak archives instead. This method remains for backward compatibility with .v12world ZIP files.")]
        public static World LoadFromArchive(string archivePath)
        {
            var resolver = new V12AssetResolver();
            var templates = new WorldTemplateProvider();
            var result = LoadFromArchive(archivePath, resolver, templates);
            var world = result.World;
            world.ExtractPath = result.TempDirectory;
            world.MountPoint = result.MountPoint;

            var root = GameRoot.Instance;
            if (root != null)
            {
                root.Registry?.Register("AssetResolver", resolver);
                root.Registry?.Register("TemplateProvider", templates);
                if (!root.Worlds.Contains(world))
                    root.Worlds.Add(world);
                Console.WriteLine($"[WorldLoader] Registered resolver, added world '{world.WorldName}' (Worlds count={root.Worlds.Count})");
            }
            else
            {
                Console.Error.WriteLine("[WorldLoader] CRITICAL: GameRoot.Instance is NULL — resolver NOT registered");
            }

            return world;
        }

        public static WorldLoadResult LoadFromArchive(string archivePath, IAssetResolver resolver, ITemplateProvider templates)
        {
            if (!File.Exists(archivePath))
                throw new FileNotFoundException($"Archive not found: {archivePath}");

            string worldName = Path.GetFileNameWithoutExtension(archivePath);
            string tempDir = Path.Combine(
                Path.GetTempPath(), "V12Worlds",
                worldName + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            ZipFile.ExtractToDirectory(archivePath, tempDir);

            // Mount the temp directory as v12://{worldName}
            resolver.Mount(worldName, tempDir);

            // Load templates from templates/ folder if present
            string templatesDir = Path.Combine(tempDir, "templates");
            if (templates is WorldTemplateProvider wtp)
                wtp.LoadFromDirectory(templatesDir);

            // Parse main.xml
            string mainXmlPath = Path.Combine(tempDir, "world.xml");
            if (!File.Exists(mainXmlPath))
            {
                mainXmlPath = Path.Combine(tempDir, "main.xml");
                if (!File.Exists(mainXmlPath))
                {
                    resolver.Unmount(worldName);
                    Directory.Delete(tempDir, true);
                    throw new FileNotFoundException($"Could not find world.xml or main.xml in archive: {archivePath}");
                }
            }

            // Set the template provider on the parser
            var parser = new WorldMLParser();
            if (templates != null)
                parser.TemplateProvider = templates;

            var worldContents = parser.ParseFile(mainXmlPath);
            var world = new World(worldContents.Name ?? worldName)
            {
                ExtractPath = tempDir,
                MountPoint = worldName
            };
            world.AddElement(worldContents);

            return new WorldLoadResult
            {
                World = world,
                TempDirectory = tempDir,
                MountPoint = worldName
            };
        }

        public static void Cleanup(World world, IAssetResolver resolver)
        {
            if (!string.IsNullOrEmpty(world.MountPoint))
                resolver?.Unmount(world.MountPoint);

            if (!string.IsNullOrEmpty(world.ExtractPath) && Directory.Exists(world.ExtractPath))
            {
                try { Directory.Delete(world.ExtractPath, true); }
                catch { /* best effort */ }
            }
        }
    }

    public class WorldLoadResult
    {
        public World World { get; set; }
        public string TempDirectory { get; set; }
        public string MountPoint { get; set; }
    }
}
