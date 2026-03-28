using System;
using V12.Core.Core.Interfaces;

namespace V12.Core.UI
{
    /// <summary>
    /// Abstraction for engine-side UI construction. Implementations produce UI
    /// as world elements with layout/widget components so any frontend that
    /// already syncs world elements gets UI rendering for free.
    /// </summary>
    public interface IUIBuilder
    {
        /// <summary>Root canvas element. Add all top-level UI children here.</summary>
        IWorldElement Root { get; }

        // ── Layout ────────────────────────────────────────────────────────────
        IWorldElement HLayout(IWorldElement parent, string name, float spacing = 4f, float padding = 0f);
        IWorldElement VLayout(IWorldElement parent, string name, float spacing = 4f, float padding = 0f);

        // ── Widgets ───────────────────────────────────────────────────────────
        IWorldElement Button(IWorldElement parent, string name, Action onClick);
        IWorldElement Label(IWorldElement parent, string name, string text);
        IWorldElement TextInput(IWorldElement parent, string name, string placeholder = "");
    }
}
