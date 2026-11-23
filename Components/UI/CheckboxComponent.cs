using System;
using MongoDB.Bson.Serialization.Attributes;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.UI;

namespace V12.Components.UI
{
    [ComponentEntry("UI", "Checkbox", SingleInstance = true, SortOrder = 13, Icon = "square-check")]
    public class CheckboxComponent : ComponentBase
    {
        public override string Name => "Checkbox";
        public override string Description => "Checkbox control";

        public string Label { get; set; } = string.Empty;
        public bool Checked { get; set; }

        /// <summary>Named action used when no <see cref="OnChanged"/> delegate is set.</summary>
        public string Action { get; set; } = string.Empty;

        [BsonIgnore]
        public Action<bool>? OnChanged { get; set; }

        /// <summary>Applies a new state and raises the delegate or the named action.</summary>
        public void SetChecked(bool on)
        {
            Checked = on;
            MarkDirty();
            try
            {
                if (OnChanged != null)
                {
                    OnChanged(on);
                    return;
                }

                UiActions.Invoke(Action, new UiActionArgs(Owner, On: on));
            }
            catch { }

            GameRoot.Instance?.MarkRenderDirty();
        }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
