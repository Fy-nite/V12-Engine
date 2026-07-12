namespace V12.UI
{
    public interface IToolbar : IWidget
    {
        void AddItem(IWidget item);
        void RemoveItem(IWidget item);
    }
}