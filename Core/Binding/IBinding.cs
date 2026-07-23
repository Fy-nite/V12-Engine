using System;

namespace V12.Core.Binding
{
    /// <summary>
    /// Base interface for all bindings. A binding is a live reference to a property
    /// that fires <see cref="OnChanged"/> when the underlying value updates.
    /// </summary>
    public interface IBinding : IDisposable
    {
        /// <summary>
        /// Unique key identifying this binding's source (e.g. "42/V12.Components.TransformComponent/X").
        /// </summary>
        string Key { get; }

        /// <summary>
        /// The type of the bound value.
        /// </summary>
        Type ValueType { get; }

        /// <summary>
        /// Whether this binding still references a valid source.
        /// Returns false after the source element or component has been destroyed.
        /// </summary>
        bool IsValid { get; }

        /// <summary>
        /// Re-read the source property and fire <see cref="OnChanged"/> if the value changed.
        /// Called automatically by the <see cref="BindingContext"/> when the source goes dirty.
        /// Can also be called manually for forced refresh.
        /// </summary>
        void Refresh();

        /// <summary>
        /// Fired after <see cref="Refresh"/> when the value has changed.
        /// The argument is this binding instance.
        /// </summary>
        event Action<IBinding>? OnChanged;
    }
}
