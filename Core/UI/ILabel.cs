namespace V12.UI
{
    public interface ILabel : IWidget
    {
        string Text { get; set; }

        /// <summary>
        /// Layout hint: minimum width in pixels for this label, so a column
        /// of labels lines their fields up on one x instead of stepping with
        /// each label's text width. Labels longer than this keep their
        /// natural width. Defaults to 0 (natural text width).
        /// </summary>
        float MinWidth { get; set; }
    }
}