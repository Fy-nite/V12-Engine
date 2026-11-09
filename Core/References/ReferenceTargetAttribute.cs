using System;

namespace V12.Core.References
{
    /// <summary>What kind of object a reference property points at.</summary>
    public enum ReferenceTargetKind
    {
        /// <summary>An element in the scene.</summary>
        Element,

        /// <summary>A component attached to an element.</summary>
        Component,
    }

    /// <summary>
    /// Declares the kind of target a reference property accepts. The runtime
    /// inspector uses it to filter the picker and validate drops, and the SDK's
    /// schema generator reflects it into components.schema.xml so CI can
    /// validate hand-authored WorldML without loading the game assembly.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class ReferenceTargetAttribute : Attribute
    {
        /// <summary>Element or component. Defaults to <see cref="ReferenceTargetKind.Element"/>.</summary>
        public ReferenceTargetKind Kind { get; init; } = ReferenceTargetKind.Element;

        /// <summary>
        /// Component type the target element (or target component) must have.
        /// Leave null to accept any target of the declared kind.
        /// </summary>
        public Type? AcceptsComponent { get; init; }

        /// <summary>
        /// Interface the target component must implement. The schema generator
        /// flattens this to the concrete component types it can see.
        /// </summary>
        public Type? AcceptsInterface { get; init; }

        /// <summary>When false the reference may be empty.</summary>
        public bool Required { get; init; } = true;

        /// <summary>Optional author-facing note emitted into the schema.</summary>
        public string? Description { get; init; }
    }
}
