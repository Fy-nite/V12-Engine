using System;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>
    /// Core interaction component that records raycast/laser hit state and selection input.
    /// Godot/Unity glue components can update this component to provide a generic interaction
    /// surface for systems (UI, selection, etc.).
    /// </summary>
    public class InteractionComponent : ComponentBase
    {
        private bool _isPointing;
        private bool _isSelecting;
        private long? _hitEntityId;
        private float _hitX, _hitY, _hitZ;
        private float _hitNX, _hitNY, _hitNZ;

        public override string Name => "Interaction";

        public bool IsPointing
        {
            get => _isPointing;
            set { if (_isPointing != value) { _isPointing = value; MarkDirty(); } }
        }

        public bool IsSelecting
        {
            get => _isSelecting;
            set { if (_isSelecting != value) { _isSelecting = value; MarkDirty(); } }
        }

        /// <summary>Entity id of the last hit, if any.</summary>
        public long? HitEntityId
        {
            get => _hitEntityId;
            set { if (_hitEntityId != value) { _hitEntityId = value; MarkDirty(); } }
        }

        public (float x, float y, float z) HitPosition
        {
            get => (_hitX, _hitY, _hitZ);
            set { if (_hitX != value.x || _hitY != value.y || _hitZ != value.z) { _hitX = value.x; _hitY = value.y; _hitZ = value.z; MarkDirty(); } }
        }

        public (float x, float y, float z) HitNormal
        {
            get => (_hitNX, _hitNY, _hitNZ);
            set { if (_hitNX != value.x || _hitNY != value.y || _hitNZ != value.z) { _hitNX = value.x; _hitNY = value.y; _hitNZ = value.z; MarkDirty(); } }
        }
    }
}
