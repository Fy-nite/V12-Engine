namespace V12.Core.Core.Interfaces;

/// <summary>
/// Which peers run <see cref="IComponent.Update"/> for a component, relative to
/// who is currently simulating its element.
/// <para>
/// Replicated state has exactly one simulating user per element: the host by
/// default, or the player who owns/grabbed it (<see cref="V12.Core.EntityAuthorityStore"/>).
/// The peer simulating an element runs its gameplay components; every other peer
/// treats the element as a replica. This enum is the per-component half of that
/// rule:
/// </para>
/// <list type="bullet">
/// <item><see cref="Simulator"/> (default): tick only where the element is
/// simulated. A script or mod component therefore runs on the host for a
/// host-owned object and on the owning client for a grabbed one.</item>
/// <item><see cref="Replica"/>: tick only where the element is *not* simulated.
/// For interpolation, HUD, and local presentation helpers.</item>
/// <item><see cref="Everywhere"/>: tick on every peer, including peers that do
/// not simulate the element. Opt in only for validation or compute work, where
/// running the same logic twice on purpose is the point.</item>
/// </list>
/// </summary>
public enum ComponentSimulationScope
{
    /// <summary>Run wherever this element is simulated (default).</summary>
    Simulator,

    /// <summary>Run only where this element is a replica.</summary>
    Replica,

    /// <summary>
    /// Run on every peer, even for elements this peer does not simulate. A
    /// component that writes state here will fight the simulating user.
    /// </summary>
    Everywhere,
}
