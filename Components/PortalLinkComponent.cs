using System;
using MongoDB.Bson.Serialization.Attributes;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.References;

namespace V12.Components
{
    public class PortalLinkComponent : ComponentBase
    {
        private long _portalAElementId;
        private long _portalBElementId;

        public override string Name => "PortalLink";
        public override string Description => "Links two portal elements as a bidirectional pair";

        [ReferenceTarget(Kind = ReferenceTargetKind.Element, AcceptsComponent = typeof(PortalComponent), Required = false)]
        public ElementRef PortalARef { get; set; } = new();

        [ReferenceTarget(Kind = ReferenceTargetKind.Element, AcceptsComponent = typeof(PortalComponent), Required = false)]
        public ElementRef PortalBRef { get; set; } = new();

        [BsonIgnore]
        public long PortalAElementId
        {
            get => PortalARef.Resolve(Owner)?.Id ?? _portalAElementId;
            set { if (_portalAElementId != value) { _portalAElementId = value; MarkDirty(); } }
        }

        [BsonIgnore]
        public long PortalBElementId
        {
            get => PortalBRef.Resolve(Owner)?.Id ?? _portalBElementId;
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
