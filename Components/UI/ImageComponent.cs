using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components.UI
{
    public class ImageComponent : ComponentBase
    {
        public override string Name => "Image";
        public override string Description => "Renderable image";

        public string Source { get; set; } = string.Empty;
        public bool PreserveAspect { get; set; } = true;
        public string Tint { get; set; } = string.Empty;
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}

