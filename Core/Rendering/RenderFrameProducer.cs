using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using V12.Core.Interfaces.Renderer;
using V12.Rendering;
using RenderEnvironmentData = V12.Rendering.EnvironmentData;
using RenderRenderer = V12.Rendering.IRenderer;

namespace V12.Core.Rendering;

/// <summary>
/// Converts the engine's immutable <see cref="FrameSnapshot"/> into the
/// backend-free renderer contract. It owns the CPU-to-handle resource cache and
/// emits ordinary <see cref="DrawItem"/>s, views, lights and environment.
///
/// v1 scope: main viewport only, triangle meshes, built-in materials, lights and
/// environment. Textures, sprites, SVG, text, nested viewports and UI draw-item
/// generation are intentionally left for the next step.
///
/// Must be called on the render thread that owns renderer resource creation.
/// </summary>
public sealed class RenderFrameProducer : IDisposable
{
    private sealed class MeshState
    {
        public double[]? Points;
        public double[]? Uvs;
        public double[]? Normals;
        public double[]? Tangents;
        public uint[]? Indices;
        public MeshData Data = MeshData.Empty;
        public long Version;
    }

    private readonly RenderFramePool _pool;
    private readonly RendererResourceCache _resources;
    private readonly PaperUiDrawBuilder _uiBuilder;
    private readonly Dictionary<long, MeshState> _meshStates = new();
    private readonly Func<string, TextureData?>? _textureLoader;
    private int _viewportWidth;
    private int _viewportHeight;
    private long _nextMeshVersion;
    private bool _disposed;

    public RenderFrameProducer(
        RenderRenderer renderer,
        int viewportWidth = 1920,
        int viewportHeight = 1080,
        RenderFramePool? pool = null,
        Func<string, TextureData?>? textureLoader = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _viewportWidth = Math.Max(1, viewportWidth);
        _viewportHeight = Math.Max(1, viewportHeight);
        _pool = pool ?? new RenderFramePool(4);
        _resources = new RendererResourceCache(renderer);
        _uiBuilder = new PaperUiDrawBuilder(_resources);
        _textureLoader = textureLoader;
    }

    public RendererResourceCache Resources => _resources;

    /// <summary>
    /// Builds a frame from a captured snapshot. Returns false when the lease pool
    /// is exhausted; the caller should skip this frame rather than block.
    /// </summary>
    public bool TryBuild(
        FrameSnapshot snapshot,
        RenderEnvironmentData? environment,
        float time,
        float deltaTime,
        out RenderFrameLease? lease) =>
        TryBuild(snapshot, environment, time, deltaTime, null, out lease);

    public bool TryBuild(
        FrameSnapshot snapshot,
        RenderEnvironmentData? environment,
        float time,
        float deltaTime,
        UiFrameContext? uiFrame,
        out RenderFrameLease? lease)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ThrowIfDisposed();

        _resources.BeginFrame();
        _pool.Epoch = _resources.CurrentEpoch;

        if (!_pool.TryRent(out var builder))
        {
            lease = null;
            return false;
        }

        var env = environment ?? RenderEnvironmentData.Default;
        BuildViews(snapshot, env, builder);
        BuildItems(snapshot, builder);
        BuildLights(snapshot, builder);

        if (uiFrame is { } ui)
            _uiBuilder.Append(ui, builder);

        builder.Environment = env;
        builder.Time = time;
        builder.DeltaTime = deltaTime;

        lease = builder.Seal();
        return true;
    }

    public RenderFrameLease Build(
        FrameSnapshot snapshot,
        RenderEnvironmentData? environment,
        float time,
        float deltaTime,
        UiFrameContext? uiFrame = null)
    {
        if (!TryBuild(snapshot, environment, time, deltaTime, uiFrame, out var lease))
            throw new InvalidOperationException("The render frame pool is exhausted.");
        return lease!;
    }

    public void Resize(int width, int height)
    {
        _viewportWidth = Math.Max(1, width);
        _viewportHeight = Math.Max(1, height);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _resources.Dispose();
        _meshStates.Clear();
    }

    private void BuildViews(
        FrameSnapshot snapshot,
        in RenderEnvironmentData environment,
        RenderFrameBuilder builder)
    {
        foreach (var renderable in snapshot.Renderables)
        {
            if (renderable.NodeType != SnapshotNodeType.Camera ||
                !renderable.IsCurrentCamera ||
                renderable.ViewportId != 0)
            {
                continue;
            }

            builder.Views.Add(BuildView(renderable, environment));
        }
    }

    private RenderView BuildView(
        in RenderableSnapshot camera,
        in RenderEnvironmentData environment)
    {
        float aspect = (float)_viewportWidth / _viewportHeight;
        float near = Math.Max(0.001f, camera.NearClip);
        float far = Math.Max(near, camera.FarClip);
        bool perspective = camera.Projection == CameraProjection.Perspective;
        float fov = Math.Clamp(camera.Fov, 1f, 179f);

        Matrix4x4 view;
        if (!Matrix4x4.Invert(camera.Transform, out view))
            view = Matrix4x4.Identity;

        Matrix4x4 projection = perspective
            ? Matrix4x4.CreatePerspectiveFieldOfView(
                fov * (MathF.PI / 180f),
                aspect,
                near,
                far)
            : Matrix4x4.CreateOrthographic(
                Math.Max(0.001f, camera.OrthoSize) * aspect,
                Math.Max(0.001f, camera.OrthoSize),
                near,
                far);

        return new RenderView
        {
            Id = 0,
            CameraElementId = camera.ElementId,
            IsCurrentCamera = true,
            View = view,
            Projection = projection,
            ProjectionMetadata = new ProjectionMetadata(
                perspective ? ProjectionKind.Perspective : ProjectionKind.Orthographic,
                near,
                far,
                fov,
                camera.OrthoSize),
            Viewport = new ViewportRect(0f, 0f, _viewportWidth, _viewportHeight),
            Clear = new ClearSettings(
                environment.Sky.SkyboxEnabled ? ClearMode.Sky : ClearMode.Color,
                new Vector4(environment.Sky.Color, 1f)),
            Target = null,
            LayerMask = RenderLayers.Default,
            Eye = StereoEye.None,
        };
    }

    private void BuildItems(FrameSnapshot snapshot, RenderFrameBuilder builder)
    {
        var activeElements = new HashSet<long>();

        foreach (var renderable in snapshot.Renderables)
        {
            if (!IsMeshNode(renderable.NodeType) ||
                renderable.ViewportId != 0 ||
                renderable.MeshPoints is null ||
                renderable.MeshPoints.Length < 9)
            {
                continue;
            }

            activeElements.Add(renderable.ElementId);

            var mesh = GetMesh(renderable);
            var snapshotMaterial = ReadMaterial(renderable);
            var material = GetMaterial(snapshotMaterial);

            int overridesIndex = builder.DrawOverrides.Count;
            var overrides = new List<DrawOverride>
            {
                new DrawOverride(
                    BuiltInMaterialParameters.Albedo,
                    MaterialValue.From(new Vector4(
                        snapshotMaterial.R,
                        snapshotMaterial.G,
                        snapshotMaterial.B,
                        snapshotMaterial.A))),
                new DrawOverride(
                    BuiltInMaterialParameters.UvOffset,
                    MaterialValue.From(snapshotMaterial.UvOffset)),
                new DrawOverride(
                    BuiltInMaterialParameters.UvScale,
                    MaterialValue.From(snapshotMaterial.UvScale)),
            };

            if (snapshotMaterial.EmissionEnergy > 0f &&
                (snapshotMaterial.EmissionR > 0f ||
                 snapshotMaterial.EmissionG > 0f ||
                 snapshotMaterial.EmissionB > 0f))
            {
                overrides.Add(new DrawOverride(
                    BuiltInMaterialParameters.Emissive,
                    MaterialValue.From(new Vector3(
                        snapshotMaterial.EmissionR,
                        snapshotMaterial.EmissionG,
                        snapshotMaterial.EmissionB))));
                overrides.Add(new DrawOverride(
                    BuiltInMaterialParameters.EmissiveEnergy,
                    MaterialValue.From(snapshotMaterial.EmissionEnergy)));
            }

            if (snapshotMaterial.NormalScale > 0f)
            {
                overrides.Add(new DrawOverride(
                    BuiltInMaterialParameters.NormalScale,
                    MaterialValue.From(snapshotMaterial.NormalScale)));
            }

            if (snapshotMaterial.AlphaCutoff > 0f)
            {
                overrides.Add(new DrawOverride(
                    BuiltInMaterialParameters.AlphaCutoff,
                    MaterialValue.From(snapshotMaterial.AlphaCutoff)));
            }

            if (snapshotMaterial.Triplanar)
            {
                overrides.Add(new DrawOverride(
                    BuiltInMaterialParameters.Triplanar,
                    MaterialValue.From(1f)));
                overrides.Add(new DrawOverride(
                    BuiltInMaterialParameters.TriplanarWorld,
                    MaterialValue.From(snapshotMaterial.TriplanarWorld ? 1f : 0f)));
                overrides.Add(new DrawOverride(
                    BuiltInMaterialParameters.TriplanarSharpness,
                    MaterialValue.From(snapshotMaterial.TriplanarSharpness)));
            }

            builder.DrawOverrides.Add(new DrawOverrides(overrides.ToArray()));

            builder.Items.Add(new DrawItem
            {
                ElementId = renderable.ElementId,
                ViewportId = 0,
                Mesh = mesh,
                Material = material,
                World = renderable.Transform,
                Clip = ClipRect.None,
                SortKey = 0,
                LayerMask = RenderLayers.Default,
                Active = true,
                OverridesIndex = overridesIndex,
            });
        }

        foreach (var id in _meshStates.Keys.ToArray())
        {
            if (!activeElements.Contains(id))
                _meshStates.Remove(id);
        }
    }

    private MeshHandle GetMesh(in RenderableSnapshot renderable)
    {
        if (!_meshStates.TryGetValue(renderable.ElementId, out var state) ||
            !ReferenceEquals(state.Points, renderable.MeshPoints) ||
            !ReferenceEquals(state.Uvs, renderable.MeshUvs) ||
            !ReferenceEquals(state.Normals, renderable.MeshNormals) ||
            !ReferenceEquals(state.Tangents, renderable.MeshTangents) ||
            !ReferenceEquals(state.Indices, renderable.MeshIndices))
        {
            state = new MeshState
            {
                Points = renderable.MeshPoints,
                Uvs = renderable.MeshUvs,
                Normals = renderable.MeshNormals,
                Tangents = renderable.MeshTangents,
                Indices = renderable.MeshIndices,
                Data = BuildMeshData(
                    renderable.MeshPoints!,
                    renderable.MeshIndices,
                    renderable.MeshUvs,
                    renderable.MeshNormals,
                    renderable.MeshTangents),
                Version = ++_nextMeshVersion,
            };
            _meshStates[renderable.ElementId] = state;
        }

        return _resources.GetOrCreateMesh(
            "mesh:" + renderable.ElementId.ToString(CultureInfo.InvariantCulture),
            state.Data,
            state.Version);
    }

    private MaterialHandle GetMaterial(in SnapshotMaterial material)
    {
        bool transparent = material.A < 0.999f;
        var blend = transparent ? BlendMode.Alpha : BlendMode.Opaque;
        var shader = _resources.GetOrCreateShader(
            material.Unlit ? BuiltInShader.Unlit : BuiltInShader.Lit);

        var bindings = new List<MaterialTextureBinding>();
        AddTexture(material.AlbedoPath, BuiltInTextureSlots.Albedo, TextureColorSpace.Srgb, bindings);
        AddTexture(material.NormalPath, BuiltInTextureSlots.Normal, TextureColorSpace.Linear, bindings);
        AddTexture(material.MetallicPath, BuiltInTextureSlots.Metallic, TextureColorSpace.Linear, bindings);
        AddTexture(material.RoughnessPath, BuiltInTextureSlots.Roughness, TextureColorSpace.Linear, bindings);
        AddTexture(material.EmissionPath, BuiltInTextureSlots.Emission, TextureColorSpace.Srgb, bindings);
        AddTexture(material.OcclusionPath, BuiltInTextureSlots.Occlusion, TextureColorSpace.Linear, bindings);

        string key = string.Create(
            CultureInfo.InvariantCulture,
            $"material:{material.Metallic:F3}:{material.Roughness:F3}:" +
            $"{(material.Unlit ? 1 : 0)}:{(material.NoDepth ? 1 : 0)}:" +
            $"{(material.DoubleSided ? 1 : 0)}:{(material.Triplanar ? 1 : 0)}:" +
            $"{(material.TriplanarWorld ? 1 : 0)}:{material.TriplanarSharpness:F3}:" +
            $"{(transparent ? 1 : 0)}:{material.AlbedoPath}|{material.NormalPath}|" +
            $"{material.MetallicPath}|{material.RoughnessPath}|{material.EmissionPath}|" +
            $"{material.OcclusionPath}");

        var parameters = new MaterialValue[]
        {
            default,
            MaterialValue.From(material.Metallic),
            MaterialValue.From(material.Roughness),
        };

        var desc = new MaterialDesc(
            Blend: blend,
            Cull: material.DoubleSided ? CullMode.None : CullMode.Back,
            DepthTest: !material.NoDepth,
            DepthWrite: !transparent,
            Lit: !material.Unlit,
            CastShadows: true,
            ReceiveShadows: true,
            Parameters: parameters,
            Textures: bindings.ToArray());

        return _resources.GetOrCreateMaterial(key, shader, desc);
    }

    private void AddTexture(
        string? path,
        int slot,
        TextureColorSpace colorSpace,
        List<MaterialTextureBinding> bindings)
    {
        if (_textureLoader is null || string.IsNullOrWhiteSpace(path))
            return;

        var data = _textureLoader(path);
        if (data is null)
            return;

        data = data with { ColorSpace = colorSpace };
        var handle = _resources.GetOrCreateTexture(
            $"texture:{colorSpace}:{path}",
            data);
        bindings.Add(new MaterialTextureBinding(slot, handle));
    }

    private static SnapshotMaterial ReadMaterial(in RenderableSnapshot renderable)
    {
        bool hasMaterial =
            renderable.MatR != 0f ||
            renderable.MatG != 0f ||
            renderable.MatB != 0f ||
            renderable.MatA != 0f ||
            renderable.MatMetallic != 0f ||
            renderable.MatRoughness != 0f ||
            renderable.MatEmissionR != 0f ||
            renderable.MatEmissionG != 0f ||
            renderable.MatEmissionB != 0f ||
            renderable.MatEmissionEnergy != 0f ||
            renderable.MatNormalScale != 0f ||
            renderable.MatAlphaCutoff != 0f ||
            renderable.MatDoubleSided ||
            renderable.MatTriplanar ||
            renderable.MatUnlit ||
            renderable.MatNoDepth ||
            !string.IsNullOrEmpty(renderable.MatTexturePath) ||
            !string.IsNullOrEmpty(renderable.MatNormalTexturePath) ||
            !string.IsNullOrEmpty(renderable.MatMetallicTexturePath) ||
            !string.IsNullOrEmpty(renderable.MatRoughnessTexturePath) ||
            !string.IsNullOrEmpty(renderable.MatEmissionTexturePath) ||
            !string.IsNullOrEmpty(renderable.MatOcclusionTexturePath);

        if (!hasMaterial)
        {
            return new SnapshotMaterial(
                1f, 1f, 1f, 1f,
                0.05f, 0.75f,
                0f, 0f, 0f, 1f,
                1f, 0f,
                false, false, false, 1f,
                false, false,
                Vector2.Zero, Vector2.One,
                null, null, null, null, null, null);
        }

        return new SnapshotMaterial(
            renderable.MatR,
            renderable.MatG,
            renderable.MatB,
            renderable.MatA,
            renderable.MatMetallic,
            renderable.MatRoughness,
            renderable.MatEmissionR,
            renderable.MatEmissionG,
            renderable.MatEmissionB,
            renderable.MatEmissionEnergy,
            renderable.MatNormalScale,
            renderable.MatAlphaCutoff,
            renderable.MatDoubleSided,
            renderable.MatTriplanar,
            renderable.MatTriplanarWorld,
            renderable.MatTriplanarSharpness,
            renderable.MatUnlit,
            renderable.MatNoDepth,
            new Vector2(renderable.MatUvOffsetX, renderable.MatUvOffsetY),
            new Vector2(renderable.MatUvScaleX, renderable.MatUvScaleY),
            EmptyToNull(renderable.MatTexturePath),
            EmptyToNull(renderable.MatNormalTexturePath),
            EmptyToNull(renderable.MatMetallicTexturePath),
            EmptyToNull(renderable.MatRoughnessTexturePath),
            EmptyToNull(renderable.MatEmissionTexturePath),
            EmptyToNull(renderable.MatOcclusionTexturePath));
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private readonly record struct SnapshotMaterial(
        float R,
        float G,
        float B,
        float A,
        float Metallic,
        float Roughness,
        float EmissionR,
        float EmissionG,
        float EmissionB,
        float EmissionEnergy,
        float NormalScale,
        float AlphaCutoff,
        bool DoubleSided,
        bool Triplanar,
        bool TriplanarWorld,
        float TriplanarSharpness,
        bool Unlit,
        bool NoDepth,
        Vector2 UvOffset,
        Vector2 UvScale,
        string? AlbedoPath,
        string? NormalPath,
        string? MetallicPath,
        string? RoughnessPath,
        string? EmissionPath,
        string? OcclusionPath);

    private void BuildLights(FrameSnapshot snapshot, RenderFrameBuilder builder)
    {
        foreach (var renderable in snapshot.Renderables)
        {
            if (renderable.ViewportId != 0 ||
                renderable.NodeType is not (
                    SnapshotNodeType.LightPoint or
                    SnapshotNodeType.LightDirectional or
                    SnapshotNodeType.LightSpot))
            {
                continue;
            }

            var kind = renderable.NodeType switch
            {
                SnapshotNodeType.LightDirectional => LightKind.Directional,
                SnapshotNodeType.LightSpot => LightKind.Spot,
                _ => LightKind.Point,
            };

            float outer = Math.Max(0f, renderable.LightAngle);
            float inner = outer * (1f - Math.Clamp(renderable.LightSpotSoftness, 0f, 1f));

            builder.Lights.Add(new LightItem
            {
                ElementId = renderable.ElementId,
                Active = true,
                Kind = kind,
                World = renderable.Transform,
                Color = new Vector3(
                    renderable.LightColor.R / 255f,
                    renderable.LightColor.G / 255f,
                    renderable.LightColor.B / 255f),
                Intensity = renderable.LightIntensity,
                Range = renderable.LightRange,
                InnerCone = inner,
                OuterCone = outer,
                CastShadows = true,
                LayerMask = RenderLayers.Default,
            });
        }
    }

    private static Vector2[] BuildUvs(
        Vector3[] positions,
        Vector3[] normals,
        double[]? sourceUvs)
    {
        var uvs = new Vector2[positions.Length];

        if (sourceUvs is not null && sourceUvs.Length == positions.Length * 2)
        {
            for (int i = 0; i < positions.Length; i++)
                uvs[i] = new Vector2((float)sourceUvs[i * 2], (float)sourceUvs[i * 2 + 1]);
            return uvs;
        }

        // Fallback for sources without UVs: project onto the plane most aligned
        // with the vertex normal. This keeps textures from collapsing on
        // procedural/custom meshes until their generators publish real UVs.
        for (int i = 0; i < positions.Length; i++)
        {
            var p = positions[i];
            var n = normals[i];
            float ax = MathF.Abs(n.X);
            float ay = MathF.Abs(n.Y);
            float az = MathF.Abs(n.Z);

            if (ay >= ax && ay >= az)
                uvs[i] = new Vector2(p.X, p.Z);
            else if (ax >= ay && ax >= az)
                uvs[i] = new Vector2(p.Z, p.Y);
            else
                uvs[i] = new Vector2(p.X, p.Y);
        }

        return uvs;
    }

    private static MeshData BuildMeshData(
        double[] points,
        uint[]? indices,
        double[]? sourceUvs,
        double[]? sourceNormals,
        double[]? sourceTangents)
    {
        int vertexCount = points.Length / 3;
        if (vertexCount <= 0)
            return MeshData.Empty;

        var positions = new Vector3[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            positions[i] = new Vector3(
                (float)points[i * 3],
                (float)points[i * 3 + 1],
                (float)points[i * 3 + 2]);
        }

        var sourceIndices = indices;
        if (sourceIndices is null || sourceIndices.Length < 3)
        {
            int triangleCount = vertexCount / 3;
            sourceIndices = new uint[triangleCount * 3];
            for (int i = 0; i < sourceIndices.Length; i++)
                sourceIndices[i] = (uint)i;
        }

        int usableIndices = sourceIndices.Length - (sourceIndices.Length % 3);
        var validIndices = new List<uint>(usableIndices);

        for (int i = 0; i < usableIndices; i += 3)
        {
            uint i0 = sourceIndices[i];
            uint i1 = sourceIndices[i + 1];
            uint i2 = sourceIndices[i + 2];
            if (i0 >= vertexCount || i1 >= vertexCount || i2 >= vertexCount)
                continue;

            validIndices.Add(i0);
            validIndices.Add(i1);
            validIndices.Add(i2);
        }

        var normals = new Vector3[vertexCount];

        // Authored normals win: a model's file already carries the shading its
        // author intended, and re-deriving from winding flattens hard edges.
        if (sourceNormals is not null && sourceNormals.Length == vertexCount * 3)
        {
            for (int i = 0; i < vertexCount; i++)
            {
                var n = new Vector3(
                    (float)sourceNormals[i * 3],
                    (float)sourceNormals[i * 3 + 1],
                    (float)sourceNormals[i * 3 + 2]);
                normals[i] = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY;
            }
        }
        else
        {
            for (int i = 0; i < validIndices.Count; i += 3)
            {
                int i0 = (int)validIndices[i];
                int i1 = (int)validIndices[i + 1];
                int i2 = (int)validIndices[i + 2];

                var n = Vector3.Cross(
                    positions[i1] - positions[i0],
                    positions[i2] - positions[i0]);

                if (n.LengthSquared() <= 1e-12f)
                    continue;

                normals[i0] += n;
                normals[i1] += n;
                normals[i2] += n;
            }

            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = normals[i].LengthSquared() > 1e-12f
                    ? Vector3.Normalize(normals[i])
                    : Vector3.UnitY;
            }
        }

        var uvs = BuildUvs(positions, normals, sourceUvs);
        var colors = new Vector4[vertexCount];
        Array.Fill(colors, Vector4.One);

        // Tangents only ride along when the source supplied them; a mismatched
        // length would silently misalign every vertex's tangent basis.
        if (sourceTangents is not null && sourceTangents.Length == vertexCount * 4)
        {
            var tangents = new Vector4[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                tangents[i] = new Vector4(
                    (float)sourceTangents[i * 4],
                    (float)sourceTangents[i * 4 + 1],
                    (float)sourceTangents[i * 4 + 2],
                    (float)sourceTangents[i * 4 + 3]);
            }

            return MeshDataFactory.CreatePositionNormalUv0TangentColor(
                positions,
                normals,
                uvs,
                tangents,
                colors,
                validIndices.ToArray());
        }

        return MeshDataFactory.CreatePositionNormalUv0Color(
            positions,
            normals,
            uvs,
            colors,
            validIndices.ToArray());
    }

    private static bool IsMeshNode(SnapshotNodeType nodeType) => nodeType is
        SnapshotNodeType.MeshBox or
        SnapshotNodeType.MeshSphere or
        SnapshotNodeType.MeshCapsule or
        SnapshotNodeType.MeshCylinder or
        SnapshotNodeType.MeshPlane or
        SnapshotNodeType.MeshCustom;

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RenderFrameProducer));
    }
}
