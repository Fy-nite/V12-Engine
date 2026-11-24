using System;

namespace V12.Core.Interfaces.Physics
{
    /// <summary>
    /// Optional physics-backend capability for overlap-only trigger colliders.
    /// The trigger body is always the first argument; the second is the body it
    /// began or stopped overlapping. Trigger bodies do not generate a contact
    /// response in the Bepu backend.
    /// </summary>
    public interface IPhysicsTriggerEvents
    {
        /// <summary>Raised on the game thread after a step when a trigger starts overlapping a body.</summary>
        event Action<IPhysicsBody, IPhysicsBody>? TriggerEnter;

        /// <summary>Raised on the game thread after a step when a trigger stops overlapping a body.</summary>
        event Action<IPhysicsBody, IPhysicsBody>? TriggerExit;
    }
}
