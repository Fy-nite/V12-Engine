namespace V12.UI
{
    public interface IDock : IWidget
    {
        string Title { get; set; }
        void AddChild(IWidget child);
        void RemoveChild(IWidget child);
    }
}