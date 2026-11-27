using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using V12.Core.Input;
using V12.Core.Interfaces.Renderer;
using V12.Core.UI.Paper;
using V12.Rendering;
using RenderEnvironmentData = V12.Rendering.EnvironmentData;
using RenderRenderer = V12.Rendering.IRenderer;

namespace V12.Core.Rendering;

/// <summary>
/// Converts the engine's immutable <see cref="FrameSnapshot"/> into the
/// backend-free renderer contract. It owns the CPU-to-handle resource cache and
/// emits ordinary <see cref="DrawItem"/>s, views, lights and environment.
///
/// v1 scope: triangle meshes with textures, built-in materials, lights,
/// environment, explicit views, and screen-space UI as ordinary draw items. The
/// producer owns the UI view and its optional presentation target, so hosts do
/// not implement UI layout or UI targets.
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
        public double[]? Colors;
        public double[]? Tangents;
        public uint[]? Indices;
        public float[]? BoneWeights;
        public int[]? BoneIndices;
        public MeshData Data = MeshData.Empty;
        public long Version;
    }

    private readonly RenderRenderer _renderer;
    private readonly RenderFramePool _pool;
    private readonly RendererResourceCache _resources;
    private readonly PaperUiDrawBuilder _uiBuilder;
    private readonly Dictionary<long, RenderTargetHandle> _viewTargets = new();
    private readonly Dictionary<long, (int Width, int Height)> _viewTargetSizes = new();
    private readonly Dictionary<long, int> _viewTargetRetire = new();
    private RenderTargetHandle? _uiTarget;
    private (int Width, int Height) _uiTargetSize;
    private int _uiTargetRetire;
    private readonly IUiSource? _uiSource;
    private IReadOnlyDictionary<long, PaperFrame>? _worldCanvasFrames;
    private readonly WorldUiInput? _worldUiInput;
    private readonly Dictionary<long, MeshState> _meshStates = new();
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);
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
        Func<string, TextureData?>? textureLoader = null,
        InputService? input = null,
        IUiSource? uiSource = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
        _uiSource = uiSource;
        _viewportWidth = Math.Max(1, viewportWidth);
        _viewportHeight = Math.Max(1, viewportHeight);
        _pool = pool ?? new RenderFramePool(4);
        _resources = new RendererResourceCache(renderer);
        _uiBuilder = new PaperUiDrawBuilder(_resources);
        _textureLoader = textureLoader;

        // When the host supplies input, the producer also owns world-canvas
        // pointer picking, hover feeding and keyboard focus - all engine-side.
        // The UI source is the only Paper-aware object, and it lives in
        // V12.Paper, not in this assembly.
        if (input != null)
            _worldUiInput = new WorldUiInput(input, () => _uiSource);
    }

    public RendererResourceCache Resources => _resources;

    /// <summary>The UI package implementation (world layout + canvas input). Null
    /// when the host did not opt into world-space UI.</summary>
    public IUiSource? UiSource => _uiSource;

    /// <summary>World-canvas input router (hover, click focus, keyboard). Null
    /// when the producer was created without an <see cref="InputService"/>.</summary>
    public WorldUiInput? WorldUi => _worldUiInput;

    /// <summary>
    /// Host presentation choice for the screen-space UI view. The default draws
    /// UI straight into the presentation surface; hosts that composite the UI as
    /// an overlay set <see cref="UiTargetPolicy.OwnTarget"/> and present the
    /// target named by <see cref="RenderFrame.PresentTarget"/>.
    /// </summary>
    public UiTargetPolicy ScreenUiTargetPolicy { get; set; } = UiTargetPolicy.PresentSurface;

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
        EnsureViewTargets(snapshot, uiFrame);
        EnsureUiTarget(uiFrame);
        if (uiFrame is { } worldUiContext)
            PrepareWorldCanvases(worldUiContext, snapshot);
        BuildViews(snapshot, env, builder, uiFrame);
        BuildItems(snapshot, builder);
        BuildParticles(snapshot, builder);
        BuildLights(snapshot, builder);
        AppendWorldCanvases(snapshot, builder);

        _uiBuilder.ViewTextureProvider = viewId =>
            _viewTargets.TryGetValue(viewId, out var target)
                ? _renderer.GetRenderTargetTexture(target)
                : null;

        if (uiFrame is { } ui)
            _uiBuilder.Append(ui, _uiTarget, builder);

        builder.Environment = env;
        builder.Time = time;
        builder.DeltaTime = deltaTime;
        builder.PresentTarget = _uiTarget;

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

        // Drop materials first: a UI material may still bind a view target
        // texture, and DestroyRenderTarget rejects live references.
        _resources.Dispose();
        _worldUiInput?.Dispose();
        _worldCanvasFrames = null;
        if (_uiTarget is { } uiTarget)
        {
            _renderer.DestroyRenderTarget(uiTarget);
            _uiTarget = null;
        }
        foreach (var target in _viewTargets.Values)
            _renderer.DestroyRenderTarget(target);
        _viewTargets.Clear();
        _viewTargetSizes.Clear();
        _viewTargetRetire.Clear();
        _meshStates.Clear();
    }

    private void EnsureViewTargets(FrameSnapshot snapshot, UiFrameContext? uiFrame)
    {
        var active = new HashSet<long>();

        foreach (var view in snapshot.Views)
        {
            if (!view.IsActive) continue;
            if (!TryResolveViewRect(view, uiFrame, out var rect)) continue;

            int width = Math.Max(1, (int)MathF.Round(rect.Width));
            int height = Math.Max(1, (int)MathF.Round(rect.Height));
            active.Add(view.ViewId);

            if (_viewTargets.TryGetValue(view.ViewId, out var existing))
            {
                if (_viewTargetSizes.TryGetValue(view.ViewId, out var size) &&
                    size.Width == width && size.Height == height)
                {
                    continue;
                }

                _renderer.ResizeRenderTarget(existing, width, height);
            }
            else
            {
                _viewTargets[view.ViewId] = _renderer.CreateRenderTarget(new RenderTargetDesc(
                    width,
                    height,
                    Transparent: false,
                    OwnWorld3D: false));
            }

            _viewTargetSizes[view.ViewId] = (width, height);
        }

        foreach (var id in active)
            _viewTargetRetire.Remove(id);

        foreach (var id in _viewTargets.Keys.ToArray())
        {
            if (active.Contains(id)) continue;

            // The UI material that sampled this target survives one cache
            // frame. Retire the target only after the cache has dropped its
            // material, otherwise DestroyRenderTarget rejects the reference.
            int frames = _viewTargetRetire.TryGetValue(id, out var count) ? count + 1 : 1;
            if (frames < 3)
            {
                _viewTargetRetire[id] = frames;
                continue;
            }

            _renderer.DestroyRenderTarget(_viewTargets[id]);
            _viewTargets.Remove(id);
            _viewTargetSizes.Remove(id);
            _viewTargetRetire.Remove(id);
        }
    }

    /// <summary>
    /// Owns the screen-space UI target. Present-surface hosts never allocate one;
    /// overlay hosts get a target sized to the UI surface and named by
    /// <see cref="RenderFrame.PresentTarget"/>.
    /// </summary>
    private void EnsureUiTarget(UiFrameContext? uiFrame)
    {
        if (uiFrame is not { } ui || ScreenUiTargetPolicy != UiTargetPolicy.OwnTarget)
        {
            RetireUiTarget();
            return;
        }

        int width = Math.Max(1, ui.Width > 0 ? ui.Width : ui.Screen.Width);
        int height = Math.Max(1, ui.Height > 0 ? ui.Height : ui.Screen.Height);
        _uiTargetRetire = 0;

        if (_uiTarget is { } existing)
        {
            if (_uiTargetSize.Width == width && _uiTargetSize.Height == height)
                return;

            _renderer.ResizeRenderTarget(existing, width, height);
            _uiTargetSize = (width, height);
            return;
        }

        _uiTarget = _renderer.CreateRenderTarget(new RenderTargetDesc(
            width,
            height,
            Transparent: true,
            OwnWorld3D: true));
        _uiTargetSize = (width, height);
    }

    /// <summary>
    /// Lays out every world-space canvas and routes input before stepping. The
    /// UI package does the layout; the engine only sequences and emits.
    /// </summary>
    private void PrepareWorldCanvases(UiFrameContext ui, FrameSnapshot snapshot)
    {
        if (_uiSource == null)
            return;

        // Surfaces must exist before input is fed, and input must be in place
        // before the UI package steps them.
        _uiSource.PrepareWorldCanvases(ui.Captured);

        int width = ui.Width > 0 ? ui.Width : ui.Screen.Width;
        int height = ui.Height > 0 ? ui.Height : ui.Screen.Height;
        _worldUiInput?.BeginFrame(snapshot, width, height);

        _uiSource.StepWorldCanvases();
        _worldCanvasFrames = _uiSource.WorldCanvases;
    }

    /// <summary>
    /// Emits world canvases as scene meshes. The canvas placement comes from
    /// <see cref="FrameSnapshot.Canvases"/>; a surface without placement is
    /// skipped rather than guessed.
    /// </summary>
    private void AppendWorldCanvases(FrameSnapshot snapshot, RenderFrameBuilder builder)
    {
        var frames = _worldCanvasFrames;
        if (frames is null || frames.Count == 0)
            return;

        if (snapshot.Canvases.Count == 0)
        {
            WarnOnce(
                "world-ui-without-placement",
                "[RenderFrameProducer] World UI surfaces exist but the frame has no canvas placement for '{Id}'.",
                string.Join(",", frames.Keys));
            return;
        }

        // Deterministic paint order across canvases: priority, then element id.
        // The high 16 sort bits order canvases, the low 16 keep the canvas's
        // own draw order.
        var ordered = snapshot.Canvases
            .OrderBy(canvas => canvas.Priority)
            .ThenBy(canvas => canvas.ElementId)
            .ToArray();

        for (int i = 0; i < ordered.Length; i++)
        {
            var canvas = ordered[i];
            if (!frames.TryGetValue(canvas.ElementId, out var frame))
                continue;

            uint sortBase = (uint)Math.Min(i, 0xFFFF) << 16;
            _uiBuilder.AppendWorld(canvas.ElementId, frame, canvas, sortBase, builder);
        }
    }

    /// <summary>
    /// Drops the UI target with the same grace as view targets: the resource
    /// cache can still hold a material from the previous frame.
    /// </summary>
    private void RetireUiTarget()
    {
        if (_uiTarget is not { } target)
            return;

        if (++_uiTargetRetire < 3)
            return;

        _renderer.DestroyRenderTarget(target);
        _uiTarget = null;
        _uiTargetSize = default;
        _uiTargetRetire = 0;
    }

    private static bool TryResolveViewRect(
        in RenderViewSnapshot view,
        UiFrameContext? uiFrame,
        out ViewportRect rect)
    {
        if (view.Width > 0 && view.Height > 0)
        {
            rect = new ViewportRect(0f, 0f, view.Width, view.Height);
            return true;
        }

        if (uiFrame is { } ui)
        {
            foreach (var surface in ui.Screen.Viewports)
            {
                if (surface.Id != view.ViewportId && surface.ViewId != view.ViewId)
                    continue;

                if (surface.Rect.Z > 0f && surface.Rect.W > 0f)
                {
                    rect = new ViewportRect(
                        surface.Rect.X,
                        surface.Rect.Y,
                        surface.Rect.Z,
                        surface.Rect.W);
                    return true;
                }
            }
        }

        rect = default;
        return false;
    }

    private void BuildViews(
        FrameSnapshot snapshot,
        in RenderEnvironmentData environment,
        RenderFrameBuilder builder,
        UiFrameContext? uiFrame)
    {
        if (snapshot.Views.Count > 0)
        {
            foreach (var view in snapshot.Views)
            {
                if (!view.IsActive) continue;
                if (!TryResolveViewRect(view, uiFrame, out var rect)) continue;
                if (!_viewTargets.TryGetValue(view.ViewId, out var target)) continue;

                var camera = FindCamera(snapshot, view);
                if (camera is null) continue;

                builder.Views.Add(BuildView(
                    view,
                    camera.Value,
                    rect,
                    target,
                    environment));
            }
            return;
        }

        // Fallback: no explicit view components. Keep the simple single-view
        // path for games that only set CameraComponent.IsCurrent.
        foreach (var renderable in snapshot.Renderables)
        {
            if (renderable.NodeType != SnapshotNodeType.Camera || !renderable.IsCurrentCamera)
                continue;

            var fallback = new RenderViewSnapshot
            {
                ViewId = 1,
                ViewportId = 0,
                WorldId = renderable.WorldId,
                CameraElementId = renderable.ElementId,
                LayerMask = RenderLayers.Default,
                Clear = ClearMode.Sky,
                Priority = 0,
                IsActive = true,
            };

            builder.Views.Add(BuildView(
                fallback,
                renderable,
                new ViewportRect(0f, 0f, _viewportWidth, _viewportHeight),
                null,
                environment));
            break;
        }
    }

    private static RenderableSnapshot? FindCamera(FrameSnapshot snapshot, in RenderViewSnapshot view)
    {
        if (view.CameraElementId != 0)
        {
            foreach (var renderable in snapshot.Renderables)
            {
                if (renderable.NodeType == SnapshotNodeType.Camera &&
                    renderable.ElementId == view.CameraElementId)
                {
                    return renderable;
                }
            }
            return null;
        }

        RenderableSnapshot? fallback = null;
        foreach (var renderable in snapshot.Renderables)
        {
            if (renderable.NodeType != SnapshotNodeType.Camera) continue;
            if (renderable.WorldId != view.WorldId) continue;

            if (renderable.IsCurrentCamera)
                return renderable;

            fallback ??= renderable;
        }

        return fallback;
    }

    private static RenderView BuildView(
        in RenderViewSnapshot view,
        in RenderableSnapshot camera,
        in ViewportRect rect,
        RenderTargetHandle? target,
        in RenderEnvironmentData environment)
    {
        float aspect = Math.Max(1f, rect.Width) / Math.Max(1f, rect.Height);
        float near = Math.Max(0.001f, camera.NearClip);
        float far = Math.Max(near, camera.FarClip);
        bool perspective = camera.Projection == CameraProjection.Perspective;
        float fov = Math.Clamp(camera.Fov, 1f, 179f);

        Matrix4x4 viewMatrix;
        if (!Matrix4x4.Invert(camera.Transform, out viewMatrix))
            viewMatrix = Matrix4x4.Identity;

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

        var clearColor = new Vector4(
            view.ClearColor.R / 255f,
            view.ClearColor.G / 255f,
            view.ClearColor.B / 255f,
            view.ClearColor.A / 255f);

        ClearSettings clear = view.Clear switch
        {
            ClearMode.None => new ClearSettings(ClearMode.None),
            ClearMode.DepthOnly => new ClearSettings(ClearMode.DepthOnly),
            ClearMode.Sky when environment.Sky.SkyboxEnabled => new ClearSettings(ClearMode.Sky),
            ClearMode.Sky => new ClearSettings(ClearMode.Color, new Vector4(environment.Sky.Color, 1f)),
            _ => new ClearSettings(ClearMode.Color, clearColor),
        };

        return new RenderView
        {
            Id = view.ViewId,
            ViewportId = view.ViewportId,
            WorldId = view.WorldId,
            CameraElementId = camera.ElementId,
            IsCurrentCamera = true,
            View = viewMatrix,
            Projection = projection,
            ProjectionMetadata = new ProjectionMetadata(
                perspective ? ProjectionKind.Perspective : ProjectionKind.Orthographic,
                near,
                far,
                fov,
                camera.OrthoSize),
            Viewport = rect,
            Clear = clear,
            Target = target,
            LayerMask = view.LayerMask == 0 ? RenderLayers.Default : view.LayerMask,
            Eye = StereoEye.None,
            Priority = view.Priority,
        };
    }

    private void BuildItems(FrameSnapshot snapshot, RenderFrameBuilder builder)
    {
        var activeElements = new HashSet<long>();
        var skinIndexById = new Dictionary<long, int>();

        foreach (var renderable in snapshot.Renderables)
        {
            if (!IsMeshNode(renderable.NodeType) ||
                renderable.MeshPoints is null ||
                renderable.MeshPoints.Length < 9)
            {
                continue;
            }

            activeElements.Add(renderable.ElementId);

            var mesh = GetMesh(renderable);
            var snapshotMaterial = ReadMaterial(renderable);
            var material = GetMaterial(
                snapshotMaterial,
                renderable.MeshColors is { Length: > 0 },
                out var shaderReflection);

            // A custom shader owns its uniforms, so the built-in override table
            // (indexed by BuiltInMaterialParameters) does not apply. Map the
            // material fields onto the custom shader's own reflection instead.
            int overridesIndex = -1;
            List<DrawOverride> overrides = shaderReflection is null
                ? BuildBuiltInOverrides(snapshotMaterial)
                : BuildCustomOverrides(shaderReflection, snapshotMaterial);

            if (overrides.Count > 0)
            {
                overridesIndex = builder.DrawOverrides.Count;
                builder.DrawOverrides.Add(new DrawOverrides(overrides.ToArray()));
            }

            int skinIndex = -1;
            if (renderable.SkinId != 0 &&
                renderable.SkinBoneLocals is { Length: > 0 } boneLocals &&
                renderable.SkinBoneParents is { } boneParents &&
                renderable.SkinInverseBinds is { } inverseBinds)
            {
                skinIndex = EnsureSkin(
                    builder,
                    skinIndexById,
                    renderable.SkinId,
                    renderable.SkinWorld,
                    boneLocals,
                    boneParents,
                    inverseBinds);
            }

            builder.Items.Add(new DrawItem
            {
                ElementId = renderable.ElementId,
                WorldId = renderable.WorldId,
                ViewId = renderable.ViewId,
                Mesh = mesh,
                Material = material,
                World = renderable.Transform,
                Clip = ClipRect.None,
                SortKey = 0,
                LayerMask = renderable.LayerMask == 0 ? RenderLayers.Default : renderable.LayerMask,
                Active = true,
                OverridesIndex = overridesIndex,
                SkinIndex = skinIndex,
            });
        }

        foreach (var id in _meshStates.Keys.ToArray())
        {
            if (!activeElements.Contains(id))
                _meshStates.Remove(id);
        }
    }

    /// <summary>
    /// Emits one skin per skeleton element and appends its current bone pose to
    /// the frame's shared bone array. Parts that share an element share the skin.
    /// </summary>
    private static int EnsureSkin(
        RenderFrameBuilder builder,
        Dictionary<long, int> cache,
        long skinId,
        Matrix4x4 world,
        Matrix4x4[] boneLocals,
        int[] boneParents,
        Matrix4x4[] inverseBinds)
    {
        if (cache.TryGetValue(skinId, out int existing))
            return existing;

        int boneStart = builder.BoneTransforms.Count;
        foreach (Matrix4x4 local in boneLocals)
            builder.BoneTransforms.Add(local);

        int index = builder.Skins.Count;
        builder.Skins.Add(new SkinItem
        {
            ElementId = skinId,
            Active = true,
            World = world,
            BoneStart = boneStart,
            BoneCount = boneLocals.Length,
            BoneParents = boneParents,
            InverseBindPoses = inverseBinds,
            LayerMask = RenderLayers.Default,
        });

        cache[skinId] = index;
        return index;
    }

    private MeshHandle GetMesh(in RenderableSnapshot renderable)
    {
        if (!_meshStates.TryGetValue(renderable.ElementId, out var state) ||
            !ReferenceEquals(state.Points, renderable.MeshPoints) ||
            !ReferenceEquals(state.Uvs, renderable.MeshUvs) ||
            !ReferenceEquals(state.Normals, renderable.MeshNormals) ||
            !ReferenceEquals(state.Colors, renderable.MeshColors) ||
            !ReferenceEquals(state.Tangents, renderable.MeshTangents) ||
            !ReferenceEquals(state.Indices, renderable.MeshIndices) ||
            !ReferenceEquals(state.BoneWeights, renderable.MeshBoneWeights) ||
            !ReferenceEquals(state.BoneIndices, renderable.MeshBoneIndices))
        {
            state = new MeshState
            {
                Points = renderable.MeshPoints,
                Uvs = renderable.MeshUvs,
                Normals = renderable.MeshNormals,
                Colors = renderable.MeshColors,
                Tangents = renderable.MeshTangents,
                Indices = renderable.MeshIndices,
                BoneWeights = renderable.MeshBoneWeights,
                BoneIndices = renderable.MeshBoneIndices,
                Data = BuildMeshData(
                    renderable.MeshPoints!,
                    renderable.MeshIndices,
                    renderable.MeshUvs,
                    renderable.MeshNormals,
                    renderable.MeshColors,
                    renderable.MeshTangents,
                    renderable.MeshBoneWeights,
                    renderable.MeshBoneIndices),
                Version = ++_nextMeshVersion,
            };
            _meshStates[renderable.ElementId] = state;
        }

        return _resources.GetOrCreateMesh(
            "mesh:" + renderable.ElementId.ToString(CultureInfo.InvariantCulture),
            state.Data,
            state.Version);
    }

    private MaterialHandle GetMaterial(
        in SnapshotMaterial material,
        bool vertexColors,
        out ShaderReflection? customReflection)
    {
        customReflection = null;

        if (!string.IsNullOrWhiteSpace(material.ShaderUri))
        {
            if (ShaderAssetRegistry.Instance.TryResolve(
                    material.ShaderUri!, out var program, out long shaderVersion))
            {
                customReflection = program.Reflection ?? ShaderReflection.Empty;
                return CreateCustomMaterial(material, vertexColors, program, shaderVersion);
            }

            WarnOnce(
                "shader:" + material.ShaderUri,
                "[Render] Material shader '{Shader}' is not registered; falling back to the built-in shader.",
                material.ShaderUri);
        }

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
            $"{(transparent ? 1 : 0)}:{(vertexColors ? 1 : 0)}:{material.AlbedoPath}|{material.NormalPath}|" +
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
            Textures: bindings.ToArray(),
            VertexColors: vertexColors);

        return _resources.GetOrCreateMaterial(key, shader, desc);
    }

    /// <summary>
    /// Compiles a gameplay material against a registered custom shader. Textures
    /// are bound by the shader's own reflection names, and object colour values
    /// travel through the per-draw override table so two elements can share one
    /// shader without sharing a material.
    /// </summary>
    private MaterialHandle CreateCustomMaterial(
        in SnapshotMaterial material,
        bool vertexColors,
        in ShaderProgram program,
        long shaderVersion)
    {
        var reflection = program.Reflection ?? ShaderReflection.Empty;
        string uri = program.AssetUri ?? "custom";
        var shader = _resources.GetOrCreateShader("shader:" + uri, program, shaderVersion);

        var bindings = new List<MaterialTextureBinding>();
        foreach (var slot in reflection.Textures)
        {
            if (!TryTextureFor(slot.Name, material, out var texture))
                continue;
            bindings.Add(new MaterialTextureBinding(slot.Slot, texture));
        }

        bool transparent = material.A < 0.999f;
        var desc = new MaterialDesc(
            Blend: transparent ? BlendMode.Alpha : BlendMode.Opaque,
            Cull: material.DoubleSided ? CullMode.None : CullMode.Back,
            DepthTest: !material.NoDepth,
            DepthWrite: !transparent,
            Lit: !material.Unlit,
            CastShadows: !transparent,
            ReceiveShadows: true,
            Parameters: ReadOnlyMemory<MaterialValue>.Empty,
            Textures: bindings.ToArray(),
            VertexColors: vertexColors);

        string key = string.Create(
            CultureInfo.InvariantCulture,
            $"custom-material:{uri}:{shaderVersion}:{(material.Unlit ? 1 : 0)}:" +
            $"{(material.NoDepth ? 1 : 0)}:{(material.DoubleSided ? 1 : 0)}:" +
            $"{(transparent ? 1 : 0)}:{(vertexColors ? 1 : 0)}:{material.AlbedoPath}|{material.NormalPath}|" +
            $"{material.MetallicPath}|{material.RoughnessPath}|{material.EmissionPath}|" +
            $"{material.OcclusionPath}");

        return _resources.GetOrCreateMaterial(key, shader, desc, shaderVersion);
    }

    private static List<DrawOverride> BuildBuiltInOverrides(in SnapshotMaterial material)
    {
        var overrides = new List<DrawOverride>
        {
            new DrawOverride(
                BuiltInMaterialParameters.Albedo,
                MaterialValue.From(new Vector4(material.R, material.G, material.B, material.A))),
            new DrawOverride(
                BuiltInMaterialParameters.UvOffset,
                MaterialValue.From(material.UvOffset)),
            new DrawOverride(
                BuiltInMaterialParameters.UvScale,
                MaterialValue.From(material.UvScale)),
        };

        if (material.EmissionEnergy > 0f &&
            (material.EmissionR > 0f || material.EmissionG > 0f || material.EmissionB > 0f))
        {
            overrides.Add(new DrawOverride(
                BuiltInMaterialParameters.Emissive,
                MaterialValue.From(new Vector3(material.EmissionR, material.EmissionG, material.EmissionB))));
            overrides.Add(new DrawOverride(
                BuiltInMaterialParameters.EmissiveEnergy,
                MaterialValue.From(material.EmissionEnergy)));
        }

        if (material.NormalScale > 0f)
        {
            overrides.Add(new DrawOverride(
                BuiltInMaterialParameters.NormalScale,
                MaterialValue.From(material.NormalScale)));
        }

        if (material.AlphaCutoff > 0f)
        {
            overrides.Add(new DrawOverride(
                BuiltInMaterialParameters.AlphaCutoff,
                MaterialValue.From(material.AlphaCutoff)));
        }

        if (material.Triplanar)
        {
            overrides.Add(new DrawOverride(
                BuiltInMaterialParameters.Triplanar,
                MaterialValue.From(1f)));
            overrides.Add(new DrawOverride(
                BuiltInMaterialParameters.TriplanarWorld,
                MaterialValue.From(material.TriplanarWorld ? 1f : 0f)));
            overrides.Add(new DrawOverride(
                BuiltInMaterialParameters.TriplanarSharpness,
                MaterialValue.From(material.TriplanarSharpness)));
        }

        return overrides;
    }

    /// <summary>
    /// Maps the authorable material fields onto a custom shader's declared
    /// parameters. Names are matched case-insensitively, so a shader can use
    /// <c>albedoTex</c>/<c>albedoColor</c>/<c>metallic</c> and friends and pick
    /// the values up with no extra plumbing; unknown uniforms keep their shader
    /// defaults.
    /// </summary>
    private static List<DrawOverride> BuildCustomOverrides(
        ShaderReflection reflection,
        in SnapshotMaterial material)
    {
        var overrides = new List<DrawOverride>(reflection.Parameters.Count);
        foreach (var parameter in reflection.Parameters)
        {
            if (TryValueFor(parameter.Name, material, out var value))
                overrides.Add(new DrawOverride(parameter.Index, value));
        }

        return overrides;
    }

    private static bool TryValueFor(
        string name,
        in SnapshotMaterial material,
        out MaterialValue value)
    {
        switch (name.ToLowerInvariant())
        {
            case "albedocolor":
            case "albedotint":
            case "basecolor":
            case "basetint":
            case "tint":
            case "color":
                value = MaterialValue.From(new Vector4(material.R, material.G, material.B, material.A));
                return true;
            case "metallic":
                value = MaterialValue.From(material.Metallic);
                return true;
            case "roughness":
                value = MaterialValue.From(material.Roughness);
                return true;
            case "emission":
            case "emissive":
            case "emissioncolor":
                value = MaterialValue.From(new Vector3(material.EmissionR, material.EmissionG, material.EmissionB));
                return true;
            case "emissionenergy":
            case "emissionstrength":
                value = MaterialValue.From(material.EmissionEnergy);
                return true;
            case "normalscale":
                value = MaterialValue.From(material.NormalScale);
                return true;
            case "alphacutoff":
                value = MaterialValue.From(material.AlphaCutoff);
                return true;
            case "uvoffset":
                value = MaterialValue.From(material.UvOffset);
                return true;
            case "uvscale":
                value = MaterialValue.From(material.UvScale);
                return true;
            default:
                value = default;
                return false;
        }
    }

    private bool TryTextureFor(string name, in SnapshotMaterial material, out TextureHandle texture)
    {
        string? path = name.ToLowerInvariant() switch
        {
            "albedotex" or "albedo" or "albedotexture" or "tex0" or "maintex" => material.AlbedoPath,
            "normaltex" or "normal" or "normaltexture" or "normalmap" => material.NormalPath,
            "metallictex" or "metallic" or "metallictexture" or "metallicmap" => material.MetallicPath,
            "roughnesstex" or "roughness" or "roughnesstexture" or "roughnessmap" => material.RoughnessPath,
            "emissiontex" or "emission" or "emissiontexture" or "emissionmap" => material.EmissionPath,
            "occlusiontex" or "occlusion" or "occlusiontexture" or "occlusionmap" or "aotex" => material.OcclusionPath,
            _ => null,
        };

        if (path is null)
        {
            texture = default;
            return false;
        }

        var colorSpace = path == material.EmissionPath || path == material.AlbedoPath
            ? TextureColorSpace.Srgb
            : TextureColorSpace.Linear;

        return TryCreateTexture(path, colorSpace, out texture);
    }

    private void WarnOnce(string key, string message, string value)
    {
        if (_warned.Add(key))
            GameRoot.Log?.Warning(message, value);
    }

    private void AddTexture(
        string? path,
        int slot,
        TextureColorSpace colorSpace,
        List<MaterialTextureBinding> bindings)
    {
        if (!TryCreateTexture(path, colorSpace, out var handle))
            return;

        bindings.Add(new MaterialTextureBinding(slot, handle));
    }

    private bool TryCreateTexture(string? path, TextureColorSpace colorSpace, out TextureHandle texture)
    {
        texture = default;
        if (_textureLoader is null || string.IsNullOrWhiteSpace(path))
            return false;

        var data = _textureLoader(path);
        if (data is null)
            return false;

        data = data with { ColorSpace = colorSpace };
        texture = _resources.GetOrCreateTexture(
            $"texture:{colorSpace}:{path}",
            data);
        return true;
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
            !string.IsNullOrEmpty(renderable.MatOcclusionTexturePath) ||
            !string.IsNullOrEmpty(renderable.MatShaderUri);

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
                null, null, null, null, null, null,
                null);
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
            EmptyToNull(renderable.MatOcclusionTexturePath),
            EmptyToNull(renderable.MatShaderUri));
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
        string? OcclusionPath,
        string? ShaderUri);

    private void BuildParticles(FrameSnapshot snapshot, RenderFrameBuilder builder)
    {
        foreach (var renderable in snapshot.Renderables)
        {
            if (renderable.NodeType != SnapshotNodeType.Particle)
                continue;

            var color = renderable.ParticleColor;
            builder.Particles.Add(new ParticleItem
            {
                ElementId = renderable.ElementId,
                WorldId = renderable.WorldId,
                Active = true,
                World = renderable.Transform,
                Amount = Math.Max(1, renderable.ParticleAmount),
                Lifetime = Math.Max(0.01f, renderable.ParticleLifetime),
                EmissionRadius = Math.Max(0f, renderable.ParticleEmissionRadius),
                SpeedMin = Math.Max(0f, renderable.ParticleSpeedMin),
                SpeedMax = Math.Max(renderable.ParticleSpeedMin, renderable.ParticleSpeedMax),
                Direction = new Vector3(
                    renderable.ParticleDirX,
                    renderable.ParticleDirY,
                    renderable.ParticleDirZ),
                Spread = Math.Clamp(renderable.ParticleSpread, 0f, 180f),
                Size = Math.Max(0.0001f, renderable.ParticleSize),
                Color = new Vector4(
                    color.R / 255f,
                    color.G / 255f,
                    color.B / 255f,
                    color.A / 255f),
                Gravity = renderable.ParticleGravity,
                Emitting = renderable.ParticleEmitting,
                OneShot = renderable.ParticleOneShot,
                LayerMask = renderable.LayerMask == 0 ? RenderLayers.Default : renderable.LayerMask,
            });
        }
    }

    private void BuildLights(FrameSnapshot snapshot, RenderFrameBuilder builder)
    {
        foreach (var renderable in snapshot.Renderables)
        {
            if (renderable.NodeType is not (
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
                WorldId = renderable.WorldId,
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

    private static Vector4[] BuildTangents(Vector3[] normals, double[]? sourceTangents)
    {
        var tangents = new Vector4[normals.Length];

        if (sourceTangents is not null && sourceTangents.Length == normals.Length * 4)
        {
            for (int i = 0; i < tangents.Length; i++)
            {
                tangents[i] = new Vector4(
                    (float)sourceTangents[i * 4],
                    (float)sourceTangents[i * 4 + 1],
                    (float)sourceTangents[i * 4 + 2],
                    (float)sourceTangents[i * 4 + 3]);
            }
            return tangents;
        }

        for (int i = 0; i < tangents.Length; i++)
        {
            Vector3 normal = normals[i];
            Vector3 axis = MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            Vector3 tangent = Vector3.Cross(axis, normal);
            if (tangent.LengthSquared() < 1e-8f)
                tangent = Vector3.Cross(Vector3.UnitZ, normal);

            tangent = tangent.LengthSquared() > 1e-8f
                ? Vector3.Normalize(tangent)
                : Vector3.UnitX;

            tangents[i] = new Vector4(tangent, 1f);
        }

        return tangents;
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
        double[]? sourceColors,
        double[]? sourceTangents,
        float[]? boneWeights,
        int[]? boneIndices)
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

        // Per-vertex colour is opt-in: a mesh that does not paint vertices keeps
        // the cheap opaque-white stream every backend already expects.
        var colors = new Vector4[vertexCount];
        if (sourceColors is not null && sourceColors.Length == vertexCount * 4)
        {
            for (int i = 0; i < vertexCount; i++)
            {
                colors[i] = new Vector4(
                    (float)sourceColors[i * 4],
                    (float)sourceColors[i * 4 + 1],
                    (float)sourceColors[i * 4 + 2],
                    (float)sourceColors[i * 4 + 3]);
            }
        }
        else
        {
            Array.Fill(colors, Vector4.One);
        }

        // A skinned mesh always needs a full tangent basis for Godot's mesh
        // format; when the source has none, derive a stable tangent per vertex.
        if (boneWeights is not null && boneIndices is not null &&
            boneWeights.Length == vertexCount * 4 &&
            boneIndices.Length == vertexCount * 4)
        {
            var skinnedTangents = BuildTangents(normals, sourceTangents);
            return MeshDataFactory.CreateSkinned(
                positions,
                normals,
                uvs,
                skinnedTangents,
                colors,
                boneWeights,
                boneIndices,
                validIndices.ToArray());
        }

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
