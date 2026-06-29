using System;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    public class PortalLinkComponent : ComponentBase
    {
        private long _portalAElementId;
        private long _portalBElementId;

        public override string Name => "PortalLink";
        public override string Description => "Links two portal elements as a bidirectional pair";

        public long PortalAElementId
        {
            get => _portalAElementId;
            set { if (_portalAElementId != value) { _portalAElementId = value; MarkDirty(); } }
        }

        public long PortalBElementId
        {
            get => _portalBElementId;
            set { if (_portalBElementId != value) { _portalBElementId = value; MarkDirty(); } }
        }

        public PortalLinkComponent() { }

        public PortalLinkComponent(long portalAId, long portalBId)
        {
            _portalAElementId = portalAId;
            _portalBElementId = portalBId;
        }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }

        public override string ToString() =>
            $"PortalLink(A:{PortalAElementId} <-> B:{PortalBElementId})";
    }
}
