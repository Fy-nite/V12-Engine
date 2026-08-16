namespace V12.Core.UI
{
    /// <summary>
    /// The active transform-gizmo mode in an editor viewport. T = translate,
    /// R = rotate, S = scale. Defined in the core assembly so both the editor
    /// (V12.dll consumer) and the host renderer (which may be loaded twice)
    /// share one type.
    /// </summary>
    public enum GizmoMode
    {
        Translate,
        Rotate,
        Scale
    }
}
