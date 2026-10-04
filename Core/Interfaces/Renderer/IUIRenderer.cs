using V12.Core.Rendering;

namespace V12.Core.Interfaces
{
    /// <summary>
    /// A UI renderer backend. Unlike <see cref="IRenderer"/> (which receives meshes via
    /// <c>RenderPacket</c>), V12 <b>hands</b> the UI over: it captures the current UI tree —
    /// screen-space <c>CanvasComponent</c> subtrees and their widget components — into a
    /// <see cref="UIFrame"/> and calls <see cref="ApplyUI"/>. The backend reconciles its own
    /// retained UI (by element id) rather than walking the world tree itself.
    ///
    /// Register an implementation under the name <c>"IUIRenderer"</c> before
    /// <c>GameRoot.Initialize()</c> (mirroring <c>"IRenderer"</c>).
    /// </summary>
    public interface IUIRenderer
    {
        /// <summary>
        /// Process the UI description V12 captured for this frame. Called on the render/main
        /// thread, once per dirty frame. Implementations should create/update/destroy their
        /// controls keyed by <see cref="UINode.Id"/>.
        /// </summary>
        void ApplyUI(UIFrame frame);

        /// <summary>Per-frame tick, mirroring <see cref="IRenderer.step"/>.</summary>
        void step();
    }
}
