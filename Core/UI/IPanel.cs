namespace V12.UI
{
    public interface IPanel : IWidget
    {
        string Name { get; set; }
        bool UseScrollableContent { set; }
        void AddChild(IWidget child);
        void RemoveChild(IWidget child);
    }
}