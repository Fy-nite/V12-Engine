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

        /// <summary>
        /// When true (the default), the host forwards a client's
        /// <see cref="MessageType.SyncBatch"/> to every other peer, so any peer
        /// can write a <see cref="SyncValue{T}"/> and have the whole session see
        /// it. Conflicts resolve last-write-wins on the packed version stamp.
        /// <para>
        /// This is the transport half of per-entity authority
        /// (<c>docs/PEER_HOSTING_AND_AUTHORITY.md</c> §5): it does not yet ask
        /// whether the sender is allowed to write that value. Until the
        /// <c>CanWrite</c> gate lands, treat this as trusted-session behaviour.
        /// </para>
        /// </summary>
        public bool RelayRemoteValueWrites { get; set; } = true;

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
                $"networkcables://world/{session.Descriptor.WorldId:N}/sync",
                isHost
                    ? message => _host?.GetServerAssignedPlayerId(message) ?? 0L
                    : null);

            if (isHost)
            {
                // Client-authored values are only accepted for elements that
                // client simulates. Without this, any peer could write any value.
                session.Sync.AcceptIncomingKey = AcceptValueKeyFrom;
            }

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

            // The host's writer identity is its server-assigned player id, so
            // host writes have a stable stamp that clients can order against.
            // Never let it collapse to 0, which is the "unassigned" sentinel.
            ushort hostWriter = (ushort)(_host.HostPlayerId & 0xFFFF);
            _session.Sync.WriterId = hostWriter == 0 ? (ushort)1 : hostWriter;

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

            // The snapshot carries component state, so a late joiner already has
            // most values. Send one full keyframe anyway: values that are not in
            // the snapshot (explicit SyncValue<T>, or [Sync][BsonIgnore] fields)
            // would otherwise never reach a client that joined after they changed.
            // The client registers keys while applying the snapshot, so this batch
            // arrives after the registry knows them.
            try { _session.Sync.MarkAllDirty(); }
            catch (Exception ex) { GameRoot.Log.Warning(ex, "Keyframe flush failed for '{World}'", _session.World.WorldName); }

            // Tell the new client who simulates what. Without this it would treat
            // every delegated element as host-owned until the next change.
            foreach (var assignment in _session.Authorities.Snapshot())
                _host?.SendToClient(playerId, CreateAuthorityMessage(assignment));
        }

        private void OnClientDisconnected(long playerId)
        {
            _session.PlayersConnected = _host?.ClientCount ?? 0;
            GameRoot.Log.Information(
                "World '{World}' lost client {PlayerId} ({Count} connected)",
                _session.World.WorldName, playerId, _session.PlayersConnected);

            // Reclaim everything this player was simulating before the game sees
            // the leave event, so a disconnect cannot leave an element frozen on
            // the remaining peers.
            ReleaseAuthorityForClient(playerId);

            ClientLeft?.Invoke(playerId);
        }

        private void ReleaseAuthorityForClient(long playerId)
        {
            var released = _session.Authorities.ReleaseAllFor(playerId);
            if (released.Count == 0) return;

            foreach (var assignment in released)
                BroadcastAuthority(assignment);

            GameRoot.Log.Information(
                "World '{World}' handed {Count} element(s) from player {PlayerId} back to the host",
                _session.World.WorldName, released.Count, playerId);
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

        /// <summary>
        /// Host only: make <paramref name="playerId"/> the simulating user for
        /// <paramref name="element"/>, or pass 0 to hand it back to the host.
        /// The assignment is applied locally and broadcast, so the new owner
        /// starts simulating and every other peer stops.
        /// </summary>
        /// <returns>false when this peer is not the host, or the element has no
        /// stable <c>Uid</c> and therefore cannot be delegated.</returns>
        public bool AssignSimulatingUser(IWorldElement element, long playerId)
        {
            if (!_isHost || _disposed || element is null) return false;
            if (playerId != 0 && element.Uid == Guid.Empty) return false;

            long epoch = _session.Authorities.Assign(_session.World, element, playerId);
            BroadcastAuthority(new EntityAssignment(
                _session.World.Id, element.Uid, playerId, epoch));
            return true;
        }

        /// <summary>
        /// Client: ask the host to make this peer the element's simulating user.
        /// The host grants only when the element is unowned or already yours, so
        /// the first grab wins; the grant arrives as an
        /// <see cref="MessageType.EntityAuthority"/> message and is what actually
        /// starts local simulation.
        /// </summary>
        public bool RequestSimulatingUser(IWorldElement element)
            => SendAuthorityRequest(element, release: false);

        /// <summary>Client: tell the host it is done with the element.</summary>
        public bool ReleaseSimulatingUser(IWorldElement element)
            => SendAuthorityRequest(element, release: true);

        private bool SendAuthorityRequest(IWorldElement element, bool release)
        {
            if (_isHost || _disposed || element is null || element.Uid == Guid.Empty)
                return false;

            return SendToServer(new MessageDTO
            {
                Sender = new Uri("networkcables://client"),
                MessageType = MessageType.EntityAuthorityRequest,
                Message = AncientCompressor.Compress(new EntityAuthorityRequestDTO
                {
                    EntityUid = element.Uid,
                    Release = release,
                }),
            });
        }

        private void BroadcastAuthority(EntityAssignment assignment)
        {
            _session.Cables.SendData(CreateAuthorityMessage(assignment));
        }

        private static MessageDTO CreateAuthorityMessage(EntityAssignment assignment)
        {
            return new MessageDTO
            {
                Sender = new Uri("networkcables://worldhost"),
                MessageType = MessageType.EntityAuthority,
                Message = AncientCompressor.Compress(new EntityAuthorityDTO
                {
                    WorldId = assignment.WorldId,
                    EntityUid = assignment.EntityUid,
                    SimulatingPlayerId = assignment.SimulatingPlayerId,
                    Epoch = assignment.Epoch,
                }),
            };
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
                    return;
                }

                // Client-authored value writes. The bridge applies only the keys
                // this peer is allowed to write; forward exactly those to the
                // rest of the session. NetworkHost.BroadcastMessage skips the
                // origin peer via SenderToken, so the writer is not echoed.
                if (RelayRemoteValueWrites && message.MessageType == MessageType.SyncBatch)
                {
                    RelayValueBatchFromClient(message);
                    return;
                }

                // Client-authored element poses: the simulating user's transform.
                if (message.MessageType == MessageType.WorldElementUpdate)
                {
                    RelayElementUpdatesFromClient(message);
                    return;
                }

                // A client asking for (or giving back) a simulating user.
                if (message.MessageType == MessageType.EntityAuthorityRequest)
                {
                    long playerId = _host?.GetServerAssignedPlayerId(message) ?? 0;
                    if (playerId != 0)
                        HandleAuthorityRequest(playerId, message);
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
                    case MessageType.EntityAuthority:
                        ApplyEntityAuthority(message);
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

        /// <summary>
        /// Host: grant or drop a client's claim on an element. First claim wins;
        /// the host can still take an element back with
        /// <see cref="AssignSimulatingUser"/>. A denied claim is simply not
        /// answered, so the client keeps treating the element as a replica.
        /// </summary>
        private void HandleAuthorityRequest(long playerId, MessageDTO message)
        {
            var dto = AncientCompressor.Decompress<EntityAuthorityRequestDTO>(message.Message);
            if (dto is null || dto.EntityUid == Guid.Empty) return;

            var element = V12.Core.References.ReferenceResolver.FindElementByUid(
                _session.World, dto.EntityUid);
            if (element is null) return;

            long current = _session.Authorities.GetSimulatingPlayer(_session.World, element);

            if (dto.Release)
            {
                if (current == playerId)
                    AssignSimulatingUser(element, 0);
                return;
            }

            if (current != 0 && current != playerId) return; // already someone else's
            AssignSimulatingUser(element, playerId);
        }

        /// <summary>
        /// Host: a client-authored value write is accepted only for an element
        /// that client currently simulates. Host-originated batches carry player
        /// id 0 and always pass.
        /// </summary>
        private bool AcceptValueKeyFrom(string key, long writerPlayerId)
        {
            if (writerPlayerId == 0L) return true;
            if (!SyncKey.TryParseElement(key, out var worldId, out var elementUid)) return false;
            if (worldId != _session.World.Id) return false;

            var element = V12.Core.References.ReferenceResolver.FindElementByUid(
                _session.World, elementUid);
            return element is not null
                && _session.Authorities.CanWrite(_session.World, element, writerPlayerId, isHost: false);
        }

        /// <summary>
        /// Host: forward the subset of a client's value batch that survived the
        /// authority check. The bridge already applied the same subset locally, so
        /// the host and the other peers converge on one set.
        /// </summary>
        private void RelayValueBatchFromClient(MessageDTO message)
        {
            long playerId = _host?.GetServerAssignedPlayerId(message) ?? 0L;

            var allowed = new List<(string Key, ReadOnlyMemory<byte> Payload)>();
            foreach (var entry in SyncBatchBuilder.EnumerateBatch(message.Message))
            {
                if (AcceptValueKeyFrom(entry.key, playerId))
                    allowed.Add(entry);
            }

            if (allowed.Count == 0) return;

            _session.Cables.SendData(new MessageDTO
            {
                Sender = message.Sender,
                MessageType = MessageType.SyncBatch,
                Message = SyncBatchBuilder.BuildBatch(allowed),
                SenderToken = message.SenderToken,
            });
        }

        /// <summary>Host: accept a client's element pose updates for the elements that
        /// client simulates, apply them, and forward them to the other peers.
        /// Only the pose is trusted; name, description, parent and uid stay
        /// host-authoritative, so a client cannot rename or reparent through this
        /// path.
        /// </summary>
        private void RelayElementUpdatesFromClient(MessageDTO message)
        {
            long playerId = _host?.GetServerAssignedPlayerId(message) ?? 0;
            if (playerId == 0) return;

            var incoming = AncientCompressor.Decompress<ElementUpdateBatchDTO>(message.Message);
            if (incoming is null) return;

            var allowed = new ElementUpdateBatchDTO { Timestamp = incoming.Timestamp };
            foreach (var update in incoming.Elements)
            {
                var element = _session.World.FindElementById(update.Id);
                if (element is null) continue;
                if (!_session.Authorities.CanWrite(_session.World, element, playerId, isHost: false))
                    continue;

                allowed.Elements.Add(new ElementUpdateDTO
                {
                    Id = update.Id,
                    WorldId = update.WorldId,
                    Uid = update.Uid,
                    Position = update.Position,
                    Rotation = update.Rotation,
                    Scale = update.Scale,
                    Timestamp = update.Timestamp,
                });
            }

            if (allowed.Elements.Count == 0) return;

            var outgoing = new MessageDTO
            {
                Sender = message.Sender,
                MessageType = MessageType.WorldElementUpdate,
                Message = AncientCompressor.Compress(allowed),
                // Keep the origin so BroadcastMessage does not echo it back.
                SenderToken = message.SenderToken,
            };

            ApplyElementUpdates(outgoing);
            _session.Cables.SendData(outgoing);
        }

        private void ApplyElementUpdates(MessageDTO message)
        {
            var batch = AncientCompressor.Decompress<ElementUpdateBatchDTO>(message.Message);
            if (batch == null) return;
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

        private void ApplyEntityAuthority(MessageDTO message)
        {
            var dto = AncientCompressor.Decompress<EntityAuthorityDTO>(message.Message);
            if (dto == null) return;

            _session.Authorities.Apply(new EntityAssignment(
                dto.WorldId, dto.EntityUid, dto.SimulatingPlayerId, dto.Epoch));
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
