using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using V12.Core.Core.Interfaces;
using V12.Core.NetworkCable;
using V12.Core.Networking;
using V12.Core.Registry;

namespace V12.Core
{
    /// <summary>
    /// The GameRoot class serves as the central entry point for the game, managing the overall game state and coordinating interactions between various components and systems. It is responsible for initializing the game world, handling game logic, and facilitating communication between different parts of the game architecture. The GameRoot class may also manage resources, handle user input, and oversee the main game loop to ensure smooth gameplay.
    /// </summary>
    public class GameRoot
    {
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

        public GameRoot() {
            Worlds.Add(UserSpace);
            Worlds.Add(HomeWorld);
            // Default to the first created world as the focused world
            SelectedWorld = HomeWorld;
            Templates["empty"] = (name) => new World(name);
            Templates["default"] = (name) => new V12.WorldML.WorldMLParser().Parse(ReadResource("V12.Templates.Default.xml"));
            Templates["Gridspace"] = (name) => new V12.WorldML.WorldMLParser().Parse("<World name=\"Gridspace\"><Element></Element></World>");

            Registry.Register("NetworkCables", Cables);
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
                service.Initialize();

            StartNetworkingThread();
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
                    if (world == null) return;
                    Console.WriteLine($"[GameRoot] New client connected, sending WorldSync: {world.WorldName}");
                    Cables.SendData(new MessageDTO
                    {
                        Sender = new Uri("networkcables://server"),
                        MessageType = MessageType.WorldSync,
                        Message = AncientCompressor.Compress(world)
                    });
                };
                Registry.Register("NetworkHost", host);
                Console.WriteLine($"[GameRoot] NetworkHost registered on port {port}.");
            }
            else
            {
                var client = new NetworkClient(connectHost, port, Cables);
                Registry.Register("NetworkClient", client);
                Console.WriteLine($"[GameRoot] NetworkClient registered, targeting {connectHost}:{port}.");
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
            if (SelectedWorld != null)
            {
                try
                {
                    SelectedWorld.Update(deltaTime);
                }
                catch { }
            }
            else
            {
                UserSpace.Update(deltaTime);
            }

            Registry.Update(deltaTime);
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
            Worlds.Add(world);
            SelectedWorld = world;
            return world;
        }
    }
    
}
