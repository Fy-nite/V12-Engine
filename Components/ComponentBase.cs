using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Networking;
using MongoDB.Bson.Serialization.Attributes;

namespace V12.Components
{
    /// <summary>
    /// Common base implementation for components.
    /// Provides id generation, dirty event wiring and default lifecycle methods.
    /// </summary>
    public abstract class ComponentBase : IComponent, ISyncValueProvider
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

        private bool _active = true;

        /// <summary>
        /// Which peers run <see cref="Update"/>. Defaults to
        /// <see cref="ComponentSimulationScope.Simulator"/>: the component runs
        /// wherever its element is simulated, which is the host for host-owned
        /// objects and the owning client for a grabbed or owned one. Not
        /// replicated: each peer's code decides for itself.
        /// </summary>
        [BsonIgnore]
        public virtual ComponentSimulationScope SimulationScope
        {
            get => _simulationScope;
            set => _simulationScope = value;
        }

        private ComponentSimulationScope _simulationScope = ComponentSimulationScope.Simulator;

        // ── Field sync ([Sync] members) ──────────────────────────────────────

        [BsonIgnore]
        private SyncFieldBinder? _syncFields;
        private bool _syncFieldsResolved;

        /// <summary>
        /// True while a remote value is being written into this component. The
        /// binder sets it so a property setter calling <see cref="MarkDirty"/>
        /// does not echo the update straight back onto the wire.
        /// </summary>
        [BsonIgnore]
        internal bool IsApplyingRemote { get; set; }

        /// <summary>
        /// One value per <c>[Sync]</c> field on this component. Discovered by the
        /// session channel through <see cref="ISyncValueProvider"/>; explicit
        /// <see cref="SyncValue{T}"/> members are enumerated separately.
        /// </summary>
        public IEnumerable<ISyncValue> GetSyncValues()
        {
            EnsureSyncFields();
            return _syncFields?.Values ?? Array.Empty<ISyncValue>();
        }

        /// <summary>Publish any <c>[Sync]</c> field that changed this tick.</summary>
        public void PollSyncValues()
        {
            EnsureSyncFields();
            _syncFields?.Poll();
        }

        private void EnsureSyncFields()
        {
            if (_syncFieldsResolved) return;
            _syncFieldsResolved = true;

            // No [Sync] members: never allocate the binder.
            if (SyncFieldSchema.For(GetType()).Length > 0)
                _syncFields = new SyncFieldBinder(this);
        }

        /// <summary>
        /// Inactive components are skipped by rendering and capture. Dirty-tracked
        /// so toggling one re-captures the frame.
        /// </summary>
        public bool Active
        {
            get => _active;
            set => SetSync(ref _active, value);
        }

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
            // A remote value write through a property setter must not bounce the
            // component snapshot back to the peer that just sent it. The render
            // refresh still runs: only the network dirty signal is suppressed.
            if (!IsApplyingRemote)
            {
                try { OnDirty?.Invoke(this); } catch { }
            }
            Core.GameRoot.Instance?.MarkRenderDirty();
        }

        /// <summary>
        /// Write a field and mark the component dirty when it changed. This is the
        /// single entry point component properties should use instead of calling
        /// <see cref="MarkDirty"/> directly. The caller member name is captured so
        /// the typed field-sync layer can bind the field without reflection later.
        /// </summary>
        protected bool SetSync<T>(ref T field, T value, [CallerMemberName] string? member = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            MarkDirty();
            return true;
        }

        /// <summary>Float overload with an epsilon check, preserving the
        /// threshold behaviour components already use for transforms and physics.</summary>
        protected bool SetSync(ref float field, float value, float epsilon, [CallerMemberName] string? member = null)
        {
            if (MathF.Abs(field - value) <= epsilon) return false;
            field = value;
            MarkDirty();
            return true;
        }

        /// <summary>Nullable-float overload with an epsilon check and null transition.</summary>
        protected bool SetSync(ref float? field, float? value, float epsilon, [CallerMemberName] string? member = null)
        {
            if (field.HasValue == value.HasValue
                && (!value.HasValue || MathF.Abs(field!.Value - value.Value) <= epsilon))
                return false;
            field = value;
            MarkDirty();
            return true;
        }

        private static long _nextId = 1;
        private static long GenerateId() => System.Threading.Interlocked.Increment(ref _nextId);
    }
}
