namespace V12.Components.UI
{
    public class ProgressBarComponent : ComponentBase
    {
        public override string Name => "ProgressBar";
        public override string Description => "Progress bar";

        public float Value { get; set; }
        public bool Indeterminate { get; set; }
    }
}

