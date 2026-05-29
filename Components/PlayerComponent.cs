using System;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>
    /// Unified player component that covers both desktop (keyboard+mouse) and VR (XR) input in a
    /// single component. At runtime the paired <c>UniversalPlayerController</c> auto-detects
    /// whether an XR interface is available and selects the appropriate mode, unless
    /// <see cref="PreferredInputMethod"/> is pinned to a specific value.
    /// </summary>
    public class PlayerComponent : ComponentBase, IPlayerControlComponent
    {
        private bool         _isLocalControlled    = true;
        private InputMethods _preferredInputMethod = InputMethods.Auto;

        // ── Shared movement settings ──────────────────────────────────────────
        private float _moveSpeed        = 4f;   // m/s base walk speed
        private float _sprintMultiplier = 1.9f;
        private float _jumpStrength     = 6f;   // impulse applied on jump

        // ── Desktop-specific settings ─────────────────────────────────────────
        private float _lookSensitivity  = 1.2f; // mouse-delta multiplier
        private bool  _canJump          = true;

        // ── VR-specific settings ──────────────────────────────────────────────
        private bool  _enableHandTracking   = true;
        private float _vrMoveSpeed          = 3f;  // thumbstick locomotion speed
        private bool  _vrSmoothLocomotion   = true; // false = teleport only

        // ─────────────────────────────────────────────────────────────────────

        public override string Name        => "Player";
        public override string Description => "Unified VR/desktop player settings";

        // ── IPlayerControlComponent ───────────────────────────────────────────

        /// <summary>
        /// Always <see cref="InputMethods.Auto"/> so that the factory routes this component
        /// to <c>UniversalPlayerController</c>. Override at runtime via
        /// <see cref="PreferredInputMethod"/> to force a specific mode.
        /// </summary>
        public InputMethods RequiredInputMethod => InputMethods.Auto;

        public bool IsLocalControlled
        {
            get => _isLocalControlled;
            set { if (_isLocalControlled != value) { _isLocalControlled = value; MarkDirty(); } }
        }

        // ── Mode preference ───────────────────────────────────────────────────

        /// <summary>
        /// Hint to the controller about which input mode to prefer.
        /// <list type="bullet">
        ///   <item><see cref="InputMethods.Auto"/> – detect at startup; use XR if available, Desktop otherwise.</item>
        ///   <item><see cref="InputMethods.Desktop"/> – always use keyboard+mouse even if a headset is present.</item>
        ///   <item><see cref="InputMethods.XR"/> – require XR; log a warning and fall back to Desktop if unavailable.</item>
        /// </list>
        /// </summary>
        public InputMethods PreferredInputMethod
        {
            get => _preferredInputMethod;
            set { if (_preferredInputMethod != value) { _preferredInputMethod = value; MarkDirty(); } }
        }

        // ── Shared ────────────────────────────────────────────────────────────

        /// <summary>Base movement speed in world units per second (desktop walk / VR thumbstick).</summary>
        public float MoveSpeed
        {
            get => _moveSpeed;
            set { if (Math.Abs(_moveSpeed - value) > 0.0001f) { _moveSpeed = MathF.Max(0f, value); MarkDirty(); } }
        }

        /// <summary>Scalar applied to <see cref="MoveSpeed"/> while the sprint action is held (desktop only).</summary>
        public float SprintMultiplier
        {
            get => _sprintMultiplier;
            set { if (Math.Abs(_sprintMultiplier - value) > 0.0001f) { _sprintMultiplier = MathF.Max(1f, value); MarkDirty(); } }
        }

        /// <summary>Upward impulse applied when the jump action fires.</summary>
        public float JumpStrength
        {
            get => _jumpStrength;
            set { if (Math.Abs(_jumpStrength - value) > 0.0001f) { _jumpStrength = MathF.Max(0f, value); MarkDirty(); } }
        }

        // ── Desktop ───────────────────────────────────────────────────────────

        /// <summary>Mouse-delta multiplier for look rotation (desktop only).</summary>
        public float LookSensitivity
        {
            get => _lookSensitivity;
            set { if (Math.Abs(_lookSensitivity - value) > 0.0001f) { _lookSensitivity = MathF.Max(0f, value); MarkDirty(); } }
        }

        /// <summary>Whether the player is allowed to jump (desktop only).</summary>
        public bool CanJump
        {
            get => _canJump;
            set { if (_canJump != value) { _canJump = value; MarkDirty(); } }
        }

        // ── VR ────────────────────────────────────────────────────────────────

        /// <summary>Whether to add XRController3D hand nodes to the XR rig.</summary>
        public bool EnableHandTracking
        {
            get => _enableHandTracking;
            set { if (_enableHandTracking != value) { _enableHandTracking = value; MarkDirty(); } }
        }

        /// <summary>Thumbstick locomotion speed in VR mode (overrides <see cref="MoveSpeed"/> for VR).</summary>
        public float VRMoveSpeed
        {
            get => _vrMoveSpeed;
            set { if (Math.Abs(_vrMoveSpeed - value) > 0.0001f) { _vrMoveSpeed = MathF.Max(0f, value); MarkDirty(); } }
        }

        /// <summary>
        /// If <c>true</c> the left thumbstick provides smooth locomotion in VR.
        /// If <c>false</c> only teleport locomotion is active (stub — implement teleport in the controller).
        /// </summary>
        public bool VRSmoothLocomotion
        {
            get => _vrSmoothLocomotion;
            set { if (_vrSmoothLocomotion != value) { _vrSmoothLocomotion = value; MarkDirty(); } }
        }

        // ── Constructors ──────────────────────────────────────────────────────

        public PlayerComponent() { }

        public PlayerComponent(
            InputMethods preferredMethod  = InputMethods.Auto,
            float        moveSpeed        = 4f,
            float        sprintMultiplier = 1.9f,
            bool         isLocalControlled = true)
        {
            _preferredInputMethod = preferredMethod;
            _moveSpeed            = MathF.Max(0f, moveSpeed);
            _sprintMultiplier     = MathF.Max(1f, sprintMultiplier);
            _isLocalControlled    = isLocalControlled;
        }
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        public override string ToString() =>
            $"Player(Preferred:{PreferredInputMethod} Local:{IsLocalControlled} " +
            $"Move:{MoveSpeed:F2} Sprint:{SprintMultiplier:F2} Jump:{JumpStrength:F2})";
    }
}
