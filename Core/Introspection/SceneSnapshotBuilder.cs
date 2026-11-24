using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using V12.Core.Core.Interfaces;
using V12.Core.References;
using V12.WorldML;

namespace V12.Core.Introspection
{
    /// <summary>
    /// Flattens live worlds into <see cref="WorldSnapshot"/> on the tick thread.
    /// Property reflection is cached per component type; the excluded set is the
    /// same one <see cref="WorldMLSerializer"/> uses to decide what is
    /// authoritative state, so mesh/material payloads never leak into a read.
    /// </summary>
    internal static class SceneSnapshotBuilder
    {
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

        public static WorldSnapshot Capture(GameRoot root, long sequence)
        {
            var snapshot = new WorldSnapshot
            {
                Sequence = sequence,
                Tick = root.SimulationTick,
                SimulationTime = root.SimulationTime,
                Paused = root.SimulationPaused,
            };

            var seen = new HashSet<Guid>();

            foreach (var session in root.WorldSessions)
            {
                var world = session.World;
                if (!seen.Add(world.Id)) continue;

                snapshot.Worlds.Add(new WorldInfoSnapshot
                {
                    Id = world.Id,
                    Name = world.WorldName,
                    Authority = session.Authority.ToString(),
                    IsPersistent = session.IsPersistent,
                    Hydrated = session.Applied.Hydrated,
                    Simulated = session.Applied.Simulated,
                    Visible = session.Applied.Visible,
                    ElementCount = world._elementsById.Count,
                    Origin = world.Origin,
                });

                // An unhydrated world has no tree worth walking.
                if (!session.Applied.Hydrated) continue;

                world.Lock.EnterReadLock();
                try
                {
                    foreach (var element in world.Root.ToArray())
                        Walk(world, element, world.Id, 0, 0, snapshot.Elements);
                }
                finally
                {
                    world.Lock.ExitReadLock();
                }
            }

            return snapshot;
        }

        private static void Walk(World world, IWorldElement element, Guid worldId, long parentId, int depth, List<ElementSnapshot> sink)
        {
            sink.Add(CaptureElement(world, element, worldId, parentId, depth));

            foreach (var child in element.Children.ToArray())
                Walk(world, child, worldId, element.Id, depth + 1, sink);
        }

        private static ElementSnapshot CaptureElement(World world, IWorldElement element, Guid worldId, long parentId, int depth)
        {
            var source = element.Components.ToArray();
            var components = new List<ComponentSnapshot>(source.Length);
            foreach (var component in source)
                components.Add(CaptureComponent(component));

            var worldTransform = element.WorldTransform;
            var global = world.Origin + new Vector3(
                worldTransform.M41,
                worldTransform.M42,
                worldTransform.M43);

            return new ElementSnapshot
            {
                Id = element.Id,
                ParentId = parentId,
                Depth = depth,
                Uid = element.Uid,
                WorldId = worldId,
                Name = element.Name,
                Description = element.Description,
                Active = element.Active,
                Local = element.LocalTransform,
                WorldTransform = worldTransform,
                GlobalPosition = global,
                Components = components,
            };
        }

        private static ComponentSnapshot CaptureComponent(IComponent component)
        {
            var type = component.GetType();
            var properties = new SortedDictionary<string, SnapshotValue>(StringComparer.Ordinal);

            foreach (var property in ReadableProperties(type))
            {
                object? raw;
                try { raw = property.GetValue(component); }
                catch { continue; }

                if (TryRead(raw, out var value))
                    properties[property.Name] = value;
            }

            return new ComponentSnapshot
            {
                Id = component.Id,
                Type = type.Name,
                FullType = type.FullName,
                Name = component.Name,
                // Active lives on ComponentBase, not IComponent; script components
                // that do not derive from it are always considered active.
                Active = component is not V12.Components.ComponentBase concrete || concrete.Active,
                IsPersistent = component.IsPersistent,
                Properties = properties,
            };
        }

        private static PropertyInfo[] ReadableProperties(Type type)
            => PropertyCache.GetOrAdd(type, static t => t
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .Where(p => !WorldMLSerializer.IsExcludedProperty(p.Name))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToArray());

        /// <summary>
        /// Map a live property value to the closed snapshot value set. Returns
        /// false for types with no safe representation, which the caller skips
        /// rather than stringifying something that could be unbounded.
        /// </summary>
        public static bool TryRead(object? value, out SnapshotValue snapshot)
        {
            snapshot = SnapshotValue.Null;

            switch (value)
            {
                case null:
                    return true;
                case bool flag:
                    snapshot = SnapshotValue.From(flag);
                    return true;
                case string text:
                    snapshot = SnapshotValue.FromText(text);
                    return true;
                case char character:
                    snapshot = SnapshotValue.FromText(character.ToString());
                    return true;
                case float single:
                    snapshot = SnapshotValue.FromFloat(single);
                    return true;
                case double number:
                    snapshot = SnapshotValue.FromFloat(number);
                    return true;
                case decimal money:
                    snapshot = SnapshotValue.FromFloat((double)money);
                    return true;
                case sbyte or byte or short or ushort or int or uint or long or ulong:
                    snapshot = SnapshotValue.FromInteger(Convert.ToInt64(value));
                    return true;
                case Enum enumValue:
                    snapshot = SnapshotValue.FromText(enumValue.ToString());
                    return true;
                case Vector2 v2:
                    snapshot = SnapshotValue.FromVector(new Vector3(v2.X, v2.Y, 0f));
                    return true;
                case Vector3 v3:
                    snapshot = SnapshotValue.FromVector(v3);
                    return true;
                case Vector4 v4:
                    snapshot = SnapshotValue.FromVector(new Vector3(v4.X, v4.Y, v4.Z));
                    return true;
                case Quaternion rotation:
                    snapshot = SnapshotValue.FromRotation(rotation);
                    return true;
                case System.Drawing.Color color:
                    snapshot = SnapshotValue.FromColor(color);
                    return true;
                case Guid guid:
                    snapshot = SnapshotValue.FromText(guid.ToString("D"));
                    return true;
                case ElementRef elementRef:
                    snapshot = SnapshotValue.FromText(elementRef.Selector);
                    return true;
                case ComponentRef componentRef:
                    snapshot = SnapshotValue.FromText(componentRef.Selector);
                    return true;
                case IWorldElement element:
                    snapshot = SnapshotValue.FromReference("element", element.Id);
                    return true;
                case IComponent component:
                    snapshot = SnapshotValue.FromReference("component", component.Id);
                    return true;
                case ICollection collection:
                    snapshot = SnapshotValue.FromList(collection.Count);
                    return true;
                case IEnumerable enumerable:
                {
                    int count = 0;
                    foreach (var _ in enumerable) count++;
                    snapshot = SnapshotValue.FromList(count);
                    return true;
                }
                default:
                    return false;
            }
        }
    }
}
