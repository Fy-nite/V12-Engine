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

        /// <summary>Sets the uniform scale of the root canvas.</summary>
        void SetScale(float scale);

        // ── Layout ────────────────────────────────────────────────────────────
        IWorldElement HLayout(IWorldElement parent, string name, float spacing = 4f, float padding = 0f);
        IWorldElement VLayout(IWorldElement parent, string name, float spacing = 4f, float padding = 0f);

        // ── Widgets ───────────────────────────────────────────────────────────
        IWorldElement Button(IWorldElement parent, string name, Action onClick);
        IWorldElement Label(IWorldElement parent, string name, string text);
        IWorldElement TextInput(IWorldElement parent, string name, string placeholder = "");
        IWorldElement Rect(IWorldElement parent, string name, float width = 0f, float height = 0f, string backgroundColor = "");
        IWorldElement Image(IWorldElement parent, string name, string source, bool preserveAspect = true);
        IWorldElement Toggle(IWorldElement parent, string name, string label, bool initialState, Action<bool> onToggled);
        IWorldElement Checkbox(IWorldElement parent, string name, string label, bool initialState, Action<bool> onChanged);
        IWorldElement Slider(IWorldElement parent, string name, float min, float max, float value, Action<float> onChanged);
        IWorldElement ProgressBar(IWorldElement parent, string name, float value, bool indeterminate = false);
        IWorldElement Icon(IWorldElement parent, string name, string iconId, float size = 16f);
        IWorldElement InputField(IWorldElement parent, string name, string placeholder = "");
    }
}
