using System;
using System.Collections.Generic;
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
using V12.Core.Core.Interfaces;
using V12.Core.UI;
using V12.Components.UI;
using V12.Components;

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

        /// <summary>
        /// The currently selected or focused world. When set, only this world will be updated by Update().
        /// </summary>
        public World? SelectedWorld { get; private set; }
           public World UserSpace = new World("UserSpace"); // always open and active no matter what, contains the UI for being able to control the game and select worlds, etc. This world is not meant to be used for actual game content, but rather for the user interface and control of the game.
           public World HomeWorld = new World("HomeWorld");

        /// <summary>
        /// Central service registry. Networking, DirtyTracker, and other engine services are registered here.
        /// </summary>
        public RegistryController Registry { get; } = new RegistryController("GameRoot");

        /// <summary>
        /// The NetworkCables message bus used by this game instance.
        /// </summary>
        public NetworkCables Cables { get; } = new NetworkCables();

        private Thread? _networkingThread;
        private CancellationTokenSource? _networkingCts;
        // Core-managed desktop dashboard (frontend-agnostic)
        private IDashboard? _dashboard;
        private IInputHandler? _dashboardHandler;

        public GameRoot() {
            Worlds.Add(UserSpace);
            Worlds.Add(HomeWorld);
            // Default to the first created world as the focused world
            SelectedWorld = HomeWorld;
            Templates["empty"] = (name) => new World(name);
            Templates["default"] = (name) => new V12.WorldML.WorldMLParser().Parse(ReadResource("V12.Templates.Default.xml"));
            Templates["Gridspace"] = (name) => new V12.WorldML.WorldMLParser().Parse(ReadResource("V12.Templates.Gridspace.xml"));
            Templates["HotReload"] = (name) => {
                var world = new World(name);
                var reloader = new V12.Core.Networking.WorldXmlHotReloader(world);
                reloader.Initialize();
                Registry.Register($"HotReloader_{name}", reloader);
                return world;
            };

            Registry.Register("NetworkCables", Cables);
            // Register a core InspectorService so glue code can render engine-agnostic UI
            //var inspector = new V12.Core.UI.InspectorService(this);
            //Registry.Register("InspectorService", inspector); // why the fuck do we need this as a service?

            // Register core InputService so glue code can forward platform input
            var inputService = new V12.Core.Input.InputService();
            Registry.Register("InputService", inputService);
            InspectorBuilder i = new InspectorBuilder();
            Element ins = new Element("RootInspectorWindow");
            ins.AddComponent(new MeshComponent(MeshShape.Box, 0.2f, 0.2f, 0.2f));
            ins.AddComponent(new CanvasComponent());
            ins.AddComponent(new InspectorComponent());
            var rootz = new Element("UIRoot");
            
            rootz.AddComponent(i.Build());
            ins.AddChild(rootz);
            SelectedWorld.AddElement(ins);
            var vrInput = new V12.Core.Input.VRInputProvider();
            Registry.Register("VRInput", vrInput);
            // Register a default in-engine UIBuilder so dashboards can build
            // UI as world elements which frontends will sync and render.
            try
            {
                var uiBuilder = new UIBuilder();
                Registry.Register("UIBuilder", uiBuilder);
            }
            catch { }
            
            Registry.Register("LocomotionSystem", new V12.Core.Systems.LocomotionSystem(this));
            Registry.Register("PhysicsLocomotionSystem", new V12.Core.Systems.PhysicsLocomotionSystem(this));
            Registry.Register("PhysicsService", new V12.Core.Systems.PhysicsService());
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
            foreach (var service in Registry.GetAll<IGameService>())
                service.Initialize(this);

            StartNetworkingThread();
            SetupUI();
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
        public void SetupNetworking(int port = 7777, string? connectHost = null, bool isServer = false)
        {
            if (isServer || connectHost == null)
            {
                var host = new NetworkHost(port, Cables);
                host.OnClientConnected += () =>
                {
                    var world = SelectedWorld;
                    if (world == null)
                    {
                        Console.WriteLine("[GameRoot] Client connected but SelectedWorld is null – no WorldSync sent.");
                        return;
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

            Registry.Register("DirtyTracker", dirtyTracker);
            Console.WriteLine("[GameRoot] DirtyTracker registered.");

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
                                d.Initialize(uiBuilder);
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
                UserSpace.Update(deltaTime);
            }

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
            SelectedWorld = world;
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

            // Ensure a player exists in the world
            bool hasPlayer = false;
            foreach (var el in world.Root)
            {
                if (el.GetComponent<V12.Components.PlayerComponent>() != null)
                {
                    hasPlayer = true;
                    break;
                }
            }

            if (!hasPlayer)
            {
                var player = new Element("DefaultPlayer");
                player.AddComponent(new V12.Components.PlayerComponent());
                player.AddComponent(new V12.Components.TransformComponent { X = 0, Y = -1.0f, Z = 0 });
                player.AddComponent(new V12.Components.PhysicsBodyComponent { IsKinematic = true });
                player.AddComponent(new V12.Components.LocomotionComponent());
                world.AddElement(player);
            }

            Worlds.Add(world);
            SelectedWorld = world;
            return world;
        }

        public World LoadArchiveWorld(string path)
        {
            var world = WorldLoader.LoadFromArchive(path);
            Worlds.Add(world);
            SelectedWorld = world;
            return world;
        }
    }
    
}
