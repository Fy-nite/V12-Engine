using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using MongoDB.Bson.Serialization.Attributes;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Networking;

namespace V12.WorldML
{
    /// <summary>
    /// Serializes a V12 element tree into WorldML XML, mirroring the format the
    /// <see cref="WorldMLParser"/> reads back: each element becomes an
    /// <c>&lt;Element&gt;</c> with an optional <c>&lt;LocalTransform&gt;</c>
    /// block and <c>&lt;Component type="…" … /&gt;</c> children carrying one
    /// attribute per simple property.
    ///
    /// The element owns its pose, so there is no TransformComponent to emit.
    /// </summary>
    public static class WorldMLSerializer
    {
        /// <summary>Properties that must never be serialized: component
        /// plumbing, render interfaces, runtime state, and redundant aliases.</summary>
        private static readonly HashSet<string> ExcludedProps = new(StringComparer.OrdinalIgnoreCase)
        {
            // ComponentBase plumbing
            "Id", "EntityId", "Uid", "Owner", "Name", "Description", "Active", "IsDirty",
            "IsPersistent", // authoring switch: false = omitted entirely, true = default on load
            // Render interfaces / handled separately
            "Transform", "WorldTransform", "LocalTransform", "RenderType",
            "IsWorldLocked", "Material", "PrimaryTexture", "Mesh", "MeshPoints",
            "Indices", "MeshPointsValue", "IndicesValue", "CustomMeshPoints", "CustomIndices",
            // Transform aliases (rotationX/Y/Z carry the truth)
            "Rotation", "RX", "RY", "RZ", "RotX", "RotY", "RotZ",
            // Light aliases (ColorR/G/B + Energy carry the truth)
            "Intensity", "Color", "FieldOfView", "AspectRatio",
            // Audio / interaction runtime state
            "Position", "Forward", "Up", "IsPlaying", "IsPaused",
            "IsPointing", "IsSelecting", "HitEntityId", "HitPosition", "HitNormal",
            // Script runtime
            "Runtime", "IsInitialized", "Key"
        };

        /// <summary>Serialize the given root elements as one WorldML document.</summary>
        public static string Serialize(IEnumerable<IWorldElement> roots, string worldName = "World")
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
            sb.Append($"<World name=\"{Escape(worldName)}\">\n");
            foreach (var el in roots)
                SerializeElement(sb, el, 1);
            sb.Append("</World>\n");
            return sb.ToString();
        }

        private static void SerializeElement(StringBuilder sb, IWorldElement el, int depth)
        {
            // Editor-transient subtrees (gizmo handles) are never saved.
            if (NonPersistentComponent.IsTransient(el)) return;
            var ind = new string('\t', depth);
            sb.Append($"{ind}<Element name=\"{Escape(el.Name ?? "")}\"");
            if (!string.IsNullOrEmpty(el.Description))
                sb.Append($" description=\"{Escape(el.Description)}\"");
            if (el.Uid != Guid.Empty)
                sb.Append($" uid=\"{el.Uid:N}\"");

            var lt = el.LocalTransform;
            bool hasPose = lt.Position != System.Numerics.Vector3.Zero
                || lt.Rotation != System.Numerics.Quaternion.Identity
                || lt.Scale != System.Numerics.Vector3.One;
            bool hasComponents = el.Components is { Count: > 0 };

            if (!hasPose && !hasComponents && el.Children.Count == 0)
            {
                sb.Append(" />\n");
                return;
            }
            sb.Append(">\n");

            // The element owns its pose. Write it as the SDK <LocalTransform>
            // block so it round-trips exactly, with no component mirror.
            if (hasPose)
            {
                sb.Append($"{ind}\t<LocalTransform>\n");
                sb.Append($"{ind}\t\t<Position>{F(lt.Position.X)},{F(lt.Position.Y)},{F(lt.Position.Z)}</Position>\n");
                var (pitch, yaw, roll) = ToEulerDegrees(lt.Rotation);
                sb.Append($"{ind}\t\t<Rotation>{F(pitch)},{F(yaw)},{F(roll)}</Rotation>\n");
                sb.Append($"{ind}\t\t<Scale>{F(lt.Scale.X)},{F(lt.Scale.Y)},{F(lt.Scale.Z)}</Scale>\n");
                sb.Append($"{ind}\t</LocalTransform>\n");
            }

            if (hasComponents)
            {
                foreach (var comp in el.Components)
                {
                    if (!comp.IsPersistent) continue; // runtime/editor-only: not part of the saved world
                    EmitComponent(sb, ind, comp, null);
                }
            }

            foreach (var child in el.Children)
                SerializeElement(sb, child, depth + 1);

            sb.Append($"{ind}</Element>\n");
        }

        private static void EmitComponent(StringBuilder sb, string ind, IComponent comp, string? nameOverride)
        {
            var type = comp.GetType();
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var attrs = new List<string>();

            // A pinned component uid is the only uid ever written. Normal
            // components leave it empty so hand-authored files stay clean.
            if (comp.Uid != Guid.Empty)
                attrs.Add($"uid=\"{comp.Uid:N}\"");

            // Component names are needed by "#name" reference selectors. Only
            // write a name when it differs from the type's default, so files
            // do not gain a redundant name on every component.
            var emittedName = nameOverride;
            if (string.IsNullOrEmpty(emittedName) && !string.IsNullOrWhiteSpace(comp.Name)
                && !string.Equals(comp.Name, DefaultName(type), StringComparison.Ordinal))
                emittedName = comp.Name;

            if (!string.IsNullOrEmpty(emittedName))
                attrs.Add($"name=\"{Escape(emittedName!)}\"");

            // Collect complex representable props to emit as <PropName><Value .../></PropName>
            var complexChildren = new List<(string propName, string valueTag)>();
            foreach (var p in props)
            {
                if (!p.CanWrite) continue;
                if (ExcludedProps.Contains(p.Name)) continue;
                if (p.GetCustomAttribute<BsonIgnoreAttribute>() != null) continue;
                if (typeof(ISyncValue).IsAssignableFrom(p.PropertyType)) continue;
                bool isRef = p.PropertyType == typeof(V12.Core.References.ElementRef)
                          || p.PropertyType == typeof(V12.Core.References.ComponentRef);
                bool isComplex = !isRef && IsComplexType(p.PropertyType);
                if (isComplex && !IsRepresentableComplex(p.PropertyType)) continue;
                if (isComplex) // will be emitted as nested Value, not attribute
                    continue;

                object? val;
                try { val = p.GetValue(comp); }
                catch { continue; }
                if (val == null) continue;

                if (val is V12.Core.References.ElementRef elementRef)
                {
                    if (!elementRef.IsSet) continue;
                    attrs.Add($"{p.Name}=\"{Escape(elementRef.ToSelector())}\"");
                    if (!string.IsNullOrEmpty(elementRef.Hint))
                        attrs.Add($"{p.Name}Hint=\"{Escape(elementRef.Hint!)}\"");
                    continue;
                }

                if (val is V12.Core.References.ComponentRef componentRef)
                {
                    if (!componentRef.IsSet) continue;
                    attrs.Add($"{p.Name}=\"{Escape(componentRef.ToSelector())}\"");
                    if (!string.IsNullOrEmpty(componentRef.Hint))
                        attrs.Add($"{p.Name}Hint=\"{Escape(componentRef.Hint!)}\"");
                    continue;
                }

                if (val is string s)
                {
                    if (string.IsNullOrEmpty(s)) continue;
                    attrs.Add($"{p.Name}=\"{Escape(s)}\"");
                    continue;
                }

                attrs.Add($"{p.Name}=\"{F(val)}\"");
            }

            // Second pass for representable complex types
            foreach (var p in props)
            {
                if (!p.CanWrite) continue;
                if (ExcludedProps.Contains(p.Name)) continue;
                if (p.GetCustomAttribute<BsonIgnoreAttribute>() != null) continue;
                if (typeof(ISyncValue).IsAssignableFrom(p.PropertyType)) continue;
                if (!IsComplexType(p.PropertyType) || !IsRepresentableComplex(p.PropertyType)) continue;

                object? val;
                try { val = p.GetValue(comp); }
                catch { continue; }
                if (val == null) continue;
                // Skip default values to keep output tidy (0,0,0 for Vector3 etc.)
                if (IsDefaultValue(val)) continue;
                var tag = FormatValueTag(val);
                if (string.IsNullOrEmpty(tag)) continue;
                complexChildren.Add((p.Name, tag));
            }

            bool hasComplex = complexChildren.Count > 0;
            if (!hasComplex)
            {
                sb.Append($"{ind}<Component type=\"{type.Name}\"");
                foreach (var a in attrs)
                    sb.Append(' ').Append(a);
                sb.Append(" />\n");
            }
            else
            {
                sb.Append($"{ind}<Component type=\"{type.Name}\"");
                foreach (var a in attrs)
                    sb.Append(' ').Append(a);
                sb.Append(">\n");
                foreach (var (propName, valueTag) in complexChildren)
                {
                    sb.Append($"{ind}\t<{propName}>\n");
                    sb.Append($"{ind}\t\t{valueTag}\n");
                    sb.Append($"{ind}\t</{propName}>\n");
                }
                sb.Append($"{ind}</Component>\n");
            }
        }

        /// <summary>
        /// The name a component gets when it does not override Name itself.
        /// Used to avoid writing redundant name attributes for the common
        /// "PortalComponent" -> "Portal" convention.
        /// </summary>
        private static readonly Dictionary<Type, string?> _defaultNames = new();

        private static string? DefaultName(Type type)
        {
            if (_defaultNames.TryGetValue(type, out var cached)) return cached;

            string? name = null;
            try
            {
                // Do not run constructors during serialization: components may
                // allocate. An uninitialized instance is enough to read the
                // constant virtual Name overrides used for the default.
                if (System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type) is IComponent instance)
                    name = instance.Name;
            }
            catch
            {
                // Types without a usable parameterless shape fall back to the
                // type-name heuristic below.
            }

            name ??= type.Name.EndsWith("Component", StringComparison.Ordinal)
                ? type.Name[..^"Component".Length]
                : type.Name;

            _defaultNames[type] = name;
            return name;
        }

        private static bool IsRepresentableComplex(Type t)
        {
            var ut = Nullable.GetUnderlyingType(t) ?? t;
            if (ut == typeof(Vector2) || ut == typeof(Vector3) || ut == typeof(Vector4) || ut == typeof(Quaternion) || ut == typeof(Color))
                return true;
            // Prowl.Vector types (by full name to avoid hard reference)
            var fn = ut.FullName ?? "";
            if (fn.StartsWith("Prowl.Vector.", StringComparison.Ordinal))
            {
                if (fn.Contains("Float2") || fn.Contains("Float3") || fn.Contains("Float4") || fn.Contains("Double2") || fn.Contains("Double3") || fn.Contains("Double4") || fn.Contains("Int2") || fn.Contains("Int3") || fn.Contains("Int4") || fn.Contains("Color"))
                    return true;
            }
            return false;
        }

        private static bool IsDefaultValue(object val)
        {
            if (val == null) return true;
            var type = val.GetType();
            var def = Activator.CreateInstance(type);
            return Equals(val, def);
        }

        private static string FormatValueTag(object val)
        {
            // System.Numerics
            if (val is Vector2 v2) return $"<Value X=\"{F(v2.X)}\" Y=\"{F(v2.Y)}\" />";
            if (val is Vector3 v3) return $"<Value X=\"{F(v3.X)}\" Y=\"{F(v3.Y)}\" Z=\"{F(v3.Z)}\" />";
            if (val is Vector4 v4) return $"<Value X=\"{F(v4.X)}\" Y=\"{F(v4.Y)}\" Z=\"{F(v4.Z)}\" W=\"{F(v4.W)}\" />";
            if (val is Quaternion q) return $"<Value X=\"{F(q.X)}\" Y=\"{F(q.Y)}\" Z=\"{F(q.Z)}\" W=\"{F(q.W)}\" />";
            if (val is Color c) return $"<Value R=\"{c.R}\" G=\"{c.G}\" B=\"{c.B}\" A=\"{c.A}\" />";
            // Prowl.Vector via reflection to avoid hard dependency on exact struct layout
            var type = val.GetType();
            var fn = type.FullName ?? "";
            if (fn.StartsWith("Prowl.Vector.", StringComparison.Ordinal))
            {
                try
                {
                    var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                    var parts = new List<string>();
                    foreach (var p in props)
                    {
                        if (!p.CanRead) continue;
                        var pv = p.GetValue(val);
                        if (pv == null) continue;
                        parts.Add($"{p.Name}=\"{F(pv)}\"");
                    }
                    if (parts.Count > 0) return $"<Value {string.Join(" ", parts)} />";
                }
                catch { }
            }
            return "";
        }

        /// <summary>True when a property cannot round-trip as a single attribute.</summary>
        private static bool IsComplexType(Type t)
        {
            if (t.IsEnum || t == typeof(string)) return false;
            if (t.IsPrimitive)
                return !(t == typeof(bool) || t == typeof(float) || t == typeof(double)
                    || t == typeof(int) || t == typeof(uint) || t == typeof(long)
                    || t == typeof(short) || t == typeof(byte));
            if (Nullable.GetUnderlyingType(t) is Type ut) return IsComplexType(ut);
            return true;
        }

        private static string F(object val)
        {
            if (val is float f) return f.ToString("0.###", CultureInfo.InvariantCulture);
            if (val is double d) return d.ToString("0.###", CultureInfo.InvariantCulture);
            if (val is bool b) return b ? "True" : "False";
            return Convert.ToString(val, CultureInfo.InvariantCulture) ?? "";
        }

        /// <summary>Quaternion to XYZ degrees in the same order the parser
        /// accepts: X pitch, Y yaw, Z roll.</summary>
        private static (float pitch, float yaw, float roll) ToEulerDegrees(System.Numerics.Quaternion q)
        {
            float sinp = 2 * (q.W * q.X - q.Y * q.Z);
            float pitch = MathF.Abs(sinp) >= 1
                ? MathF.CopySign(MathF.PI / 2, sinp)
                : MathF.Asin(sinp);

            float yaw = MathF.Atan2(
                2 * (q.X * q.Z + q.W * q.Y),
                1 - 2 * (q.X * q.X + q.Y * q.Y));

            float roll = MathF.Atan2(
                2 * (q.X * q.Y + q.W * q.Z),
                1 - 2 * (q.X * q.X + q.Z * q.Z));

            const float rad2deg = 180f / MathF.PI;
            return (pitch * rad2deg, yaw * rad2deg, roll * rad2deg);
        }

        private static string Escape(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;").Replace("'", "&apos;");
        }
    }
}
