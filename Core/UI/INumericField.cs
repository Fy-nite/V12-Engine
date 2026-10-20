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
}
