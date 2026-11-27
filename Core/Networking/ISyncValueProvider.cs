using System.Collections.Generic;

namespace V12.Core.Networking
{
    /// <summary>
    /// A component that owns replicated values which are not declared as
    /// <see cref="SyncValue{T}"/> members. The field-sync binder implements this:
    /// it creates one value per <c>[Sync]</c> field, and the session channel
    /// discovers them through this interface instead of reflection over members.
    /// <para>
    /// <see cref="PollSyncValues"/> runs once per fixed tick on the peer that
    /// simulates the element, so direct field writes are picked up without the
    /// author writing setter plumbing.
    /// </para>
    /// </summary>
    public interface ISyncValueProvider
    {
        IEnumerable<ISyncValue> GetSyncValues();

        void PollSyncValues();
    }
}
