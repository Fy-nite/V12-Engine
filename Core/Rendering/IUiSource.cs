using System;
using System.Collections.Generic;
using V12.Core.Input;
using V12.Core.Rendering;
using V12.Core.UI.Paper;

namespace V12.Core.Rendering
{
    /// <summary>
    /// Backend-neutral UI source. The engine asks it to lay out world canvases
    /// and route canvas input. The implementation lives in the UI package
    /// (V12.Paper), so the engine and renderers never reference toolkit types.
    /// </summary>
    public interface IUiSource
    {
        /// <summary>
        /// Lays out every world canvas in the captured UI frame. Surfaces are
        /// created or refreshed here; nothing is stepped yet.
        /// </summary>
        void PrepareWorldCanvases(UIFrame frame);

        /// <summary>Steps every canvas surface. Call after input has been fed.</summary>
        void StepWorldCanvases();

        /// <summary>Draw batches per canvas element id, from the last step.</summary>
        IReadOnlyDictionary<long, PaperFrame> WorldCanvases { get; }

        /// <summary>Ids of every canvas that currently has a surface.</summary>
        IEnumerable<long> CanvasIds { get; }

        /// <summary>Feeds canvas-local pointer state. False when the canvas is unknown.</summary>
        bool FeedCanvasPointer(long canvasId, float x, float y, bool down);

        /// <summary>Routes a key or text event to a canvas. True when it carried UI input.</summary>
        bool FeedCanvasInput(long canvasId, InputEvent evt);

        /// <summary>True while the canvas still has a live surface.</summary>
        bool ContainsCanvas(long canvasId);
    }
}
