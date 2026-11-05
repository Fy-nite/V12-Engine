using System;

namespace V12.UI
{
    /// <summary>
    /// A numeric text field that also supports drag-to-scrub adjustment.
    /// <see cref="SetOnChanged"/> fires only when the parsed value actually
    /// changes (valid commit, focus loss, or drag step) — never for partial
    /// or unparseable input.
    /// </summary>
    public interface INumericField : IWidget
    {
        float Value { get; set; }

        /// <summary>.NET numeric format for display, e.g. "F2", "F4", "0.###".</summary>
        string Format { get; set; }

        bool Enabled { get; set; }

        void SetOnChanged(Action<float> onChanged);
    }

    /// <summary>
    /// Optional numeric-field extension for inspector axis rows. A field that
    /// implements this can render its own scrub handle (<see cref="ScrubLabel"/>)
    /// and snap while Control is held (<see cref="SnapStep"/>). Providers that
    /// do not support it keep the inspector's external label instead.
    /// </summary>
    public interface IScrubbableNumericField : INumericField
    {
        /// <summary>Axis/role tag drawn on the field's drag handle (e.g. "X").</summary>
        string ScrubLabel { get; set; }

        /// <summary>Value increment applied while Control is held; 0 disables snapping.</summary>
        float SnapStep { get; set; }

        /// <summary>True when the field draws <see cref="ScrubLabel"/> itself.</summary>
        bool RendersScrubLabel { get; }
    }
}
