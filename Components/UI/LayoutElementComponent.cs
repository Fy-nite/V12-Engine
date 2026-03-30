
namespace V12.Components.UI;

public class LayoutElementComponent: ComponentBase
{
    public override string Name => "LayoutElement";
    public override string Description => "Layout sizing hints for layout systems";

    // Width hints (negative = unspecified)
    public float MinWidth { get; set; } = -1f;
    public float PreferredWidth { get; set; } = -1f;
    public float FlexibleWidth { get; set; } = 0f;

    // Height hints (negative = unspecified)
    public float MinHeight { get; set; } = -1f;
    public float PreferredHeight { get; set; } = -1f;
    public float FlexibleHeight { get; set; } = 0f;
}