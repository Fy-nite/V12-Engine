namespace V12.Core.Input
{
    /// <summary>
    /// Engine-level gamepad button. Names follow the common SDL/Xbox layout so a
    /// host can map its own enum by name, and games can bind actions to a
    /// physical button instead of relying on host-defined named actions.
    /// </summary>
    public enum GamepadButton
    {
        None = 0,

        A, B, X, Y,

        Back, Guide, Start,

        LeftStick, RightStick,

        LeftShoulder, RightShoulder,

        DpadUp, DpadDown, DpadLeft, DpadRight,

        Misc1, Misc2, Misc3, Misc4, Misc5, Misc6,

        Paddle1, Paddle2, Paddle3, Paddle4,

        Touchpad,
    }

    /// <summary>
    /// Engine-level gamepad axis. Trigger axes read <c>0..1</c>; stick axes read
    /// <c>-1..1</c>.
    /// </summary>
    public enum GamepadAxis
    {
        LeftX,
        LeftY,
        RightX,
        RightY,
        TriggerLeft,
        TriggerRight,
    }
}
