namespace V12.UI
{
    public interface IPanel : IWidget
    {
        string Name { get; set; }
        void AddChild(IWidget child);
        void RemoveChild(IWidget child);
    }
}