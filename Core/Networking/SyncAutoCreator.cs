namespace V12.Core.Networking
{
    using System;
    using System.Reflection;

    public static class SyncAutoCreator
    {
        /// <summary>
        /// Ensure properties marked with [Sync] that are declared as SyncValue&lt;T&gt; are
        /// populated with a new SyncValue&lt;T&gt; instance when currently null. The created
        /// SyncValue is registered automatically by its constructor.
        /// </summary>
        public static void EnsureSyncValues(object target, string basePath = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var t = target.GetType();

            // Properties: if property is of type SyncValue<T> and is null, try to set via setter.
            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var attr = p.GetCustomAttribute<SyncAttribute>();
                if (attr == null) continue;
                if (!p.CanWrite || !p.CanRead) continue;
                var pType = p.PropertyType;
                if (!(pType.IsGenericType && pType.GetGenericTypeDefinition() == typeof(SyncValue<>))) continue;
                var cur = p.GetValue(target);
                if (cur != null) continue;

                var elemType = pType.GetGenericArguments()[0];
                // Build default path
                var path = attr.Path ?? (basePath != null ? $"{basePath}/{p.Name}" : $"{t.Name}/{p.Name}");

                // Find SyncValue<T> constructor: .ctor(string key, T initial = default, bool markDirtyInitially = true)
                var svType = typeof(SyncValue<>).MakeGenericType(elemType);
                var ctor = svType.GetConstructor(new[] { typeof(string), elemType, typeof(bool) });
                object sv = null;
                if (ctor != null)
                {
                    sv = ctor.Invoke(new[] { path, GetDefault(elemType), false });
                }
                else
                {
                    // Fallback: try constructor without default flag
                    var ctor2 = svType.GetConstructor(new[] { typeof(string), elemType });
                    if (ctor2 != null) sv = ctor2.Invoke(new[] { path, GetDefault(elemType) });
                }

                if (sv != null)
                {
                    try { p.SetValue(target, sv); }
                    catch (Exception ex) { _ = ex.Message; }
                }
            }
        }

        private static object GetDefault(Type t) => t.IsValueType ? Activator.CreateInstance(t) : null;
    }
}

