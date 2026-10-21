namespace V12.UI
{
    public interface IHBox : IWidget
    {
        void AddChild(IWidget child);
        void RemoveChild(IWidget child);

        /// <summary>
        /// Adds a child and flags it to absorb this box's spare width on the
        /// horizontal axis (e.g. a full-width header button sitting next to a
        /// right-aligned action that should hug).
        /// </summary>
        void AddChild(IWidget child, bool expand);

        /// <summary>
        /// Layout hint: when false the host keeps this box at its minimum
        /// content size on the parent container's main axis instead of
        /// expanding it to absorb spare space. Defaults to true.
        /// </summary>
        bool Expand { get; set; }
    }
}