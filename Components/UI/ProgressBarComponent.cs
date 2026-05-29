using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components.UI
{
    public class ProgressBarComponent : ComponentBase
    {
        public override string Name => "ProgressBar";
        public override string Description => "Progress bar";

        public float Value { get; set; }
        public bool Indeterminate { get; set; }
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}

