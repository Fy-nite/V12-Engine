using System;

namespace V12.UI
{
    public enum SplitterOrientation
    {
        /// <summary>Panes sit side by side; the divider is vertical.</summary>
        SideBySide,

        /// <summary>Panes stack; the divider is horizontal.</summary>
        Stacked
    }

    /// <summary>
    /// A two-pane container with a user-draggable divider.
    /// </summary>
    public interface ISplitter : IWidget
    {
        void SetFirst(IWidget child);
        void SetSecond(IWidget child);

        /// <summary>Share of the first pane along the split axis, 0..1.</summary>
        float SplitRatio { get; set; }

        /// <summary>Initialize the divider so one pane starts at its natural minimum size.</summary>
        void SetInitialMinimumPane(int paneIndex);

        void SetOnSplitChanged(Action<float> onSplitChanged);
    }
}
