using System;
using System.Collections.Generic;
using V12.Core.Core.Interfaces;

namespace V12.Core.Binding
{
    /// <summary>
    /// A live, typed reference to a component property. Holds a cached value that
    /// is re-read whenever the source component fires <see cref="ComponentBase.OnDirty"/>.
    /// Supports both one-way (read-only) and two-way (read + write) binding.
    /// </summary>
    public class Bindable<T> : IBinding
    {
        private readonly PropertyBindingSource _source;
        private readonly Func<IComponent, T> _getter;
        private readonly Action<IComponent, T>? _setter;
        private readonly Func<IComponent, bool>? _isValid;
        private T? _cachedValue;
        private bool _hasValue;

        public string Key => _source.Key;
        public Type ValueType => typeof(T);

        public bool IsValid
        {
            get
            {
                if (_isValid != null)
                {
                    var component = GetComponent();
                    return component != null && _isValid(component);
                }
                return GetComponent() != null;
            }
        }

        /// <summary>
        /// The current cached value. Reads from cache; call <see cref="Refresh"/>
        /// to re-read from the source component.
        /// </summary>
        public T Value
        {
            get
            {
                if (!_hasValue) Refresh();
                return _cachedValue!;
            }
        }

        /// <summary>Whether a value has been read at least once.</summary>
        public bool HasValue => _hasValue;

        public event Action<IBinding>? OnChanged;

        internal Bindable(
            PropertyBindingSource source,
            Func<IComponent, T> getter,
            Action<IComponent, T>? setter,
            Func<IComponent, bool>? isValid)
        {
            _source = source;
            _getter = getter;
            _setter = setter;
            _isValid = isValid;
        }

        /// <summary>
        /// Re-read the source component's property. Fires <see cref="OnChanged"/>
        /// if the value changed since the last read.
        /// </summary>
        public void Refresh()
        {
            var component = GetComponent();
            if (component == null) return;

            var newValue = _getter(component);
            if (_hasValue && EqualityComparer<T>.Default.Equals(_cachedValue, newValue))
                return;

            _cachedValue = newValue;
            _hasValue = true;
            OnChanged?.Invoke(this);
        }

        /// <summary>
        /// Write a new value back to the source component (two-way binding).
        /// Only works if a setter was provided at creation time.
        /// </summary>
        public void SetValue(T value)
        {
            if (_setter == null)
                throw new InvalidOperationException(
                    $"Binding {Key} is read-only. Use BindTwoWay for writable bindings.");

            var component = GetComponent();
            if (component == null)
                throw new InvalidOperationException(
                    $"Binding {Key} source component is no longer valid.");

            _setter(component, value);
            _cachedValue = value;
            _hasValue = true;
            OnChanged?.Invoke(this);
        }

        private IComponent? GetComponent()
        {
            var world = GameRoot.Instance?.SelectedWorld;
            if (world == null) return null;

            IWorldElement? element = null;
            world.Lock.EnterReadLock();
            try
            {
                world._elementsById.TryGetValue(_source.ElementId, out element);
            }
            finally { world.Lock.ExitReadLock(); }

            if (element == null) return null;

            var componentType = Type.GetType(_source.ComponentType);
            if (componentType == null) return null;

            foreach (var c in element.Components)
            {
                if (componentType.IsInstanceOfType(c)) return c;
            }
            return null;
        }

        public void Dispose()
        {
            OnChanged = null;
        }
    }
}
