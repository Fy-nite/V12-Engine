using System;

namespace V12.Core
{
    /// <summary>
    /// Optional first-stop resolver for component types. The live C# scripting
    /// path installs a resolver backed by <c>CSharpComponentRegistry</c> so a
    /// freshly reloaded script type wins over an older generation that is still
    /// present in the AppDomain. WorldML falls back to the normal assembly scan
    /// when this returns null.
    /// </summary>
    public static class ComponentTypeResolver
    {
        private static Func<string, Type?>? _resolver;

        /// <summary>Installs the resolver. Pass null to clear it.</summary>
        public static void SetResolver(Func<string, Type?>? resolver)
        {
            _resolver = resolver;
        }

        /// <summary>Resolves a component type by simple or qualified name.</summary>
        public static Type? Resolve(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;
            try { return _resolver?.Invoke(typeName); }
            catch { return null; }
        }
    }
}
