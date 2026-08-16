using System;

namespace V12.Core.Networking
{
    /// <summary>
    /// Marks a method as remotely callable via <see cref="RpcDispatcher"/>.
    /// Any instance method on a component or element carrying this attribute can be
    /// invoked across the network by name. This is the engine's RPC contract: only
    /// methods marked <c>[Remote]</c> may be executed on a peer's behalf.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RemoteAttribute : Attribute
    {
    }
}
