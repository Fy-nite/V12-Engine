using System;
using V12.Core.Core.Interfaces;
using V12.Core.UI;

namespace V12.Components
{
    public class InspectorComponent : IComponent
    {
        public event Action<IComponent>? OnDirty;
        public long Id { get; } = GenerateId();
        public long? EntityId { get; set; }
        public string Name { get; set; } = "Inspector";
        public string Description { get; set; } = "Renders an inspector panel for this entity.";

        public void Update(float deltaTime) { }
        public void OnAttach(IWorldElement worldElement) { EntityId = worldElement.Id; }
        public void OnDetach(IWorldElement worldElement) { EntityId = null; }
        public void OnUpdate() { }
        public void OnDestroy() { }

        public IWorldElement BuildUI() => throw new NotImplementedException();
        public void BuildInspector(IInspector inspector) { }
        public void CopyFrom(IComponent other) { }
        
        public void MarkDirty() => OnDirty?.Invoke(this);

        private static long _nextId = 1;
        private static long GenerateId() => System.Threading.Interlocked.Increment(ref _nextId);
    }
}
