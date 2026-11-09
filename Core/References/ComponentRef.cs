using System;
using System.Linq;
using MongoDB.Bson.Serialization.Attributes;
using V12.Core.Core.Interfaces;

namespace V12.Core.References
{
    /// <summary>
    /// A stable, author-facing reference to a component. The selector is
    /// "element#component", where the component part may be a name, a type name,
    /// an indexed type ("MeshRenderer[2]"), or an @uid pin.
    /// </summary>
    public sealed class ComponentRef
    {
        public string Selector { get; set; } = "";
        public string? Hint { get; set; }

        [BsonIgnore]
        public IComponent? Target { get; private set; }

        public bool IsSet => !string.IsNullOrWhiteSpace(Selector);

        public ComponentRef() { }

        public ComponentRef(string selector, string? hint = null)
        {
            Selector = selector ?? "";
            Hint = hint;
        }

        public IComponent? Resolve(IWorldElement? scope = null)
        {
            var root = GameRoot.Instance;
            var world = scope != null ? root?.GetWorldForElement(scope) : null;
            world ??= root?.SelectedWorld;

            if (Target != null)
            {
                if (world == null || IsAttached(Target, world))
                    return Target;
                Target = null;
            }

            if (world == null || !IsSet) return null;
            Target = ReferenceResolver.ResolveComponent(world, Selector);
            return Target;
        }

        public string ToSelector()
        {
            // Preserve an explicit uid pin on either side of the '#'
            // separator; resolving only fills the live target cache.
            var (elementSelector, componentSelector) = ReferenceSelector.SplitComponent(Selector);
            if (ReferenceSelector.IsUidSelector(elementSelector)
                || ReferenceSelector.IsUidSelector(componentSelector))
                return Selector;
            return Target != null ? ReferenceSelector.Build(Target) : Selector;
        }

        private static bool IsAttached(IComponent component, World world)
        {
            if (component is V12.Components.ComponentBase cb)
            {
                var owner = cb.Owner;
                return owner != null
                    && world.ContainsElement(owner)
                    && owner.Components.Contains(component);
            }

            // Non-ComponentBase implementations do not expose an owner; keep
            // the existing cache behaviour rather than clearing it every call.
            return true;
        }

        public static ComponentRef From(IComponent target)
            => new(ReferenceSelector.Build(target));

        public override string ToString() => ToSelector();
    }
}
