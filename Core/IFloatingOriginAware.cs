using System.Numerics;

namespace V12.Core
{
    /// <summary>
    /// Implemented by components that cache a world-space position outside the
    /// element transform, such as a physics body, an orbit centre or a camera
    /// target. When the world bubble is re-centred, every such value must move
    /// by the same delta or the component snaps back to the old frame.
    /// </summary>
    public interface IFloatingOriginAware
    {
        /// <summary>Shift cached world-space state by <paramref name="delta"/>.
        /// The caller passes the amount the bubble moved; subtract it from any
        /// stored absolute/local-world position.</summary>
        void OnOriginShift(Vector3 delta);
    }
}
