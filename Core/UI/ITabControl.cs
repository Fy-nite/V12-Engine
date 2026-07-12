namespace V12.UI
{
    public interface ITabControl : IWidget
    {
        void AddTab(string name, IWidget content);
        void RemoveTab(string name);
        int SelectedTabIndex { get; set; }
    }
}