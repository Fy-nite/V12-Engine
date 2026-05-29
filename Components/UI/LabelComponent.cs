namespace V12.Components.UI
{
    /// <summary>
    /// A read-only text label. Frontends render the <see cref="Text"/> value as
    /// non-interactive text inside the owning element.
    /// </summary>
    public class LabelComponent : ComponentBase
    {
        public override string Name => "Label";
        public override string Description => "Non-interactive text label";

        private string _text = string.Empty;

        /// <summary>The text to display.</summary>
        public string Text
        {
            get => _text;
            set
            {
                if (_text != value)
                {
                    _text = value ?? string.Empty;
                    MarkDirty();
                }
            }
        }

        /// <summary>Optional font size hint (0 = use frontend default).</summary>
        public float FontSize { get; set; } = 0f;

        public LabelComponent() { }
        public LabelComponent(string text) { _text = text ?? string.Empty; }
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
