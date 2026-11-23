using System;
using MongoDB.Bson.Serialization.Attributes;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.UI;

namespace V12.Components.UI
{
    [ComponentEntry("UI", "Slider", SingleInstance = true, SortOrder = 14, Icon = "sliders")]
    public class SliderComponent : ComponentBase
    {
        public override string Name => "Slider";
        public override string Description => "Slider control";

        public float Value { get; set; }
        public float Min { get; set; } = 0f;
        public float Max { get; set; } = 1f;
        public float Step { get; set; } = 0f;

        /// <summary>Optional label shown before the value (e.g. "Power").</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Named action used when no <see cref="OnChanged"/> delegate is set.</summary>
        public string Action { get; set; } = string.Empty;

        [BsonIgnore]
        public Action<float>? OnChanged { get; set; }

        /// <summary>
        /// Clamps to the authored range (and step), stores the value, and raises
        /// the delegate or the named action.
        /// </summary>
        public void SetValue(float value)
        {
            if (Max > Min) value = Math.Clamp(value, Min, Max);
            if (Step > 0f && Max > Min)
                value = Math.Clamp(Min + MathF.Round((value - Min) / Step) * Step, Min, Max);

            Value = value;
            MarkDirty();
            try
            {
                if (OnChanged != null)
                {
                    OnChanged(value);
                    return;
                }

                UiActions.Invoke(Action, new UiActionArgs(Owner, Value: value));
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
