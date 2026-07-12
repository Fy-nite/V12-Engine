namespace V12.UI
{
    public interface IModal : IWidget
    {
        string Title { get; set; }
        string Content { get; set; }
        void Show();
        void Close();
    }
}