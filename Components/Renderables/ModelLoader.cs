using Assimp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using V12.Core;
using V12.Core.Interfaces;
using NMatrix4x4 = System.Numerics.Matrix4x4;

namespace V12.Components.Renderables
{
    /// <summary>
    /// One material authored in a model file, flattened out of Assimp into plain
    /// managed values. Texture paths are already resolved (embedded images are
    /// extracted to a temp cache first), so nothing here points at native memory.
    /// </summary>
    public sealed class ModelMaterial
    {
        public string Name = "";

        public Vector4 Albedo = Vector4.One;
        public float Metallic;
        public float Roughness = 1f;
        public Vector4 Emission;
        public float EmissionStrength = 1f;

        public float NormalScale = 1f;
        public float AlphaCutoff = 0.5f;
        public bool AlphaBlend;
        public bool DoubleSided;
        public bool Unlit;

        /// <summary>UV set the material's textures address (glTF often uses 1).</summary>
        public int UvChannel;

        public string? AlbedoTexture;
        public string? NormalTexture;
        public string? MetallicTexture;
        public string? RoughnessTexture;
        public string? EmissionTexture;
        public string? OcclusionTexture;

        public bool HasAnyTexture =>
            AlbedoTexture is not null || NormalTexture is not null ||
            MetallicTexture is not null || RoughnessTexture is not null ||
            EmissionTexture is not null || OcclusionTexture is not null;
    }

    /// <summary>One mesh primitive: geometry in its NODE-LOCAL space plus the
    /// material it draws with. Splitting geometry per node is what makes a
    /// reconstructed hierarchy possible.</summary>
    public sealed class ModelMeshPart
    {
        public string Name = "";
        public double[] Points = Array.Empty<double>();
        public double[]? Normals;
        public double[]? Uvs;
        public double[]? Tangents;
        public uint[] Indices = Array.Empty<uint>();
        public int MaterialIndex;

        /// <summary>True when the source mesh carried no UV set at all, so the
        /// surface has to be projected (triplanar) rather than sampled.</summary>
        public bool NeedsTriplanar;
    }

    /// <summary>A node in the model's own hierarchy, with its local transform
    /// intact so callers can rebuild the tree instead of baking it flat.</summary>
    public sealed class ModelNode
    {
        public string Name = "";
        public Matrix4x4 LocalTransform = Matrix4x4.Identity;
        public List<ModelMeshPart> Parts = new();
        public List<ModelNode> Children = new();
    }

    /// <summary>A loaded model: the node hierarchy, the materials, and a
    /// world-baked triangle soup for callers that want one flat mesh.</summary>
    public sealed class ModelDocument
    {
        public string ResolvedPath = "";
        public ModelNode Root = new();
        public List<ModelMaterial> Materials = new();
        public int DominantMaterialIndex;

        // Flattened output (every node transform baked in).
        public double[] Points = Array.Empty<double>();
        public double[]? Normals;
        public double[]? Uvs;
        public double[]? Tangents;
        public uint[] Indices = Array.Empty<uint>();
    }

    /// <summary>
    /// Imports a model file with Assimp and converts it into plain managed data:
    /// a node hierarchy (local transforms preserved), per-node mesh parts, and
    /// the materials those parts draw with. The Assimp context is disposed as
    /// soon as the conversion finishes, so nothing retains native memory.
    /// </summary>
    public static class ModelLoader
    {
        public static ModelDocument Load(string assetPath)
        {
            string resolvedPath = ResolvePath(assetPath);

            using var context = new AssimpContext();
            // Deliberately NOT FlipUVs: glTF is top-left like Godot and Vulkan.
            // V is flipped per-format below. Tangents come from the file's own
            // UVs rather than Assimp's channel-0 assumption, so
            // CalculateTangentSpace is dropped too.
            var scene = context.ImportFile(
                resolvedPath,
                PostProcessSteps.Triangulate | PostProcessSteps.JoinIdenticalVertices)
                ?? throw new InvalidOperationException(
                    $"Assimp could not import '{assetPath}' (resolved: '{resolvedPath}').");

            if (scene.MeshCount == 0)
                throw new InvalidOperationException(
                    $"No meshes found in asset '{assetPath}' (resolved: '{resolvedPath}').");

            var document = new ModelDocument { ResolvedPath = resolvedPath };

            string extension = Path.GetExtension(resolvedPath).ToLowerInvariant();
            // OBJ/DAE author V bottom-up; glTF/FBX are top-down like Godot.
            bool flipV = extension is ".obj" or ".dae";

            ReadMaterials(scene, Path.GetDirectoryName(resolvedPath) ?? "", document);
            document.DominantMaterialIndex = DominantMaterialIndex(scene);

            document.Root = ReadNode(scene.RootNode ?? throw new InvalidOperationException(
                $"Asset '{resolvedPath}' has no root node."), scene, document, flipV);

            Flatten(document);
            return document;
        }

        // ── Hierarchy ───────────────────────────────────────────────────────

        private static ModelNode ReadNode(
            Node node, Assimp.Scene scene, ModelDocument document, bool flipV)
        {
            var result = new ModelNode
            {
                // Assimp hands back the raw aiMatrix4x4, so the translation sits
                // in M14/M24/M34. Transpose into System.Numerics so the matrix
                // composes like every other V12 transform.
                LocalTransform = NMatrix4x4.Transpose(node.Transform),
                Name = string.IsNullOrWhiteSpace(node.Name) ? "Node" : node.Name,
            };

            foreach (int meshIndex in node.MeshIndices)
            {
                if (meshIndex < 0 || meshIndex >= scene.Meshes.Count)
                    continue;

                var mesh = scene.Meshes[meshIndex];
                var material = mesh.MaterialIndex >= 0 && mesh.MaterialIndex < document.Materials.Count
                    ? document.Materials[mesh.MaterialIndex]
                    : null;

                var part = ReadMesh(mesh, material, document, flipV);
                if (part is not null)
                    result.Parts.Add(part);
            }

            foreach (var child in node.Children)
                result.Children.Add(ReadNode(child, scene, document, flipV));

            return result;
        }

        private static ModelMeshPart? ReadMesh(
            Assimp.Mesh mesh, ModelMaterial? material, ModelDocument document, bool flipV)
        {
            if (mesh.VertexCount == 0 || mesh.FaceCount == 0)
                return null;

            int uvChannel = PickUvChannel(material, mesh);
            var uvData = uvChannel >= 0 ? mesh.TextureCoordinateChannels[uvChannel] : null;

            var part = new ModelMeshPart
            {
                Name = string.IsNullOrWhiteSpace(mesh.Name) ? "Mesh" : mesh.Name,
                MaterialIndex = mesh.MaterialIndex,
                NeedsTriplanar = uvData is null && material is not null && material.HasAnyTexture,
            };

            var points = new double[mesh.VertexCount * 3];
            var uvs = uvData is null ? null : new double[mesh.VertexCount * 2];
            var normals = mesh.HasNormals ? new double[mesh.VertexCount * 3] : null;

            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var position = mesh.Vertices[i];
                points[i * 3] = position.X;
                points[i * 3 + 1] = position.Y;
                points[i * 3 + 2] = position.Z;

                if (normals is not null)
                {
                    var normal = Vector3.Normalize(mesh.Normals[i]);
                    normals[i * 3] = normal.X;
                    normals[i * 3 + 1] = normal.Y;
                    normals[i * 3 + 2] = normal.Z;
                }

                if (uvs is not null && uvData is not null && i < uvData.Count)
                {
                    var uv = uvData[i];
                    uvs[i * 2] = uv.X;
                    uvs[i * 2 + 1] = flipV ? 1f - uv.Y : uv.Y;
                }
            }

            var indices = new List<uint>(mesh.FaceCount * 3);
            foreach (var face in mesh.Faces)
            {
                if (face.IndexCount != 3)
                    continue; // non-triangles cannot survive Triangulate

                indices.Add((uint)face.Indices[0]);
                indices.Add((uint)face.Indices[1]);
                indices.Add((uint)face.Indices[2]);
            }

            if (indices.Count == 0)
                return null;

            part.Points = points;
            part.Normals = normals;
            part.Uvs = uvs;
            part.Indices = indices.ToArray();

            if (part.Normals is not null && part.Uvs is not null)
                part.Tangents = ComputeTangents(part.Points, part.Uvs, part.Normals, part.Indices);

            return part;
        }

        // ── Flattening ──────────────────────────────────────────────────────

        /// <summary>Bakes the hierarchy into one world-space triangle soup, which
        /// is what a single-mesh consumer (and physics) wants.</summary>
        private static void Flatten(ModelDocument document)
        {
            var points = new List<double>();
            var normals = new List<double>();
            var uvs = new List<double>();
            var tangents = new List<double>();
            var indices = new List<uint>();

            bool allNormals = true;
            bool allTangents = true;
            bool anyUvs = false;
            uint vertexOffset = 0;

            Walk(document.Root, NMatrix4x4.Identity);

            document.Points = points.ToArray();
            document.Indices = indices.ToArray();
            document.Uvs = anyUvs ? uvs.ToArray() : null;
            document.Normals = allNormals ? normals.ToArray() : null;
            document.Tangents = allTangents && anyUvs ? tangents.ToArray() : null;

            void Walk(ModelNode node, NMatrix4x4 parent)
            {
                var world = node.LocalTransform * parent;
                // Normals need the inverse-transpose of the 3×3 (row-vector
                // convention), so a non-uniformly scaled node shades correctly.
                var normalMatrix = NMatrix4x4.Invert(world, out var inverse)
                    ? NMatrix4x4.Transpose(inverse)
                    : world;

                foreach (var part in node.Parts)
                {
                    int vertexCount = part.Points.Length / 3;

                    for (int i = 0; i < vertexCount; i++)
                    {
                        var position = Vector3.Transform(
                            new Vector3(
                                (float)part.Points[i * 3],
                                (float)part.Points[i * 3 + 1],
                                (float)part.Points[i * 3 + 2]),
                            world);

                        points.Add(position.X);
                        points.Add(position.Y);
                        points.Add(position.Z);

                        if (part.Normals is not null)
                        {
                            var normal = Vector3.Normalize(Vector3.TransformNormal(
                                new Vector3(
                                    (float)part.Normals[i * 3],
                                    (float)part.Normals[i * 3 + 1],
                                    (float)part.Normals[i * 3 + 2]),
                                normalMatrix));

                            normals.Add(normal.X);
                            normals.Add(normal.Y);
                            normals.Add(normal.Z);
                        }
                        else
                        {
                            allNormals = false;
                        }

                        if (part.Uvs is not null)
                        {
                            anyUvs = true;
                            uvs.Add(part.Uvs[i * 2]);
                            uvs.Add(part.Uvs[i * 2 + 1]);
                        }
                        else
                        {
                            uvs.Add(0);
                            uvs.Add(0);
                        }

                        if (part.Tangents is not null)
                        {
                            // Tangents rotate by the world basis; w (handedness)
                            // survives because it is relative to the normal.
                            var tangent = Vector3.TransformNormal(
                                new Vector3(
                                    (float)part.Tangents[i * 4],
                                    (float)part.Tangents[i * 4 + 1],
                                    (float)part.Tangents[i * 4 + 2]),
                                world);

                            if (tangent.LengthSquared() > 1e-12f)
                                tangent = Vector3.Normalize(tangent);

                            tangents.Add(tangent.X);
                            tangents.Add(tangent.Y);
                            tangents.Add(tangent.Z);
                            tangents.Add(part.Tangents[i * 4 + 3]);
                        }
                        else
                        {
                            allTangents = false;
                        }
                    }

                    foreach (var index in part.Indices)
                        indices.Add(index + vertexOffset);

                    vertexOffset += (uint)vertexCount;
                }

                foreach (var child in node.Children)
                    Walk(child, world);
            }
        }

        // ── UV channel selection ────────────────────────────────────────────

        /// <summary>The UV set the model's own textures address. glTF authors
        /// routinely put them on TEXCOORD_1, so assuming channel 0 silently
        /// samples an empty set.</summary>
        private static int PickUvChannel(ModelMaterial? material, Assimp.Mesh mesh)
        {
            int preferred = material?.UvChannel ?? 0;

            if (preferred >= 0 && mesh.HasTextureCoords(preferred)) return preferred;
            if (mesh.HasTextureCoords(0)) return 0;

            for (int channel = 0; channel < 8; channel++)
                if (mesh.HasTextureCoords(channel))
                    return channel;

            return -1;
        }

        private static int PreferredUvChannel(Assimp.Material material)
        {
            if (material.PBR.HasTextureBaseColor) return material.PBR.TextureBaseColor.UVIndex;
            if (material.HasTextureDiffuse) return material.TextureDiffuse.UVIndex;
            return 0;
        }

        // ── Tangents ────────────────────────────────────────────────────────

        /// <summary>
        /// Accumulates per-triangle tangents from the chosen UV set (Lengyel),
        /// Gram-Schmidt against the stored normal, and packs the binormal
        /// sign into w: the layout Godot's ARRAY_TANGENT reads.
        /// </summary>
        private static double[] ComputeTangents(
            double[] points, double[] uvs, double[] normals, uint[] indices)
        {
            int vertexCount = points.Length / 3;
            var tangents = new Vector3[vertexCount];
            var bitangents = new Vector3[vertexCount];

            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                int i0 = (int)indices[i];
                int i1 = (int)indices[i + 1];
                int i2 = (int)indices[i + 2];
                if (i0 >= vertexCount || i1 >= vertexCount || i2 >= vertexCount)
                    continue;

                var e1 = Position(points, i1) - Position(points, i0);
                var e2 = Position(points, i2) - Position(points, i0);
                var w0 = Uv(uvs, i0);
                var w1 = Uv(uvs, i1);
                var w2 = Uv(uvs, i2);

                float s1 = w1.X - w0.X, t1 = w1.Y - w0.Y;
                float s2 = w2.X - w0.X, t2 = w2.Y - w0.Y;
                float determinant = s1 * t2 - s2 * t1;
                if (MathF.Abs(determinant) < 1e-8f)
                    continue; // degenerate UV triangle carries no direction

                float inv = 1f / determinant;
                var tangent = (e1 * t2 - e2 * t1) * inv;
                var bitangent = (e2 * s1 - e1 * s2) * inv;

                tangents[i0] += tangent; tangents[i1] += tangent; tangents[i2] += tangent;
                bitangents[i0] += bitangent; bitangents[i1] += bitangent; bitangents[i2] += bitangent;
            }

            var packed = new double[vertexCount * 4];
            for (int i = 0; i < vertexCount; i++)
            {
                var normal = Normal(normals, i);
                if (normal.LengthSquared() < 1e-12f) normal = Vector3.UnitY;

                var fallback = MathF.Abs(normal.Y) < 0.99f
                    ? Vector3.Cross(normal, Vector3.UnitY)
                    : Vector3.Cross(normal, Vector3.UnitX);

                var tangent = tangents[i];
                tangent -= normal * Vector3.Dot(normal, tangent);   // Gram-Schmidt
                if (tangent.LengthSquared() < 1e-12f) tangent = fallback;
                tangent = Vector3.Normalize(tangent);

                float sign = Vector3.Dot(Vector3.Cross(normal, tangent), bitangents[i]) < 0f ? -1f : 1f;

                packed[i * 4] = tangent.X;
                packed[i * 4 + 1] = tangent.Y;
                packed[i * 4 + 2] = tangent.Z;
                packed[i * 4 + 3] = sign;
            }

            return packed;

            static Vector3 Position(double[] src, int i) =>
                new((float)src[i * 3], (float)src[i * 3 + 1], (float)src[i * 3 + 2]);

            static Vector2 Uv(double[] src, int i) =>
                new((float)src[i * 2], (float)src[i * 2 + 1]);

            static Vector3 Normal(double[] src, int i) =>
                new((float)src[i * 3], (float)src[i * 3 + 1], (float)src[i * 3 + 2]);
        }

        // ── Materials ───────────────────────────────────────────────────────

        /// <summary>The material used by the most triangles. A model can carry
        /// a dozen materials; one flat mesh can only draw one, so the dominant
        /// one wins rather than whichever happened to be first.</summary>
        private static int DominantMaterialIndex(Assimp.Scene scene)
        {
            if (scene.MaterialCount == 0) return 0;

            var triangles = new int[scene.MaterialCount];
            foreach (var mesh in scene.Meshes)
            {
                if (mesh.MaterialIndex < 0 || mesh.MaterialIndex >= scene.MaterialCount)
                    continue;

                foreach (var face in mesh.Faces)
                    if (face.IndexCount == 3)
                        triangles[mesh.MaterialIndex]++;
            }

            int dominant = 0;
            for (int i = 1; i < triangles.Length; i++)
                if (triangles[i] > triangles[dominant])
                    dominant = i;

            return dominant;
        }

        private static void ReadMaterials(
            Assimp.Scene scene, string modelDirectory, ModelDocument document)
        {
            for (int i = 0; i < scene.MaterialCount; i++)
                document.Materials.Add(ReadMaterial(scene, scene.Materials[i], modelDirectory));
        }

        private static ModelMaterial ReadMaterial(
            Assimp.Scene scene, Assimp.Material material, string modelDirectory)
        {
            var result = new ModelMaterial
            {
                Name = material.HasName ? material.Name : "",
                UvChannel = PreferredUvChannel(material),
                Metallic = ReadFloatProperty(material, "$mat.metallicFactor", 0f),
                Roughness = ReadFloatProperty(material, "$mat.roughnessFactor", 1f),
            };

            if (material.HasColorDiffuse)
            {
                var c = material.ColorDiffuse; // System.Numerics.Vector4
                result.Albedo = new Vector4(
                    c.X, c.Y, c.Z,
                    material.HasOpacity ? c.W * material.Opacity : c.W);
            }

            if (material.HasColorEmissive)
                result.Emission = material.ColorEmissive;

            result.EmissionStrength = ReadFloatProperty(material, "$mat.emissiveStrength", 1f);
            result.NormalScale = ReadFloatProperty(material, "$mat.normalScale", 1f);
            result.AlphaCutoff = ReadFloatProperty(material, "$mat.alphaCutoff", 0.5f);

            result.DoubleSided = material.HasTwoSided && material.IsTwoSided;
            result.Unlit = material.HasShadingMode && material.ShadingMode == ShadingMode.NoShading;
            result.AlphaBlend =
                (material.HasOpacity && material.Opacity < 0.999f) ||
                material.HasTextureOpacity ||
                (material.HasBlendMode && material.BlendMode == Assimp.BlendMode.Additive);

            // glTF packs metallic (blue) and roughness (green) into one image;
            // bind that same file to both slots and let the backend pick the
            // right channel per slot.
            string? metallicRef = null;
            string? roughnessRef = null;
            if (material.PBR.HasTextureMetalness)
                metallicRef = material.PBR.TextureMetalness.FilePath;
            else if (material.PBR.HasTextureRoughness)
                metallicRef = material.PBR.TextureRoughness.FilePath;

            if (material.PBR.HasTextureRoughness)
                roughnessRef = material.PBR.TextureRoughness.FilePath;
            else
                roughnessRef = metallicRef;

            string? albedoRef = material.PBR.HasTextureBaseColor
                ? material.PBR.TextureBaseColor.FilePath
                : material.HasTextureDiffuse ? material.TextureDiffuse.FilePath : null;

            string? normalRef = material.PBR.HasTextureNormalCamera
                ? material.PBR.TextureNormalCamera.FilePath
                : material.HasTextureNormal
                    ? material.TextureNormal.FilePath
                    : null;

            string? emissionRef = material.PBR.HasTextureEmissionColor
                ? material.PBR.TextureEmissionColor.FilePath
                : material.HasTextureEmissive
                    ? material.TextureEmissive.FilePath
                    : null;

            string? occlusionRef = material.HasTextureAmbientOcclusion
                ? material.TextureAmbientOcclusion.FilePath
                : null;

            result.AlbedoTexture = ResolveTexture(scene, albedoRef, modelDirectory);
            result.NormalTexture = ResolveTexture(scene, normalRef, modelDirectory);
            result.MetallicTexture = ResolveTexture(scene, metallicRef, modelDirectory);
            result.RoughnessTexture = ResolveTexture(scene, roughnessRef, modelDirectory);
            result.EmissionTexture = ResolveTexture(scene, emissionRef, modelDirectory);
            result.OcclusionTexture = ResolveTexture(scene, occlusionRef, modelDirectory);

            return result;
        }

        private static float ReadFloatProperty(Assimp.Material material, string key, float fallback)
        {
            try
            {
                if (!material.HasProperty(key)) return fallback;
                var property = material.GetProperty(key);
                return property is null ? fallback : property.GetFloatValue();
            }
            catch
            {
                return fallback;
            }
        }

        /// <summary>Turns an Assimp texture reference into a path the texture
        /// loader can read: embedded images are extracted once into a
        /// content-hashed temp cache, loose files resolve against the model.</summary>
        private static string? ResolveTexture(
            Assimp.Scene scene, string? reference, string modelDirectory)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return null;

            try
            {
                var embedded = scene.GetEmbeddedTexture(reference);
                if (embedded is not null)
                    return ExtractEmbedded(embedded);
            }
            catch
            {
                // Not embedded (or unparsable): fall through to the file path.
            }

            if (Path.IsPathRooted(reference))
                return File.Exists(reference) ? reference : null;

            string candidate = Path.Combine(modelDirectory, reference);
            return File.Exists(candidate) ? candidate : null;
        }

        private static string? ExtractEmbedded(EmbeddedTexture texture)
        {
            try
            {
                if (!texture.HasCompressedData)
                    return null;

                byte[] bytes = texture.CompressedData;
                if (bytes is null || bytes.Length == 0)
                    return null;

                string directory = Path.Combine(Path.GetTempPath(), "v12", "modeltextures");
                Directory.CreateDirectory(directory);

                // Content hash, so the same texture extracted by two models
                // lands in one file and repeated loads never rewrite it.
                string hash = Convert.ToHexString(SHA256.HashData(bytes));
                string path = Path.Combine(directory, hash + GuessExtension(texture, bytes));
                if (!File.Exists(path))
                    File.WriteAllBytes(path, bytes);

                return path;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The texture loader dispatches on extension, so an embedded
        /// image has to be written with one it recognises.</summary>
        private static string GuessExtension(EmbeddedTexture texture, byte[] bytes)
        {
            string hint = (texture.CompressedFormatHint ?? "").Trim().ToLowerInvariant();
            switch (hint)
            {
                case "jpg":
                case "jpeg":
                    return ".jpg";
                case "png":
                    return ".png";
                case "webp":
                    return ".webp";
                case "bmp":
                    return ".bmp";
                case "tga":
                    return ".tga";
                case "dds":
                    return ".dds";
                case "exr":
                    return ".exr";
            }

            // Fall back to the reference's own extension, then magic bytes.
            string fromName = Path.GetExtension(texture.Filename ?? "").ToLowerInvariant();
            if (fromName is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".tga" or ".dds" or ".exr")
                return fromName;

            if (bytes.Length >= 4)
            {
                if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return ".jpg";
                if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return ".png";
                if (bytes[0] == 0x42 && bytes[1] == 0x4D) return ".bmp";
                if (bytes[0] == 0x44 && bytes[1] == 0x44 && bytes[2] == 0x53) return ".dds";
                if (bytes.Length >= 12 &&
                    bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
                    bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
                    return ".webp";
            }

            return ".png";
        }

        // ── Path resolution ─────────────────────────────────────────────────

        /// <summary>
        /// <c>v12://</c> through the registry's asset resolver, <c>res://</c>
        /// through the Godot project, then relative to the working directory.
        /// </summary>
        public static string ResolvePath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new InvalidOperationException("Model has no asset path.");

            string path = assetPath;
            if (path.StartsWith("v12://", StringComparison.OrdinalIgnoreCase))
                path = V12AssetResolver.ResolveGlobal(path);
            else if (path.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(6).TrimStart('/');

            if (File.Exists(path))
                return Path.GetFullPath(path);

            if (GameRoot.Instance?.Registry?.Get<IAssetResolver>() is V12AssetResolver resolver)
            {
                string resolved = resolver.ResolveRelative(path);
                if (!string.Equals(resolved, path, StringComparison.Ordinal) && File.Exists(resolved))
                    return Path.GetFullPath(resolved);
            }

            string fromWorkingDirectory = Path.Combine(Environment.CurrentDirectory, path);
            if (File.Exists(fromWorkingDirectory))
                return Path.GetFullPath(fromWorkingDirectory);

            throw new FileNotFoundException(
                $"Model asset '{assetPath}' did not resolve to a file (tried '{path}').", assetPath);
        }
    }
}
