using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using V12.Components;
using V12.Core.Core.Interfaces;

namespace V12.Core.Binding
{
    /// <summary>
    /// World-level binding manager. Tracks all active bindings, subscribes to
    /// component dirty events, and propagates changes to bound consumers.
    /// One BindingContext per World instance.
    /// </summary>
    public class BindingContext : IDisposable
    {
        private readonly Dictionary<string, IBinding> _bindings = new();
        private readonly Dictionary<long, List<IBinding>> _byElement = new();
        private readonly Dictionary<string, List<Action<IComponent>>> _dirtyHandlers = new();
        private bool _disposed;

        /// <summary>
        /// Creates a one-way binding: reads <paramref name="propertyName"/> from a
        /// component on the element with <paramref name="elementId"/>.
        /// Key format: world://{worldId}/entity/{elementId}/component/{componentId}/property/{propertyName}
        /// </summary>
        public IBinding Bind<T>(string worldId, long elementId, long componentId, string componentName, string propertyName, Action<T>? onChange = null)
        {
            var source = BuildSource(worldId, elementId, componentId, componentName, propertyName);
            if (_bindings.ContainsKey(source.Key))
            {
                var existing = _bindings[source.Key];
                if (onChange != null) existing.OnChanged += _ => onChange(((Bindable<T>)existing).Value);
                return existing;
            }

            var (getter, componentType) = BuildGetter<T>(componentName, propertyName);
            var binding = new Bindable<T>(source, getter, setter: null, isValid: null);

            if (onChange != null)
                binding.OnChanged += _ => onChange(((Bindable<T>)binding).Value);

            RegisterBinding(elementId, source.Key, binding, componentType);
            return binding;
        }

        /// <summary>
        /// Creates a two-way binding: reads and writes <paramref name="propertyName"/>.
        /// Key format: world://{worldId}/entity/{elementId}/component/{componentId}/property/{propertyName}
        /// </summary>
        public Bindable<T> BindTwoWay<T>(string worldId, long elementId, long componentId, string componentName, string propertyName, Action<T>? onChange = null)
        {
            var source = BuildSource(worldId, elementId, componentId, componentName, propertyName);
            if (_bindings.ContainsKey(source.Key) && _bindings[source.Key] is Bindable<T> existing)
            {
                if (onChange != null) existing.OnChanged += _ => onChange(existing.Value);
                return existing;
            }

            var (getter, setter, componentType) = BuildGetterSetter<T>(componentName, propertyName);
            var binding = new Bindable<T>(source, getter, setter, isValid: null);

            if (onChange != null)
                binding.OnChanged += _ => onChange(binding.Value);

            RegisterBinding(elementId, source.Key, binding, componentType);
            return binding;
        }

        /// <summary>
        /// Creates a computed binding that derives its value from one or more source bindings.
        /// The compute function runs whenever any source changes.
        /// </summary>
        public IBinding BindComputed<T>(Func<T> compute, params IBinding[] sources)
        {
            var key = $"computed/{Guid.NewGuid():N}";
            var binding = new ComputedBinding<T>(key, compute, sources);
            foreach (var src in sources)
            {
                if (_bindings.ContainsKey(src.Key))
                    _bindings[src.Key].OnChanged += _ => binding.Recompute();
            }
            _bindings[key] = binding;
            return binding;
        }

        /// <summary>
        /// Strongly-typed one-way binding. Compile-time safe, no reflection on the property.
        /// Extracts property name and getter from the expression at call time.
        /// </summary>
        public IBinding BindStrong<TEntity, TProperty>(
            IWorldElement entity,
            Expression<Func<TEntity, TProperty>> propertyAccessor,
            Action<TProperty>? onChange = null
        ) where TEntity : class, IComponent
        {
            var component = entity.GetComponent<TEntity>()
                ?? throw new ArgumentException(
                    $"Entity '{entity.Name}' (id={entity.Id}) has no component of type {typeof(TEntity).Name}.");

            var propertyName = ExtractPropertyName(propertyAccessor);
            var compiled = propertyAccessor.Compile();
            var world = GameRoot.Instance?.SelectedWorld;
            var worldId = world?.WorldName ?? "unknown";

            var source = new PropertyBindingSource
            {
                WorldId = worldId,
                ElementId = entity.Id,
                ComponentId = component.Id,
                ComponentType = typeof(TEntity).AssemblyQualifiedName!,
                PropertyName = propertyName
            };

            if (_bindings.ContainsKey(source.Key))
            {
                var existing = _bindings[source.Key];
                if (onChange != null) existing.OnChanged += _ => onChange(((Bindable<TProperty>)existing).Value);
                return existing;
            }

            Func<IComponent, TProperty> getter = c => compiled((TEntity)c);
            var binding = new Bindable<TProperty>(source, getter, setter: null, isValid: null);

            if (onChange != null)
                binding.OnChanged += _ => onChange(((Bindable<TProperty>)binding).Value);

            RegisterBinding(entity.Id, source.Key, binding, typeof(TEntity));
            return binding;
        }

        /// <summary>
        /// Strongly-typed one-way binding with expression-based setter for two-way.
        /// </summary>
        public Bindable<TProperty> BindStrongTwoWay<TEntity, TProperty>(
            IWorldElement entity,
            Expression<Func<TEntity, TProperty>> propertyAccessor,
            Action<TProperty>? onChange = null
        ) where TEntity : class, IComponent
        {
            var component = entity.GetComponent<TEntity>()
                ?? throw new ArgumentException(
                    $"Entity '{entity.Name}' (id={entity.Id}) has no component of type {typeof(TEntity).Name}.");

            var propertyName = ExtractPropertyName(propertyAccessor);
            var getCompiled = propertyAccessor.Compile();
            var world = GameRoot.Instance?.SelectedWorld;
            var worldId = world?.WorldName ?? "unknown";

            // Build setter from expression: (component, value) => component.Property = value
            var componentParam = Expression.Parameter(typeof(IComponent), "c");
            var valueParam = Expression.Parameter(typeof(TProperty), "v");
            var castComponent = Expression.Convert(componentParam, typeof(TEntity));
            var propAccess = Expression.Property(castComponent, propertyName);
            var assign = Expression.Assign(propAccess, valueParam);
            var setterLambda = Expression.Lambda<Action<IComponent, TProperty>>(assign, componentParam, valueParam);
            var setter = setterLambda.Compile();

            var source = new PropertyBindingSource
            {
                WorldId = worldId,
                ElementId = entity.Id,
                ComponentId = component.Id,
                ComponentType = typeof(TEntity).AssemblyQualifiedName!,
                PropertyName = propertyName
            };

            if (_bindings.ContainsKey(source.Key) && _bindings[source.Key] is Bindable<TProperty> existing)
            {
                if (onChange != null) existing.OnChanged += _ => onChange(existing.Value);
                return existing;
            }

            Func<IComponent, TProperty> getter = c => getCompiled((TEntity)c);
            var binding = new Bindable<TProperty>(source, getter, setter, isValid: null);

            if (onChange != null)
                binding.OnChanged += _ => onChange(binding.Value);

            RegisterBinding(entity.Id, source.Key, binding, typeof(TEntity));
            return binding;
        }

        /// <summary>
        /// Dynamic binding from a URI string. Uses reflection.
        /// For scripting, serialization, network messages, and other dynamic cases.
        /// </summary>
        public IBinding BindFromUri(string uri, Action<object?>? onChange = null)
        {
            var (worldId, elementId, componentId, propertyName) = ParseUri(uri);

            // Find the component on the element to discover its type
            var world = GameRoot.Instance?.SelectedWorld;
            if (world == null)
                throw new InvalidOperationException("No world is currently selected.");

            IWorldElement? element = null;
            world.Lock.EnterReadLock();
            try
            {
                world._elementsById.TryGetValue(elementId, out element);
            }
            finally { world.Lock.ExitReadLock(); }

            if (element == null)
                throw new ArgumentException($"Element {elementId} not found in world '{worldId}'.");

            var component = element.Components.FirstOrDefault(c => c.Id == componentId)
                ?? throw new ArgumentException(
                    $"Component {componentId} not found on element {elementId}.");

            var prop = component.GetType().GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.Instance)
                ?? throw new ArgumentException(
                    $"Property '{propertyName}' not found on component {componentId}.");

            // Build a generic Bind<T> using reflection for the property type
            var bindMethod = typeof(BindingContext).GetMethod(nameof(Bind), BindingFlags.Public | BindingFlags.Instance)!
                .MakeGenericMethod(prop.PropertyType);

            var componentName = component.GetType().Name;
            var binding = (IBinding)bindMethod.Invoke(this, new object?[]
            {
                worldId, elementId, componentId, componentName, propertyName,
                onChange != null ? CreateUntypedCallback(prop.PropertyType, onChange) : null
            })!;

            return binding;
        }

        /// <summary>
        /// Refresh all tracked bindings. Useful for bulk updates or polling scenarios.
        /// </summary>
        public void RefreshAll()
        {
            foreach (var binding in _bindings.Values)
            {
                if (binding.IsValid)
                    binding.Refresh();
            }
        }

        /// <summary>
        /// Refresh all bindings for a specific element.
        /// </summary>
        public void RefreshElement(long elementId)
        {
            if (!_byElement.TryGetValue(elementId, out var bindings)) return;
            foreach (var binding in bindings)
            {
                if (binding.IsValid)
                    binding.Refresh();
            }
        }

        /// <summary>
        /// Get a binding by key, if it exists.
        /// </summary>
        public IBinding? GetBinding(string key)
        {
            _bindings.TryGetValue(key, out var binding);
            return binding;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var binding in _bindings.Values)
                binding.Dispose();
            _bindings.Clear();
            _byElement.Clear();
            _dirtyHandlers.Clear();
        }

        // --- Private helpers ---

        private static PropertyBindingSource BuildSource(string worldId, long elementId, long componentId, string componentName, string propertyName)
        {
            var assembly = Assembly.GetAssembly(typeof(ComponentBase))!;
            var componentType = assembly.GetTypes()
                .FirstOrDefault(t => t.Name == componentName && typeof(IComponent).IsAssignableFrom(t));

            return new PropertyBindingSource
            {
                WorldId = worldId,
                ElementId = elementId,
                ComponentId = componentId,
                ComponentType = componentType?.AssemblyQualifiedName ?? componentName,
                PropertyName = propertyName
            };
        }

        private static (Func<IComponent, T> getter, Type componentType) BuildGetter<T>(string componentName, string propertyName)
        {
            var assembly = Assembly.GetAssembly(typeof(ComponentBase))!;
            var componentType = assembly.GetTypes()
                .FirstOrDefault(t => t.Name == componentName && typeof(IComponent).IsAssignableFrom(t))
                ?? throw new ArgumentException($"Component '{componentName}' not found.");

            var prop = componentType.GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.Instance)
                ?? throw new ArgumentException($"Property '{propertyName}' not found on '{componentName}'.");

            if (!typeof(T).IsAssignableFrom(prop.PropertyType))
                throw new ArgumentException(
                    $"Property '{propertyName}' is {prop.PropertyType.Name}, expected {typeof(T).Name}.");

            return (c => (T)prop.GetValue(c)!, componentType);
        }

        private static (Func<IComponent, T> getter, Action<IComponent, T> setter, Type componentType) BuildGetterSetter<T>(string componentName, string propertyName)
        {
            var assembly = Assembly.GetAssembly(typeof(ComponentBase))!;
            var componentType = assembly.GetTypes()
                .FirstOrDefault(t => t.Name == componentName && typeof(IComponent).IsAssignableFrom(t))
                ?? throw new ArgumentException($"Component '{componentName}' not found.");

            var prop = componentType.GetProperty(propertyName,
                BindingFlags.Public | BindingFlags.Instance)
                ?? throw new ArgumentException($"Property '{propertyName}' not found on '{componentName}'.");

            if (!typeof(T).IsAssignableFrom(prop.PropertyType))
                throw new ArgumentException(
                    $"Property '{propertyName}' is {prop.PropertyType.Name}, expected {typeof(T).Name}.");

            if (!prop.CanWrite)
                throw new ArgumentException(
                    $"Property '{propertyName}' on '{componentName}' has no setter.");

            return (
                c => (T)prop.GetValue(c)!,
                (c, v) => prop.SetValue(c, v),
                componentType
            );
        }

        private void RegisterBinding(long elementId, string key, IBinding binding, Type componentType)
        {
            _bindings[key] = binding;

            if (!_byElement.ContainsKey(elementId))
                _byElement[elementId] = new List<IBinding>();
            _byElement[elementId].Add(binding);

            // Subscribe to the component's OnDirty via the world element's component list
            var world = GameRoot.Instance?.SelectedWorld;
            if (world == null) return;

            world.Lock.EnterReadLock();
            try
            {
                if (!world._elementsById.TryGetValue(elementId, out var element)) return;

                foreach (var c in element.Components)
                {
                    if (!componentType.IsInstanceOfType(c)) continue;
                    if (c is ComponentBase cb)
                    {
                        cb.OnDirty += _ => binding.Refresh();
                    }
                    break;
                }
            }
            finally { world.Lock.ExitReadLock(); }
        }

        /// <summary>
        /// Extracts the property name from an expression like c => c.Position.
        /// </summary>
        private static string ExtractPropertyName<TSource, TProperty>(Expression<Func<TSource, TProperty>> expression)
        {
            if (expression.Body is MemberExpression member && member.Member is PropertyInfo)
                return member.Member.Name;

            if (expression.Body is UnaryExpression unary && unary.Operand is MemberExpression unaryMember
                && unaryMember.Member is PropertyInfo)
                return unaryMember.Member.Name;

            throw new ArgumentException(
                "Expression must be a simple property access (e.g. c => c.Position).");
        }

        /// <summary>
        /// Parses a binding URI: world://{worldId}/entity/{elementId}/component/{componentId}/property/{propertyName}
        /// </summary>
        private static (string worldId, long elementId, long componentId, string propertyName) ParseUri(string uri)
        {
            // world://World/entity/42/component/8/property/Position
            var withoutScheme = uri.Replace("world://", "").Replace("World://", "");
            var parts = withoutScheme.Split('/');
            // parts: [worldId, "entity", elementId, "component", componentId, "property", propertyName]

            if (parts.Length < 7)
                throw new ArgumentException(
                    $"Invalid binding URI: '{uri}'. Expected format: world://{{worldId}}/entity/{{id}}/component/{{id}}/property/{{name}}");

            var worldId = parts[0];
            if (!long.TryParse(parts[2], out var elementId))
                throw new ArgumentException($"Invalid element ID in URI: '{parts[2]}'");
            if (!long.TryParse(parts[4], out var componentId))
                throw new ArgumentException($"Invalid component ID in URI: '{parts[4]}'");
            var propertyName = parts[6];

            return (worldId, elementId, componentId, propertyName);
        }

        /// <summary>
        /// Creates a non-generic delegate that bridges an object callback to the typed OnChanged event.
        /// Used by BindFromUri when the property type is only known at runtime.
        /// </summary>
        private static object CreateUntypedCallback(Type propertyType, Action<object?> onChange)
        {
            // Build: (Action<IBinding>)(b => onChange(((IBinding)b)....))
            // We need to get the typed value from the binding, so we use reflection on the Value property.
            var bindingInterfaceType = typeof(IBinding);
            var valueProp = typeof(Bindable<>).MakeGenericType(propertyType).GetProperty("Value")!;

            return new Action<IBinding>(b =>
            {
                if (b.GetType().IsGenericType && b.GetType().GetGenericTypeDefinition() == typeof(Bindable<>))
                {
                    var val = valueProp.GetValue(b);
                    onChange(val);
                }
            });
        }
    }

    /// <summary>
    /// A binding whose value is computed from other bindings rather than read
    /// directly from a component property.
    /// </summary>
    internal class ComputedBinding<T> : IBinding
    {
        private readonly Func<T> _compute;
        private readonly IBinding[] _sources;
        private T? _cachedValue;
        private bool _hasValue;

        public string Key { get; }
        public Type ValueType => typeof(T);
        public bool IsValid => true;

        public event Action<IBinding>? OnChanged;

        internal ComputedBinding(string key, Func<T> compute, IBinding[] sources)
        {
            Key = key;
            _compute = compute;
            _sources = sources;
        }

        public void Refresh() => Recompute();

        internal void Recompute()
        {
            var newValue = _compute();
            if (_hasValue && EqualityComparer<T>.Default.Equals(_cachedValue, newValue))
                return;

            _cachedValue = newValue;
            _hasValue = true;
            OnChanged?.Invoke(this);
        }

        public void Dispose()
        {
            OnChanged = null;
        }
    }
}
