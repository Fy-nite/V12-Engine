using System;
using V12.Core.Interfaces.Renderer;
using V12.Core.Rendering;

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
        [Obsolete("use QueueItems instead of QueueItem for better performance")]
        void QueueItem(IRenderable item);
        [Obsolete("use RemoveItems instead of RemoveItem for better performance")]
        void RemoveItem(IRenderable item);

        void QueueItems(RenderPacket packet);
        void RemoveItems(RenderPacket packet);
        int GetScreenWidth();
        int GetScreenHeight();
        void step();

        /// <summary>
        /// Process a frame snapshot produced by <see cref="GameRoot.CaptureFrame"/>.
        /// Called on the Godot main thread; creates/updates/destroys scene nodes.
        /// </summary>
        void ApplySnapshot(FrameSnapshot snapshot);

    }
}
