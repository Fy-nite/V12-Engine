using System;

namespace V12.Components.UI
{
    /// <summary>
    /// A clickable button UI element. Frontends render this as a pressable widget.
    /// The <see cref="OnClick"/> delegate is invoked when the user activates the button.
    /// </summary>
    public class ButtonComponent : ComponentBase
    {
        public override string Name => "Button";
        public override string Description => "Clickable button";

        /// <summary>Visible text on the button face.</summary>
        public string Label { get; set; } = "Button";

        /// <summary>Called by the frontend when the button is clicked / activated.</summary>
        public Action OnClick { get; set; }

        public ButtonComponent() { }

        public ButtonComponent(string label, Action onClick = null)
        {
            Label = label ?? "Button";
            OnClick = onClick;
        }

        /// <summary>Invoke the click handler safely.</summary>
        public void InvokeClick()
        {
            try { OnClick?.Invoke(); } catch { }
        }
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
