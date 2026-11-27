using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using V12.Core.Binding;
using V12.Core.Core.Interfaces;
using V12.Core.Diagnostics;
using V12.Core.Networking;
namespace V12.Core
{
    public class World 
    {

        public List<IWorldElement> Root { get; set; }

        /// <summary>Stable world identity carried on the wire. The local element ids
        /// remain scoped to this world during the single-region phase.</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Double-precision origin of the simulation bubble. Element transforms,
        /// physics bodies and the renderer all use coordinates local to this
        /// point; the true global position is <see cref="Origin"/> plus a local
        /// transform. Kept at zero until something rebases it, so existing worlds
        /// and tests are unchanged.
        /// </summary>
        public WorldOffset Origin { get; private set; } = WorldOffset.Zero;

        /// <summary>How many times this world's bubble has been re-centred.</summary>
        public int OriginRebaseCount { get; private set; }

        /// <summary>Raised after a re-centre, with the local delta that was
        /// subtracted from every root transform.</summary>
        public event Action<World, Vector3>? OriginRebased;

        /// <summary>Binding context for this world. Lazily created by <see cref="Bind.Current"/>.</summary>
        public BindingContext? Bindings { get; set; }
        public string WorldName { get; set; }
        internal Dictionary<long, IWorldElement> _elementsById = new();

        /// <summary>Default spawn position when the Player enters this world.</summary>
        public System.Numerics.Vector3 SpawnPosition { get; set; } = new System.Numerics.Vector3(0, 1.5f, 0);

        /// <summary>Filesystem path where this world's V12World archive was extracted.</summary>
        public string ExtractPath { get; set; }

        /// <summary>The v12:// mount point for this world's assets.</summary>
        public string MountPoint { get; set; }

        /// <summary>When false this world does not tick components via <see cref="Update"/>. Used for the editor's non-running scene.</summary>
        public bool TickingEnabled { get; set; } = true;

        /// <summary>Global lock for all world and element tree mutations.</summary>
        public ReaderWriterLockSlim Lock { get; } = new(LockRecursionPolicy.SupportsRecursion);

        /// <summary>Raised on the calling thread when an element is added via <see cref="AddElement"/>.</summary>
        public event Action<IWorldElement>? ElementAdded;

        /// <summary>Raised on the calling thread when an element is removed via <see cref="RemoveElement"/>.</summary>
        public event Action<IWorldElement>? ElementRemoved;

        public World() { 
            WorldName = "DefaultWorld";
            Root = new List<IWorldElement>();
        }

        public World(string Name)
        {
            WorldName = Name;
            Root = new List<IWorldElement>();
        }
        public IWorldElement? FindElementWithComponent<T>()
    where T : IComponent
        {
            Lock.EnterReadLock();
            try
            {
                return Root.FirstOrDefault(e => e.GetComponent<T>() != null);
            }
            finally { Lock.ExitReadLock(); }
        }

        public IWorldElement? FindElementWithComponentRecursive<T>() where T : IComponent
        {
            Lock.EnterReadLock();
            try
            {
                foreach (var element in Root.ToArray())
                {
                    if (element.GetComponent<T>() != null) return element;
                    var found = FindInChildren<T>(element);
                    if (found != null) return found;
                }
                return null;
            }
            finally { Lock.ExitReadLock(); }
        }

        private IWorldElement? FindInChildren<T>(IWorldElement parent) where T : IComponent
        {
            foreach (var child in parent.Children.ToArray())
            {
                if (child.GetComponent<T>() != null) return child;
                var found = FindInChildren<T>(child);
                if (found != null) return found;
            }
            return null;
        }
        public void AddElement(IWorldElement element)
        {
            Lock.EnterWriteLock();
            try
            {
                Root.Add(element);
                IndexSubtree(element);
            }
            finally { Lock.ExitWriteLock(); }
            ElementAdded?.Invoke(element);
            GameRoot.Instance?.MarkRenderDirty();
        }

        /// <summary>
        /// True when <paramref name="element"/> is still indexed in this world.
        /// Used by reference caches to drop targets that were deleted or moved
        /// to another world before a lazy re-resolve.
        /// </summary>
        public bool ContainsElement(IWorldElement? element)
        {
            if (element == null) return false;
            Lock.EnterReadLock();
            try
            {
                return _elementsById.TryGetValue(element.Id, out var indexed)
                    && ReferenceEquals(indexed, element);
            }
            finally { Lock.ExitReadLock(); }
        }

        /// <summary>
        /// Convert an element's local world transform into a double-precision
        /// global position by adding the bubble origin. Safe to call from any
        /// thread; takes a read lock for the transform walk.
        /// </summary>
        public WorldOffset GlobalPositionOf(IWorldElement element)
        {
            Lock.EnterReadLock();
            try
            {
                var m = element.WorldTransform;
                return Origin + new Vector3(m.M41, m.M42, m.M43);
            }
            finally { Lock.ExitReadLock(); }
        }

        /// <summary>Add the bubble origin to a local world-space position.</summary>
        public WorldOffset GlobalPositionOf(Vector3 localWorldPosition) => Origin + localWorldPosition;

        /// <summary>Convert a global position into this world's local bubble.</summary>
        public Vector3 LocalPositionOf(WorldOffset globalPosition) => (globalPosition - Origin).ToVector3();

        /// <summary>
        /// Re-centre the float bubble by <paramref name="localShift"/>. Every
        /// root transform and every <see cref="IFloatingOriginAware"/> component
        /// is moved by the same delta, so all relative geometry is preserved and
        /// only the mapping between local space and global space changes.
        /// </summary>
        /// <returns>False when the shift was zero or not finite.</returns>
        public bool RebaseOrigin(Vector3 localShift)
        {
            if (!float.IsFinite(localShift.X)
                || !float.IsFinite(localShift.Y)
                || !float.IsFinite(localShift.Z)
                || localShift.LengthSquared() < 1e-8f)
                return false;

            // World.Update holds a read lock while component updates run. The
            // re-centre only moves transforms and component state; it never
            // changes the tree, so it can run under that read lock. External
            // callers take the write lock normally.
            bool tookWriteLock = !Lock.IsReadLockHeld;
            if (tookWriteLock)
                Lock.EnterWriteLock();
            try
            {
                foreach (var root in Root.ToArray())
                {
                    if (root == null) continue;
                    var lt = root.LocalTransform;
                    lt.Position -= localShift;
                    root.LocalTransform = lt;
                }

                SpawnPosition -= localShift;
                Origin += localShift;
                OriginRebaseCount++;

                foreach (var root in Root.ToArray())
                    NotifyOriginShift(root, localShift);
            }
            finally
            {
                if (tookWriteLock)
                    Lock.ExitWriteLock();
            }

            GameRoot.Instance?.MarkRenderDirty();
            OriginRebased?.Invoke(this, localShift);
            return true;
        }

        private static void NotifyOriginShift(IWorldElement element, Vector3 delta)
        {
            foreach (var component in element.Components.ToArray())
            {
                if (component is IFloatingOriginAware aware)
                    aware.OnOriginShift(delta);
            }

            foreach (var child in element.Children.ToArray())
                NotifyOriginShift(child, delta);
        }

        /// <summary>Look up an element by id inside this world only. Unlike
        /// <see cref="GameRoot.FindElement(Func{IWorldElement,bool})"/>, this never
        /// falls through to another active world, so it is safe once multiple
        /// worlds or regions are loaded at the same time.</summary>
        public IWorldElement? FindElementById(long id)
        {
            Lock.EnterReadLock();
            try
            {
                return _elementsById.TryGetValue(id, out var element) ? element : null;
            }
            finally { Lock.ExitReadLock(); }
        }

        /// <summary>Remove an element from the world and fire <see cref="ElementRemoved"/>.</summary>
        public void RemoveElement(IWorldElement element)
        {
            bool removed;
            Lock.EnterWriteLock();
            try
            {
                removed = Root.Remove(element);
                if (removed)
                    UnindexSubtree(element);
            }
            finally { Lock.ExitWriteLock(); }
            if (removed)
            {
                ElementRemoved?.Invoke(element);
                GameRoot.Instance?.MarkRenderDirty();
            }
        }

        /// <summary>
        /// Detach and destroy every component in the world, recursively.
        /// <para>
        /// This is the teardown half of <see cref="ReplaceFrom"/>: without it a
        /// closed or replaced world leaves components subscribed to their events,
        /// script runtimes loaded, and backend resources (physics bodies, GPU
        /// buffers) alive after the tree is dropped. Call it on the game thread,
        /// before the last reference to the tree goes away.
        /// </para>
        /// <para>
        /// It is deliberately not called by <see cref="RemoveElement"/>: removing
        /// an element is also how a reparent works, and that must not detach the
        /// components it is carrying.
        /// </para>
        /// </summary>
        public void DetachAll()
        {
            foreach (var root in Root.ToArray())
                DetachRecursive(root);

            GameRoot.Instance?.MarkRenderDirty();
        }

        private static void DetachRecursive(IWorldElement element)
        {
            foreach (var component in element.Components.ToArray())
            {
                // Snapshot before detaching: a component may drop its values in
                // OnDetach, and we still need to release them.
                var values = SyncValueScanner.EnumerateMembers(component).ToArray();

                try { component.OnDetach(element); }
                catch (Exception ex)
                {
                    GameRoot.Log?.Warning(ex, "Component detach failed on '{Element}'", element.Name);
                }

                try { component.OnDestroy(); }
                catch (Exception ex)
                {
                    GameRoot.Log?.Warning(ex, "Component destroy failed on '{Element}'", element.Name);
                }

                // Values registered process-wide outlive the component otherwise.
                // Reference-checked removal, so a replacement on the same key stays.
                foreach (var value in values)
                {
                    try { value.Unregister(); } catch { }
                }
            }

            foreach (var child in element.Children.ToArray())
                DetachRecursive(child);
        }

        /// <summary>
        /// Replace this world's entire element tree with elements from another world,
        /// preserving the world name. Thread-safe: acquires the write lock.
        /// </summary>
        public void ReplaceFrom(World source)
        {
            Lock.EnterWriteLock();
            try
            {
                Root.Clear();
                _elementsById.Clear();
                foreach (var el in source.Root)
                {
                    Root.Add(el);
                    IndexSubtree(el);
                }
            }
            finally { Lock.ExitWriteLock(); }
            GameRoot.Instance?.MarkRenderDirty();
        }

        /// <summary>Index an element and every descendant by id. Call under the write lock.</summary>
        internal void IndexSubtree(IWorldElement element)
        {
            _elementsById[element.Id] = element;
            if (element.Children == null) return;

            foreach (var child in element.Children)
                IndexSubtree(child);
        }

        /// <summary>Drop an element and every descendant from the id index. Call under the write lock.</summary>
        internal void UnindexSubtree(IWorldElement element)
        {
            _elementsById.Remove(element.Id);
            if (element.Children == null) return;

            foreach (var child in element.Children)
                UnindexSubtree(child);
        }

        public void GenerateWorld()
        {
            // This method can be overridden in derived classes to create specific world content.
            // For example, you could create a HomeWorld class that inherits from World and overrides this method to populate the world with specific elements.
        }
        public void Update(float deltaTime) => Update(deltaTime, null, 0L, isHost: true);

        /// <summary>
        /// Tick this world's components for the elements this peer simulates.
        /// <para>
        /// <paramref name="authorities"/> is the session's ownership map. The host
        /// simulates every element it has not delegated; a client simulates only
        /// the elements delegated to it. Components that are
        /// <see cref="ComponentSimulationScope.Replica"/> run on the peers that do
        /// not simulate the element, and <see cref="ComponentSimulationScope.Everywhere"/>
        /// runs on all of them.
        /// </para>
        /// <para>
        /// Passing <c>null</c> means "this peer simulates everything", which is
        /// the local/solo case and stays allocation- and lookup-free.
        /// </para>
        /// </summary>
        public void Update(
            float deltaTime,
            EntityAuthorityStore? authorities,
            long localPlayerId,
            bool isHost)
        {
            using var _zone = V12Profiler.BeginZone("World.Update", V12Profiler.Colors.World);
            if (!TickingEnabled) return;

            // Fast path: a host with nothing delegated simulates everything, so
            // skip the per-element ownership lookup entirely.
            if (isHost && authorities is not null && !authorities.HasDelegatedEntities)
                authorities = null;

            Lock.EnterReadLock();
            try
            {
                foreach (var element in Root.ToArray())
                {
                    UpdateElementRecursive(element, deltaTime, authorities, localPlayerId, isHost);
                }
            }
            finally { Lock.ExitReadLock(); }
        }

        /// <summary>
        /// Snapshots every element's local matrix at the start of a fixed step,
        /// so render interpolation has a "from" pose. Paired with
        /// <see cref="EndRenderPoseSnapshot"/> around the whole step, after
        /// physics and gameplay services have written their transforms.
        /// </summary>
        public void BeginRenderPoseSnapshot()
        {
            if (!TickingEnabled) return;
            Lock.EnterReadLock();
            try
            {
                foreach (var element in Root)
                    CaptureRenderPoseStart(element);
            }
            finally { Lock.ExitReadLock(); }
        }

        /// <summary>Snapshots the end-of-step pose used as the interpolation target.</summary>
        public void EndRenderPoseSnapshot()
        {
            if (!TickingEnabled) return;
            Lock.EnterReadLock();
            try
            {
                foreach (var element in Root)
                    CaptureRenderPoseEnd(element);
            }
            finally { Lock.ExitReadLock(); }
        }

        private static void CaptureRenderPoseStart(IWorldElement element)
        {
            if (element is Element engineElement)
                engineElement.BeginRenderPose();

            if (element.Children == null) return;
            foreach (var child in element.Children)
                CaptureRenderPoseStart(child);
        }

        private static void CaptureRenderPoseEnd(IWorldElement element)
        {
            if (element is Element engineElement)
                engineElement.EndRenderPose();

            if (element.Children == null) return;
            foreach (var child in element.Children)
                CaptureRenderPoseEnd(child);
        }

        private void UpdateElementRecursive(
            IWorldElement element,
            float deltaTime,
            EntityAuthorityStore? authorities,
            long localPlayerId,
            bool isHost)
        {
            try
            {
                bool simulatedHere = authorities is null ||
                    authorities.IsSimulatedLocally(this, element, localPlayerId, isHost);

                foreach (var component in element.Components.ToArray())
                {
                    var scope = component.SimulationScope;
                    bool ticks = simulatedHere
                        ? scope != ComponentSimulationScope.Replica
                        : scope != ComponentSimulationScope.Simulator;
                    if (ticks) component.Update(deltaTime);

                    // Publishing [Sync] fields follows the same rule as ticking:
                    // only the peer that simulates the element writes state.
                    if (simulatedHere && component is ISyncValueProvider provider)
                        provider.PollSyncValues();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error updating element {element.Name}: {e.Message}"); Console.WriteLine(e);
            }

            foreach (var child in element.Children.ToArray())
            {
                UpdateElementRecursive(child, deltaTime, authorities, localPlayerId, isHost);
            }
        }
    }
}
