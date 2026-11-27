using System;
using System.Collections.Generic;
using System.Linq;
using V12.Core.Networking;

namespace V12.Core
{
    /// <summary>
    /// Owns the live <see cref="WorldSession"/> set for a <see cref="GameRoot"/>.
    /// Worlds are still stored in <see cref="GameRoot.Worlds"/> for compatibility;
    /// the manager keeps a session per world and derives what each session should
    /// do from its stored inputs.
    /// </summary>
    public sealed class WorldManager
    {
        private readonly GameRoot _root;
        private readonly List<WorldSession> _sessions = new();
        private readonly Dictionary<World, WorldSession> _byWorld =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Guid, WorldSession> _byId = new();
        private WorldSession? _focused;

        public WorldManager(GameRoot root)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));

            // The persistent world keeps its long-standing role as the shell world
            // for UI and legacy player content. It always ticks, but it is not
            // focused or rendered unless something explicitly targets it.
            EnsureSession(
                root.PersistentWorld,
                WorldAuthority.Local,
                HiddenWorldPolicy.Simulate,
                WorldSourceKind.Local,
                isPersistent: true);
        }

        /// <summary>Every session, including sessions whose world is not hydrated yet.</summary>
        public IReadOnlyList<WorldSession> Sessions
        {
            get
            {
                SyncWorlds();
                return _sessions;
            }
        }

        public WorldSession? FocusedSession => _focused;
        public World? FocusedWorld => _focused?.World;

        /// <summary>The simulated set. This keeps the old meaning of ActiveWorlds.</summary>
        public IEnumerable<World> ActiveWorlds =>
            Sessions.Where(s => s.Applied.Simulated).Select(s => s.World);

        /// <summary>The rendered set: focused or pinned sessions that are hydrated.</summary>
        public IEnumerable<World> VisibleWorlds =>
            Sessions.Where(s => s.Applied.Visible).Select(s => s.World);

        /// <summary>Every hydrated world, including hidden ones. Used to resolve views.</summary>
        public IEnumerable<World> LoadedWorlds =>
            Sessions.Where(s => s.Applied.Hydrated).Select(s => s.World);

        /// <summary>
        /// Add a world to the root and create its session. Focus is opt-in so a
        /// caller can load a background world without making it visible.
        /// </summary>
        public WorldSession OpenWorld(
            World world,
            bool focus = false,
            WorldAuthority authority = WorldAuthority.Local,
            HiddenWorldPolicy hiddenPolicy = HiddenWorldPolicy.Paused,
            WorldSourceKind source = WorldSourceKind.Local)
        {
            ArgumentNullException.ThrowIfNull(world);

            if (!_root.Worlds.Contains(world))
                _root.Worlds.Add(world);

            var session = EnsureSession(world, authority, hiddenPolicy, source);
            if (focus)
                FocusSession(session);

            return session;
        }

        /// <summary>Create or return the session for a world already in the root.</summary>
        public WorldSession EnsureSession(
            World world,
            WorldAuthority authority = WorldAuthority.Local,
            HiddenWorldPolicy hiddenPolicy = HiddenWorldPolicy.Paused,
            WorldSourceKind source = WorldSourceKind.Local,
            bool isPersistent = false)
        {
            ArgumentNullException.ThrowIfNull(world);

            if (_byWorld.TryGetValue(world, out var existing))
                return existing;

            var descriptor = new WorldDescriptor
            {
                WorldId = world.Id,
                Name = world.WorldName,
                Source = source,
            };

            var session = new WorldSession(world, descriptor, authority, hiddenPolicy, isPersistent);
            _sessions.Add(session);
            _byWorld[world] = session;
            _byId[session.Descriptor.WorldId] = session;
            session.Reconcile();
            return session;
        }

        public WorldSession? FindSession(World? world)
        {
            if (world == null) return null;
            SyncWorlds();
            return _byWorld.TryGetValue(world, out var session) ? session : null;
        }

        public WorldSession? FindSession(Guid worldId)
        {
            if (worldId == Guid.Empty) return null;
            SyncWorlds();
            return _byId.TryGetValue(worldId, out var session) ? session : null;
        }

        public WorldSession? FindSession(string worldName)
        {
            if (string.IsNullOrWhiteSpace(worldName)) return null;
            SyncWorlds();
            return _sessions.FirstOrDefault(s =>
                string.Equals(s.World.WorldName, worldName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Adopt a host-assigned world id for a client session and keep the id
        /// index consistent.
        /// </summary>
        public void RebindWorldId(WorldSession? session, Guid worldId)
        {
            if (session == null || worldId == Guid.Empty) return;
            if (session.Descriptor.WorldId == worldId) return;

            var oldId = session.Descriptor.WorldId;
            if (_byId.TryGetValue(oldId, out var current) && ReferenceEquals(current, session))
                _byId.Remove(oldId);

            session.RebindWorldId(worldId);
            _byId[worldId] = session;
        }

        /// <summary>Focus a world already registered with the root. Returns false when unknown.</summary>
        public bool FocusWorld(World? world)
        {
            if (world == null)
                return FocusSession(null);

            if (!_root.Worlds.Contains(world))
                return false;

            var session = EnsureSession(world);
            return FocusSession(session);
        }

        public bool FocusWorld(string worldName)
        {
            var found = _root.Worlds.FirstOrDefault(w =>
                string.Equals(w.WorldName, worldName, StringComparison.OrdinalIgnoreCase));
            return found != null && FocusWorld(found);
        }

        /// <summary>
        /// Focus one session, clearing the previous focus. Focus only changes the
        /// stored input; <see cref="WorldSession.Reconcile"/> decides whether the
        /// session becomes visible, simulated, or subscribed.
        /// </summary>
        public bool FocusSession(WorldSession? session)
        {
            if (session != null && !_sessions.Contains(session))
                return false;

            if (ReferenceEquals(_focused, session))
            {
                ReconcileAll();
                return true;
            }

            if (_focused != null)
                _focused.IsFocused = false;

            _focused = session;

            if (_focused != null)
            {
                _focused.IsFocused = true;

                // A world focused after networking was set up still needs to be
                // tracked. Do not untrack the previous world: hidden worlds keep
                // their subscriptions and replication.
                var tracker = _root.Registry.Get<DirtyTracker>("DirtyTracker");
                tracker?.TrackWorld(_focused.World);
            }

            ReconcileAll();
            _root.MarkRenderDirty();
            return true;
        }

        /// <summary>Recompute every session after a stored input changed.</summary>
        public void ReconcileAll()
        {
            SyncWorlds();
            foreach (var session in _sessions.ToArray())
                session.Reconcile();
        }

        /// <summary>
        /// Close a session. Hosted worlds with players require confirmation unless
        /// authority is handed off or moved to a headless host.
        /// </summary>
        public WorldCloseResult Close(WorldSession? session, WorldCloseOptions? options = null)
        {
            if (session == null) return WorldCloseResult.NotFound;
            if (session.IsPersistent) return WorldCloseResult.Unsupported;

            options ??= new WorldCloseOptions();

            if (options.Mode == WorldCloseMode.HandoffTo)
            {
                if (options.Handoff == null) return WorldCloseResult.Unsupported;
                options.Handoff(session);
            }
            else if (options.Mode == WorldCloseMode.MoveToHeadless)
            {
                if (options.MoveToHeadless == null) return WorldCloseResult.Unsupported;
                options.MoveToHeadless(session);
            }
            else if (session.Authority == WorldAuthority.Hosted &&
                     session.PlayersConnected > 0 &&
                     !options.Confirmed)
            {
                return WorldCloseResult.RequiresConfirmation;
            }

            var wasFocused = ReferenceEquals(_focused, session) || session.IsFocused;
            session.Link?.Close(session, options.Mode);
            if (session.Link is IDisposable link)
                link.Dispose();

            session.IsFocused = false;
            if (wasFocused)
                _focused = null;

            _sessions.Remove(session);
            _byWorld.Remove(session.World);
            _byId.Remove(session.Descriptor.WorldId);
            _root.Worlds.Remove(session.World);

            var tracker = _root.Registry.Get<DirtyTracker>("DirtyTracker");
            tracker?.UntrackWorld(session.World);
            session.Dirty.UntrackWorld(session.World);
            session.Sync.Dispose();

            // Detach before the tree loses its last reference: components own
            // event subscriptions, script runtimes and backend resources (Bepu
            // bodies) that must be released with the world.
            session.World.DetachAll();

            if (options.ClearWorld)
                session.World.ReplaceFrom(new World(session.World.WorldName));

            ReconcileAll();
            _root.MarkRenderDirty();

            return options.Mode == WorldCloseMode.Leave
                ? WorldCloseResult.Left
                : WorldCloseResult.Closed;
        }

        /// <summary>
        /// Keep sessions in sync with direct <see cref="GameRoot.Worlds"/> edits.
        /// A world added directly gets a paused local session; a world removed
        /// directly loses its session.
        /// </summary>
        private void SyncWorlds()
        {
            foreach (var world in _root.Worlds.ToArray())
            {
                if (!_byWorld.ContainsKey(world))
                    EnsureSession(world);
            }

            foreach (var session in _sessions.ToArray())
            {
                if (session.IsPersistent) continue;
                if (_root.Worlds.Contains(session.World)) continue;

                if (ReferenceEquals(_focused, session) || session.IsFocused)
                    _focused = null;
                session.IsFocused = false;

                _sessions.Remove(session);
                _byWorld.Remove(session.World);
                _byId.Remove(session.Descriptor.WorldId);
                _root.Registry.Get<DirtyTracker>("DirtyTracker")?.UntrackWorld(session.World);
                session.Dirty.UntrackWorld(session.World);
                if (session.Link is IDisposable link)
                    link.Dispose();
                session.Sync.Dispose();

                // Same teardown as Close: a world removed from GameRoot.Worlds
                // directly still has live components and backend bodies.
                session.World.DetachAll();
            }
        }
    }
}
