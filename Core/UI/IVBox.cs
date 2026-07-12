namespace V12.UI
{
    public interface IVBox : IWidget
    {
        void AddChild(IWidget child);
        void RemoveChild(IWidget child);
    }
}