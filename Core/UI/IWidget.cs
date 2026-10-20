namespace V12.UI
{
    /// <summary>
    /// Base interface for all UI widgets.
    /// </summary>
    public interface IWidget
    {
        /// <summary>
        /// Gets the underlying native control (e.g., Godot Control, Unity UI element).
        /// </summary>
        object NativeControl { get; }

        /// <summary>
        /// Hides or shows the widget. A hidden widget stays in the tree but is
        /// not rendered and does not take input.
        /// </summary>
        bool Visible { get; set; }
    }
}