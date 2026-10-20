using System.Collections.Generic;
using System.Numerics;

namespace V12.Core.Input
{
    /// <summary>
    /// Generic per-frame input state from any source (host renderer, OS input system).
    /// No editor-specific keybindings — just raw state. The editor (or game) maps
    /// this to its own actions.
    /// </summary>
    public interface IInputSource
    {
        InputSnapshot GetSnapshot();
    }

    /// <summary>
    /// Raw input state for one frame, in window coordinates.
    /// Consumers (editor, game, UI) interpret these keys/buttons as needed.
    /// </summary>
    public struct InputSnapshot
    {
        /// <summary>Mouse position in window/screen space.</summary>
        public Vector2 MousePosition;

        /// <summary>Primary buttons.</summary>
        public bool LeftDown;
        public bool RightDown;
        public bool MiddleDown;

        /// <summary>Absolute wheel value (increases on scroll-up).
        /// Consumers derive per-frame deltas.</summary>
        public int Wheel;

        /// <summary>Pressed keys as a set of <see cref="InputKeys"/> codes
        /// (fast lookup).</summary>
        public HashSet<int> PressedKeys;

        /// <summary>Window has input focus.</summary>
        public bool WindowActive;

        /// <summary>Host has mouse captured (hidden + confined).</summary>
        public bool MouseLocked;
    }

    /// <summary>
    /// Renderer-agnostic key codes for <see cref="InputSnapshot.PressedKeys"/>.
    /// Letters and digits use their ASCII uppercase values ('A'..'Z', '0'..'9')
    /// so most hosts map their native codes straight through; named keys use a
    /// small distinct range that cannot collide with ASCII. Hosts translate
    /// platform-specific keys into these codes when filling a snapshot.
    /// </summary>
    public static class InputKeys
    {
        // Letters (ASCII 'A'..'Z').
        public const int A = 'A'; public const int B = 'B'; public const int C = 'C';
        public const int D = 'D'; public const int E = 'E'; public const int F = 'F';
        public const int G = 'G'; public const int H = 'H'; public const int I = 'I';
        public const int J = 'J'; public const int K = 'K'; public const int L = 'L';
        public const int M = 'M'; public const int N = 'N'; public const int O = 'O';
        public const int P = 'P'; public const int Q = 'Q'; public const int R = 'R';
        public const int S = 'S'; public const int T = 'T'; public const int U = 'U';
        public const int V = 'V'; public const int W = 'W'; public const int X = 'X';
        public const int Y = 'Y'; public const int Z = 'Z';

        // Digits (ASCII '0'..'9').
        public const int D0 = '0'; public const int D1 = '1'; public const int D2 = '2';
        public const int D3 = '3'; public const int D4 = '4'; public const int D5 = '5';
        public const int D6 = '6'; public const int D7 = '7'; public const int D8 = '8';
        public const int D9 = '9';

        // Named keys (distinct range, no ASCII collision).
        public const int Escape = 0x100001;
        public const int Enter = 0x100002;
        public const int Tab = 0x100003;
        public const int Space = 0x100004;
        public const int Backspace = 0x100005;
        public const int Shift = 0x100006;
        public const int Ctrl = 0x100007;
        public const int Alt = 0x100008;
        public const int Left = 0x100009;
        public const int Right = 0x10000A;
        public const int Up = 0x10000B;
        public const int Down = 0x10000C;
    }
}
