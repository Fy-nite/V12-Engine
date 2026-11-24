using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Serialization;
using V12.Core.Interfaces.Renderer;

namespace V12.Core.Introspection
{
    /// <summary>
    /// The value kinds a live component property can surface as in a snapshot.
    /// A deliberately small closed set: tooling (the introspection probe, the
    /// editor tree, tests) needs to render and diff properties without knowing
    /// every component type, and an open <c>object</c> bag serializes badly.
    /// </summary>
    public enum SnapshotKind : byte
    {
        Null = 0,
        Bool = 1,
        Integer = 2,
        Float = 3,
        Text = 4,
        Vector3 = 5,
        Quaternion = 6,
        /// <summary>Packed ARGB in <see cref="SnapshotValue.Number"/>.</summary>
        Color = 7,
        /// <summary>Engine id in <see cref="SnapshotValue.Number"/>; "element" or
        /// "component" in <see cref="SnapshotValue.Text"/>.</summary>
        Reference = 8,
        /// <summary>Collection; element count in <see cref="SnapshotValue.Number"/>.
        /// Contents are not captured (they can be unbounded).</summary>
        List = 9,
    }

    /// <summary>
    /// One property value in a snapshot. A struct rather than
    /// <c>Dictionary&lt;string, object&gt;</c> so the probe can serialize it
    /// without reflection and diffs compare cleanly.
    /// </summary>
    public readonly struct SnapshotValue
    {
        public SnapshotKind Kind { get; }
        public bool Flag { get; }
        public double Number { get; }
        public string? Text { get; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public Vector3 Vector { get; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public Quaternion Rotation { get; }

        [JsonConstructor]
        public SnapshotValue(
            SnapshotKind kind,
            bool flag = false,
            double number = 0,
            string? text = null,
            Vector3 vector = default,
            Quaternion rotation = default)
        {
            Kind = kind;
            Flag = flag;
            Number = number;
            Text = text;
            Vector = vector;
            Rotation = rotation;
        }

        public static SnapshotValue Null => default;

        public static SnapshotValue From(bool value)
            => new(SnapshotKind.Bool, flag: value);

        public static SnapshotValue FromInteger(long value)
            => new(SnapshotKind.Integer, number: value);

        public static SnapshotValue FromFloat(double value)
            => new(SnapshotKind.Float, number: value);

        public static SnapshotValue FromText(string? value)
            => value is null ? Null : new(SnapshotKind.Text, text: value);

        public static SnapshotValue FromVector(Vector3 value)
            => new(SnapshotKind.Vector3, vector: value);

        public static SnapshotValue FromRotation(Quaternion value)
            => new(SnapshotKind.Quaternion, rotation: value);

        public static SnapshotValue FromColor(System.Drawing.Color value)
            => new(SnapshotKind.Color, number: value.ToArgb());

        public static SnapshotValue FromReference(string kind, long id)
            => new(SnapshotKind.Reference, number: id, text: kind);

        public static SnapshotValue FromList(int count)
            => new(SnapshotKind.List, number: count);

        public override string ToString() => Kind switch
        {
            SnapshotKind.Bool => Flag ? "true" : "false",
            SnapshotKind.Integer => ((long)Number).ToString(),
            SnapshotKind.Float => Number.ToString("0.#####"),
            SnapshotKind.Text => Text ?? "",
            SnapshotKind.Vector3 => $"{Vector.X:0.###},{Vector.Y:0.###},{Vector.Z:0.###}",
            SnapshotKind.Quaternion => $"{Rotation.X:0.###},{Rotation.Y:0.###},{Rotation.Z:0.###},{Rotation.W:0.###}",
            SnapshotKind.Color => $"#{unchecked((uint)(int)Number):X8}",
            SnapshotKind.Reference => $"{Text}:{(long)Number}",
            SnapshotKind.List => $"[{Number:0}]",
            _ => "null",
        };
    }

    /// <summary>One component on one element, as of one capture.</summary>
    public sealed class ComponentSnapshot
    {
        public long Id { get; init; }
        /// <summary>Simple type name, e.g. <c>MeshComponent</c>.</summary>
        public string Type { get; init; } = "";
        public string? FullType { get; init; }
        public string? Name { get; init; }
        public bool Active { get; init; }
        public bool IsPersistent { get; init; }
        /// <summary>Sorted so two captures of unchanged state are byte-identical.</summary>
        public SortedDictionary<string, SnapshotValue> Properties { get; init; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// One element. The list is flat and parents are referenced by id, so a
    /// tool can page, diff and filter without rebuilding a tree server-side.
    /// </summary>
    public sealed class ElementSnapshot
    {
        public long Id { get; init; }
        /// <summary>0 when the element sits directly in the world root.</summary>
        public long ParentId { get; init; }
        /// <summary>Distance from the world root. 0 for a root element.</summary>
        public int Depth { get; init; }
        public Guid Uid { get; init; }
        public Guid WorldId { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public bool Active { get; init; }
        public TRS Local { get; init; }
        public Matrix4x4 WorldTransform { get; init; }
        /// <summary>Double-precision global position: world origin plus the
        /// local world transform. Local remains the frame the renderer and
        /// physics actually use.</summary>
        public WorldOffset GlobalPosition { get; init; }
        public List<ComponentSnapshot> Components { get; init; } = new();
    }

    /// <summary>Header for one world in a snapshot.</summary>
    public sealed class WorldInfoSnapshot
    {
        public Guid Id { get; init; }
        public string? Name { get; init; }
        /// <summary><c>Local</c>, <c>Hosted</c> or <c>Remote</c>. Remote worlds reject writes.</summary>
        public string Authority { get; init; } = "";
        public bool IsPersistent { get; init; }
        public bool Hydrated { get; init; }
        public bool Simulated { get; init; }
        public bool Visible { get; init; }
        public int ElementCount { get; init; }
        /// <summary>Double-precision origin of the local simulation bubble.</summary>
        public WorldOffset Origin { get; init; }
    }

    /// <summary>
    /// A consistent read of every loaded world at one point in time. Produced on
    /// the tick thread so callers on other threads never touch live components.
    /// </summary>
    public sealed class WorldSnapshot
    {
        /// <summary>Monotonic capture counter for this <see cref="SceneIntrospection"/>.</summary>
        public long Sequence { get; init; }
        /// <summary>Fixed-step tick at capture time.</summary>
        public long Tick { get; init; }
        public double SimulationTime { get; init; }
        public bool Paused { get; init; }
        public List<WorldInfoSnapshot> Worlds { get; init; } = new();
        public List<ElementSnapshot> Elements { get; init; } = new();
    }
}
