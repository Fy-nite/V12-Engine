using System;
using MongoDB.Bson.Serialization.Attributes;
using V12.Core.Core.Interfaces;

namespace V12.Core.References
{
    /// <summary>
    /// A stable, author-facing reference to an element. The persisted value is a
    /// selector string (name, suffix path, or @uid pin); the live target is a
    /// runtime cache resolved lazily against the owning world.
    /// </summary>
    public sealed class ElementRef
    {
        /// <summary>Persisted selector. See <see cref="ReferenceSelector"/>.</summary>
        public string Selector { get; set; } = "";

        /// <summary>Optional fingerprint used only to rank relink suggestions.</summary>
        public string? Hint { get; set; }

        /// <summary>Live target cache. Never serialized by WorldML or BSON.</summary>
        [BsonIgnore]
        public IWorldElement? Target { get; private set; }

        public bool IsSet => !string.IsNullOrWhiteSpace(Selector);

        public ElementRef() { }

        public ElementRef(string selector, string? hint = null)
        {
            Selector = selector ?? "";
            Hint = hint;
        }

        /// <summary>Resolve against the world that owns <paramref name="scope"/>.</summary>
        public IWorldElement? Resolve(IWorldElement? scope = null)
        {
            var root = GameRoot.Instance;
            var world = scope != null ? root?.GetWorldForElement(scope) : null;
            world ??= root?.SelectedWorld;

            if (Target != null)
            {
                // The target cache is only valid while the element is still
                // attached to the same world. GetWorldForElement falls back to
                // SelectedWorld for detached elements, so it cannot prove this.
                if (world == null || world.ContainsElement(Target))
                    return Target;
                Target = null;
            }

            if (world == null || !IsSet) return null;
            Target = ReferenceResolver.ResolveElement(world, Selector);
            return Target;
        }

        /// <summary>Selector to persist. An explicit <c>@uid</c> pin is kept
        /// verbatim even after the target has been resolved, so a rename on
        /// one peer cannot invalidate a copied/persisted reference.</summary>
        public string ToSelector()
        {
            if (ReferenceSelector.IsUidSelector(Selector)) return Selector;
            return Target != null ? ReferenceSelector.Build(Target) : Selector;
        }

        public static ElementRef From(IWorldElement target)
            => new(ReferenceSelector.Build(target));

        public override string ToString() => ToSelector();
    }
}
