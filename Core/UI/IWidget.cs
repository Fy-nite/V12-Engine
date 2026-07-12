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
    }
}