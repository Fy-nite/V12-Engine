using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using V12.Core.UI.Paper;
using V12.Rendering;

namespace V12.Core.Rendering;

/// <summary>
/// Converts Paper's CPU draw calls into ordinary screen-space UI draw items.
/// The UI shader URI is resolved through the shader asset registry, so Paper
/// never needs to know about backend shader enums.
/// </summary>
internal sealed class PaperUiDrawBuilder
{
    private readonly RendererResourceCache _resources;
    private long _uiVersion;

    /// <summary>
    /// The 1x1 white texture every untextured UI draw falls back to. The GPU
    /// handle is never cached here: <see cref="RendererResourceCache"/> retires a
    /// texture after a frame without use, and the handle would then be stale.
    /// </summary>
    private static readonly TextureData WhiteTextureData = new(
        TextureDimension.Texture2D,
        1,
        1,
        1,
        1,
        TextureFormat.Rgba8Unorm,
        TextureColorSpace.Linear,
        TextureUsage.Sampled,
        new[] { new TextureSubresource(0, 0, 4, 4, new byte[] { 255, 255, 255, 255 }) });

    public PaperUiDrawBuilder(RendererResourceCache resources)
    {
        _resources = resources;
    }

    /// <summary>
    /// Resolves a render-target view id to its GPU texture. Used when Paper
    /// draws a viewport surface; returning null falls back to white.
    /// </summary>
    public Func<long, TextureHandle?>? ViewTextureProvider { get; set; }

    /// <summary>
    /// Emits the screen-space UI as ordinary draw items.
    /// </summary>
    /// <param name="context">Captured Paper frame plus the surface size in pixels.</param>
    /// <param name="target">Engine-owned target to render into, or null to draw
    /// into the host's presentation surface.</param>
    /// <param name="builder">Frame under construction.</param>
    public void Append(UiFrameContext context, RenderTargetHandle? target, RenderFrameBuilder builder)
    {
        var frame = context.Screen;
        int width = Math.Max(1, context.Width > 0 ? context.Width : frame.Width);
        int height = Math.Max(1, context.Height > 0 ? context.Height : frame.Height);
        long version = ++_uiVersion;

        // No camera: the ortho matrices carry placement. Drawn last so it
        // composites over every scene view.
        builder.Views.Add(new RenderView
        {
            Id = RenderViewIds.ScreenUi,
            ViewportId = 0,
            WorldId = Guid.Empty,
            CameraElementId = 0,
            IsCurrentCamera = false,
            View = Matrix4x4.CreateTranslation(0f, 0f, -1f),
            Projection = Matrix4x4.CreateOrthographic(width, height, 0.1f, 10f),
            ProjectionMetadata = new ProjectionMetadata(
                ProjectionKind.Orthographic,
                0.1f,
                10f,
                0f,
                height),
            Viewport = new ViewportRect(0f, 0f, width, height),
            Clear = new ClearSettings(ClearMode.Color, new Vector4(0f, 0f, 0f, 0f)),
            Target = target,
            LayerMask = RenderLayers.Ui,
            Eye = StereoEye.None,
            Priority = int.MaxValue,
        });

        if (frame.Meshes.Count == 0)
            return;

        string shapeUri = string.IsNullOrWhiteSpace(frame.ShapeShaderUri)
            ? "shader://v12.paper/paper"
            : frame.ShapeShaderUri;
        string textUri = string.IsNullOrWhiteSpace(frame.TextShaderUri)
            ? "shader://v12.paper/text"
            : frame.TextShaderUri;

        for (int i = 0; i < frame.Meshes.Count; i++)
        {
            var mesh = frame.Meshes[i];
            if (mesh.Indices.Length < 3 || mesh.Positions.Length == 0)
                continue;

            string uri = mesh.IsText ? textUri : shapeUri;
            if (!ShaderAssetRegistry.Instance.TryResolve(uri, out var program, out long shaderVersion))
                continue;

            var shader = _resources.GetOrCreateShader("shader:" + uri, program, shaderVersion);
            var uiMesh = BuildMesh(mesh, width, height, version, i, RenderViewIds.ScreenUi);
            if (uiMesh is null)
                continue;

            var parameters = BuildParameters(mesh, frame, program.Reflection);
            var textures = BuildTextures(mesh, program.Reflection);

            var desc = new MaterialDesc(
                Blend: BlendMode.Alpha,
                Cull: CullMode.None,
                DepthTest: false,
                DepthWrite: false,
                Lit: false,
                CastShadows: false,
                ReceiveShadows: false,
                Parameters: parameters,
                Textures: textures);

            var material = _resources.GetOrCreateMaterial(
                $"ui-mat:{RenderViewIds.ScreenUi}:{i}:{uri}",
                shader,
                desc,
                version);

            builder.Items.Add(new DrawItem
            {
                ElementId = 0,
                WorldId = Guid.Empty,
                ViewId = RenderViewIds.ScreenUi,
                Mesh = uiMesh.Value,
                Material = material,
                World = Matrix4x4.Identity,
                Clip = ClipRect.None,
                SortKey = (uint)i,
                LayerMask = RenderLayers.Ui,
                Active = true,
                OverridesIndex = -1,
            });
        }
    }

    /// <summary>
    /// Emits a world-space canvas as ordinary scene meshes. Vertices stay in
    /// canvas pixel space so Paper's brush and scissor uniforms keep working
    /// unchanged; the draw matrix scales them to metres and applies the canvas
    /// placement. No view and no render target is created.
    /// </summary>
    public void AppendWorld(
        long canvasId,
        PaperFrame frame,
        in WorldCanvasSnapshot canvas,
        uint sortBase,
        RenderFrameBuilder builder)
    {
        if (frame.Meshes.Count == 0)
            return;

        int width = Math.Max(1, canvas.PixelWidth);
        int height = Math.Max(1, canvas.PixelHeight);
        long version = ++_uiVersion;

        // Pixel space -> canvas metres -> element placement (row-vector order).
        float metresPerPixelX = canvas.WidthMetres / width;
        float metresPerPixelY = canvas.HeightMetres / height;
        var canvasToWorld = Matrix4x4.CreateScale(
            metresPerPixelX,
            metresPerPixelY,
            MathF.Min(metresPerPixelX, metresPerPixelY)) * canvas.World;

        // The shader reconstructs canvas pixels from vertex positions; make sure
        // it has the surface size even if the frame was captured without one.
        var shaderFrame = frame.Width > 0 && frame.Height > 0
            ? frame
            : new PaperFrame
            {
                Width = width,
                Height = height,
                ShapeShaderUri = frame.ShapeShaderUri,
                TextShaderUri = frame.TextShaderUri,
            };

        string shapeUri = string.IsNullOrWhiteSpace(frame.ShapeShaderUri)
            ? "shader://v12.paper/paper"
            : frame.ShapeShaderUri;
        string textUri = string.IsNullOrWhiteSpace(frame.TextShaderUri)
            ? "shader://v12.paper/text"
            : frame.TextShaderUri;

        for (int i = 0; i < frame.Meshes.Count; i++)
        {
            var mesh = frame.Meshes[i];
            if (mesh.Indices.Length < 3 || mesh.Positions.Length == 0)
                continue;

            string uri = mesh.IsText ? textUri : shapeUri;
            if (!ShaderAssetRegistry.Instance.TryResolve(uri, out var program, out long shaderVersion))
                continue;

            var shader = _resources.GetOrCreateShader("shader:" + uri, program, shaderVersion);
            var uiMesh = BuildMesh(mesh, width, height, version, i, canvasId);
            if (uiMesh is null)
                continue;

            var parameters = BuildParameters(mesh, shaderFrame, program.Reflection);
            var textures = BuildTextures(mesh, program.Reflection);

            var desc = new MaterialDesc(
                Blend: BlendMode.Alpha,
                Cull: CullMode.None,
                // Depth-test against the scene so a wall in front hides the
                // canvas, but never write depth: UI layers are painter ordered.
                DepthTest: true,
                DepthWrite: false,
                Lit: false,
                CastShadows: false,
                ReceiveShadows: false,
                Parameters: parameters,
                Textures: textures);

            var material = _resources.GetOrCreateMaterial(
                $"ui-mat:w{canvasId}:{i}:{uri}",
                shader,
                desc,
                version);

            builder.Items.Add(new DrawItem
            {
                ElementId = canvas.ElementId,
                WorldId = canvas.WorldId,
                // Scene item: renders in every view whose world/layer matches.
                ViewId = 0,
                Mesh = uiMesh.Value,
                Material = material,
                World = canvasToWorld,
                Clip = ClipRect.None,
                SortKey = sortBase + (uint)i,
                LayerMask = RenderLayers.Default,
                Active = true,
                OverridesIndex = -1,
            });
        }
    }

    private MeshHandle? BuildMesh(
        PaperMesh mesh,
        int width,
        int height,
        long version,
        int drawIndex,
        long viewId)
    {
        int sourceVertices = mesh.Positions.Length;
        var remap = new Dictionary<int, int>(Math.Min(sourceVertices, mesh.Indices.Length));
        var positions = new List<Vector3>(mesh.Indices.Length);
        var uvs = new List<Vector2>(mesh.Indices.Length);
        var colors = new List<Vector4>(mesh.Indices.Length);
        var indices = new List<uint>(mesh.Indices.Length);
        float halfW = width * 0.5f;
        float halfH = height * 0.5f;
        float z = 0.0005f * drawIndex;

        foreach (uint raw in mesh.Indices)
        {
            if (raw >= sourceVertices)
                continue;

            int g = (int)raw;
            if (!remap.TryGetValue(g, out int local))
            {
                local = positions.Count;
                remap[g] = local;

                var p = mesh.Positions[g];
                positions.Add(new Vector3(p.X - halfW, halfH - p.Y, z));

                uvs.Add(g < mesh.UVs.Length ? mesh.UVs[g] : Vector2.Zero);

                if (g < mesh.Colors.Length)
                {
                    colors.Add(mesh.Colors[g]);
                }
                else
                {
                    colors.Add(Vector4.One);
                }
            }

            indices.Add((uint)local);
        }

        if (indices.Count < 3 || positions.Count == 0)
            return null;

        var normals = new Vector3[positions.Count];
        Array.Fill(normals, Vector3.UnitZ);

        var data = MeshDataFactory.CreatePositionNormalUv0Color(
            positions.ToArray(),
            normals,
            uvs.ToArray(),
            colors.ToArray(),
            indices.ToArray());

        return _resources.GetOrCreateMesh(
            $"ui-mesh:{viewId}:{drawIndex}:{mesh.TextureId}:{mesh.FontAtlasId}:{mesh.IndexOffset}",
            data,
            version);
    }

    private MaterialValue[] BuildParameters(
        PaperMesh mesh,
        PaperFrame frame,
        ShaderReflection? reflection)
    {
        if (reflection is null || reflection.Parameters.Count == 0)
            return Array.Empty<MaterialValue>();

        int maxIndex = -1;
        foreach (var parameter in reflection.Parameters)
            maxIndex = Math.Max(maxIndex, parameter.Index);

        if (maxIndex < 0)
            return Array.Empty<MaterialValue>();

        var values = new MaterialValue[maxIndex + 1];
        foreach (var parameter in reflection.Parameters)
        {
            values[parameter.Index] = ValueFor(parameter.Name, mesh, frame);
        }

        return values;
    }

    private static MaterialValue ValueFor(string name, PaperMesh mesh, PaperFrame frame)
    {
        return name switch
        {
            "scissorTransform" => MaterialValue.From(mesh.ScissorTransform),
            "scissorTranslation" => MaterialValue.From(mesh.ScissorTranslation),
            "scissorExt" => MaterialValue.From(mesh.ScissorExt),
            "brushTransform" => MaterialValue.From(mesh.BrushTransform),
            "brushTranslation" => MaterialValue.From(mesh.BrushTranslation),
            "brushType" => MaterialValue.From(mesh.Brush.Type),
            "brushColor1" => MaterialValue.From(mesh.Brush.Color1),
            "brushColor2" => MaterialValue.From(mesh.Brush.Color2),
            "brushParams" => MaterialValue.From(new Vector4(
                mesh.Brush.Point1.X,
                mesh.Brush.Point1.Y,
                mesh.Brush.Point2.X,
                mesh.Brush.Point2.Y)),
            "brushParams2" => MaterialValue.From(new Vector2(
                mesh.Brush.CornerRadii,
                mesh.Brush.Feather)),
            "textureTransform" => MaterialValue.From(mesh.TextureTransform),
            "textureTranslation" => MaterialValue.From(mesh.TextureTranslation),
            "atlasTexelSize" => MaterialValue.From(mesh.AtlasTexelSize),
            "sdfPxRange" => MaterialValue.From(mesh.SdfPxRange),
            "canvasSize" => MaterialValue.From(new Vector2(
                frame.Width > 0 ? frame.Width : 1,
                frame.Height > 0 ? frame.Height : 1)),
            _ => default,
        };
    }

    private MaterialTextureBinding[] BuildTextures(PaperMesh mesh, ShaderReflection? reflection)
    {
        var bindings = new List<MaterialTextureBinding>(2);

        TextureHandle texture;
        if (mesh.Texture is PaperTextureRef viewRef &&
            ViewTextureProvider?.Invoke(viewRef.ViewId) is { } viewTexture)
        {
            texture = viewTexture;
        }
        else if (mesh.TextureData is null)
        {
            texture = GetWhiteTexture();
        }
        else
        {
            texture = _resources.GetOrCreateTexture(
                "ui-tex:" + TextureKey(mesh.Texture, mesh.TextureId),
                mesh.TextureData,
                mesh.TextureVersion);
        }

        var font = mesh.FontAtlasData is null
            ? GetWhiteTexture()
            : _resources.GetOrCreateTexture(
                "ui-font:" + TextureKey(mesh.FontAtlas, mesh.FontAtlasId),
                mesh.FontAtlasData,
                mesh.FontAtlasVersion);

        if (reflection is null || reflection.Textures.Count == 0)
        {
            bindings.Add(new MaterialTextureBinding(0, texture));
            if (mesh.IsText)
                bindings.Add(new MaterialTextureBinding(1, font));
            return bindings.ToArray();
        }

        foreach (var slot in reflection.Textures)
        {
            var handle = slot.Name switch
            {
                "fontTex" => font,
                "tex0" => texture,
                _ => texture,
            };
            bindings.Add(new MaterialTextureBinding(slot.Slot, handle));
        }

        return bindings.ToArray();
    }

    private TextureHandle GetWhiteTexture()
    {
        // Asked of the cache every time on purpose: the entry may have been
        // retired, and a retained handle would fail validation downstream.
        return _resources.GetOrCreateTexture("ui-white", WhiteTextureData, 0);
    }

    private static string TextureKey(object? texture, string id) =>
        !string.IsNullOrEmpty(id)
            ? id
            : texture is null
                ? "null"
                : RuntimeHelpers.GetHashCode(texture).ToString();

}
