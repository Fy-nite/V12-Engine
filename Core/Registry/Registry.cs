using System;
using V12.Core;

namespace V12
{
    /// <summary>
    /// Static convenience facade over <see cref="GameRoot.Instance"/>.Registry.
    /// Provides the typed Register/Resolve API used by game paks.
    /// </summary>
    public static class Registry
    {
        /// <summary>
        /// Register an instance by its type. The instance is stored under its
        /// type name in the underlying <see cref="Registry.RegistryController"/>.
        /// </summary>
        public static void Register<T>(T instance) where T : class
        {
            var root = GameRoot.Instance;
            if (root == null)
                throw new InvalidOperationException("GameRoot.Instance is not set. Cannot register services before the game root is created.");
            root.Registry.Register(typeof(T).Name, instance);
        }

        /// <summary>
        /// Register an instance under an explicit name.
        /// </summary>
        public static void Register(string name, object instance)
        {
            var root = GameRoot.Instance;
            if (root == null)
                throw new InvalidOperationException("GameRoot.Instance is not set. Cannot register services before the game root is created.");
            root.Registry.Register(name, instance);
        }

        /// <summary>
        /// Resolve the first registered service of type <typeparamref name="T"/>.
        /// Returns null if not found.
        /// </summary>
        public static T? Resolve<T>() where T : class
        {
            var root = GameRoot.Instance;
            if (root == null) return null;
            return root.Registry.Get<T>();
        }

        /// <summary>
        /// Resolve a named service, cast to <typeparamref name="T"/>.
        /// Returns null if not found.
        /// </summary>
        public static T? Get<T>(string name) where T : class
        {
            var root = GameRoot.Instance;
            if (root == null) return null;
            return root.Registry.Get<T>(name);
        }
    }
}
