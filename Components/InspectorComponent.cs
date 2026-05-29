using System;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.UI;

namespace V12.Components
{
    public class InspectorComponent : ComponentBase
    {
        public override string Name { get; set; } = "Inspector";
        public override string Description => "Renders an inspector panel for this entity.";
        public InspectorComponent() { }
        public override void Update(float deltaTime) { }
        
        public override void OnAttach(IWorldElement worldElement) 
        { 
            base.OnAttach(worldElement);
            EntityId = worldElement.Id; 
            InspectorBuilder b = new InspectorBuilder();
            worldElement.AddChild(b.Build());
        }

        public override void OnDetach(IWorldElement worldElement) 
        { 
            base.OnDetach(worldElement);
            EntityId = null; 
        }

        public override void OnUpdate() { }
        public override void OnDestroy() { }

        public override IWorldElement BuildUI() => new Element();
        public override void BuildInspector(IInspector inspector) { }
    }
}
