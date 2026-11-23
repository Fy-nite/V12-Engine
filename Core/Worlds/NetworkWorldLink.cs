using System;
using System.Collections.Generic;
using System.Linq;
using V12.Core.Core.Interfaces;
using V12.Core.NetworkCable;
using V12.Core.Networking;

namespace V12.Core
{
    /// <summary>
    /// One network connection for one <see cref="WorldSession"/>. The host owns a
    /// <see cref="NetworkHost"/> and broadcasts the world snapshot when a client
    /// connects; a client owns a <see cref="NetworkClient"/> and applies the
    /// snapshot and subsequent state. Value-level sync rides the same session
    /// cables through <see cref="SyncNetworkBridge"/>.
    /// </summary>
    public sealed class NetworkWorldLink : IWorldLink, IDisposable
    {
        private readonly WorldSession _session;
        private readonly SyncNetworkBridge _syncBridge;
        private readonly bool _isHost;
        private NetworkHost? _host;
        private NetworkClient? _client;
        private bool _disposed;

        public WorldSession Session => _session;
        public bool IsHost => _isHost;
        public long HostPlayerId => _host?.HostPlayerId ?? 0;

        /// <summary>Raised on the network poll thread; queue work onto the game thread.</summary>
        public event Action<long>? ClientJoined;

        /// <summary>Raised on the network poll thread; queue work onto the game thread.</summary>
        public event Action<long>? ClientLeft;

        /// <summary>Host-side command received from a connected player, raised on the game thread.</summary>
        public event Action<long, MessageDTO>? CommandReceived;

        /// <summary>Client-side message outside the built-in world protocol, raised on the game thread.</summary>
        public event Action<MessageDTO>? ServerMessage;

        public bool IsConnected => _isHost
            ? _host?.ClientCount > 0
            : _client?.IsConnected == true;

        private NetworkWorldLink(WorldSession session, bool isHost)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _isHost = isHost;

            // MessageDTO and V12 component payloads need the V12 BSON conventions
            // before the first snapshot or batch is serialized.
            BsonConfig.Initialize();

            _syncBridge = new SyncNetworkBridge(
                session.Sync,
                session.Cables,
                $"networkcables://world/{session.Descriptor.WorldId:N}/sync");

            session.Cables.OnMessageReceived += OnMessageReceived;
            session.Link = this;
        }

        /// <summary>Start a world host that listens on <paramref name="port"/>.</summary>
        public static NetworkWorldLink Host(WorldSession session, int port)
        {
            var link = new NetworkWorldLink(session, isHost: true);
            link.StartHost(port);
            return link;
        }

        /// <summary>Connect a client session to a world host.</summary>
        public static NetworkWorldLink Connect(WorldSession session, string host, int port)
        {
            var link = new NetworkWorldLink(session, isHost: false);
            link.StartClient(host, port);
            return link;
        }

        private void StartHost(int port)
        {
            _session.Authority = WorldAuthority.Hosted;

            _host = new NetworkHost(port, _session.Cables);
            _host.OnClientConnected += OnClientConnected;
            _host.OnClientDisconnected += OnClientDisconnected;
            _host.OnStartFailed += ex =>
                GameRoot.Log.Error(ex, "World host '{World}' failed to start", _session.World.WorldName);
            _ = _host.StartAsync();
        }

        private void StartClient(string host, int port)
        {
            _session.Authority = WorldAuthority.Remote;

            _client = new NetworkClient(host, port, _session.Cables);
            _client.OnConnected += () =>
                GameRoot.Log.Information("World link connected to {Host}:{Port}", host, port);
            _client.OnDisconnected += () =>
                GameRoot.Log.Warning("World link disconnected from {Host}:{Port}", host, port);
            _client.OnConnectionFailed += ex =>
                GameRoot.Log.Warning(ex, "World link failed to connect to {Host}:{Port}", host, port);
            _ = _client.ConnectAsync();
        }

        private void OnClientConnected(long playerId)
        {
            _session.PlayersConnected = _host?.ClientCount ?? 0;
            GameRoot.Log.Information(
                "World '{World}' accepted client {PlayerId} ({Count} connected)",
                _session.World.WorldName, playerId, _session.PlayersConnected);

            SendSnapshot();
            ClientJoined?.Invoke(playerId);
        }

        private void OnClientDisconnected(long playerId)
        {
            _session.PlayersConnected = _host?.ClientCount ?? 0;
            GameRoot.Log.Information(
                "World '{World}' lost client {PlayerId} ({Count} connected)",
                _session.World.WorldName, playerId, _session.PlayersConnected);
            ClientLeft?.Invoke(playerId);
        }

        private void SendSnapshot()
        {
            if (_disposed) return;

            GameRoot.Log.Information(
                "World '{World}' sending snapshot: {Roots} root element(s)",
                _session.World.WorldName, _session.World.Root.Count);

            _session.Cables.SendData(new MessageDTO
            {
                Sender = new Uri("networkcables://worldhost"),
                MessageType = MessageType.WorldSync,
                Message = AncientCompressor.Compress(_session.World),
            });
        }

        /// <summary>Client → host message.</summary>
        public bool SendToServer(MessageDTO message)
        {
            if (_isHost || _disposed || message == null) return false;
            _session.Cables.SendData(message);
            return true;
        }

        /// <summary>Host → one connected player message.</summary>
        public bool SendToClient(long playerId, MessageDTO message)
        {
            if (!_isHost || _disposed || message == null) return false;
            return _host?.SendToClient(playerId, message) == true;
        }

        private void OnMessageReceived(MessageDTO message)
        {
            if (_disposed || message?.Message == null) return;

            if (_isHost)
            {
                // The host only accepts commands from clients, never snapshots
                // or state that could overwrite its authoritative world.
                if (message.MessageType == MessageType.Command)
                {
                    long playerId = _host?.GetServerAssignedPlayerId(message) ?? 0;
                    if (playerId != 0)
                        CommandReceived?.Invoke(playerId, message);
                }
                return;
            }

            try
            {
                switch (message.MessageType)
                {
                    case MessageType.WorldSync:
                        ApplyWorldSync(message);
                        break;
                    case MessageType.WorldElementUpdate:
                        ApplyElementUpdates(message);
                        break;
                    case MessageType.WorldDelta:
                        ApplyWorldDelta(message);
                        break;
                    case MessageType.ComponentRemoved:
                        ApplyComponentRemovals(message);
                        break;
                    case MessageType.WorldUpdate:
                        ApplyComponentBatch(message);
                        break;
                    case MessageType.PlayerWelcome:
                        ServerMessage?.Invoke(message);
                        break;
                    default:
                        ServerMessage?.Invoke(message);
                        break;
                }
            }
            catch (Exception ex)
            {
                GameRoot.Log.Error(ex, "World link failed to apply {Type}", message.MessageType);
            }
        }

        private void ApplyWorldSync(MessageDTO message)
        {
            var incoming = AncientCompressor.Decompress<World>(message.Message);
            if (incoming == null) return;

            GameRoot.Instance?.WorldManager.RebindWorldId(_session, incoming.Id);

            // Local-only subtrees (the local player rig, editor helpers) are
            // marked NonPersistent and are deliberately absent from the host
            // snapshot. Keep them across a snapshot replacement, under the same
            // parent path they had before.
            var localOnly = new List<PreservedElement>();
            foreach (var root in _session.World.Root)
                CollectLocalOnly(root, new List<string>(), localOnly);

            GameRoot.Log.Information(
                "World '{World}' applying snapshot: {Incoming} incoming root(s), {Local} local-only subtree(s)",
                _session.World.WorldName, incoming.Root.Count, localOnly.Count);

            // Unsubscribe from the old tree before replacing it, otherwise the
            // dirty tracker keeps handlers on elements that are no longer in the world.
            _session.Dirty.UntrackWorld(_session.World);

            foreach (var preserved in localOnly)
            {
                if (preserved.Element.Parent != null)
                    preserved.Element.Parent.RemoveChild(preserved.Element);
            }

            _session.World.ReplaceFrom(incoming);

            foreach (var preserved in localOnly)
            {
                var parent = ResolveParent(_session.World, preserved.ParentPath);
                if (parent != null)
                    parent.AddChild(preserved.Element);
                else
                    _session.World.AddElement(preserved.Element);
            }

            _session.World.SpawnPosition = incoming.SpawnPosition;
            _session.Sync.Refresh();
            _session.Dirty.TrackWorld(_session.World);
            _session.Revision++;

            GameRoot.Log.Information(
                "World '{World}' received snapshot: {Roots} root element(s), revision {Revision}",
                _session.World.WorldName, _session.World.Root.Count, _session.Revision);
        }

        private void ApplyElementUpdates(MessageDTO message)
        {
            var batch = AncientCompressor.Decompress<ElementUpdateBatchDTO>(message.Message);
            if (batch == null) return;

            _session.Dirty.WithCaptureSuppressed(() =>
            {
                foreach (var update in batch.Elements)
                {
                    var element = _session.World.FindElementById(update.Id);
                    if (element == null) continue;

                    if (update.Name != null) element.Name = update.Name;
                    if (update.Description != null) element.Description = update.Description;
                    if (update.Uid != Guid.Empty) element.Uid = update.Uid;

                    var transform = element.LocalTransform;
                    transform.Position = update.Position;
                    transform.Rotation = update.Rotation;
                    transform.Scale = update.Scale;
                    element.LocalTransform = transform;
                }
            });
        }

        private void ApplyWorldDelta(MessageDTO message)
        {
            var delta = AncientCompressor.Decompress<WorldDeltaDTO>(message.Message);
            if (delta == null) return;

            _session.Dirty.WithCaptureSuppressed(() =>
            {
                foreach (var create in delta.Creates)
                {
                    if (create?.Element == null) continue;
                    if (_session.World.FindElementById(create.Element.Id) != null) continue;

                    var element = AncientCompressor.DecompressElement(create.Element);
                    if (element == null) continue;

                    var parent = create.ParentId != 0
                        ? _session.World.FindElementById(create.ParentId)
                        : null;

                    if (parent != null)
                        parent.AddChild(element);
                    else
                        _session.World.AddElement(element);
                }

                foreach (var deleteId in delta.Deletes)
                {
                    var element = _session.World.FindElementById(deleteId);
                    if (element == null) continue;

                    if (element.Parent != null)
                        element.Parent.RemoveChild(element);
                    else
                        _session.World.RemoveElement(element);
                }
            });
        }

        private void ApplyComponentRemovals(MessageDTO message)
        {
            var dto = AncientCompressor.Decompress<ComponentRemovalDTO>(message.Message);
            if (dto == null) return;

            var element = _session.World.FindElementById(dto.ElementId);
            if (element == null) return;

            _session.Dirty.WithCaptureSuppressed(() =>
            {
                foreach (var componentId in dto.ComponentIds)
                {
                    var component = element.Components.FirstOrDefault(c => c.Id == componentId);
                    if (component != null)
                        element.RemoveComponent(component);
                }
            });
        }

        private void ApplyComponentBatch(MessageDTO message)
        {
            var batch = AncientCompressor.Decompress<ComponentBatchDTO>(message.Message);
            if (batch == null) return;

            _session.Dirty.WithCaptureSuppressed(() =>
            {
                foreach (var snapshot in batch.Components)
                {
                    if (snapshot.Payload == null || snapshot.Payload.Length == 0) continue;

                    var componentDto = AncientCompressor.Decompress<ComponentSyncDTO>(snapshot.Payload);
                    var incoming = AncientCompressor.DecompressComponent(componentDto);
                    if (incoming == null) continue;

                    var element = _session.World.FindElementById(snapshot.ElementId);
                    if (element == null) continue;

                    var existing = componentDto != null
                        ? element.Components.FirstOrDefault(c => c.Id == incoming.Id)
                        : null;
                    if (existing != null)
                        element.RemoveComponent(existing);

                    element.AddComponent(incoming);
                }
            });
        }

        private sealed class PreservedElement
        {
            public IWorldElement Element { get; }
            public string[] ParentPath { get; }

            public PreservedElement(IWorldElement element, string[] parentPath)
            {
                Element = element;
                ParentPath = parentPath;
            }
        }

        private static void CollectLocalOnly(
            IWorldElement element,
            List<string> path,
            List<PreservedElement> output)
        {
            if (V12.Components.NonPersistentComponent.IsTransient(element))
            {
                output.Add(new PreservedElement(element, path.ToArray()));
                return;
            }

            path.Add(element.Name ?? "");
            foreach (var child in element.Children)
                CollectLocalOnly(child, path, output);
            path.RemoveAt(path.Count - 1);
        }

        private static IWorldElement? ResolveParent(World world, string[] parentPath)
        {
            IWorldElement? current = null;
            foreach (var segment in parentPath)
            {
                var siblings = current?.Children ?? world.Root;
                current = siblings.FirstOrDefault(e =>
                    string.Equals(e.Name, segment, StringComparison.OrdinalIgnoreCase));
                if (current == null) return null;
            }
            return current;
        }

        public void Subscribe(WorldSession session) { }
        public void Unsubscribe(WorldSession session) { }

        public void RequestSnapshot(WorldSession session, long sinceRevision)
        {
            // The host sends a snapshot on connect. A future protocol version can
            // request a delta from the current Revision instead.
        }

        public void Close(WorldSession session, WorldCloseMode mode)
        {
            // Disposing the link also tears down the sync bridge and the session's
            // receive subscription. WorldManager.Close removes the session next.
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _session.Cables.OnMessageReceived -= OnMessageReceived;
            _syncBridge.Dispose();

            if (_host != null)
            {
                _host.OnClientConnected -= OnClientConnected;
                _host.OnClientDisconnected -= OnClientDisconnected;
                _host.Dispose();
                _host = null;
            }

            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }
        }
    }
}
