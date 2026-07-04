using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using V12.Core.Core.Interfaces;
using V12.Core.NetworkCable;
using V12.Core.Networking;

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
        /// Maximum number of dirty items to batch per network message.
        /// </summary>
        public int MaxBatchSize { get; set; } = 50;

        /// <summary>
        /// Minimum time (in seconds) between sending batched updates. Set to 0 to send immediately.
        /// </summary>
        public float ThrottleInterval { get; set; } = 0.01f; // 10Hz default

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
        /// Subscribe to a component's dirty events.
        /// </summary>
        public void TrackComponent(IComponent component)
        {
            component.OnDirty += OnComponentDirty;
        }

        /// <summary>
        /// Unsubscribe from a component's dirty events.
        /// </summary>
        public void UntrackComponent(IComponent component)
        {
            component.OnDirty -= OnComponentDirty;
        }

        /// <summary>
        /// Subscribe to a world element's dirty events.
        /// </summary>
        public void TrackElement(IWorldElement element)
        {
            element.OnDirty += OnElementDirty;
            
            // Also track all components
            foreach (var component in element.Components)
            {
                TrackComponent(component);
            }
        }

        /// <summary>
        /// Unsubscribe from a world element's dirty events.
        /// </summary>
        public void UntrackElement(IWorldElement element)
        {
            element.OnDirty -= OnElementDirty;
            
            // Also untrack all components
            foreach (var component in element.Components)
            {
                UntrackComponent(component);
            }
        }

        /// <summary>
        /// Subscribe to all elements in a world and their components,
        /// and auto-track any elements added/removed in the future.
        /// </summary>
        public void TrackWorld(World world)
        {
            if (world.Root == null) return;

            // Subscribe to future element changes
            world.ElementAdded += OnWorldElementAdded;
            world.ElementRemoved += OnWorldElementRemoved;

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
            if (world.Root == null) return;

            world.ElementAdded -= OnWorldElementAdded;
            world.ElementRemoved -= OnWorldElementRemoved;

            foreach (var element in world.Root)
            {
                UntrackElement(element);
            }
        }

        private void OnWorldElementAdded(IWorldElement element)
        {
            TrackElement(element);
        }

        private void OnWorldElementRemoved(IWorldElement element)
        {
            UntrackElement(element);
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

            // Send dirty components
            if (!_dirtyComponents.IsEmpty)
            {
                var batch = new List<IComponent>();

                while (batch.Count < MaxBatchSize)
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

                while (batch.Count < MaxBatchSize)
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

        private void SendComponentBatch(List<IComponent> components)
        {
            try
            {
                var dto = new ComponentBatchDTO
                {
                    Timestamp = DateTime.UtcNow
                };

                foreach (var c in components)
                {
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
                        Id      = c.Id,
                        Name    = c.Name,
                        Payload = payload
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

        private void SendElementBatch(List<IWorldElement> elements)
        {
            try
            {
                var dto = new ElementBatchDTO
                {
                    Timestamp = DateTime.UtcNow
                };

                foreach (var e in elements)
                {
                    var es = new ElementSnapshot
                    {
                        Id = e.Id,
                        Name = e.Name,
                        Description = e.Description
                    };
                    foreach (var c in e.Components)
                        es.ComponentIds.Add(c.Id);
                    dto.Elements.Add(es);
                }

                _cables.SendData(new MessageDTO
                {
                    Sender = _senderUri,
                    MessageType = MessageType.WorldUpdate,
                    Message = AncientCompressor.Compress(dto)
                });

                Console.WriteLine($"[DirtyTracker] Sent {elements.Count} dirty element(s)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DirtyTracker] Error sending element batch: {ex.Message}");
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
            public string? Name { get; set; }
            // Future: include a payload blob for full state
            public byte[]? Payload { get; set; }
        }

        /// <summary>
        /// DTO for batching multiple element updates.
        /// </summary>
        public class ElementBatchDTO
        {
            public List<ElementSnapshot> Elements { get; set; } = new();
            public DateTime Timestamp { get; set; }
        }

        public class ElementSnapshot
        {
            public long Id { get; set; }
            public string? Name { get; set; }
            public string? Description { get; set; }
            public List<long> ComponentIds { get; set; } = new();
        }
}
