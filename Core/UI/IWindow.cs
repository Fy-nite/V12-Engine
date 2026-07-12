namespace V12.UI
{
    /// <summary>
    /// Represents a window in the UI system.
    /// </summary>
    public interface IWindow : IWidget
    {
        string Title { get; set; }
        int Width { get; set; }
        int Height { get; set; }
        bool Visible { get; set; }
        void Close();
    }
}