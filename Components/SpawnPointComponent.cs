using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.UI;

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

        /// <summary>Marker component: no editable fields (position lives on the
        /// element's transform).</summary>
        public override void BuildInspector(IInspector inspector)
        {
            inspector.Section("Spawn Point");
            inspector.HelpBox("Player spawn location. Use the transform to set where players appear.");
        }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        public SpawnPointComponent() { }
    }
}
