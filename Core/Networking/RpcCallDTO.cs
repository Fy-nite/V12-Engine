using System;

namespace V12.Core.Networking
{
    /// <summary>
    /// Payload for <see cref="MessageType.RpcCall"/>. Carries an element reference and a
    /// method name so the receiving peer can look up the element in its own world and invoke
    /// the matching handler. Handlers are plain code (lambdas/closures) and cannot cross the
    /// wire, so this DTO only transports the identity of what to call, never the callable.
    /// </summary>
    public class RpcCallDTO
    {
        /// <summary>Element id of the target (e.g. a button) in the receiving peer's world.</summary>
        public long ElementId { get; set; }

        /// <summary>Fallback target lookup by name, for elements every peer spawns independently with the same name.</summary>
        public string? ElementName { get; set; }

        /// <summary>Method/handler name to invoke, e.g. "Press" or "OnPressed".</summary>
        public string Method { get; set; } = string.Empty;

        /// <summary>
        /// BSON-serialized argument array (object[]). Serialized out-of-band by
        /// <see cref="RpcDispatcher"/> so the wire DTO never has to carry a polymorphic
        /// object[] member directly.
        /// </summary>
        public byte[]? ArgsData { get; set; }
    }
}
