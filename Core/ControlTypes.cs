using System;
using System.Collections.Generic;
using System.Text;

namespace V12.Core
{
    [Flags]
    public enum InputMethods
    {
        None           = 0,
        Keyboard       = 1 << 0,
        Mouse          = 1 << 1,
        Gamepad        = 1 << 2,
        Touch          = 1 << 3,
        XRHandTracking = 1 << 4,
        XRController   = 1 << 5,

        // Convenience combinations
        Desktop = Keyboard | Mouse,
        XR      = XRController | XRHandTracking,

        /// <summary>
        /// Auto-detect at runtime: use XR if an interface is available, otherwise fall back to Desktop.
        /// Used by <see cref="V12.Components.PlayerComponent"/> and handled by UniversalPlayerController.
        /// </summary>
        Auto    = Desktop | XR,
    }
}