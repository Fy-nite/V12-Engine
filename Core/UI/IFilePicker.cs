using System;

namespace V12.UI
{
    /// <summary>
    /// Native open/save file dialog. Transient by nature — not part of the
    /// widget tree. Exactly one result callback is armed per Show call; the
    /// argument is the chosen path, or null when the user cancels.
    /// </summary>
    public interface IFilePicker
    {
        /// <param name="extensions">Extensions without dots, e.g. "v12pak", "worldml". Empty = all files.</param>
        void ShowOpen(string title, string description, string[] extensions, Action<string?> onResult);

        /// <summary>Pick a directory rather than a file. The callback receives
        /// the chosen directory path, or null when the user cancels.</summary>
        void ShowFolder(string title, Action<string?> onResult);

        /// <param name="suggestedFileName">Suggested file name including extension; may be empty.</param>
        void ShowSave(string title, string description, string[] extensions, string suggestedFileName, Action<string?> onResult);

        /// <summary>Dismisses the dialog if it is open, reporting cancellation.</summary>
        void Cancel();
    }
}
