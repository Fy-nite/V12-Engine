using V12.Core.Interfaces.Renderer;

namespace V12.Core.Interfaces
{
    public interface IRenderer
    {
        /// <summary>
        /// Returns Renderer FPS
        /// </summary>
        /// <returns>The Active renderer's FPS. allbeit will return multiple based on the renderer.</returns>
        int GetFPS();
        
        RendererInfo GetAllInfo();
        void QueueItem(IRenderable item);
        void RemoveItem(IRenderable item);
        int GetScreenWidth();
        int GetScreenHeight();
        void step();

    }
}
