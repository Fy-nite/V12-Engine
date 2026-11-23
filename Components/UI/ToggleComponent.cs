using System;
using MongoDB.Bson.Serialization.Attributes;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.UI;

namespace V12.Components.UI
{
    [ComponentEntry("UI", "Toggle", SingleInstance = true, SortOrder = 12, Icon = "toggle-on")]
    public class ToggleComponent : ComponentBase
    {
        public override string Name => "Toggle";
        public override string Description => "Toggle control";

        public string Label { get; set; } = string.Empty;
        public bool IsOn { get; set; }

        /// <summary>Named action used when no <see cref="OnToggled"/> delegate is set.</summary>
        public string Action { get; set; } = string.Empty;

        [BsonIgnore]
        public Action<bool>? OnToggled { get; set; }

        /// <summary>
        /// Applies a new state, keeps the component in sync, and raises the
        /// delegate or the named action. Renderers call this instead of only
        /// flipping their own copy, so the state survives the next capture.
        /// </summary>
        public void SetToggled(bool on)
        {
            IsOn = on;
            MarkDirty();
            try
            {
                if (OnToggled != null)
                {
                    OnToggled(on);
                    return;
                }

                UiActions.Invoke(Action, new UiActionArgs(Owner, On: on));
            }
            catch { }

            // The widget's own state changed; repaint without waiting for input.
            GameRoot.Instance?.MarkRenderDirty();
        }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}

