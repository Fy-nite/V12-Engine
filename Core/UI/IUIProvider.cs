using System;
using System.Collections.Generic;

namespace V12.UI
{
    /// <summary>
    /// Abstraction over the underlying UI framework (Godot, Unity, custom, etc.).
    /// </summary>
    public interface IUIProvider
    {
        // --- Window management ---
        IWindow CreateWindow(string title, WindowOptions options);

        string Palletname { get; set; }
        void SetPallet(string name);
        public  IReadOnlyList<string> PalletNames { get; }


        // --- Viewport hosting ---
        IViewportHost CreateViewportHost();

        // --- Basic controls ---
        IButton CreateButton(string text, Action onClick);
        ILabel CreateLabel(string text);
        ITextField CreateTextField(string placeholder, Action<string> onChanged);
        IDropdown CreateDropdown(IEnumerable<string> options, Action<int> onSelected);

        // --- Layout containers ---
        IVBox CreateVBox();
        IHBox CreateHBox();
        IScrollView CreateScrollView();

        // --- Panels and docks ---
        IDock CreateDock(string title, DockPosition position);
        IPanel CreatePanel(string name);
        ITabControl CreateTabControl();

        // --- Toolbar ---
        IToolbar CreateToolbar(string name);

        // --- Modal dialogs ---
        IModal ShowModal(string title, string content);

        // --- Editor-grade controls ---
        ITreeView CreateTreeView();
        IAssetGrid CreateAssetGrid();
        ICheckbox CreateCheckbox(string text, bool value);
        IToggle CreateToggle(string text, bool toggled);
        ISlider CreateSlider(float min, float max, float step, float value);
        INumericField CreateNumericField(float value, string format = "F2");
        ISplitter CreateSplitter(SplitterOrientation orientation);
        IContextMenu CreateContextMenu();

        // --- Native file dialogs ---
        IFilePicker CreateFilePicker();

        // --- Theming ---
        /// <summary>
        /// Apply a named theme variation to the widget's native control (e.g.
        /// "SectionLabel", "ComponentCard", "DangerButton"). Variations are
        /// host-defined styling hooks; a host that does not know the name
        /// leaves the widget on its default styling.
        /// </summary>
        void ApplyVariation(IWidget widget, string variation);

        // --- Root window ---
        IWindow RootWindow { get; }

        // --- Frame processing ---
        void ProcessFrame(double delta);
    }

    public enum DockPosition
    {
        Left,
        Right,
        Top,
        Bottom,
        Center
    }

    public class WindowOptions
    {
        public int Width { get; set; } = 800;
        public int Height { get; set; } = 600;
        public bool Resizable { get; set; } = true;
        public bool Borderless { get; set; } = false;
    }
}
