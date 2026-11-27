using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace V12.Core.Networking
{
    /// <summary>
    /// Finds every value a component contributes to replication: the
    /// <see cref="ISyncValue"/> members it declares, plus anything an
    /// <see cref="ISyncValueProvider"/> creates (the <c>[Sync]</c> field binder).
    /// <para>
    /// The session channel uses this to register values, and teardown uses it to
    /// release them. It is the one place that knows a component's value layout, so
    /// registration and cleanup cannot drift apart.
    /// </para>
    /// </summary>
    public static class SyncValueScanner
    {
        private static readonly ConcurrentDictionary<Type, MemberInfo[]> Members = new();

        public static IEnumerable<ISyncValue> Enumerate(object target)
        {
            if (target == null) yield break;

            // Components with a field binder expose their values directly; the
            // binder creates one SyncValue per [Sync] member, so the member scan
            // below cannot see them.
            if (target is ISyncValueProvider provider)
            {
                foreach (var provided in provider.GetSyncValues())
                    if (provided is not null) yield return provided;
            }

            foreach (var value in EnumerateMembers(target))
                yield return value;
        }

        /// <summary>
        /// Only the values a component declares as <see cref="ISyncValue"/>
        /// members. Teardown uses this: binder-created values never enter the
        /// process-wide registry, and enumerating them would build a binder for a
        /// component that is already going away.
        /// </summary>
        public static IEnumerable<ISyncValue> EnumerateMembers(object target)
        {
            if (target == null) yield break;

            Type type = target.GetType();
            var members = Members.GetOrAdd(type, static t =>
                t.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.MemberType is MemberTypes.Property or MemberTypes.Field)
                    .Where(m => m switch
                    {
                        PropertyInfo p => p.GetIndexParameters().Length == 0,
                        FieldInfo f => !f.IsStatic,
                        _ => false,
                    })
                    .Where(m => m switch
                    {
                        PropertyInfo p => typeof(ISyncValue).IsAssignableFrom(p.PropertyType),
                        FieldInfo f => typeof(ISyncValue).IsAssignableFrom(f.FieldType),
                        _ => false,
                    })
                    .ToArray());

            foreach (var member in members)
            {
                object? value;
                try
                {
                    value = member switch
                    {
                        PropertyInfo p => p.GetValue(target),
                        FieldInfo f => f.GetValue(target),
                        _ => null,
                    };
                }
                catch
                {
                    continue;
                }

                if (value is ISyncValue sync) yield return sync;
            }
        }
    }
}
