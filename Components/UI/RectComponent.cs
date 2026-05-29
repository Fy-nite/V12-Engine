using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components.UI
{
    public class RectComponent : ComponentBase
    {
        public override string Name => "Rect";
        public override string Description => "Rectangular UI region";

        public float Width { get; set; }
        public float Height { get; set; }
        public string BackgroundColor { get; set; } = string.Empty;
        public float CornerRadius { get; set; }
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}

