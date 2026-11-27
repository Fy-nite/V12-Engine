using System;

namespace V12.Core.Networking
{
    /// <summary>
    /// Host-authoritative statement of which player simulates one element.
    /// <see cref="SimulatingPlayerId"/> 0 means the host takes it back.
    /// Broadcast to every peer: the new owner needs to start simulating, and
    /// everyone else needs to stop.
    /// </summary>
    public class EntityAuthorityDTO
    {
        public Guid WorldId { get; set; }
        public Guid EntityUid { get; set; }
        public long SimulatingPlayerId { get; set; }
        public long Epoch { get; set; }
    }

    /// <summary>
    /// Client → host: claim or release the simulating user for one element.
    /// The host decides; it grants only when the element is unowned or already
    /// owned by the requester, so first claim wins. A grant comes back as
    /// <see cref="EntityAuthorityDTO"/>, which is what actually starts the client
    /// simulating.
    /// </summary>
    public class EntityAuthorityRequestDTO
    {
        public Guid EntityUid { get; set; }
        public bool Release { get; set; }
    }
}
