namespace V12.Rendering
{
    /// <summary>
    /// A rectangular surface that V12 can render into.
    /// The backend owns the concrete implementation (e.g., a Godot SubViewport).
    /// </summary>
    public interface IRenderTarget
    {
        int Width { get; }
        int Height { get; }
        void Resize(int width, int height);

        /// <summary>
        /// Opaque handle for the UI provider to embed this target.
        /// For Godot this will be a SubViewport or SubViewportContainer.
        /// </summary>
        object NativeHandle { get; }
    }
}