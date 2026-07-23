using System;
using System.Linq.Expressions;
using V12.Core.Core.Interfaces;

namespace V12.Core.Binding
{
    /// <summary>
    /// Static factory for creating bindings. Entry point for the binding system.
    /// Key format: world://{worldId}/entity/{elementId}/component/{componentId}/property/{property}
    /// </summary>
    public static class Bind
    {
        /// <summary>
        /// Get the binding context for the currently selected world.
        /// Creates one if it doesn't exist yet.
        /// </summary>
        public static BindingContext Current
        {
            get
            {
                var world = GameRoot.Instance?.SelectedWorld
                    ?? throw new InvalidOperationException("No world is currently selected.");

                if (world.Bindings == null)
                    world.Bindings = new BindingContext();

                return world.Bindings;
            }
        }

        // ── Strongly-typed API (compile-time safe, no reflection on property) ──

        /// <summary>
        /// Strongly-typed one-way binding. Compile-time safe, no reflection.
        /// <code>Bind.To&lt;TransformComponent, Vector3&gt;(entity, c =&gt; c.Position, p =&gt; Log(p));</code>
        /// Generates: world://{worldId}/entity/{id}/component/{id}/property/Position
        /// </summary>
        public static IBinding To<TEntity, TProperty>(
            IWorldElement entity,
            Expression<Func<TEntity, TProperty>> propertyAccessor,
            Action<TProperty>? onChange = null
        ) where TEntity : class, IComponent
            => Current.BindStrong(entity, propertyAccessor, onChange);

        /// <summary>
        /// Strongly-typed two-way binding with expression-based setter.
        /// <code>var pos = Bind.ToTwoWay&lt;TransformComponent, Vector3&gt;(entity, c =&gt; c.Position);</code>
        /// </summary>
        public static Bindable<TProperty> ToTwoWay<TEntity, TProperty>(
            IWorldElement entity,
            Expression<Func<TEntity, TProperty>> propertyAccessor,
            Action<TProperty>? onChange = null
        ) where TEntity : class, IComponent
            => Current.BindStrongTwoWay(entity, propertyAccessor, onChange);

        // ── String-based API (for scripting, serialization, network, ObjectIR) ──

        /// <summary>
        /// One-way binding from string parameters. For dynamic/scripting use.
        /// </summary>
        public static IBinding To<T>(string worldId, long elementId, long componentId, string componentName, string propertyName, Action<T>? onChange = null)
            => Current.Bind<T>(worldId, elementId, componentId, componentName, propertyName, onChange);

        /// <summary>
        /// Two-way binding from string parameters. For dynamic/scripting use.
        /// </summary>
        public static Bindable<T> ToTwoWay<T>(string worldId, long elementId, long componentId, string componentName, string propertyName, Action<T>? onChange = null)
            => Current.BindTwoWay<T>(worldId, elementId, componentId, componentName, propertyName, onChange);

        /// <summary>
        /// Dynamic binding from a full URI string. Infers everything at runtime via reflection.
        /// <code>Bind.To("world://Main/entity/42/component/8/property/Position");</code>
        /// </summary>
        public static IBinding To(string uri, Action<object?>? onChange = null)
            => Current.BindFromUri(uri, onChange);

        // ── Computed bindings ──

        /// <summary>
        /// Computed binding: value is derived from a function evaluated over other bindings.
        /// Recomputes whenever any source binding changes.
        /// </summary>
        public static IBinding Computed<T>(Func<T> compute, params IBinding[] sources)
            => Current.BindComputed(compute, sources);

        // ── Refresh helpers ──

        /// <summary>Refresh all bindings in the current world's context.</summary>
        public static void RefreshAll() => Current.RefreshAll();

        /// <summary>Refresh all bindings for a specific element.</summary>
        public static void RefreshElement(long elementId) => Current.RefreshElement(elementId);
    }
}
