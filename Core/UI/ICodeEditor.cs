using System;

namespace V12.UI
{
    /// <summary>
    /// An editable code surface backed by the host's text editor (Origami's
    /// CodeEditor in the Paper provider, a Godot CodeEdit in the fallback).
    /// </summary>
    public interface ICodeEditor : IWidget
    {
        string Text { get; set; }

        /// <summary>Path shown in the editor chrome (used for diagnostics/title).</summary>
        string FilePath { get; set; }

        bool ReadOnly { get; set; }

        /// <summary>Language id: "csharp", "lua" or "plain".</summary>
        void SetLanguage(string languageId);

        /// <summary>
        /// Installs a compiler-backed language (Origami <c>CodeLanguage</c>) so the
        /// editor gets diagnostics, completion, hover and go-to-definition.
        /// </summary>
        void SetCompilerLanguage(global::Prowl.OrigamiUI.CodeLanguage language);

        /// <summary>Raised after every edit with the full document text.</summary>
        void SetOnChanged(Action<string> onChanged);

        /// <summary>Raised when the user presses the editor's save shortcut (Ctrl+S).</summary>
        void SetOnSaveRequested(Action onSave);

        void Focus();
    }
}
