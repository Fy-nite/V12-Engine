using System;
using System.IO;
using System.IO.Compression;
using V12.Core;
using V12.WorldML;

namespace V12.Core
{
    public static class WorldLoader
    {
        public static World LoadFromArchive(string archivePath)
        {
            if (!File.Exists(archivePath))
                throw new FileNotFoundException($"Archive not found: {archivePath}");

            string tempDir = Path.Combine(Path.GetTempPath(), "V12Worlds", Path.GetFileNameWithoutExtension(archivePath) + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            
            ZipFile.ExtractToDirectory(archivePath, tempDir);
            
            string mainXmlPath = Path.Combine(tempDir, "main.xml");
            if (!File.Exists(mainXmlPath))
            {
                throw new FileNotFoundException($"Could not find main.xml in archive: {archivePath}");
            }
            
            var parser = new WorldMLParser();
            var worldcontents = parser.ParseFile(mainXmlPath);
            var world = new World(worldcontents.Name);
            world.AddElement(worldcontents);
            return world;
        }
    }
}
