using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace V12.Core.Introspection
{
    /// <summary>
    /// The engine-owned live scene surface: a snapshot the tick thread can build
    /// for any caller, and a command queue it drains at the tick boundary.
    ///
    /// <para>
    /// Why a queue instead of letting the caller mutate the tree directly: the
    /// world tree is only consistent on the loop that owns it. Writers that take
    /// <c>World.Lock</c> from another thread stall the tick mid-frame and can
    /// interleave with capture. Queueing keeps every mutation between two fixed
    /// steps, where the dirty tracker, the netcode and the renderer all see one
    /// coherent change.
    /// </para>
    /// </summary>
    public sealed class SceneIntrospection
    {
        /// <summary>Cap per drain so a runaway client cannot eat a frame.</summary>
        public const int MaxCommandsPerDrain = 256;

        /// <summary>How many finished receipts are kept for retry lookups.</summary>
        public const int ReceiptHistory = 512;

        /// <summary>How many applied commands are kept for the audit/replay journal.</summary>
        public const int JournalHistory = 1024;

        private sealed class PendingCommand
        {
            public readonly SceneCommand Command;
            public readonly TaskCompletionSource<CommandReceipt> Completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public PendingCommand(SceneCommand command) => Command = command;
        }

        private sealed class SnapshotRequest
        {
            public readonly TaskCompletionSource<WorldSnapshot> Completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private readonly GameRoot _root;
        private readonly object _drainGate = new();
        private readonly ConcurrentQueue<PendingCommand> _queue = new();
        private readonly ConcurrentDictionary<string, PendingCommand> _inFlight = new(StringComparer.Ordinal);
        private readonly List<PendingCommand> _deferred = new();
        private readonly ConcurrentQueue<SnapshotRequest> _snapshotRequests = new();
        private readonly ConcurrentDictionary<string, CommandReceipt> _recent = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _recentOrder = new();
        private readonly ConcurrentQueue<CommandJournalEntry> _journal = new();
        private long _sequence;

        public SceneIntrospection(GameRoot root)
            => _root = root ?? throw new ArgumentNullException(nameof(root));

        /// <summary>Commands waiting for the next drain, including deferred ones.</summary>
        public int PendingCount => _queue.Count + _deferred.Count + _snapshotRequests.Count;

        /// <summary>
        /// Queue a mutation and get a task for its receipt. Safe from any thread.
        /// A repeated <see cref="SceneCommand.RequestId"/> joins the original
        /// command (or returns its finished receipt) instead of applying twice.
        /// </summary>
        public Task<CommandReceipt> Enqueue(SceneCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);

            if (string.IsNullOrWhiteSpace(command.RequestId))
                command.RequestId = Guid.NewGuid().ToString("N");

            if (_recent.TryGetValue(command.RequestId, out var applied))
                return Task.FromResult(applied);

            if (_inFlight.TryGetValue(command.RequestId, out var existing))
                return existing.Completion.Task;

            var pending = new PendingCommand(command);
            if (!_inFlight.TryAdd(command.RequestId, pending))
            {
                // Lost the race with another enqueue; join whichever won.
                return _inFlight.TryGetValue(command.RequestId, out existing)
                    ? existing.Completion.Task
                    : Task.FromResult(Rejected(command, "request id collision; retry"));
            }

            _queue.Enqueue(pending);
            return pending.Completion.Task;
        }

        /// <summary>
        /// Apply every due command. Called by <see cref="GameRoot"/> at the start
        /// of a fixed step, and from the render tick while the simulation is
        /// paused (so live editing still works with the clock stopped).
        /// </summary>
        public int DrainCommands(long tick, bool paused = false)
        {
            lock (_drainGate)
            {
                int applied = 0;

                for (int i = _deferred.Count - 1; i >= 0 && applied < MaxCommandsPerDrain; i--)
                {
                    var pending = _deferred[i];
                    if (!Due(pending.Command, tick, paused)) continue;

                    _deferred.RemoveAt(i);
                    Apply(pending, tick);
                    applied++;
                }

                while (applied < MaxCommandsPerDrain && _queue.TryDequeue(out var pending))
                {
                    if (!Due(pending.Command, tick, paused))
                    {
                        _deferred.Add(pending);
                        continue;
                    }

                    Apply(pending, tick);
                    applied++;
                }

                // Snapshot requests drain after the commands queued before them,
                // so a client that writes then reads always sees its own write.
                DrainSnapshotRequests();

                return applied;
            }
        }

        /// <summary>
        /// Build a snapshot from any thread. The request is answered by the next
        /// drain (fixed step normally, render tick while paused), so the caller
        /// never touches live components. Multiple threads can call this
        /// concurrently; each gets its own snapshot, all built on the tick thread.
        /// </summary>
        public async Task<WorldSnapshot> RequestSnapshotAsync(TimeSpan? timeout = null)
        {
            var request = new SnapshotRequest();
            _snapshotRequests.Enqueue(request);

            if (timeout is not { } limit)
                return await request.Completion.Task.ConfigureAwait(false);

            var delay = Task.Delay(limit);
            if (await Task.WhenAny(request.Completion.Task, delay).ConfigureAwait(false) != request.Completion.Task)
            {
                request.Completion.TrySetException(new TimeoutException(
                    $"The simulation did not drain a snapshot request within {limit.TotalMilliseconds:0}ms. " +
                    "Is the host still ticking?"));
                return await request.Completion.Task.ConfigureAwait(false);
            }

            return await request.Completion.Task.ConfigureAwait(false);
        }

        private void DrainSnapshotRequests()
        {
            while (_snapshotRequests.TryDequeue(out var request))
            {
                if (request.Completion.Task.IsCompleted) continue;

                _sequence++;
                request.Completion.TrySetResult(SceneSnapshotBuilder.Capture(_root, _sequence));
            }
        }

        /// <summary>
        /// Build a snapshot on the calling thread. Fast path for the tick thread
        /// (and tests). Component state is not guarded by <c>World.Lock</c>, so a
        /// snapshot taken from another thread can tear mid-update: use
        /// <see cref="RequestSnapshotAsync"/> off the tick thread instead.
        /// </summary>
        public WorldSnapshot CaptureSnapshot()
        {
            lock (_drainGate)
            {
                _sequence++;
                return SceneSnapshotBuilder.Capture(_root, _sequence);
            }
        }

        /// <summary>Look up a finished receipt (for callers that poll instead of awaiting).</summary>
        public bool TryGetReceipt(string requestId, out CommandReceipt receipt)
            => _recent.TryGetValue(requestId, out receipt!);

        /// <summary>Most recent applied commands, oldest first. Audit log and replay script.</summary>
        public IReadOnlyList<CommandJournalEntry> RecentJournal(int take = 50)
            => _journal.TakeLast(Math.Max(0, take)).ToArray();

        private static bool Due(SceneCommand command, long tick, bool paused)
            => paused || command.ApplyAtTick <= 0 || command.ApplyAtTick <= tick;

        private void Apply(PendingCommand pending, long tick)
        {
            var command = pending.Command;
            var receipt = new CommandReceipt
            {
                RequestId = command.RequestId,
                Op = command.Op,
                AppliedTick = tick,
                Actor = command.Actor,
            };

            try
            {
                receipt.Error = SceneCommandApplier.Apply(_root, command, receipt);
                receipt.Accepted = receipt.Error is null;
            }
            catch (Exception ex)
            {
                receipt.Accepted = false;
                receipt.Error = ex.GetBaseException().Message;
                GameRoot.Log.Warning(ex, "Introspection command {Op} ({RequestId}) failed", command.Op, command.RequestId);
            }
            finally
            {
                _inFlight.TryRemove(command.RequestId, out _);
                Remember(receipt);
                Journal(receipt, command);
                pending.Completion.TrySetResult(receipt);
            }
        }

        private static CommandReceipt Rejected(SceneCommand command, string error) => new()
        {
            RequestId = command.RequestId,
            Op = command.Op,
            Accepted = false,
            Error = error,
            Actor = command.Actor,
        };

        private void Remember(CommandReceipt receipt)
        {
            _recent[receipt.RequestId] = receipt;
            _recentOrder.Enqueue(receipt.RequestId);
            while (_recent.Count > ReceiptHistory && _recentOrder.TryDequeue(out var oldest))
                _recent.TryRemove(oldest, out _);
        }

        private void Journal(CommandReceipt receipt, SceneCommand command)
        {
            _journal.Enqueue(new CommandJournalEntry(
                receipt.RequestId,
                receipt.Op,
                receipt.Actor,
                receipt.AppliedTick,
                receipt.Accepted,
                receipt.Error,
                receipt.CreatedId,
                command));

            while (_journal.Count > JournalHistory && _journal.TryDequeue(out _)) { }
        }
    }

    /// <summary>
    /// One applied (or rejected) command. Includes the command itself, so the
    /// journal can be re-posted to reproduce a session: give each entry a fresh
    /// <c>RequestId</c> and send them in order.
    /// </summary>
    public readonly record struct CommandJournalEntry(
        string RequestId,
        SceneOp Op,
        string? Actor,
        long AppliedTick,
        bool Accepted,
        string? Error,
        long? CreatedId,
        SceneCommand Command);
}
