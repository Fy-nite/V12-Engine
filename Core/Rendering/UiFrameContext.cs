using V12.Core.UI.Paper;
using V12.Rendering;

namespace V12.Core.Rendering;

/// <summary>
/// Screen-space Paper UI input to the render-frame producer. The target is a
/// transparent render target that the backend presents as an overlay.
/// </summary>
public readonly record struct UiFrameContext(
    PaperFrame Frame,
    RenderTargetHandle Target,
    long ViewId,
    int Width,
    int Height);
