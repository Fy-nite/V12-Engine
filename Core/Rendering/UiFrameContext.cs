using V12.Core.UI.Paper;

namespace V12.Core.Rendering;

/// <summary>
/// UI input to the render-frame producer. Both halves are engine-captured and
/// immutable: <see cref="Captured"/> is the UI model (world-canvas layout source)
/// and <see cref="Screen"/> is the laid-out screen-space Paper output. The
/// producer owns UI views and the optional presentation target, so hosts never
/// implement UI layout or UI targets.
/// </summary>
public readonly record struct UiFrameContext(
    UIFrame Captured,
    PaperFrame Screen,
    int Width,
    int Height);
