using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using V12.Core.Core.Interfaces;
using V12.Core.NetworkCable;
using V12.Core.Networking;
using V12.Components;

namespace V12.Core.Networking
{
    /// <summary>
    /// Tracks dirty components and world elements, batches them, and sends network updates.
    /// </summary>
    public class DirtyTracker : IGameService
    {
        private readonly NetworkCables _cables;
        private readonly ConcurrentDictionary<long, IComponent> _dirtyComponents = new();
        private readonly ConcurrentDictionary<IWorldElement, byte> _dirtyElements = new();
        private readonly Uri _senderUri;

        /// <summary>
        /// Maps a component id to its owning element so outgoing component batches can
        /// carry the element id (the client uses it to add previously-unknown components).
        /// </summary>
        private readonly ConcurrentDictionary<long, IWorldElement> _componentOwners = new();

        /// <summary>
        /// When true, this instance is the host/authority and captures element
        /// creates/deletes into <see cref="MessageType.WorldDelta"/> broadcasts so
        /// remote peers spawn/despawn elements. Clients keep this false: they never
        /// broadcast lifecycle, they only apply the host's deltas.
        /// </summary>
        public bool IsAuthority { get; set; }

        // ── Element lifecycle delta state ─────────────────────────────────────
        private readonly object _deltaLock = new();
        private bool _suppressCapture;
        private readonly List<PendingCreate> _pendingCreates = new();
        private readonly List<long> _pendingDeletes = new();
        private readonly List<PendingRemoval> _pendingRemovals = new();

        /// <summary>Elements whose dirty/child events we are subscribed to (avoids double subscription).</summary>
        private readonly HashSet<IWorldElement> _trackedElements = new();

        /// <summary>Per-world event handlers so TrackWorld/UntrackWorld can unsubscribe the exact lambdas.</summary>
        private readonly Dictionary<World, Action<IWorldElement>> _worldAddedHandlers = new();
        private readonly Dictionary<World, Action<IWorldElement>> _worldRemovedHandlers = new();

        private readonly struct PendingCreate
        {
            public readonly IWorldElement Element;
            public readonly long ParentId;
            public readonly string WorldName;

            public PendingCreate(IWorldElement element, long parentId, string worldName)
            {
                Element = element;
                ParentId = parentId;
                WorldName = worldName ?? "";
            }
        }

        private readonly struct PendingRemoval
        {
            public readonly long ElementId;
            public readonly long ComponentId;

            public PendingRemoval(long elementId, long componentId)
            {
                ElementId = elementId;
                ComponentId = componentId;
            }
        }

        /// <summary>
        /// Maximum number of dirty items to batch per network message.
        /// </summary>
        public int MaxBatchSize { get; set; } = 50;

        /// <summary>
        /// Minimum time (in seconds) between sending batched updates. Set to 0 to send immediately.
        /// </summary>
        public float ThrottleInterval { get; set; } = 0f; // instantly send by default

        /// <summary>
        /// Optional predicate that returns true when there is at least one connected peer
        /// (a client on the server, or a live server connection on the client). When this
        /// returns false, <see cref="SendDirtyUpdates"/> skips queueing anything onto the
        /// cables, so an idle/disconnected instance produces no outgoing traffic.
        /// </summary>
        public Func<bool>? HasConnectedPeer { get; set; }

        private float _timeSinceLastSend = 0f;

        public DirtyTracker(NetworkCables? cables = null, string? senderUri = null)
        {
            _cables = cables ?? NetworkCables.Default;
            _senderUri = new Uri(senderUri ?? "networkcables://dirtytracker");
        }
        public void Update(GameRoot g)
        {
            
        }
        public void Initialize() { }

        /// <summary>
        /// Subscribe to a component's dirty events and record which element owns it
        /// (so batches can carry the element id for client-side component adds).
        /// </summary>
        public void TrackComponent(IComponent component, IWorldElement? owner = null)
        {
            component.OnDirty += OnComponentDirty;
            if (owner != null)
                _componentOwners[component.Id] = owner;
        }

        /// <summary>
        /// Unsubscribe from a component's dirty events.
        /// </summary>
        public void UntrackComponent(IComponent component)
        {
            component.OnDirty -= OnComponentDirty;
            _componentOwners.TryRemove(component.Id, out _);
        }

        /// <summary>
        /// Subscribe to a world element's dirty events, recursively tracking all children.
        /// Also subscribes the element's child add/remove events so nested spawns/despawns
        /// are captured. Idempotent: a tracked element is never subscribed twice.
        /// </summary>
        public void TrackElement(IWorldElement element)
        {
            if (element == null) return;
            if (!_trackedElements.Add(element)) return;

            element.OnDirty += OnElementDirty;

            if (element is Element el)
            {
                el.ChildAdded += OnElementChildAdded;
                el.ChildRemoved += OnElementChildRemoved;
            }

            // Also track all components
            foreach (var component in element.Components)
            {
                TrackComponent(component, element);
            }

            // Recursively track all child elements
            foreach (var child in element.Children)
            {
                TrackElement(child);
            }
        }

        /// <summary>
        /// Unsubscribe from a world element's dirty events, recursively untracking all children.
        /// </summary>
        public void UntrackElement(IWorldElement element)
        {
            if (element == null) return;
            _trackedElements.Remove(element);
            element.OnDirty -= OnElementDirty;

            if (element is Element el)
            {
                el.ChildAdded -= OnElementChildAdded;
                el.ChildRemoved -= OnElementChildRemoved;
            }

            // Also untrack all components
            foreach (var component in element.Components)
            {
                UntrackComponent(component);
            }

            // Recursively untrack all child elements
            foreach (var child in element.Children)
            {
                UntrackElement(child);
            }
        }

        /// <summary>
        /// Subscribe to all elements in a world and their components,
        /// and auto-track any elements added/removed in the future.
        /// </summary>
        public void TrackWorld(World world)
        {
            if (world?.Root == null) return;
            if (_worldAddedHandlers.ContainsKey(world)) return; // already tracking

            Action<IWorldElement> onAdded = el => OnWorldElementAdded(world, el);
            Action<IWorldElement> onRemoved = el => OnWorldElementRemoved(world, el);
            world.ElementAdded += onAdded;
            world.ElementRemoved += onRemoved;
            _worldAddedHandlers[world] = onAdded;
            _worldRemovedHandlers[world] = onRemoved;

            foreach (var element in world.Root)
            {
                TrackElement(element);
            }
        }

        /// <summary>
        /// Stop tracking a world and all its elements/components.
        /// </summary>
        public void UntrackWorld(World world)
        {
            if (world?.Root == null) return;

            if (_worldAddedHandlers.TryGetValue(world, out var onAdded))
            {
                world.ElementAdded -= onAdded;
                _worldAddedHandlers.Remove(world);
            }
            if (_worldRemovedHandlers.TryGetValue(world, out var onRemoved))
            {
                world.ElementRemoved -= onRemoved;
                _worldRemovedHandlers.Remove(world);
            }

            foreach (var element in world.Root)
            {
                UntrackElement(element);
            }
        }

        private void OnWorldElementAdded(World world, IWorldElement element)
        {
            if (element == null) return;
            TrackElement(element);
            if (!IsAuthority || _suppressCapture) return;
            if (IsPlayerElement(element)) return;
            QueueCreate(element, 0, world.WorldName);
        }

        private void OnWorldElementRemoved(World world, IWorldElement element)
        {
            if (element == null) return;
            UntrackElement(element);
            if (!IsAuthority || _suppressCapture) return;
            if (IsPlayerElement(element)) return;
            QueueDelete(element);
        }

        private void OnElementChildAdded(IWorldElement child)
        {
            if (child == null) return;
            TrackElement(child);
            if (!IsAuthority || _suppressCapture) return;
            if (IsPlayerElement(child)) return;

            // Only capture children whose parent is already attached to a world.
            // A child added before its parent was attached to a world will be
            // covered by the parent's subtree serialization when the parent's
            // create is sent — queueing it separately would emit a duplicate.
            if (child.Parent == null || !IsAttachedToWorld(child.Parent)) return;

            QueueCreate(child, child.Parent.Id, GetWorldNameFor(child));
        }

        private void OnElementChildRemoved(IWorldElement child)
        {
            if (child == null) return;
            UntrackElement(child);
            if (!IsAuthority || _suppressCapture) return;
            if (IsPlayerElement(child)) return;
            QueueDelete(child);
        }

        private static string GetWorldNameFor(IWorldElement element)
        {
            var g = GameRoot.Instance;
            if (g == null || element == null) return "";
            var root = element.GetRoot();
            if (root == null) return "";
            if (g.PersistentWorld._elementsById.ContainsKey(root.Id))
                return g.PersistentWorld.WorldName;
            foreach (var w in g.Worlds)
                if (w._elementsById.ContainsKey(root.Id))
                    return w.WorldName;
            return g.SelectedWorld?.WorldName ?? "";
        }

        /// <summary>
        /// True when the element's root is registered in a world's <c>_elementsById</c>
        /// (i.e. it was added via <see cref="World.AddElement"/> or attached under such a
        /// root). Roots built by direct <c>Root.Add</c> are not indexed, so they read as
        /// unattached — that only matters for the host's capture path, and host worlds
        /// are built through AddElement.
        /// </summary>
        private static bool IsAttachedToWorld(IWorldElement element)
        {
            var g = GameRoot.Instance;
            if (g == null || element == null) return false;
            var root = element.GetRoot();
            if (root == null) return false;
            if (g.PersistentWorld._elementsById.ContainsKey(root.Id)) return true;
            foreach (var w in g.Worlds)
                if (w._elementsById.ContainsKey(root.Id))
                    return true;
            return false;
        }

        /// <summary>
        /// Player representations are replicated through PlayerSync/RemotePlayerManager,
        /// not the delta layer, so they must never be captured as element creates/deletes.
        /// </summary>
        private static bool IsPlayerElement(IWorldElement el)
        {
            if (el == null) return false;
            if (string.Equals(el.Name, "Player", StringComparison.OrdinalIgnoreCase)) return true;
            if (el.Name != null && el.Name.StartsWith("RemotePlayer_", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// True when <paramref name="el"/> sits anywhere under the local player rig
        /// (root named "Player") or a remote-player replica. Those subtrees are
        /// replicated via PlayerSync/RemotePlayerManager and their element batches
        /// are dropped by peers, so they are skipped in <see cref="SendElementBatch"/>
        /// to avoid dead per-frame traffic.
        /// </summary>
        private static bool IsPlayerRigElement(IWorldElement el)
        {
            for (var cur = el; cur != null; cur = cur.Parent)
            {
                if (cur.Name == null) continue;
                if (string.Equals(cur.Name, "Player", StringComparison.OrdinalIgnoreCase)) return true;
                if (cur.Name.StartsWith("RemotePlayer_", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void QueueCreate(IWorldElement element, long parentId, string worldName)
        {
            lock (_deltaLock)
            {
                // Already pending as a create → keep the first entry.
                foreach (var pc in _pendingCreates)
                    if (ReferenceEquals(pc.Element, element)) return;

                // An ancestor is already pending a create → this element is covered by
                // that ancestor's subtree serialization, so skip it (avoids duplicates).
                for (var anc = element.Parent; anc != null; anc = anc.Parent)
                    foreach (var pc in _pendingCreates)
                        if (ReferenceEquals(pc.Element, anc)) return;

                // A prior pending delete of the same id is cancelled by this create.
                _pendingDeletes.Remove(element.Id);

                _pendingCreates.Add(new PendingCreate(element, parentId, worldName));
            }
        }

        private void QueueDelete(IWorldElement element)
        {
            lock (_deltaLock)
            {
                // If the element's create is still pending, it has never been sent remotely —
                // cancel the pending create instead of sending a bogus delete for it.
                if (_pendingCreates.RemoveAll(p => ReferenceEquals(p.Element, element)) > 0)
                    return;

                if (!_pendingDeletes.Contains(element.Id))
                    _pendingDeletes.Add(element.Id);
            }
        }

        /// <summary>
        /// Queue a component removal for broadcast (host only). Called by
        /// <see cref="Element.RemoveComponent"/> after the component has been untracked.
        /// Teardown paths (<see cref="UntrackElement"/>) never call this, so whole-element
        /// deletes are covered by the WorldDelta delete alone.
        /// </summary>
        public void NotifyComponentRemoved(IWorldElement element, IComponent component)
        {
            if (element == null || component == null) return;
            if (!IsAuthority || _suppressCapture) return;
            if (IsPlayerElement(element)) return;

            lock (_deltaLock)
            {
                _pendingRemovals.Add(new PendingRemoval(element.Id, component.Id));
            }
        }

        /// <summary>
        /// Run <paramref name="action"/> with lifecycle capture suppressed, so applying
        /// a received delta on this instance does not re-queue the creates/deletes it
        /// produces. Used by the receive-side WorldDelta handler.
        /// </summary>
        public void WithCaptureSuppressed(Action action)
        {
            lock (_deltaLock)
            {
                _suppressCapture = true;
                try { action?.Invoke(); }
                finally { _suppressCapture = false; }
            }
        }

        private void OnComponentDirty(IComponent component)
        {
            _dirtyComponents.TryAdd(component.Id, component);
        }

        private void OnElementDirty(IWorldElement element)
        {
            _dirtyElements.TryAdd(element, 0);
        }

        /// <summary>
        /// Update the tracker and send batched dirty updates if throttle interval has elapsed.
        /// </summary>
        /// <param name="deltaTime">Time since last update in seconds.</param>
        public void Update(float deltaTime)
        {
            _timeSinceLastSend += deltaTime;

            if (_timeSinceLastSend >= ThrottleInterval)
            {
                SendDirtyUpdates();
                _timeSinceLastSend = 0f;
            }
        }

        /// <summary>
        /// Immediately send all pending dirty updates, regardless of throttle interval.
        /// </summary>
        public void SendDirtyUpdates()
        {
            // If nobody is connected there is no point serialising/batching dirty state.
            // We still drain the dirty sets so they don't grow unbounded while offline;
            // we just don't push anything onto the cables.
            var hasPeer = HasConnectedPeer?.Invoke() ?? true;
            if (!hasPeer)
            {
                DrainDirtyWithoutSending();
                return;
            }

            // Clients are never authoritative: they only apply the host's deltas and
            // component batches, never broadcast their own. Drain locally so dirty
            // state can't accumulate (receiving host updates marks things dirty via
            // the property setters). Hosts broadcast; clients stay silent.
            if (!IsAuthority)
            {
                DrainDirtyWithoutSending();
                return;
            }

            // Send dirty components
            if (!_dirtyComponents.IsEmpty)
            {
                var batch = new List<IComponent>();

                // MaxBatchSize <= 0 means "drain everything queued this tick".
                // A negative value must not disable sending (see SetupNetworking).
                while (MaxBatchSize <= 0 || batch.Count < MaxBatchSize)
                {
                    if (_dirtyComponents.IsEmpty) break;

                    var key = _dirtyComponents.Keys.FirstOrDefault();
                    if (key == 0) break; // No more keys

                    if (_dirtyComponents.TryRemove(key, out var component))
                    {
                        batch.Add(component);
                    }
                }

                if (batch.Count > 0)
                {
                    SendComponentBatch(batch);
                }
            }

            // Send dirty elements
            if (!_dirtyElements.IsEmpty)
            {
                var batch = new List<IWorldElement>();

                while (MaxBatchSize <= 0 || batch.Count < MaxBatchSize)
                {
                    if (_dirtyElements.IsEmpty) break;

                    var key = _dirtyElements.Keys.FirstOrDefault();
                    if (key == null) break; // No more keys

                    if (_dirtyElements.TryRemove(key, out _))
                    {
                        batch.Add(key);
                    }
                }

                if (batch.Count > 0)
                {
                    SendElementBatch(batch);
                }
            }

            // Send element lifecycle deltas (host only). Only authority instances
            // ever have queued creates/deletes, so this is a no-op on clients.
            if (IsAuthority)
            {
                SendWorldDelta();
                SendComponentRemovals();
            }

            // Also flush any SyncValue-based dirty state via SyncManager
            try
            {
                SyncManager.FlushDirty();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DirtyTracker] Error flushing SyncManager: {ex.Message}");
            }
        }

        /// <summary>
        /// Flush any queued element creates/deletes as a <see cref="MessageType.WorldDelta"/>.
        /// The element ids carried are this instance's sequential ids, so applying them
        /// on every peer gives the same element the same id everywhere (host-authoritative).
        /// </summary>
        private void SendWorldDelta()
        {
            WorldDeltaDTO? dto = null;

            lock (_deltaLock)
            {
                if (_pendingCreates.Count == 0 && _pendingDeletes.Count == 0) return;

                dto = new WorldDeltaDTO { Timestamp = DateTime.UtcNow };

                foreach (var pc in _pendingCreates)
                {
                    try
                    {
                        var esDto = AncientCompressor.CompressElement(pc.Element);
                        if (esDto != null)
                            dto.Creates.Add(new ElementCreateDTO { ParentId = pc.ParentId, WorldName = pc.WorldName, Element = esDto });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DirtyTracker] Failed to compress element '{pc.Element.Name}' for delta: {ex.Message}");
                    }
                }

                dto.Deletes.AddRange(_pendingDeletes);

                _pendingCreates.Clear();
                _pendingDeletes.Clear();
            }

            if (dto == null || (dto.Creates.Count == 0 && dto.Deletes.Count == 0)) return;

            try
            {
                _cables.SendData(new MessageDTO
                {
                    Sender = _senderUri,
                    MessageType = MessageType.WorldDelta,
                    Message = AncientCompressor.Compress(dto)
                });

                Console.WriteLine($"[DirtyTracker] Sent world delta: {dto.Creates.Count} create(s), {dto.Deletes.Count} delete(s)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DirtyTracker] Error sending world delta: {ex.Message}");
            }
        }

        /// <summary>
        /// Flush any queued component removals as <see cref="MessageType.ComponentRemoved"/>
        /// messages (host only). Removals are grouped by element, one message per element.
        /// </summary>
        private void SendComponentRemovals()
        {
            List<PendingRemoval> removals;
            lock (_deltaLock)
            {
                if (_pendingRemovals.Count == 0) return;
                removals = new List<PendingRemoval>(_pendingRemovals);
                _pendingRemovals.Clear();
            }

            foreach (var group in removals.GroupBy(r => r.ElementId))
            {
                try
                {
                    var dto = new ComponentRemovalDTO
                    {
                        ElementId = group.Key,
                        Timestamp = DateTime.UtcNow
                    };
                    foreach (var r in group)
                        dto.ComponentIds.Add(r.ComponentId);

                    _cables.SendData(new MessageDTO
                    {
                        Sender = _senderUri,
                        MessageType = MessageType.ComponentRemoved,
                        Message = AncientCompressor.Compress(dto)
                    });

                    Console.WriteLine($"[DirtyTracker] Sent {dto.ComponentIds.Count} component removal(s) for element {dto.ElementId}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DirtyTracker] Error sending component removals: {ex.Message}");
                }
            }
        }

        private void SendComponentBatch(List<IComponent> components)
        {
            try
            {                var dto = new ComponentBatchDTO
                {
                    Timestamp = DateTime.UtcNow
                };

                foreach (var c in components)
                {
                    // If this component is a MeshComponent, ensure its SyncValues are
                    // published before serialization so the SyncManager path also carries
                    // the mesh vertex/index data to remote peers.
                    if (c is MeshComponent mc)
                    {
                        try { mc.PublishMeshToSyncAuthoritative(); } catch { }
                    }

                    byte[]? payload = null;
                    try
                    {
                        var csDto = AncientCompressor.CompressComponent(c);
                        if (csDto != null)
                            payload = AncientCompressor.Compress(csDto);
                    }
                    catch { /* non-serializable component: skip payload */ }

                    dto.Components.Add(new ComponentSnapshot
                    {
                        Id        = c.Id,
                        ElementId = FindOwnerElementId(c),
                        Name      = c.Name,
                        Payload   = payload
                    });
                }

                _cables.SendData(new MessageDTO
                {
                    Sender = _senderUri,
                    MessageType = MessageType.WorldUpdate,
                    Message = AncientCompressor.Compress(dto)
                });

                Console.WriteLine($"[DirtyTracker] Sent {components.Count} dirty component(s)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DirtyTracker] Error sending component batch: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolve the id of the element owning <paramref name="component"/> for the
        /// wire (<see cref="ComponentSnapshot.ElementId"/>). Prefers the tracked owner
        /// map, falling back to a recursive world scan for untracked components.
        /// </summary>
        private long FindOwnerElementId(IComponent component)
        {
            if (component != null && _componentOwners.TryGetValue(component.Id, out var owner) && owner != null)
                return owner.Id;

            var g = GameRoot.Instance;
            if (g != null && component != null)
            {
                var el = g.FindElement(e => e.Components != null && e.Components.Any(c => c.Id == component.Id));
                if (el != null) return el.Id;
            }
            return 0;
        }

        private void SendElementBatch(List<IWorldElement> elements)
        {
            try
            {
                var dto = new ElementUpdateBatchDTO
                {
                    Timestamp = DateTime.UtcNow
                };

                foreach (var e in elements)
                {
                    if (IsPlayerRigElement(e)) continue;

                    dto.Elements.Add(new ElementUpdateDTO
                    {
                        Id          = e.Id,
                        ParentId    = e.Parent?.Id ?? 0,
                        Name        = e.Name,
                        Description = e.Description,
                        Position    = e.LocalTransform.Position,
                        Rotation    = e.LocalTransform.Rotation,
                        Scale       = e.LocalTransform.Scale,
                        Timestamp   = DateTime.UtcNow
                    });
                }

                if (dto.Elements.Count == 0) return;

                _cables.SendData(new MessageDTO
                {
                    Sender = _senderUri,
                    MessageType = MessageType.WorldElementUpdate,
                    Message = AncientCompressor.Compress(dto)
                });

                Console.WriteLine($"[DirtyTracker] Sent {dto.Elements.Count} dirty element update(s)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DirtyTracker] Error sending element update: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the current number of pending dirty components.
        /// </summary>
        public int PendingComponentCount => _dirtyComponents.Count;

        /// <summary>
        /// Get the current number of pending dirty elements.
        /// </summary>
        public int PendingElementCount => _dirtyElements.Count;

        /// <summary>
        /// Clear all pending dirty state without sending.
        /// </summary>
        public void Clear()
        {
            _dirtyComponents.Clear();
            _dirtyElements.Clear();
        }

        /// <summary>
        /// Drain the dirty sets without pushing anything onto the cables. Used when
        /// there is no connected peer so the sets don't grow unbounded while offline
        /// but we also don't generate any outgoing traffic.
        /// </summary>
        private void DrainDirtyWithoutSending()
        {
            while (!_dirtyComponents.IsEmpty)
            {
                var key = _dirtyComponents.Keys.FirstOrDefault();
                if (key == 0) break;
                _dirtyComponents.TryRemove(key, out _);
            }

            while (!_dirtyElements.IsEmpty)
            {
                var key = _dirtyElements.Keys.FirstOrDefault();
                if (key == null) break;
                _dirtyElements.TryRemove(key, out _);
            }

            // Drain pending lifecycle deltas too (no peer → nothing to send, keep queues empty).
            lock (_deltaLock)
            {
                _pendingCreates.Clear();
                _pendingDeletes.Clear();
            }
        }

        public void Initialize(GameRoot g)
        {
    
        }
    }

    /// <summary>
    /// DTO for batching multiple component updates.
    /// </summary>
        // Serializable DTOs used on the wire. These contain only primitive fields
        // so they can be BSON-serialized without class-mapping for polymorphic types.
        public class ComponentBatchDTO
        {
            public List<ComponentSnapshot> Components { get; set; } = new();
            public DateTime Timestamp { get; set; }
        }

        public class ComponentSnapshot
        {
            public long Id { get; set; }
            /// <summary>Id of the element owning this component (0 if unknown). Used by the
            /// receiver to attach previously-unknown components to the right element.</summary>
            public long ElementId { get; set; }
            public string? Name { get; set; }
            // Future: include a payload blob for full state
            public byte[]? Payload { get; set; }
        }
}
