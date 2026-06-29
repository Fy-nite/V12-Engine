using System;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    public enum PortalTeleportDirection
    {
        FrontAndBack,
        Front,
        Back
    }

    public enum PortalViewDirection
    {
        FrontAndBack,
        OnlyFront,
        OnlyBack
    }

    public class PortalComponent : ComponentBase
    {
        private float _width = 2f;
        private float _height = 2.5f;
        private long _exitPortalElementId;
        private bool _isActive = true;
        private bool _isTeleport = true;
        private float _viewportScale = 0.5f;
        private PortalTeleportDirection _teleportDirection = PortalTeleportDirection.FrontAndBack;
        private PortalViewDirection _viewDirection = PortalViewDirection.FrontAndBack;
        private float _teleportTolerance = 0.5f;

        public override string Name => "Portal";
        public override string Description => "3D portal that renders and teleports between linked portals";

        public float Width
        {
            get => _width;
            set { if (Math.Abs(_width - value) > 0.001f) { _width = Math.Max(0.1f, value); MarkDirty(); } }
        }

        public float Height
        {
            get => _height;
            set { if (Math.Abs(_height - value) > 0.001f) { _height = Math.Max(0.1f, value); MarkDirty(); } }
        }

        public long ExitPortalElementId
        {
            get => _exitPortalElementId;
            set { if (_exitPortalElementId != value) { _exitPortalElementId = value; MarkDirty(); } }
        }

        public bool IsActive
        {
            get => _isActive;
            set { if (_isActive != value) { _isActive = value; MarkDirty(); } }
        }

        public bool IsTeleport
        {
            get => _isTeleport;
            set { if (_isTeleport != value) { _isTeleport = value; MarkDirty(); } }
        }

        public float ViewportScale
        {
            get => _viewportScale;
            set { if (Math.Abs(_viewportScale - value) > 0.001f) { _viewportScale = Math.Clamp(value, 0.1f, 1f); MarkDirty(); } }
        }

        public PortalTeleportDirection TeleportDirection
        {
            get => _teleportDirection;
            set { if (_teleportDirection != value) { _teleportDirection = value; MarkDirty(); } }
        }

        public PortalViewDirection ViewDirection
        {
            get => _viewDirection;
            set { if (_viewDirection != value) { _viewDirection = value; MarkDirty(); } }
        }

        public float TeleportTolerance
        {
            get => _teleportTolerance;
            set { if (Math.Abs(_teleportTolerance - value) > 0.001f) { _teleportTolerance = Math.Max(0f, value); MarkDirty(); } }
        }

        public PortalComponent() { }

        public PortalComponent(long exitPortalElementId, float width = 2f, float height = 2.5f)
        {
            _exitPortalElementId = exitPortalElementId;
            _width = width;
            _height = height;
        }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }

        public override string ToString() =>
            $"Portal({Width:F1}x{Height:F1} Exit:{ExitPortalElementId} Active:{IsActive} Teleport:{IsTeleport})";
    }
}
