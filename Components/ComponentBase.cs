using System;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>
    /// Common base implementation for components.
    /// Provides id generation, dirty event wiring and default lifecycle methods.
    /// </summary>
    public abstract class ComponentBase : IComponent
    {
        public event Action<IComponent>? OnDirty;

        public long Id { get; protected set; }
        public long? EntityId { get; set; }

        public virtual string Name { get; set; }
        public virtual string Description => string.Empty;
        public bool IsDirty => OnDirty != null;
        public bool Active {  get; set; }

        protected ComponentBase()
        {
            Id = GenerateId();
        }

        public virtual void Update(float deltaTime) { }
        public virtual void OnAttach(IWorldElement worldElement) { }
        public virtual void OnDetach(IWorldElement worldElement) { }
        public virtual void OnUpdate() { }
        public virtual void OnDestroy() { }
        public abstract IWorldElement BuildUI();

        /// <summary>
        /// Mark component as dirty so DirtyTracker can pick it up.
        /// </summary>
        protected void MarkDirty()
        {
            try { OnDirty?.Invoke(this); } catch { }
        }

        private static long _nextId = 1;
        private static long GenerateId() => System.Threading.Interlocked.Increment(ref _nextId);
    }
}
