namespace V12.Components.UI
{
    public class IconComponent : ComponentBase
    {
        public override string Name => "Icon";
        public override string Description => "Icon glyph";

        public string Icon { get; set; } = string.Empty;
        public float Size { get; set; } = 16f;
    }
}

