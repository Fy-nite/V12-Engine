using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using System.IO;
using V12.Core.Core.Interfaces;
using V12.Core.NetworkCable;
using V12.Core.Networking;
using V12.Core.Registry;
using V12.Core.Input;
using V12.Core.Rendering;
using V12.Core.UI;
using V12.Components.UI;
using V12.Components;
using V12.Components.Renderables;
using V12.Core.Interfaces;
using V12.Core.Interfaces.Renderer;
using V12.WorldML;
namespace V12.Core
{
    /// <summary>
    /// The GameRoot class serves as the central entry point for the game, managing the overall game state and coordinating interactions between various components and systems. It is responsible for initializing the game world, handling game logic, and facilitating communication between different parts of the game architecture. The GameRoot class may also manage resources, handle user input, and oversee the main game loop to ensure smooth gameplay.
    /// </summary>
    public class GameRoot
    {
        /// <summary>
        /// Raised when a NetworkClient instance is registered via SetupNetworking.
        /// Handlers receive the registered NetworkClient.
        /// </summary>
        public event Action<V12.Core.NetworkCable.NetworkClient?>? OnNetworkClientRegistered;

        public List<World> Worlds = new List<World>();
        public static GameRoot Instance { get; private set; }
        /// <summary>
        /// The currently selected or focused world. When set, only this world will be updated by Update().
        /// </summary>
        public World? SelectedWorld { get; private set; }
           //public World UserSpace = new World("UserSpace"); // always open and active no matter what, contains the UI for being able to control the game and select worlds, etc. This world is not meant to be used for actual game content, but rather for the user interface and control of the game.
           //public World HomeWorld = new World("HomeWorld");

        /// <summary>
        /// Central service registry. Networking, DirtyTracker, and other engine services are registered here.
        /// </summary>
        public RegistryController Registry { get; } = new RegistryController("GameRoot");

        /// <summary>
        /// The NetworkCables message bus used by this game instance.
        /// </summary>
        public NetworkCables Cables { get; } = new NetworkCables();

        /// <summary>
        /// Elements that persist across all worlds — never touched by WorldSync.
        /// The Player and PlayerCamera3D live here so world switches don't lose them.
        /// </summary>
        public World PersistentWorld { get; } = new World("__Persistent__");

        /// <summary>Convenience accessor for the local Player element in PersistentWorld.</summary>
        public IWorldElement? Player => PersistentWorld.Root.FirstOrDefault(e => e.Name == "Player");

        private Thread? _networkingThread;
        private CancellationTokenSource? _networkingCts;
        // Core-managed desktop dashboard (frontend-agnostic)
        private IDashboard? _dashboard;
        private IInputHandler? _dashboardHandler;
        public IRenderer renderer;

        public GameRoot() {
            //Worlds.Add(UserSpace);
            //Worlds.Add(HomeWorld);
            // Default to the first created world as the focused world
            //SelectedWorld = HomeWorld;
            Templates["empty"] = (name) => new World(name);
            //Templates["default"] = (name) => new V12.WorldML.WorldMLParser().Parse(ReadResource("V12.Templates.Default.xml"));
            //Templates["Gridspace"] = (name) => new V12.WorldML.WorldMLParser().Parse(ReadResource("V12.Templates.Gridspace.xml"));
            Templates["HotReload"] = (name) => {
                var world = new World(name);
                var reloader = new V12.Core.Networking.WorldXmlHotReloader(world);
                reloader.Initialize();
                Registry.Register($"HotReloader_{name}", reloader);
                return world;
            };

            Registry.Register("NetworkCables", Cables);
            // Register core InputService so glue code can forward platform input
            var inputService = new V12.Core.Input.InputService();
            Registry.Register("InputService", inputService);
     
            // XRTrackingService is registered by the renderer (e.g. TwoDog)
            // when XR mode is active. PlayerComponent finds head/hand elements
            // by looking for XRHeadComponent / XRHandComponent on children.
            // Register a default in-engine UIBuilder so dashboards can build
            // UI as world elements which frontends will sync and render.
            try
            {
                var uiBuilder = new UIBuilder();
                Registry.Register("UIBuilder", uiBuilder);
            }
            catch { }



            Instance = this;
        }

        public string ReadResource(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null)
                {
                    Console.WriteLine($"Resource '{name}' not found.");
                    return "";
                }
                using (StreamReader reader = new StreamReader(stream))
                {
                    string result = reader.ReadToEnd();
                    return result;
                }
            }
        }
        public void Initialize()
        {
#if DEBUG
            foreach (var World in Worlds)
            {
                Console.WriteLine($"Initializing world: {World.WorldName}");
            }
            
#endif
            foreach (var service in Registry.GetAll<IGameService>().ToArray())
                service.Initialize(this);

            StartNetworkingThread();
            SetupUI();

            if (Registry.Get("IRenderer") == null)
            {
                Console.WriteLine("starting V12 in headless mode");
                renderer = null;
            }
            else
            {
                Console.WriteLine("found IRenderer, loading...");
                renderer = (IRenderer)Registry.Get("IRenderer").ServiceInstance;
            }

        }
            
        
        public void V12Tick()
        {
        
                foreach (var service in Registry.GetAll<IGameService>())
                    service.Update(this);
                
                List<IRenderable> renderables = GetAllRenderables();
                if (renderer != null)
                {
                    foreach (var r in renderables)
                    {
                        renderer.QueueItem(r);
                    }
                }
                renderer.step();
                
        }

        public List<IRenderable> GetAllRenderables()
        {
            var renderables = new List<IRenderable>();

            void CollectRenderables(IWorldElement element)
            {
                if (element == null)
                    return;

                if (element.Components != null)
                {
                    // Only add TransformComponent as IRenderable if the element
                    // has no other IRenderable (avoiding duplicate nodes).
                    bool hasOtherRenderable = false;
                    bool hasMeshRenderer = false;
                    TransformComponent transformComp = null;
                    foreach (var component in element.Components.ToArray())
                    {
                        if (component is TransformComponent tc)
                            transformComp = tc;
                        else if (component is V12.Components.Renderables.MeshRenderer)
                        {
                            hasMeshRenderer = true;
                            hasOtherRenderable = true;
                        }
                        else if (component is IRenderable)
                            hasOtherRenderable = true;
                    }

                    if (transformComp != null && !hasOtherRenderable)
                        renderables.Add(transformComp);

                    foreach (var component in element.Components.ToArray())
                    {
                        if (component is IRenderable r && !(component is TransformComponent))
                        {
                            // Skip bare MeshComponent when a MeshRenderer wrapper exists
                            if (component is V12.Components.MeshComponent && hasMeshRenderer)
                                continue;
                            renderables.Add(r);
                        }
                    }
                }

                if (element.Children != null)
                {
                    foreach (var child in element.Children.ToArray())
                        CollectRenderables(child);
                }
            }

            // Collect from persistent world first
            foreach (var element in PersistentWorld.Root.ToArray())
                CollectRenderables(element);

            // Then from selected world
            if (SelectedWorld != null)
            {
                foreach (var element in SelectedWorld.Root.ToArray())
                    CollectRenderables(element);
            }

            return renderables;
        }

        public FrameSnapshot CaptureFrame()
        {
            var snapshot = new FrameSnapshot();

            // Capture the selected world first (level geometry, entities)
            if (SelectedWorld != null)
                CaptureWorldFrame(snapshot, SelectedWorld);

            // Then capture the persistent world (Player, camera, etc.) so
            // the player's CameraComponent.IsCurrent=true always wins.
            CaptureWorldFrame(snapshot, PersistentWorld);

            return snapshot;
        }

        /// <summary>
        /// Capture all renderables and audio sources from a single world into the snapshot.
        /// Thread-safe: acquires the world's read lock.
        /// </summary>
        private void CaptureWorldFrame(FrameSnapshot snapshot, World world)
        {
            world.Lock.EnterReadLock();
            try
            {
                // ── Walk tree for renderables ──
                void CaptureRenderables(IWorldElement element, HashSet<long> renderedElementIds)
                {
                    if (element?.Components == null) return;

                    // Determine which components to snapshot (avoid duplicates per element)
                    bool hasMeshRenderer = false;
                    bool hasOtherRenderable = false;
                    TransformComponent transformComp = null;
                    IRenderable primaryRenderable = null;
                    foreach (var c in element.Components)
                    {
                        if (c is TransformComponent tc)
                            transformComp = tc;
                        else if (c is V12.Components.Renderables.MeshRenderer)
                            hasMeshRenderer = hasOtherRenderable = true;
                        else if (c is IRenderable)
                            hasOtherRenderable = true;
                    }

                    // Pick the primary renderable: prefer non-Transform if one exists
                    foreach (var c in element.Components)
                    {
                        if (c is IRenderable r)
                        {
                            if (r is TransformComponent) continue;
                            if (r is MeshComponent && hasMeshRenderer) continue;
                            primaryRenderable = r;
                            break;
                        }
                    }
                    primaryRenderable ??= transformComp;

                    if (primaryRenderable != null)
                        EmitSnapshot(element, primaryRenderable, renderedElementIds);

                    if (element.Children != null)
                        foreach (var child in element.Children)
                            CaptureRenderables(child, renderedElementIds);
                }

                void EmitSnapshot(IWorldElement element, IRenderable renderable, HashSet<long> renderedElementIds)
                {
                    var rs = new RenderableSnapshot();
                    rs.ElementId = element.Id;
                    rs.Name = element.Name ?? "";

                    // ParentId for hierarchy parenting
                    if (element.Parent != null)
                        rs.ParentId = element.Parent.Id;

                    // World transform (for non-hierarchy renderers)
                    rs.Transform = element.WorldTransform;
                    rs.IsWorldLocked = renderable is ITransformRenderable itr && itr.IsWorldLocked;

                    // Local transform (for hierarchy-based renderers)
                    var tc = element.GetComponent<TransformComponent>();
                    if (tc != null)
                        rs.LocalTransform = tc.Transform;
                    else
                    {
                        var lt = element.LocalTransform;
                        rs.LocalTransform = System.Numerics.Matrix4x4.CreateScale(lt.Scale)
                                          * System.Numerics.Matrix4x4.CreateFromQuaternion(lt.Rotation)
                                          * System.Numerics.Matrix4x4.CreateTranslation(lt.Position);
                    }
                    rs.HasLocalTransform = true;

                    // Emit parent-chain placeholders for non-renderable ancestors
                    var ancestor = element.Parent;
                    while (ancestor != null)
                    {
                        if (!renderedElementIds.Add(ancestor.Id)) break;

                        bool hasAnyRenderable = false;
                        foreach (var comp in ancestor.Components)
                        {
                            if (comp is IRenderable) { hasAnyRenderable = true; break; }
                        }

                        if (!hasAnyRenderable)
                        {
                            var parentRs = new RenderableSnapshot();
                            parentRs.ElementId = ancestor.Id;
                            parentRs.Name = ancestor.Name ?? "";
                            parentRs.ParentId = ancestor.Parent?.Id ?? 0;
                            parentRs.NodeType = SnapshotNodeType.RawElement;
                            var atc = ancestor.GetComponent<TransformComponent>();
                            parentRs.Transform = ancestor.WorldTransform;
                            if (atc != null)
                                parentRs.LocalTransform = atc.Transform;
                            else
                            {
                                var alt = ancestor.LocalTransform;
                                parentRs.LocalTransform = System.Numerics.Matrix4x4.CreateScale(alt.Scale)
                                                        * System.Numerics.Matrix4x4.CreateFromQuaternion(alt.Rotation)
                                                        * System.Numerics.Matrix4x4.CreateTranslation(alt.Position);
                            }
                            parentRs.HasLocalTransform = true;
                            snapshot.Renderables.Add(parentRs);
                        }

                        ancestor = ancestor.Parent;
                    }

                    renderedElementIds.Add(element.Id);

                    // Light
                    if (renderable is ILightRenderable light)
                    {
                        switch (light.Type)
                        {
                            case LightType.Directional: rs.NodeType = SnapshotNodeType.LightDirectional; break;
                            case LightType.Spot:        rs.NodeType = SnapshotNodeType.LightSpot; break;
                            default:                    rs.NodeType = SnapshotNodeType.LightPoint; break;
                        }
                        rs.LightColor = light.Color;
                        rs.LightIntensity = light.Intensity;
                        rs.LightRange = light.Range;
                        rs.LightAngle = light.Angle;
                        rs.LightSpotSoftness = light.SpotSoftness;
                    }
                    // Mesh
                    else if (renderable is IMeshRenderable mesh)
                    {
                        MeshComponent mc = null;
                        if (mesh is MeshComponent mcDirect)
                            mc = mcDirect;
                        else if (mesh is MeshRenderer mr && mr.Mesh is MeshComponent mcWrap)
                            mc = mcWrap;

                        if (mc != null)
                        {
                            switch (mc.Shape)
                            {
                                case MeshShape.Box:      rs.NodeType = SnapshotNodeType.MeshBox; break;
                                case MeshShape.Sphere:   rs.NodeType = SnapshotNodeType.MeshSphere; break;
                                case MeshShape.Capsule:  rs.NodeType = SnapshotNodeType.MeshCapsule; break;
                                case MeshShape.Cylinder: rs.NodeType = SnapshotNodeType.MeshCylinder; break;
                                case MeshShape.Plane:    rs.NodeType = SnapshotNodeType.MeshPlane; break;
                                case MeshShape.Custom:   rs.NodeType = SnapshotNodeType.MeshCustom; break;
                                default:                 rs.NodeType = SnapshotNodeType.MeshBox; break;
                            }
                            rs.MeshWidth = mc.Width;
                            rs.MeshHeight = mc.Height;
                            rs.MeshDepth = mc.Depth;
                            rs.MeshPoints = mc.MeshPoints;
                            rs.MeshIndices = mc.Indices;

                            // Capture MaterialComponent if present on the same element
                            var matComp = element.GetComponent<V12.Components.MaterialComponent>();
                            if (matComp != null)
                            {
                                rs.MatR = matComp.R;
                                rs.MatG = matComp.G;
                                rs.MatB = matComp.B;
                                rs.MatA = matComp.A;
                                rs.MatMetallic = matComp.Metallic;
                                rs.MatRoughness = matComp.Roughness;
                                rs.MatTexturePath = matComp.PrimaryTexture ?? "";
                                rs.MatUvOffsetX = matComp.Uv1OffsetX;
                                rs.MatUvOffsetY = matComp.Uv1OffsetY;
                                rs.MatUvScaleX = matComp.Uv1ScaleX;
                                rs.MatUvScaleY = matComp.Uv1ScaleY;
                            }
                        }
                        else
                        {
                            rs.NodeType = SnapshotNodeType.RawElement;
                        }
                    }
                    // Camera
                    else if (renderable is ICameraRenderable cam)
                    {
                        rs.NodeType = SnapshotNodeType.Camera;
                        rs.Fov = cam.FieldOfView;
                        rs.NearClip = cam.NearClip;
                        rs.FarClip = cam.FarClip;
                        rs.IsCurrentCamera = cam.IsCurrent;
                    }
                    // Sprite
                    else if (renderable is ISpriteRenderable sprite)
                    {
                        rs.NodeType = SnapshotNodeType.Sprite;
                        rs.TextureSource = sprite.Texture?.Source ?? "";
                        rs.SizeX = sprite.Size.X;
                        rs.SizeY = sprite.Size.Y;
                        rs.Tint = sprite.Tint;
                    }
                    // SVG
                    else if (renderable is ISvgRenderable svg)
                    {
                        rs.NodeType = SnapshotNodeType.Svg;
                        rs.SvgContent = svg.SvgContent ?? "";
                        rs.SizeX = svg.Size.X;
                        rs.SizeY = svg.Size.Y;
                        rs.Tint = svg.Tint;
                    }
                    // Text
                    else if (renderable is ITextRenderable text)
                    {
                        rs.NodeType = SnapshotNodeType.Text;
                        rs.TextContent = text.Text ?? "";
                        rs.TextColor = text.Color;
                        rs.FontSize = text.FontSize;
                    }
                    // Fallback
                    else
                    {
                        rs.NodeType = SnapshotNodeType.RawElement;
                    }

                    snapshot.Renderables.Add(rs);
                }

                var renderedIds = new HashSet<long>();
                foreach (var root in world.Root.ToArray())
                    CaptureRenderables(root, renderedIds);

                // ── Audio sources ──
                void CaptureAudio(IWorldElement element)
                {
                    if (element?.Components == null) return;
                    foreach (var c in element.Components.ToArray())
                    {
                        if (c is IAudioSource src)
                        {
                            var a = new AudioSourceSnapshot();
                            a.OwnerElementId = src.Id;
                            a.Position = src.Position;
                            a.Volume = src.Volume;
                            a.Pitch = src.Pitch;
                            a.MaxDistance = src.MaxDistance;
                            a.IsPlaying = src.IsPlaying;
                            a.ClipPath = src.AudioClipPath ?? "";
                            snapshot.AudioSources.Add(a);
                        }
                    }
                    if (element.Children != null)
                        foreach (var child in element.Children)
                            CaptureAudio(child);
                }

                foreach (var root in world.Root.ToArray())
                    CaptureAudio(root);

                // ── Audio listener ──
                var listenerElement = world.FindElementWithComponentRecursive<IAudioListener>();
                if (listenerElement != null)
                {
                    var listenerComp = listenerElement.GetComponent<IAudioListener>();
                    if (listenerComp != null)
                    {
                        snapshot.Listener = new AudioListenerSnapshot
                        {
                            Position = listenerComp.Position,
                            Forward = listenerComp.Forward,
                            Up = listenerComp.Up,
                            HasValue = true
                        };
                    }
                }
            }
            finally { world.Lock.ExitReadLock(); }
        }

        private void SetupUI()
        {
            var input = Registry.Get<InputService>();

            // If a dashboard implementation is already registered in the
            // registry (key: "Dashboard" or by type), adopt it so the host
            // can supply its own implementation at any time.
            try
            {
                // Prefer named registration
                var named = Registry.Get<IDashboard>("Dashboard");
                if (named != null) _dashboard = named;
                // Fallback: first registered IDashboard instance
                if (_dashboard == null) _dashboard = Registry.Get<IDashboard>();

                if (input != null && _dashboard != null && _dashboardHandler == null)
                {
                    _dashboardHandler = new DashboardInputHandler(_dashboard);
                    input.RegisterHandler(_dashboardHandler);
                }
            }
            catch { }
            
        }
        /// <summary>
        /// Configure networking for this game instance. Call before <see cref="Initialize"/>.
        /// Registers <see cref="NetworkHost"/> or <see cref="NetworkClient"/> and a <see cref="DirtyTracker"/>
        /// in the <see cref="Registry"/> and hooks the tracker to the selected world.
        /// </summary>
        /// <param name="port">Port to listen on (server) or connect to (client).</param>
        /// <param name="connectHost">Host to connect to. If null, runs as server.</param>
        /// <param name="isServer">Explicitly force server mode even when <paramref name="connectHost"/> is null.</param>
        public void SetupNetworking(int port = 7777, string? connectHost = null, bool isServer = false, string? worldArchivePath = null)
        {
            if (isServer || connectHost == null)
            {
                var host = new NetworkHost(port, Cables);
                host.OnClientConnected += (playerId) =>
                {
                    var world = SelectedWorld;
                    if (world == null)
                    {
                        Console.WriteLine("[GameRoot] Client connected but SelectedWorld is null – no WorldSync sent.");
                        return;
                    }

                    // Send V12World archive first so the client can resolve asset URIs
                    if (worldArchivePath != null && File.Exists(worldArchivePath))
                    {
                        try
                        {
                            var archiveBytes = File.ReadAllBytes(worldArchivePath);
                            var archiveName = Path.GetFileName(worldArchivePath);
                            var payload = new byte[4 + archiveName.Length + archiveBytes.Length];
                            BitConverter.GetBytes(archiveName.Length).CopyTo(payload, 0);
                            System.Text.Encoding.UTF8.GetBytes(archiveName).CopyTo(payload, 4);
                            archiveBytes.CopyTo(payload, 4 + archiveName.Length);

                            Cables.SendData(new MessageDTO
                            {
                                Sender = new Uri("networkcables://server"),
                                MessageType = MessageType.WorldArchive,
                                Message = payload
                            });
                            Console.WriteLine($"[GameRoot] Sent V12World archive '{archiveName}' ({archiveBytes.Length} bytes)");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[GameRoot] ERROR sending V12World archive: {ex.GetType().Name}: {ex.Message}");
                        }
                    }

                    Console.WriteLine($"[GameRoot] Client connected → sending WorldSync '{world.WorldName}' ({world.Root.Count} elements)");
                    try
                    {
                        Cables.SendData(new MessageDTO
                        {
                            Sender = new Uri("networkcables://server"),
                            MessageType = MessageType.WorldSync,
                            Message = AncientCompressor.Compress(world)
                        });
                        Console.WriteLine($"[GameRoot] WorldSync queued for send.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[GameRoot] ERROR serialising WorldSync: {ex.GetType().Name}: {ex.Message}");
                    }
                };
                Registry.Register("NetworkHost", host);
                Console.WriteLine($"[GameRoot] NetworkHost registered on port {port}.");
            }
            else
            {
                var client = new NetworkClient(connectHost, port, Cables);
                Registry.Register("NetworkClient", client);
                Console.WriteLine($"[GameRoot] NetworkClient registered, targeting {connectHost}:{port}.");
                try { OnNetworkClientRegistered?.Invoke(client); } catch { }
            }

            var dirtyTracker = new DirtyTracker(Cables, "networkcables://gameroot")
            {
                ThrottleInterval = 0.1f,
                MaxBatchSize = 50
            };

            if (SelectedWorld != null)
                dirtyTracker.TrackWorld(SelectedWorld);

            // Track the persistent world so Player components are synced
            dirtyTracker.TrackWorld(PersistentWorld);

            Registry.Register("DirtyTracker", dirtyTracker);
            Console.WriteLine("[GameRoot] DirtyTracker registered.");

            // Wire up a connection-state predicate so the DirtyTracker stays quiet when
            // nobody is connected (server with no clients, or client with no live link).
            // The host/client are registered above/below in the Registry, so we resolve
            // them lazily on each check rather than capturing a possibly-null reference now.
            dirtyTracker.HasConnectedPeer = () =>
            {
                try
                {
                    var host = Registry.Get<NetworkHost>("NetworkHost");
                    if (host != null)
                        return host.ClientCount > 0;

                    var client = Registry.Get<NetworkClient>("NetworkClient");
                    if (client != null)
                        return client.IsConnected;
                }
                catch { /* registry not ready yet – treat as no peer */ }

                return false;
            };

            // Wire SyncManager to the NetworkCables used by this GameRoot so SyncValues are transported
            try
            {
                var syncBridge = new V12.Core.Networking.SyncNetworkBridge(Cables, "networkcables://gameroot");
                Registry.Register("SyncNetworkBridge", syncBridge);
                Console.WriteLine("[GameRoot] SyncNetworkBridge registered.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameRoot] Failed to create SyncNetworkBridge: {ex.Message}");
            }
        }

        /// <summary>
        /// Start networking: either host (<see cref="NetworkHost.StartAsync"/>) or client (<see cref="NetworkClient.ConnectAsync"/>).
        /// Uses the <paramref name="token"/> for clean shutdown. Call after <see cref="SetupNetworking"/>.
        /// </summary>
        public void StartNetworkingAsync(CancellationToken token = default)
        {
            var host = Registry.Get<NetworkHost>("NetworkHost");
            if (host != null)
            {
                _ = host.StartAsync(token);
                return;
            }

            var client = Registry.Get<NetworkClient>("NetworkClient");
            if (client != null)
                _ = client.ConnectAsync(token);
        }

        private void StartNetworkingThread()
        {
            if (_networkingCts != null) return; // already running
            _networkingCts = new CancellationTokenSource();
            var token = _networkingCts.Token;

            _networkingThread = new Thread(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    Cables.Update();
                    Thread.Sleep(10);
                }
            }) { IsBackground = true, Name = "V12NetworkingThread" };
            _networkingThread.Start();
        }

        /// <summary>
        /// Stop the internal networking thread and dispose networking resources registered in the registry.
        /// </summary>
        public void Shutdown()
        {
            _networkingCts?.Cancel();
            Registry.Get<NetworkHost>("NetworkHost")?.Dispose();
            Registry.Get<NetworkClient>("NetworkClient")?.Dispose();
        }

        public void Update(float deltaTime)
        {
            // If a dashboard implementation appears later in the registry,
            // pick it up lazily so frontends may register on demand.
            try
            {
                if (_dashboard == null)
                {
                    var named = Registry.Get<IDashboard>("Dashboard");
                    if (named != null) _dashboard = named;
                    if (_dashboard == null) _dashboard = Registry.Get<IDashboard>();

                    // If still null, but a core IUIBuilder exists, create the default dashboard.
                    if (_dashboard == null)
                    {
                        try
                        {
                            var uiBuilder = Registry.Get<V12.Core.UI.IUIBuilder>("UIBuilder");
                            if (uiBuilder == null) uiBuilder = Registry.Get<V12.Core.UI.IUIBuilder>();
                            if (uiBuilder != null)
                            {
                                var d = new V12.Core.UI.DefaultDashboard();
                                d.Initialize(uiBuilder, this);
                                _dashboard = d;
                                // register so future lookups find it
                                Registry.Register("Dashboard", _dashboard);
                            }
                        }
                        catch { }
                    }

                    if (_dashboard != null)
                    {
                        var input = Registry.Get<InputService>();
                        if (input != null && _dashboardHandler == null)
                        {
                            _dashboardHandler = new DashboardInputHandler(_dashboard);
                            input.RegisterHandler(_dashboardHandler);
                        }
                    }
                }
            }
            catch { }
            if (SelectedWorld != null)
            {
                try
                {
                    SelectedWorld.Update(deltaTime);
                    try { _dashboard?.Update(deltaTime); } catch { }
                }
                catch { }
            }
            else
            {
                //UserSpace.Update(deltaTime);
            }

            // Always update the persistent world (Player, camera, etc.)
            PersistentWorld.Update(deltaTime);

            // Let the dashboard update first (if present) so UI values are
            // refreshed from the current world state before services run.
            try { _dashboard?.Update(deltaTime); } catch { }

            Registry.Update(deltaTime);
        }

        // Input handler that toggles the core Dashboard/Inspector on Escape or Dash.
        class DashboardInputHandler : IInputHandler
        {
            private readonly IDashboard _dash;

            public DashboardInputHandler(IDashboard dash) { _dash = dash; }

            public void OnInputEvent(InputEvent evt)
            {
                if (evt == null) return;
                try
                {
                    // Toggles Dashboard (which serves as the Inspector UI)
                    if (evt.Type == InputEventType.ButtonDown && (evt.Name == "Escape" || evt.Name == "dash"))
                    {
                        if (_dash.IsOpen)
                            _dash.Close();
                        else
                            _dash.Open();
                    }
                }
                catch { }
            }
        }

        public void SelectWorld(World world)
        {
            if (world == null) return;
            if (!Worlds.Contains(world)) return;

            var oldWorld = SelectedWorld;
            SelectedWorld = world;

            // Auto-track via DirtyTracker when selecting a world.
            // If the same world is being re-selected (e.g. after ReplaceFrom),
            // still re-track it so new elements from ReplaceFrom are picked up.
            var tracker = Registry.Get<DirtyTracker>("DirtyTracker");
            if (tracker != null)
            {
                if (oldWorld != null && oldWorld != world)
                    tracker.UntrackWorld(oldWorld);
                tracker.TrackWorld(world);
            }
        }

        public bool SelectWorldByName(string name)
        {
            var found = Worlds.Find(w => w.WorldName == name);
            if (found != null)
            {
                SelectedWorld = found;
                return true;
            }
            return false;
        }

        public void DeselectWorld() => SelectedWorld = null;

        // Templates: mapping from template name -> factory that creates a World given the desired name
        public Dictionary<string, Func<string, World>> Templates { get; } = new Dictionary<string, Func<string, World>>(StringComparer.OrdinalIgnoreCase);

        public World CreateWorld(string worldName, string? templateName = null)
        {
            if (string.IsNullOrWhiteSpace(worldName))
            {
                // generate a default name if none provided
                worldName = $"World_{Guid.NewGuid():N}";
            }

            // return existing world if name already used
            var existing = Worlds.Find(w => w.WorldName == worldName);
            if (existing != null)
            {
                SelectedWorld = existing;
                return existing;
            }

            World world;
            if (!string.IsNullOrEmpty(templateName) && Templates.TryGetValue(templateName, out var factory))
            {
                world = factory(worldName);
            }
            else
            {
                world = new World(worldName);
            }

            

         

            Worlds.Add(world);
            SelectWorld(world);
            return world;
        }

        public World LoadArchiveWorld(string path)
        {
            var resolver = new V12AssetResolver();
            var templates = new WorldTemplateProvider();
            var result = WorldLoader.LoadFromArchive(path, resolver, templates);
            var world = result.World;

            Registry.Register("AssetResolver", resolver);
            Registry.Register("TemplateProvider", templates);
            world.ExtractPath = result.TempDirectory;
            world.MountPoint = result.MountPoint;

            Worlds.Add(world);
            SelectedWorld = world;
            return world;
        }
    }
    
}
