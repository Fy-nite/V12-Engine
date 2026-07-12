namespace V12.Rendering
{
    /// <summary>
    /// Factory for creating render targets.
    /// </summary>
    public interface IRenderTargetFactory
    {
        IRenderTarget Create(int width, int height);
    }
}