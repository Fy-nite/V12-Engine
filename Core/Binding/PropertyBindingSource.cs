namespace V12.Core.Binding
{
    /// <summary>
    /// Describes where a binding reads its value from.
    /// Provides a stable URI key for lookup and a human-readable description.
    /// </summary>
    public class PropertyBindingSource
    {
        /// <summary>The world name or ID this binding belongs to.</summary>
        public string WorldId { get; set; } = "";

        /// <summary>The element (entity) ID this binding reads from.</summary>
        public long ElementId { get; set; }

        /// <summary>The component ID (not the type name).</summary>
        public long ComponentId { get; set; }

        /// <summary>The assembly-qualified type name of the component (used for reflection lookup).</summary>
        public string ComponentType { get; set; } = "";

        /// <summary>The property name on the component.</summary>
        public string PropertyName { get; set; } = "";

        /// <summary>
        /// Stable, unique URI key: "world://{worldId}/entity/{elementId}/component/{componentId}/property/{propertyName}".
        /// </summary>
        public string Key => $"world://{WorldId}/entity/{ElementId}/component/{ComponentId}/property/{PropertyName}";

        public override string ToString() => Key;
    }
}
