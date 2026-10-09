using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using MongoDB.Bson.Serialization.Attributes;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.WorldML
{
    /// <summary>
    /// Serializes a V12 element tree into WorldML XML, mirroring the format the
    /// <see cref="WorldMLParser"/> reads back: each element becomes an
    /// <c>&lt;Element&gt;</c> with <c>&lt;Component type="…" … /&gt;</c> children
    /// carrying one attribute per simple property.
    ///
    /// Elements without a TransformComponent get a synthesized one from their
    /// LocalTransform (plus a ScaleComponent when scaled), so every saved
    /// element round-trips its transform.
    /// </summary>
    public static class WorldMLSerializer
    {
        /// <summary>Properties that must never be serialized: component
        /// plumbing, render interfaces, runtime state, and redundant aliases.</summary>
        private static readonly HashSet<string> ExcludedProps = new(StringComparer.OrdinalIgnoreCase)
        {
            // ComponentBase plumbing
            "Id", "EntityId", "Owner", "Name", "Description", "Active", "IsDirty",
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
            if (EditorTransientComponent.IsTransient(el)) return;
            var ind = new string('\t', depth);
            sb.Append($"{ind}<Element name=\"{Escape(el.Name ?? "")}\"");
            if (!string.IsNullOrEmpty(el.Description))
                sb.Append($" description=\"{Escape(el.Description)}\"");

            if ((el.Components == null || el.Components.Count == 0) && el.Children.Count == 0)
            {
                sb.Append(" />\n");
                return;
            }
            sb.Append(">\n");

            // Transform: emit the element's TransformComponent if it has one,
            // otherwise synthesize one from its LocalTransform so position and
            // rotation survive the round-trip. A non-identity scale becomes a
            // ScaleComponent.
            var tc = el.GetComponent<TransformComponent>();
            if (tc != null)
            {
                EmitComponent(sb, ind, tc, null);
            }
            else
            {
                var lt = el.LocalTransform;
                var (yaw, pitch, roll) = ToEulerDegrees(lt.Rotation);
                sb.Append($"{ind}<Component type=\"TransformComponent\" x=\"{F(lt.Position.X)}\" y=\"{F(lt.Position.Y)}\" z=\"{F(lt.Position.Z)}\" rotationX=\"{F(yaw)}\" rotationY=\"{F(pitch)}\" rotationZ=\"{F(roll)}\" />\n");
                if (lt.Scale != System.Numerics.Vector3.One)
                    sb.Append($"{ind}<Component type=\"ScaleComponent\" ScaleX=\"{F(lt.Scale.X)}\" ScaleY=\"{F(lt.Scale.Y)}\" ScaleZ=\"{F(lt.Scale.Z)}\" />\n");
            }

            foreach (var comp in el.Components)
            {
                if (comp is TransformComponent) continue; // emitted above
                EmitComponent(sb, ind, comp, null);
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
            if (!string.IsNullOrEmpty(nameOverride))
                attrs.Add($"name=\"{Escape(nameOverride)}\"");

            foreach (var p in props)
            {
                if (!p.CanWrite) continue;
                if (ExcludedProps.Contains(p.Name)) continue;
                if (p.GetCustomAttribute<BsonIgnoreAttribute>() != null) continue;
                if (IsComplexType(p.PropertyType)) continue;

                object? val;
                try { val = p.GetValue(comp); }
                catch { continue; }
                if (val == null) continue;

                if (val is string s)
                {
                    if (string.IsNullOrEmpty(s)) continue;
                    attrs.Add($"{p.Name}=\"{Escape(s)}\"");
                    continue;
                }

                attrs.Add($"{p.Name}=\"{F(val)}\"");
            }

            sb.Append($"{ind}<Component type=\"{type.Name}\"");
            foreach (var a in attrs)
                sb.Append(' ').Append(a);
            sb.Append(" />\n");
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

        private static (float yaw, float pitch, float roll) ToEulerDegrees(System.Numerics.Quaternion q)
        {
            float siny_cosp = 2 * (q.W * q.Y + q.Z * q.X);
            float cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
            float yaw = MathF.Atan2(siny_cosp, cosy_cosp);
            float sinp = 2 * (q.W * q.X - q.Y * q.Z);
            float pitch = Math.Abs(sinp) >= 1 ? MathF.CopySign(MathF.PI / 2, sinp) : MathF.Asin(sinp);
            float sinr_cosp = 2 * (q.W * q.Z + q.X * q.Y);
            float cosr_cosp = 1 - 2 * (q.X * q.X + q.Z * q.Z);
            float roll = MathF.Atan2(sinr_cosp, cosr_cosp);
            return (yaw * 180f / MathF.PI, pitch * 180f / MathF.PI, roll * 180f / MathF.PI);
        }

        private static string Escape(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;").Replace("'", "&apos;");
        }
    }
}
