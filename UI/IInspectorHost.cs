namespace V12.UI
{
    /// <summary>
    /// Optional capability for a UI provider: host a component's inspector using the
    /// same Paper/Origami adapter the engine uses at runtime, so the Studio inspector
    /// and an in-game inspector look and behave identically.
    /// </summary>
    public interface IInspectorHost
    {
        /// <summary>Build a widget that renders <paramref name="target"/>'s inspector
        /// (authored <c>BuildInspector</c>, or auto-generated rows when absent).</summary>
        IWidget CreateInspectorView(object target, string? projectDir = null);
    }
}
