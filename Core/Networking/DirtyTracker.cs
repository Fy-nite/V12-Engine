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
    public class DirtyTracker
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
        public float ThrottleInterval { get; set; } = 0.1f; // 10Hz default

        private float _timeSinceLastSend = 0f;

        public DirtyTracker(NetworkCables? cables = null, string? senderUri = null)
        {
            _cables = cables ?? NetworkCables.Default;
            _senderUri = new Uri(senderUri ?? "networkcables://dirtytracker");
        }

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
        /// Subscribe to all elements in a world and their components.
        /// </summary>
        public void TrackWorld(World world)
        {
            if (world.Root == null) return;

            foreach (var element in world.Root)
            {
                TrackElement(element);
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
        }

        private void SendComponentBatch(List<IComponent> components)
        {
            try
            {
                var dto = new ComponentBatchDTO
                {
                    Components = components,
                    Timestamp = DateTime.UtcNow
                };

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
                    Elements = elements,
                    Timestamp = DateTime.UtcNow
                };

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
    }

    /// <summary>
    /// DTO for batching multiple component updates.
    /// </summary>
    public class ComponentBatchDTO
    {
        public List<IComponent> Components { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// DTO for batching multiple element updates.
    /// </summary>
    public class ElementBatchDTO
    {
        public List<IWorldElement> Elements { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }
}
