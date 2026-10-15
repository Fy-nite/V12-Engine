namespace V12.Core.Input
{
    /// <summary>
    /// Engine-level physical keyboard key, mirroring the common keys of the host
    /// keyboards (names match the renderers' key enums so backends can map by name).
    /// Raw key edges are carried on <see cref="InputEvent.Key"/> so handlers and
    /// scripts can react to any key, not just the named action map.
    /// </summary>
    public enum InputKey
    {
        None = 0,

        // Letters
        A, B, C, D, E, F, G, H, I, J, K, L, M,
        N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

        // Top-row digits
        D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,

        // Function keys
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
        F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,

        // Numpad
        NumPad0, NumPad1, NumPad2, NumPad3, NumPad4,
        NumPad5, NumPad6, NumPad7, NumPad8, NumPad9,
        Add, Subtract, Multiply, Divide, Decimal, NumLock,

        // Whitespace / editing
        Space, Enter, Escape, Tab, Back, CapsLock, Delete, Insert,

        // Navigation
        Home, End, PageUp, PageDown, Up, Down, Left, Right,

        // Modifiers
        LeftShift, RightShift, LeftControl, RightControl,
        LeftAlt, RightAlt, LeftWindows, RightWindows,

        // System / lock
        PrintScreen, Scroll, Pause,

        // OEM punctuation
        OemSemicolon, OemPlus, OemComma, OemMinus, OemPeriod, OemQuestion,
        OemTilde, OemOpenBrackets, OemPipe, OemCloseBrackets, OemQuotes,
        OemBackslash,
    }
}
