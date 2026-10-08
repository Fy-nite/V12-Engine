using System.Numerics;

namespace V12.Core.UI
{
    /// <summary>
    /// Per-frame editor input provided by the host (MonoGame polls
    /// mouse/keyboard, other hosts map their own devices). The service tracks
    /// previous snapshots itself for edge detection.
    /// </summary>
    public interface IEditorInputSource
    {
        EditorInputSnapshot GetSnapshot();
    }

    /// <summary>Raw editor input state for one frame, in window coordinates.</summary>
    public struct EditorInputSnapshot
    {
        /// <summary>Mouse position in window (backbuffer) coordinates.</summary>
        public Vector2 MousePosition;

        public bool LeftDown;
        public bool RightDown;

        /// <summary>Absolute wheel value (increases on scroll-up); the service
        /// derives per-frame deltas.</summary>
        public int Wheel;

        public bool KeyT;
        public bool KeyR;
        public bool KeyS;
        public bool KeyEscape;

        /// <summary>Window has focus.</summary>
        public bool WindowActive;

        /// <summary>Host has the mouse captured (hidden + confined).</summary>
        public bool MouseLocked;
    }
}
