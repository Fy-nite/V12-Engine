using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components.UI
{
    [ComponentEntry("UI", "Icon component", SingleInstance = true)]
    public class IconComponent : ComponentBase
    {
        public override string Name => "Icon";
        public override string Description => "Icon glyph";

        public string Icon { get; set; } = string.Empty;
        public float Size { get; set; } = 16f;
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}

