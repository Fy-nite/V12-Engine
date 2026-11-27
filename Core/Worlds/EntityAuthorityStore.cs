using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using V12.Core.Core.Interfaces;

namespace V12.Core
{
    /// <summary>
    /// One element's simulating user. <see cref="SimulatingPlayerId"/> 0 means
    /// the host simulates it, which is the default for everything.
    /// </summary>
    public readonly record struct EntityAssignment(
        Guid WorldId,
        Guid EntityUid,
        long SimulatingPlayerId,
        long Epoch);

    /// <summary>
    /// Session-scoped map of "which peer simulates this element". Ownership is
    /// session and transport state, not authored content, so it lives here
    /// rather than on <see cref="V12.Core.Element"/>
    /// (see <c>docs/PEER_HOSTING_AND_AUTHORITY.md</c> §5.2).
    /// <para>
    /// The host owns everything by default. A player takes ownership of an object
    /// it is holding, wearing, or otherwise driving; that peer then runs the
    /// object's components and sends the result, and every other peer treats the
    /// element as a replica. Assignment is per element, so one player can hold a
    /// different object than another.
    /// </para>
    /// <para>
    /// Reads are lock-free and happen once per element per tick, so the store
    /// must stay cheap. Assignments are rare and serialised.
    /// </para>
    /// </summary>
    public sealed class EntityAuthorityStore
    {
        private readonly record struct Key(Guid WorldId, Guid EntityUid);

        private readonly ConcurrentDictionary<Key, EntityAssignment> _assignments = new();
        private readonly object _gate = new();
        private int _delegated;

        /// <summary>
        /// True when at least one element is delegated to a player. The host uses
        /// this to keep the no-delegation tick path free of a per-element lookup.
        /// </summary>
        public bool HasDelegatedEntities => Volatile.Read(ref _delegated) > 0;

        /// <summary>Snapshot of every known assignment, for a joining client.</summary>
        public List<EntityAssignment> Snapshot()
        {
            lock (_gate) return new List<EntityAssignment>(_assignments.Values);
        }

        /// <summary>Player simulated by, or 0 for the host / unknown element.</summary>
        public long GetSimulatingPlayer(World world, IWorldElement element)
        {
            if (!TryKey(world, element, out var key)) return 0L;
            return _assignments.TryGetValue(key, out var assignment) ? assignment.SimulatingPlayerId : 0L;
        }

        public EntityAssignment? GetAssignment(World world, IWorldElement element)
        {
            if (!TryKey(world, element, out var key)) return null;
            return _assignments.TryGetValue(key, out var assignment) ? assignment : null;
        }

        /// <summary>
        /// Give <paramref name="element"/> to <paramref name="playerId"/> and return
        /// the new authority epoch. A <paramref name="playerId"/> of 0 releases it
        /// back to the host. Returns 0 when the element has no stable
        /// <see cref="IWorldElement.Uid"/> and therefore cannot be delegated.
        /// </summary>
        public long Assign(World world, IWorldElement element, long playerId)
        {
            if (!TryKey(world, element, out var key)) return 0L;

            lock (_gate)
            {
                long epoch = _assignments.TryGetValue(key, out var previous) ? previous.Epoch + 1 : 1;
                _assignments[key] = new EntityAssignment(
                    key.WorldId, key.EntityUid, playerId, epoch);
                TrackDelegation(previous.SimulatingPlayerId, playerId);
                return epoch;
            }
        }

        /// <summary>Hand the element back to the host, keeping the epoch history.</summary>
        public bool Release(World world, IWorldElement element)
        {
            if (!TryKey(world, element, out var key)) return false;

            lock (_gate)
            {
                bool had = _assignments.TryGetValue(key, out var previous);
                long epoch = had ? previous.Epoch + 1 : 1;
                _assignments[key] = new EntityAssignment(
                    key.WorldId, key.EntityUid, 0L, epoch);
                TrackDelegation(had ? previous.SimulatingPlayerId : 0L, 0L);
                return had && previous.SimulatingPlayerId != 0L;
            }
        }

        /// <summary>
        /// Hand every element owned by <paramref name="playerId"/> back to the
        /// host, returning the released assignments (already carrying the next
        /// epoch) so the caller can broadcast them. Used when a client
        /// disconnects: its objects must not stay frozen on the peers.
        /// </summary>
        public List<EntityAssignment> ReleaseAllFor(long playerId)
        {
            var released = new List<EntityAssignment>();
            if (playerId == 0L) return released;

            lock (_gate)
            {
                foreach (var pair in _assignments)
                {
                    var assignment = pair.Value;
                    if (assignment.SimulatingPlayerId != playerId) continue;

                    var release = new EntityAssignment(
                        assignment.WorldId, assignment.EntityUid, 0L, assignment.Epoch + 1);
                    _assignments[pair.Key] = release;
                    released.Add(release);
                }

                if (released.Count > 0)
                    Interlocked.Add(ref _delegated, -released.Count);
            }

            return released;
        }

        /// <summary>
        /// Apply an assignment that arrived from the host. Stale and duplicate
        /// epochs are rejected, so a reordered or replayed message cannot revert
        /// ownership.
        /// </summary>
        public void Apply(EntityAssignment assignment)
        {
            if (assignment.EntityUid == Guid.Empty) return;
            var key = new Key(assignment.WorldId, assignment.EntityUid);

            lock (_gate)
            {
                bool had = _assignments.TryGetValue(key, out var previous);
                if (had && assignment.Epoch <= previous.Epoch) return;

                _assignments[key] = assignment;
                TrackDelegation(had ? previous.SimulatingPlayerId : 0L, assignment.SimulatingPlayerId);
            }
        }

        /// <summary>Maintain the delegated count across an owner transition.</summary>
        private void TrackDelegation(long before, long after)
        {
            bool was = before != 0L;
            bool isNow = after != 0L;
            if (was == isNow) return;
            Interlocked.Add(ref _delegated, isNow ? 1 : -1);
        }

        /// <summary>
        /// Whether this peer runs <paramref name="element"/>. The host simulates
        /// every element it has not delegated; a client simulates only the
        /// elements delegated to it. It needs to know its own player id first,
        /// which arrives with the session welcome.
        /// </summary>
        public bool IsSimulatedLocally(
            World world, IWorldElement element, long localPlayerId, bool isHost)
        {
            long owner = GetSimulatingPlayer(world, element);
            if (isHost) return owner == 0L;
            return localPlayerId != 0L && owner == localPlayerId;
        }

        /// <summary>
        /// Whether <paramref name="playerId"/> may write this element. The host
        /// always may (it arbitrates and may reclaim). This is policy, not the
        /// tick gate: a well-behaved client only writes what it simulates.
        /// </summary>
        public bool CanWrite(
            World world, IWorldElement element, long playerId, bool isHost)
            => isHost || GetSimulatingPlayer(world, element) == playerId;

        private static bool TryKey(World world, IWorldElement element, out Key key)
        {
            key = default;
            if (world is null || element is null || element.Uid == Guid.Empty) return false;
            key = new Key(world.Id, element.Uid);
            return true;
        }
    }
}
