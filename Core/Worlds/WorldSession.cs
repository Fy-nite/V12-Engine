using System;
using System.Collections.Generic;
using V12.Core.Core.Interfaces;
using V12.Core.NetworkCable;
using V12.Core.Networking;

namespace V12.Core
{
    /// <summary>Who owns the simulation for a world session.</summary>
    public enum WorldAuthority
    {
        /// <summary>A local draft or an in-process world. This client owns the simulation.</summary>
        Local,

        /// <summary>This client hosts the world for networked players, but it is not necessarily visible.</summary>
        Hosted,

        /// <summary>Another host owns the simulation. This session only applies state.</summary>
        Remote,
    }

    /// <summary>What a hidden world session is allowed to do.</summary>
    public enum HiddenWorldPolicy
    {
        /// <summary>Do not tick or subscribe while hidden.</summary>
        Paused,

        /// <summary>Apply host state while hidden, but do not tick gameplay locally.</summary>
        NetworkOnly,

        /// <summary>Keep ticking while hidden.</summary>
        Simulate,
    }

    /// <summary>How a world session is being closed.</summary>
    public enum WorldCloseMode
    {
        /// <summary>Leave a remote world. The host and other players continue.</summary>
        Leave,

        /// <summary>Shut a local or hosted world down. Connected players are disconnected.</summary>
        Shutdown,

        /// <summary>Transfer authority to an explicit successor before closing.</summary>
        HandoffTo,

        /// <summary>Transfer authority to a headless host before closing.</summary>
        MoveToHeadless,

        /// <summary>Discard an unsaved local draft.</summary>
        DiscardDraft,
    }

    /// <summary>Outcome of a close request.</summary>
    public enum WorldCloseResult
    {
        Closed,
        Left,
        RequiresConfirmation,
        Unsupported,
        NotFound,
    }

    /// <summary>Where a world descriptor came from.</summary>
    public enum WorldSourceKind
    {
        Local,
        Pak,
        Remote,
        Draft,
    }

    /// <summary>Stable metadata for a world session. Presentation is a client concern.</summary>
    public sealed record WorldDescriptor
    {
        public Guid WorldId { get; init; }
        public string Name { get; init; } = "";
        public WorldSourceKind Source { get; init; } = WorldSourceKind.Local;
        public string? HostEndpoint { get; init; }
        public string? GamepackHash { get; init; }
        public string? PresentationId { get; init; }
    }

    /// <summary>The derived state of a world session. Never set this directly.</summary>
    public readonly record struct WorldSessionState(
        bool Visible,
        bool Simulated,
        bool Subscribed,
        bool Hydrated,
        HiddenWorldPolicy EffectivePolicy)
    {
        public static WorldSessionState Empty { get; } =
            new(false, false, false, false, HiddenWorldPolicy.Paused);
    }

    /// <summary>
    /// Transport seam for a world session. Local sessions leave this null; networked
    /// sessions supply a link that subscribes, resyncs, and closes.
    /// </summary>
    public interface IWorldLink
    {
        bool IsConnected { get; }
        void Subscribe(WorldSession session);
        void Unsubscribe(WorldSession session);
        void RequestSnapshot(WorldSession session, long sinceRevision);
        void Close(WorldSession session, WorldCloseMode mode);
    }

    /// <summary>
    /// A live session around a <see cref="World"/>. Stored inputs are the facts the
    /// host or user controls; <see cref="Applied"/> is derived by <see cref="Reconcile"/>.
    /// </summary>
    public sealed class WorldSession
    {
        private WorldAuthority _authority;
        private HiddenWorldPolicy _hiddenPolicy;
        private bool _pinned;
        private int _playersConnected;
        private bool _keepStale;
        private IWorldLink? _link;
        private bool _linkSubscribed;
        private bool _linkHydrated;

        public WorldDescriptor Descriptor { get; private set; }
        public World World { get; }

        /// <summary>
        /// Value-level replication for this session. Keys are scoped by world,
        /// element, and component so two sessions cannot collide.
        /// </summary>
        public SyncChannel Sync { get; }

        /// <summary>Transport bus owned by this session. One session, one connection.</summary>
        public NetworkCables Cables { get; }

        /// <summary>Component/element replication for this session.</summary>
        public DirtyTracker Dirty { get; }

        /// <summary>
        /// Transport for a networked session. Assigning a link reconciles the
        /// session so an already-visible world subscribes immediately.
        /// </summary>
        public IWorldLink? Link
        {
            get => _link;
            set
            {
                if (ReferenceEquals(_link, value)) return;
                _link = value;
                _linkSubscribed = false;
                _linkHydrated = false;
                Reconcile();
            }
        }

        public long Revision { get; internal set; }
        public long AuthorityEpoch { get; internal set; } = 1;
        public bool IsPersistent { get; }

        /// <summary>Who simulates this world.</summary>
        public WorldAuthority Authority
        {
            get => _authority;
            set
            {
                if (_authority == value) return;
                _authority = value;
                Dirty.IsAuthority = value != WorldAuthority.Remote;
                Reconcile();
            }
        }

        /// <summary>What the session should do while it is hidden.</summary>
        public HiddenWorldPolicy HiddenPolicy
        {
            get => _hiddenPolicy;
            set
            {
                if (_hiddenPolicy == value) return;
                _hiddenPolicy = value;
                Reconcile();
            }
        }

        /// <summary>Something such as picture-in-picture wants this session rendered.</summary>
        public bool Pinned
        {
            get => _pinned;
            set
            {
                if (_pinned == value) return;
                _pinned = value;
                Reconcile();
            }
        }

        private bool _SimulateRemoteWorld = true;

        /// <summary>
        /// Whether this session ticks the components on a replicated world that
        /// opted into <c>ComponentSimulationScope.Everywhere</c>. On by default:
        /// components opt in individually, so this only affects script and mod
        /// components that asked to run on the client. Turn it off for a pure
        /// viewer or a bandwidth-throttled client.
        /// </summary>
        public bool SimulateRemoteWorld
        {
            get => _SimulateRemoteWorld;
            set => _SimulateRemoteWorld = value;
        }

        private EntityAuthorityStore? _authorities;

        /// <summary>
        /// Who simulates each element in this session. The host simulates
        /// everything it has not delegated to a player; a client simulates the
        /// elements delegated to it and treats the rest as replicas.
        /// </summary>
        public EntityAuthorityStore Authorities => _authorities ??= new EntityAuthorityStore();

        /// <summary>
        /// The player id the host assigned to this peer, or 0 when unknown (a
        /// local or hosted session, or a client that has not been welcomed yet).
        /// A client compares this against <see cref="Authorities"/> to decide
        /// which elements it simulates.
        /// </summary>
        public long LocalPlayerId { get; set; }

        /// <summary>
        /// Whether this peer is the simulating user for <paramref name="element"/>:
        /// the host for everything it has not delegated, a client only for the
        /// elements delegated to it. Physics, component ticking and pose
        /// replication all follow this one answer.
        /// </summary>
        public bool IsSimulatedLocally(IWorldElement element)
            => Authorities.IsSimulatedLocally(
                World, element, LocalPlayerId, Authority != WorldAuthority.Remote);

        /// <summary>Host-side count of other players in this world.</summary>
        public int PlayersConnected
        {
            get => _playersConnected;
            set
            {
                value = Math.Max(0, value);
                if (_playersConnected == value) return;
                _playersConnected = value;
                Reconcile();
            }
        }

        /// <summary>Set only by <see cref="WorldManager.FocusSession"/>.</summary>
        public bool IsFocused { get; internal set; }

        /// <summary>
        /// Keep the last hydrated shell when a remote world is paused, so focus can
        /// resume with a delta instead of a full snapshot.
        /// </summary>
        public bool KeepStale
        {
            get => _keepStale;
            set
            {
                if (_keepStale == value) return;
                _keepStale = value;
                Reconcile();
            }
        }

        /// <summary>The derived state. Read this rather than recomputing flags.</summary>
        public WorldSessionState Applied { get; private set; }

        internal WorldSession(
            World world,
            WorldDescriptor descriptor,
            WorldAuthority authority,
            HiddenWorldPolicy hiddenPolicy,
            bool isPersistent)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Descriptor = descriptor with
            {
                WorldId = descriptor.WorldId == Guid.Empty ? world.Id : descriptor.WorldId,
                Name = string.IsNullOrWhiteSpace(descriptor.Name) ? world.WorldName : descriptor.Name,
            };
            _authority = authority;
            _hiddenPolicy = hiddenPolicy;
            IsPersistent = isPersistent;

            Sync = new SyncChannel(world);
            Cables = new NetworkCables();
            Dirty = new DirtyTracker(Cables, $"networkcables://world/{world.Id:N}")
            {
                IsAuthority = authority != WorldAuthority.Remote,
                Sync = Sync,
                HasConnectedPeer = () => _link?.IsConnected == true,
                // A client may only report the pose of elements it simulates;
                // the host relays those to everyone else.
                CanSendElement = IsSimulatedLocally,
            };
            Dirty.TrackWorld(world);

            Applied = Derive();
        }

        /// <summary>
        /// Adopt a host-assigned world id after a snapshot. Sync keys use the
        /// world id, so the channel is refreshed against the new scope.
        /// </summary>
        public void RebindWorldId(Guid worldId)
        {
            if (worldId == Guid.Empty || worldId == Descriptor.WorldId) return;

            Descriptor = Descriptor with { WorldId = worldId };
            World.Id = worldId;
            Sync.Refresh();
        }

        /// <summary>
        /// Recompute <see cref="Applied"/> and run edge-triggered link transitions.
        /// Call after any stored input changes. Focus, pin, authority, policy, and
        /// player-count changes already call this.
        /// </summary>
        public void Reconcile()
        {
            var next = Derive();

            if (next.Subscribed && !_linkSubscribed && Link != null)
            {
                Link.Subscribe(this);
                _linkSubscribed = true;
            }
            else if (!next.Subscribed && _linkSubscribed)
            {
                Link?.Unsubscribe(this);
                _linkSubscribed = false;
            }

            if (!next.Hydrated)
                _linkHydrated = false;
            else if (next.Subscribed && !_linkHydrated && Link != null)
            {
                Link.RequestSnapshot(this, Revision);
                _linkHydrated = true;
            }

            Applied = next;
            Validate(next);
        }

        /// <summary>Refresh and flush this session's value channel when it has a sender.</summary>
        public void PumpSync()
        {
            if (Sync.HasSender)
                Sync.Pump();
        }

        /// <summary>Pump component/element replication and the value channel for this session.</summary>
        public void PumpReplication(float deltaTime)
        {
            // Drain this session's transport on the game thread. The link owns
            // the incoming dispatch, so world mutations stay single-threaded.
            Cables.Update();
            Dirty.Update(deltaTime);
            PumpSync();
        }

        /// <summary>Pure derivation of the session state from its stored inputs.</summary>
        private WorldSessionState Derive()
        {
            bool requestedVisible = IsFocused || Pinned;

            HiddenWorldPolicy effective;
            if (requestedVisible)
            {
                effective = _authority == WorldAuthority.Remote
                    ? HiddenWorldPolicy.NetworkOnly
                    : HiddenWorldPolicy.Simulate;
            }
            else if (_authority == WorldAuthority.Hosted && _playersConnected > 0)
            {
                // Hiding a hosted world must not freeze other players.
                effective = HiddenWorldPolicy.Simulate;
            }
            else
            {
                effective = _hiddenPolicy;
            }

            bool simulated = _authority != WorldAuthority.Remote && effective == HiddenWorldPolicy.Simulate;
            bool subscribed = _authority == WorldAuthority.Remote && effective != HiddenWorldPolicy.Paused;
            bool hydrated = _authority != WorldAuthority.Remote || subscribed || KeepStale;

            return new WorldSessionState(
                Visible: requestedVisible && hydrated,
                Simulated: simulated,
                Subscribed: subscribed,
                Hydrated: hydrated,
                EffectivePolicy: effective);
        }

        private void Validate(in WorldSessionState state)
        {
            if (state.Visible && !state.Hydrated)
                throw new InvalidOperationException(
                    $"World session '{Descriptor.Name}' is visible but not hydrated.");

            if (state.Visible && _authority == WorldAuthority.Remote && !state.Subscribed)
                throw new InvalidOperationException(
                    $"Remote world session '{Descriptor.Name}' is visible but not subscribed.");

            if (state.Simulated && !state.Hydrated)
                throw new InvalidOperationException(
                    $"World session '{Descriptor.Name}' is simulating but not hydrated.");

            if (_authority == WorldAuthority.Local && !state.Hydrated)
                throw new InvalidOperationException(
                    $"Local world session '{Descriptor.Name}' must always be hydrated.");

            if (_authority == WorldAuthority.Hosted && _playersConnected > 0 && !state.Simulated)
                throw new InvalidOperationException(
                    $"Hosted world session '{Descriptor.Name}' has players but is not simulating.");
        }
    }

    /// <summary>Optional callbacks and confirmation for <see cref="WorldManager.Close"/>.</summary>
    public sealed class WorldCloseOptions
    {
        public WorldCloseMode Mode { get; init; } = WorldCloseMode.Shutdown;

        /// <summary>Required before shutting down a hosted world that still has players.</summary>
        public bool Confirmed { get; init; }

        /// <summary>Called for <see cref="WorldCloseMode.HandoffTo"/>.</summary>
        public Action<WorldSession>? Handoff { get; init; }

        /// <summary>Called for <see cref="WorldCloseMode.MoveToHeadless"/>.</summary>
        public Action<WorldSession>? MoveToHeadless { get; init; }

        /// <summary>When false, the session keeps its element tree and only leaves the world list.</summary>
        public bool ClearWorld { get; init; } = true;
    }
}
