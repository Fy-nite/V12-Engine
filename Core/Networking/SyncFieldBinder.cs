using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Bson.Serialization.Attributes;

namespace V12.Core.Networking;

/// <summary>One <c>[Sync]</c> member on a component type.</summary>
internal sealed class SyncFieldDescriptor
{
    public required string Key;
    public required MemberInfo Member;
    public required Type ValueType;
}

/// <summary>
/// Cached per-type view of a component's <c>[Sync]</c> members. Built once, on
/// first use: the tick path must never do reflection over members.
/// <para>
/// Rules: instance fields and properties only, no static/readonly/const, no
/// <see cref="ISyncValue"/> members (those are the explicit
/// <see cref="SyncValue{T}"/> path), no delegates. Both peers derive the same key
/// from the member name, or from <c>[Sync("name")]</c> when given.
/// </para>
/// </summary>
internal static class SyncFieldSchema
{
    private static readonly ConcurrentDictionary<Type, SyncFieldDescriptor[]> Cache = new();

    public static SyncFieldDescriptor[] For(Type type) => Cache.GetOrAdd(type, Build);

    private static SyncFieldDescriptor[] Build(Type type)
    {
        const BindingFlags flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var list = new List<SyncFieldDescriptor>();

        foreach (var field in type.GetFields(flags))
        {
            if (field.IsStatic || field.IsInitOnly || field.IsLiteral) continue;
            if (!TryDescribe(field, field.FieldType, out var key)) continue;
            list.Add(new SyncFieldDescriptor { Key = key, Member = field, ValueType = field.FieldType });
        }

        foreach (var property in type.GetProperties(flags))
        {
            if (property.GetIndexParameters().Length != 0) continue;
            if (property.GetMethod is null || property.SetMethod is null) continue;
            if (!TryDescribe(property, property.PropertyType, out var key)) continue;
            list.Add(new SyncFieldDescriptor { Key = key, Member = property, ValueType = property.PropertyType });
        }

        // Deterministic order: the schema is a value array, not a map.
        list.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        return list.ToArray();
    }

    private static bool TryDescribe(MemberInfo member, Type valueType, out string key)
    {
        key = member.Name;

        var attribute = member.GetCustomAttribute<SyncAttribute>();
        if (attribute is null) return false;

        // Explicit SyncValue<T> members have their own binding path.
        if (typeof(ISyncValue).IsAssignableFrom(valueType)) return false;
        if (typeof(Delegate).IsAssignableFrom(valueType)) return false;

        if (!string.IsNullOrEmpty(attribute.Path))
            key = attribute.Path!;

        return true;
    }
}

/// <summary>
/// Creates and owns the values for one component instance's <c>[Sync]</c> fields.
/// Each field becomes its own <see cref="SyncValue{T}"/>, so the wire format and
/// last-write-wins rules are identical to an explicit value; the binder only
/// supplies the getter/setter and the dirty polling.
/// </summary>
internal sealed class SyncFieldBinder
{
    private readonly SyncFieldHandle[] _handles;

    public SyncFieldBinder(object component)
    {
        var descriptors = SyncFieldSchema.For(component.GetType());
        _handles = new SyncFieldHandle[descriptors.Length];
        for (int i = 0; i < descriptors.Length; i++)
            _handles[i] = SyncFieldHandle.Create(component, descriptors[i]);
    }

    public IEnumerable<ISyncValue> Values
    {
        get
        {
            foreach (var handle in _handles)
                yield return handle.Value;
        }
    }

    /// <summary>
    /// Read every bound field and publish it if it changed since the last tick.
    /// Cheap enough for per-tick use: one delegate call and one equality check.
    /// </summary>
    public void Poll()
    {
        foreach (var handle in _handles)
            handle.Poll();
    }
}

internal abstract class SyncFieldHandle
{
    public abstract ISyncValue Value { get; }
    public abstract void Poll();

    public static SyncFieldHandle Create(object component, SyncFieldDescriptor descriptor)
    {
        var handleType = typeof(SyncFieldHandle<>).MakeGenericType(descriptor.ValueType);
        return (SyncFieldHandle)Activator.CreateInstance(handleType, component, descriptor)!;
    }
}

internal sealed class SyncFieldHandle<T> : SyncFieldHandle
{
    private readonly object _component;
    private readonly Func<object, T> _get;
    private readonly Action<object, T> _set;
    private readonly SyncValue<T> _value;
    private readonly V12.Components.ComponentBase? _owner;
    private bool _applying;

    public SyncFieldHandle(object component, SyncFieldDescriptor descriptor)
    {
        _component = component;
        _owner = component as V12.Components.ComponentBase;
        (_get, _set) = BindAccessors(descriptor.Member);

        // markDirtyInitially: false — the component snapshot already carries the
        // current value, so the channel only has to stream changes after that.
        _value = new SyncValue<T>(
            descriptor.Key,
            _get(component),
            markDirtyInitially: false,
            registerInGlobalRegistry: false);

        _value.OnRemoteApplied += OnRemote;
    }

    public override ISyncValue Value => _value;

    public override void Poll()
    {
        if (_applying) return;

        var current = _get(_component);
        if (!EqualityComparer<T>.Default.Equals(current, _value.Value))
            _value.Set(current);
    }

    private void OnRemote(T value, ulong version)
    {
        _applying = true;
        if (_owner is not null) _owner.IsApplyingRemote = true;
        try
        {
            _set(_component, value);
        }
        finally
        {
            if (_owner is not null) _owner.IsApplyingRemote = false;
            _applying = false;
        }
    }

    private static (Func<object, T>, Action<object, T>) BindAccessors(MemberInfo member)
    {
        // Compiled accessors: one emit per member per process, then ~ns per call.
        // Plain reflection per tick would allocate and box on every poll.
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(T), "value");
        var declaring = member.DeclaringType
            ?? throw new ArgumentException($"Member {member.Name} has no declaring type", nameof(member));
        var castTarget = Expression.Convert(target, declaring);

        Expression access = member switch
        {
            FieldInfo field => Expression.Field(castTarget, field),
            PropertyInfo property => Expression.Property(castTarget, property),
            _ => throw new ArgumentException($"Unsupported [Sync] member {member.Name}", nameof(member)),
        };

        var getter = Expression.Lambda<Func<object, T>>(
            Expression.Convert(access, typeof(T)), target).Compile();

        var setter = Expression.Lambda<Action<object, T>>(
            Expression.Assign(access, Expression.Convert(value, access.Type)), target, value).Compile();

        return (getter, setter);
    }
}
