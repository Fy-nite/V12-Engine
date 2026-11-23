using System;
using MongoDB.Bson.Serialization.Attributes;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Networking;
using V12.Core.UI;

namespace V12.Components.UI
{
    /// <summary>
    /// A clickable button UI element. Frontends render this as a pressable widget.
    /// The <see cref="OnClick"/> delegate is invoked when the user activates the button.
    /// </summary>
    [ComponentEntry("UI", "Button", SingleInstance = true, SortOrder = 11, Icon = "hand-pointer")]
    public class ButtonComponent : ComponentBase
    {
        public override string Name => "Button";
        public override string Description => "Clickable button";

        /// <summary>Visible text on the button face.</summary>
        public string Label { get; set; } = "Button";

        /// <summary>
        /// Named action invoked when the button is clicked and no
        /// <see cref="OnClick"/> delegate is attached. Register the handler with
        /// <c>UiActions.Register("name", ...)</c>. This is what lets WorldML wire
        /// a button with no C# closure.
        /// </summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>Called by the frontend when the button is clicked / activated.
        /// A code reference, never serialized — clicks are forwarded as RPCs.</summary>
        [BsonIgnore]
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
            try
            {
                if (OnClick != null)
                    OnClick();
                else
                    UiActions.Invoke(Action, new UiActionArgs(Owner));
            }
            catch { }

            // A click almost always changes something visible.
            GameRoot.Instance?.MarkRenderDirty();
        }

        /// <summary>
        /// Remote-callable press handler: the WorldCanvasSystem invokes this locally and
        /// also broadcasts it, so every peer with a matching element runs the same handler.
        /// </summary>
        [Remote]
        public void Press() => InvokeClick();
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
