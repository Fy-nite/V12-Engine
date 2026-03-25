using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>
    /// Marker component that indicates this element is a player spawn point.
    /// Use a <see cref="TransformComponent"/> on the same element to set the spawn position.
    /// </summary>
    public class SpawnPointComponent : ComponentBase
    {
        public override string Name => "SpawnPoint";
        public override string Description => "Marks this element as a player spawn location";

        public SpawnPointComponent() { }
    }
}
