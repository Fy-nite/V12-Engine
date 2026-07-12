using V12.Rendering;

namespace V12.UI
{
    /// <summary>
    /// A UI widget that can display a render target (game viewport).
    /// </summary>
    public interface IViewportHost : IWidget
    {
        /// <summary>
        /// Sets the render target to display.
        /// </summary>
        void SetRenderTarget(IRenderTarget target);

        /// <summary>
        /// Clears the current render target.
        /// </summary>
        void ClearRenderTarget();
    }
}