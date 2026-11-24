using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using V12.Core.Core.Interfaces;
using V12.Core.Input;
using V12.Core.Networking;
using V12.Core.References;

namespace V12.Core.Introspection
{
    /// <summary>
    /// Applies one <see cref="SceneCommand"/> to the live tree. Only ever called
    /// from <see cref="SceneIntrospection.DrainCommands"/> on the tick thread.
    ///
    /// <para>
    /// Mutations route through the same entry points the rest of the engine uses
    /// (<c>World.AddElement</c>, <c>IWorldElement.AddComponent</c>,
    /// <c>IWorldElement.LocalTransform</c>) so <c>DirtyTracker</c> sees them and
    /// replication still works. Nothing here touches a component field directly.
    /// </para>
    /// </summary>
    internal static class SceneCommandApplier
    {
        private static readonly ConcurrentDictionary<string, Type?> ComponentTypeCache =
            new(StringComparer.OrdinalIgnoreCase);

        public static string? Apply(GameRoot root, SceneCommand command, CommandReceipt receipt)
        {
            switch (command.Op)
            {
                case SceneOp.Pause:
                    root.SimulationPaused = command.Value.Kind == SnapshotKind.Bool
                        ? command.Value.Flag
                        : true;
                    return null;

                case SceneOp.SetTimeScale:
                    if (command.Value.Kind is not (SnapshotKind.Float or SnapshotKind.Integer))
                        return "SetTimeScale needs a numeric Value";
                    if (command.Value.Number is < 0 or > 64)
                        return "SetTimeScale must be between 0 and 64";
                    root.TimeScale = (float)command.Value.Number;
                    return null;

                case SceneOp.SendInput:
                    return SendInput(root, command);
            }

            var world = ResolveWorld(root, command, out var element);
            if (world is null)
                return command.WorldId == Guid.Empty
                    ? $"element {command.TargetId} was not found in any loaded world"
                    : $"world {command.WorldId} was not found";

            if (!Writable(root, world, out var authorityError))
                return authorityError;

            receipt.WorldName = world.WorldName;

            if (command.Op == SceneOp.Spawn)
                return Spawn(world, command, receipt);

            if (element is null)
                return $"element {command.TargetId} was not found in world '{world.WorldName}'";

            return command.Op switch
            {
                SceneOp.Despawn => Despawn(world, element, receipt),
                SceneOp.Reparent => Reparent(world, element, command, receipt),
                SceneOp.SetTransform => SetTransform(element, command, receipt),
                SceneOp.SetActive => SetActive(root, element, command, receipt),
                SceneOp.SetProperty => SetProperty(root, element, command, receipt),
                SceneOp.AttachComponent => Attach(world, element, command, receipt),
                SceneOp.DetachComponent => Detach(world, element, command, receipt),
                _ => $"unsupported op {command.Op}",
            };
        }

        private static bool Writable(GameRoot root, World world, out string? error)
        {
            error = null;
            var session = root.FindWorldSession(world);
            if (session is { Authority: WorldAuthority.Remote })
            {
                // A remote world mirrors another authority; a local edit would be
                // overwritten by the next replication batch and fight the tracker.
                error = $"world '{world.WorldName}' is remote-replicated and read-only";
                return false;
            }
            return true;
        }

        private static World? ResolveWorld(GameRoot root, SceneCommand command, out IWorldElement? element)
        {
            element = null;

            if (command.WorldId == Guid.Empty)
            {
                if (command.Op == SceneOp.Spawn)
                    return root.FocusedWorld ?? root.SelectedWorld ?? root.PersistentWorld;

                foreach (var session in root.WorldSessions)
                {
                    var found = session.World.FindElementById(command.TargetId);
                    if (found is null) continue;

                    element = found;
                    return session.World;
                }
                return null;
            }

            var target = root.FindWorldSession(command.WorldId)?.World;
            if (target is null) return null;

            element = command.Op == SceneOp.Spawn ? null : target.FindElementById(command.TargetId);
            return target;
        }

        private static string? Spawn(World world, SceneCommand command, CommandReceipt receipt)
        {
            var element = new Element(string.IsNullOrWhiteSpace(command.Name) ? "Element" : command.Name);

            if (command.Transform is { } transform)
                element.LocalTransform = transform;

            if (command.ParentId != 0)
            {
                var parent = world.FindElementById(command.ParentId);
                if (parent is null) return $"parent {command.ParentId} was not found in world '{world.WorldName}'";
                parent.AddChild(element);
            }
            else
            {
                world.AddElement(element);
            }

            receipt.CreatedId = element.Id;
            return null;
        }

        private static string? Despawn(World world, IWorldElement element, CommandReceipt receipt)
        {
            world.Lock.EnterWriteLock();
            try
            {
                if (element.Parent is { } parent)
                    parent.RemoveChild(element);
                else
                    world.RemoveElement(element);
            }
            finally
            {
                world.Lock.ExitWriteLock();
            }

            GameRoot.Instance?.MarkRenderDirty();
            return null;
        }

        private static string? Reparent(World world, IWorldElement element, SceneCommand command, CommandReceipt receipt)
        {
            IWorldElement? newParent = null;
            if (command.ParentId != 0)
            {
                newParent = world.FindElementById(command.ParentId);
                if (newParent is null)
                    return $"parent {command.ParentId} was not found in world '{world.WorldName}'";

                for (var cursor = newParent; cursor is not null; cursor = cursor.Parent)
                {
                    if (ReferenceEquals(cursor, element))
                        return "cannot reparent an element under its own descendant";
                }
            }

            if (ReferenceEquals(element.Parent, newParent))
                return null;

            world.Lock.EnterWriteLock();
            try
            {
                element.Parent?.RemoveChild(element);

                if (newParent is not null)
                {
                    newParent.AddChild(element);
                }
                else
                {
                    if (!world.Root.Contains(element))
                        world.AddElement(element);
                    else
                        element.Parent = null;
                }
            }
            finally
            {
                world.Lock.ExitWriteLock();
            }

            element.RaiseDirty();
            GameRoot.Instance?.MarkRenderDirty();
            return null;
        }

        private static string? SetTransform(IWorldElement element, SceneCommand command, CommandReceipt receipt)
        {
            if (command.Transform is not { } transform)
                return "SetTransform needs a Transform";

            receipt.PreviousTransform = element.LocalTransform;
            element.LocalTransform = transform;
            return null;
        }

        private static string? SetActive(GameRoot root, IWorldElement element, SceneCommand command, CommandReceipt receipt)
        {
            if (command.Value.Kind != SnapshotKind.Bool)
                return "SetActive needs a boolean Value";

            receipt.PreviousValue = SnapshotValue.From(element.Active);
            element.Active = command.Value.Flag;

            // Element.Active is dirty-tracked; the setter already marks the frame.
            return null;
        }

        private static string? SetProperty(GameRoot root, IWorldElement element, SceneCommand command, CommandReceipt receipt)
        {
            if (string.IsNullOrWhiteSpace(command.Property))
                return "SetProperty needs a Property name";

            var component = FindComponent(element, command);
            if (component is null)
                return DescribeMissingComponent(element, command);

            var property = component.GetType().GetProperty(
                command.Property,
                BindingFlags.Public | BindingFlags.Instance);

            if (property is null)
                return $"component '{component.GetType().Name}' has no property '{command.Property}'";
            if (!property.CanWrite)
                return $"component '{component.GetType().Name}' property '{command.Property}' is read-only";

            if (SceneSnapshotBuilder.TryRead(property.GetValue(component), out var previous))
                receipt.PreviousValue = previous;

            if (!TryConvert(command.Value, property.PropertyType, out var converted))
                return $"cannot assign a {command.Value.Kind} value to {property.PropertyType.Name} '{command.Property}'";

            property.SetValue(component, converted);

            // A plain property set does not raise OnDirty; do it here so the
            // tracker and the next capture both see the change.
            component.RaiseDirty();
            root.MarkRenderDirty();
            return null;
        }

        private static string? Attach(World world, IWorldElement element, SceneCommand command, CommandReceipt receipt)
        {
            var type = ResolveComponentType(command.ComponentType);
            if (type is null)
                return $"component type '{command.ComponentType}' was not found";

            if (Activator.CreateInstance(type) is not IComponent component)
                return $"type '{type.FullName}' is not an IComponent";

            world.Lock.EnterWriteLock();
            try
            {
                // Interface default: OnAttach, add, track with DirtyTracker, raise
                // dirty and re-capture. (Element.AddComponent takes the focused
                // world's lock instead of the element's world, so it is avoided.)
                element.AddComponent(component);
            }
            finally
            {
                world.Lock.ExitWriteLock();
            }

            receipt.CreatedId = component.Id;
            return null;
        }

        private static string? Detach(World world, IWorldElement element, SceneCommand command, CommandReceipt receipt)
        {
            var component = FindComponent(element, command);
            if (component is null)
                return DescribeMissingComponent(element, command);

            world.Lock.EnterWriteLock();
            try
            {
                // Element's override also notifies peers of the removal; the
                // interface default does not.
                if (element is Element concrete)
                    concrete.RemoveComponent(component);
                else
                    element.RemoveComponent(component);
            }
            finally
            {
                world.Lock.ExitWriteLock();
            }

            receipt.PreviousValue = SceneSnapshotBuilder.TryRead(component, out var snapshot)
                ? snapshot
                : SnapshotValue.Null;
            return null;
        }

        private static string? SendInput(GameRoot root, SceneCommand command)
        {
            if (command.Input is null) return "SendInput needs an Input payload";

            var input = root.Registry.Get<IInputService>();
            if (input is null) return "no IInputService is registered";

            input.SendEvent(command.Input.ToEvent(command.RequestId));
            return null;
        }

        private static IComponent? FindComponent(IWorldElement element, SceneCommand command)
        {
            if (command.ComponentId != 0)
                return element.Components.FirstOrDefault(c => c.Id == command.ComponentId);

            if (string.IsNullOrWhiteSpace(command.ComponentType))
                return null;

            return element.Components.FirstOrDefault(c =>
                string.Equals(c.GetType().Name, command.ComponentType, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.GetType().FullName, command.ComponentType, StringComparison.OrdinalIgnoreCase));
        }

        private static string DescribeMissingComponent(IWorldElement element, SceneCommand command)
        {
            string wanted = command.ComponentId != 0
                ? $"id {command.ComponentId}"
                : $"type '{command.ComponentType}'";
            string have = element.Components.Count == 0
                ? "none"
                : string.Join(", ", element.Components.Select(c => c.GetType().Name));
            return $"element has no component matching {wanted} (has: {have})";
        }

        private static Type? ResolveComponentType(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            return ComponentTypeCache.GetOrAdd(name, static typeName =>
            {
                var resolved = ComponentTypeResolver.Resolve(typeName);
                if (resolved is not null) return resolved;

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = assembly.GetTypes(); }
                    catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }
                    catch { continue; }

                    foreach (var type in types)
                    {
                        if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition) continue;
                        if (!typeof(IComponent).IsAssignableFrom(type)) continue;

                        if (string.Equals(type.Name, typeName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(type.FullName, typeName, StringComparison.OrdinalIgnoreCase))
                            return type;
                    }
                }

                return null;
            });
        }

        internal static bool TryConvert(SnapshotValue value, Type target, out object? converted)
        {
            converted = null;
            var nullable = Nullable.GetUnderlyingType(target);
            var type = nullable ?? target;

            if (value.Kind == SnapshotKind.Null)
                return !type.IsValueType || nullable is not null;

            if (type == typeof(string))
            {
                converted = value.Kind == SnapshotKind.Text ? value.Text : value.ToString();
                return true;
            }

            if (type == typeof(bool))
            {
                if (value.Kind != SnapshotKind.Bool) return false;
                converted = value.Flag;
                return true;
            }

            if (type.IsEnum)
            {
                try
                {
                    converted = value.Kind == SnapshotKind.Text
                        ? Enum.Parse(type, value.Text ?? "", ignoreCase: true)
                        : Enum.ToObject(type, (long)value.Number);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            if (IsNumeric(type))
            {
                if (value.Kind is not (SnapshotKind.Integer or SnapshotKind.Float)) return false;
                try
                {
                    converted = Convert.ChangeType(value.Number, type, CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            if (type == typeof(Vector3))
            {
                if (value.Kind != SnapshotKind.Vector3) return false;
                converted = value.Vector;
                return true;
            }

            if (type == typeof(Vector2))
            {
                if (value.Kind != SnapshotKind.Vector3) return false;
                converted = new Vector2(value.Vector.X, value.Vector.Y);
                return true;
            }

            if (type == typeof(Vector4))
            {
                if (value.Kind != SnapshotKind.Vector3) return false;
                converted = new Vector4(value.Vector.X, value.Vector.Y, value.Vector.Z, 0f);
                return true;
            }

            if (type == typeof(Quaternion))
            {
                if (value.Kind != SnapshotKind.Quaternion) return false;
                converted = value.Rotation;
                return true;
            }

            if (type == typeof(System.Drawing.Color))
            {
                if (value.Kind != SnapshotKind.Color) return false;
                converted = System.Drawing.Color.FromArgb(unchecked((int)(long)value.Number));
                return true;
            }

            if (type == typeof(Guid))
            {
                if (value.Kind != SnapshotKind.Text || !Guid.TryParse(value.Text, out var guid)) return false;
                converted = guid;
                return true;
            }

            if (type == typeof(ElementRef))
            {
                if (value.Kind != SnapshotKind.Text) return false;
                converted = new ElementRef(value.Text ?? "");
                return true;
            }

            if (type == typeof(ComponentRef))
            {
                if (value.Kind != SnapshotKind.Text) return false;
                converted = new ComponentRef(value.Text ?? "");
                return true;
            }

            return false;
        }

        private static bool IsNumeric(Type type) => Type.GetTypeCode(type) is
            TypeCode.Byte or TypeCode.SByte or
            TypeCode.Int16 or TypeCode.UInt16 or
            TypeCode.Int32 or TypeCode.UInt32 or
            TypeCode.Int64 or TypeCode.UInt64 or
            TypeCode.Single or TypeCode.Double or TypeCode.Decimal;
    }
}
