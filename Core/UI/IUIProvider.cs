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