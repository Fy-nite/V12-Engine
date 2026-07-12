namespace V12.UI
{
    public interface IHBox : IWidget
    {
        void AddChild(IWidget child);
        void RemoveChild(IWidget child);
    }
}