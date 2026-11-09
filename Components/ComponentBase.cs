using System;
using V12.Core;
using V12.Core.Core.Interfaces;
using MongoDB.Bson.Serialization.Attributes;

namespace V12.Components
{
    /// <summary>
    /// Common base implementation for components.
    /// Provides id generation, dirty event wiring and default lifecycle methods.
    /// </summary>
    public abstract class ComponentBase : IComponent
    {
        public event Action<IComponent>? OnDirty;

        public long Id { get; set; }
        public long? EntityId { get; set; }

        private Guid _uid = Guid.Empty;
        private string? _name;

        /// <summary>Optional pinned authored id. Empty for normal components.
        /// Unlike a plain auto-property, changes are dirty-tracked so pinned
        /// component references stay valid across peers.</summary>
        public Guid Uid
        {
            get => _uid;
            set
            {
                if (_uid == value) return;
                _uid = value;
                MarkDirty();
            }
        }

        [BsonIgnore]
        public IWorldElement? Owner { get; private set; }

        /// <summary>Author-facing component name used by <c>element#name</c>
        /// reference selectors. Changes are dirty-tracked so peers relink.</summary>
        public virtual string? Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                MarkDirty();
            }
        }

        public virtual string? Description { get; set; }
        public bool IsDirty => OnDirty != null;
        public bool Active {  get; set; } = true;

        /// <summary>When false this component is runtime/editor-only: the WorldML
        /// serializer omits it on save/export, so it never round-trips through a
        /// saved world. Toggle it in the inspector's component block.</summary>
        public bool IsPersistent { get; set; } = true;

        protected ComponentBase()
        {
            Id = GenerateId();
        }

        public virtual void Update(float deltaTime) { }
        public virtual void OnAttach(IWorldElement worldElement) { Owner = worldElement; }
        public virtual void OnDetach(IWorldElement worldElement) { Owner = null; }
        public virtual void OnUpdate() { }
        public virtual void OnDestroy() { }
        /// <summary>
        /// Builds this component's editor UI. Custom/script components may leave
        /// it unimplemented; the default returns an empty element.
        /// </summary>
        public virtual IWorldElement BuildUI() => new Element();
        public virtual void BuildInspector(V12.Core.UI.IInspector inspector) { }

        /// <summary>
        /// Mark component as dirty so DirtyTracker can pick it up.
        /// </summary>
        protected void MarkDirty()
        {
            try { OnDirty?.Invoke(this); } catch { }
            Core.GameRoot.Instance?.MarkRenderDirty();
        }

        private static long _nextId = 1;
        private static long GenerateId() => System.Threading.Interlocked.Increment(ref _nextId);
    }
}
