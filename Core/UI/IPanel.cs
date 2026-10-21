namespace V12.UI
{
    public interface IPanel : IWidget
    {
        string Name { get; set; }
        bool UseScrollableContent { set; }

        /// <summary>Floor width in pixels the panel asks its parent container
        /// to honor (0 = no floor). Keeps a sidebar pane from being squeezed
        /// below the width its content needs.</summary>
        float MinWidth { get; set; }

        void AddChild(IWidget child);
        void RemoveChild(IWidget child);
    }
}