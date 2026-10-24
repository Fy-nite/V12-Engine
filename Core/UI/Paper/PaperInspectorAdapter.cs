using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MongoDB.Bson.Serialization.Attributes;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using PPaper = Prowl.PaperUI.Paper;
using POrigami = Prowl.OrigamiUI.Origami;
using PVector = Prowl.Vector;

namespace V12.Core.UI.Paper
{
    /// <summary>
    /// Renders <see cref="IInspector"/> calls into Paper/Origami widgets. This is the
    /// single inspector both the editor and in-game UI use: components author their
    /// inspector once (<see cref="V12.Core.Core.Interfaces.IComponent.BuildInspector"/>)
    /// and it looks and behaves the same in the Studio and inside a running game.
    ///
    /// When a component does not author an inspector, <see cref="Render"/> auto-generates
    /// rows from its public properties (respecting <c>[BsonIgnore]</c>) and public fields,
    /// which is the V12 equivalent of Origami's field grid.
    /// </summary>
    public sealed class PaperInspectorAdapter : IInspector
    {
        private readonly PPaper _paper;
        private readonly string _id;
        private readonly Action? _changed;
        private readonly Func<string[], Action<string>, bool>? _filePicker;
        private readonly Prowl.Scribe.FontFile? _font;

        private int _row;
        private int _indent;

        public PaperInspectorAdapter(
            PPaper paper,
            string id,
            Action? changed = null,
            Func<string[], Action<string>, bool>? filePicker = null,
            Prowl.Scribe.FontFile? font = null)
        {
            _paper = paper ?? throw new ArgumentNullException(nameof(paper));
            _id = id ?? throw new ArgumentNullException(nameof(id));
            _changed = changed;
            _filePicker = filePicker;
            _font = font ?? POrigami.Current.Font;
        }

        /// <summary>Rows emitted by the last <see cref="Render"/> (mirrors the other adapters).</summary>
        public int ControlCount { get; private set; }

        /// <summary>
        /// Host-provided file picker for <see cref="AssetPath"/> rows. The editor plugs
        /// its native dialog; a game can set this to its own in-game asset browser.
        /// Parameters: (extensions, onPicked) → true when a picker was shown.
        /// </summary>
        public static Func<string[], Action<string>, bool>? HostFilePicker;

        /// <summary>Label gutter width; matches the editor's inspector alignment.</summary>
        public float LabelWidth { get; set; } = 130f;

        /// <summary>
        /// Build the inspector for one object: the component's authored inspector, or an
        /// auto-generated one when it authored none.
        /// </summary>
        public void Render(object target)
        {
            if (target == null) return;
            using (_paper.Column($"{_id}_body").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                .Gap(1f).Enter())
            {
                int before = ControlCount;
                if (target is V12.Core.Core.Interfaces.IComponent component)
                {
                    try { component.BuildInspector(this); }
                    catch (Exception ex) { V12.Core.GameRoot.Log?.Warning(ex, "[PaperInspector] BuildInspector {Type}", target.GetType().Name); }
                }
                if (ControlCount == before)
                    AutoGenerate(target);
            }
        }

        // ── Row helpers ─────────────────────────────────────────────────────

        private string NextId(string kind) => $"{_id}_{kind}{++_row}";

        private void Row(string label, Action drawControl)
        {
            ControlCount++;
            string id = $"{_id}_r{_row}";
            using (_paper.Row(id).Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                .MinHeight(24f).Padding(6f + _indent * 12f, 6f, 0f, 0f).Gap(8f).Enter())
            {
                DrawLabel($"{id}_l", label);
                using (_paper.Column($"{id}_c").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Enter())
                    drawControl();
            }
        }

        private void FullWidth(string title, Action<ElementBuilder> style)
        {
            ControlCount++;
            var el = _paper.Box($"{_id}_fw{_row}").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                .Padding(6f + _indent * 12f, 6f, 0f, 0f);
            style(el);
        }

        private void DrawLabel(string id, string text)
        {
            var el = _paper.Box(id).Width(UnitValue.Pixels(LabelWidth)).Height(UnitValue.Auto);
            if (_font != null && !string.IsNullOrEmpty(text))
                el.Text(text, _font).FontSize(12f).TextColor(POrigami.Current.Ink.C300);
        }

        private void Notify()
        {
            try { _changed?.Invoke(); } catch { }
        }

        private void Header(string title)
        {
            ControlCount++;
            using (_paper.Column($"{_id}_h{_row}").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                .Padding(6f, 6f, 4f + _indent * 12f, 0f).Enter())
            {
                POrigami.Header(_paper, $"{_id}_hdr{_row}", title).Show();
            }
        }

        private void Note(string message, PVector.Color color)
        {
            ControlCount++;
            var el = _paper.Box($"{_id}_n{_row}").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                .Padding(8f + _indent * 12f, 6f, 0f, 0f);
            if (_font != null)
                el.Text(message, _font).FontSize(12f).TextColor(color);
        }

        // ── Layer 1 — primitives ────────────────────────────────────────────

        public void Bool(string label, Func<bool> getter, Action<bool> setter) => Row(label, () =>
        {
            POrigami.Switch(_paper, NextId("b"), getter(), v => { setter(v); Notify(); })
                .NoLabel().Show();
        });

        public void Int(string label, Func<int> getter, Action<int> setter) => Row(label, () =>
        {
            POrigami.NumericField(_paper, NextId("i"), getter(), v => { setter(v); Notify(); })
                .Format("F0").Show();
        });

        public void Float(string label, Func<float> getter, Action<float> setter) => Row(label, () =>
        {
            POrigami.NumericField(_paper, NextId("f"), getter(), v => { setter(v); Notify(); })
                .Format("0.###").Show();
        });

        public void String(string label, Func<string> getter, Action<string> setter) => Row(label, () =>
        {
            POrigami.TextField(_paper, NextId("s"), getter() ?? "", v => { setter(v); Notify(); }).Show();
        });

        public void AssetPath(string label, Func<string> getter, Action<string> setter, string[]? extensions = null)
        {
            var picker = _filePicker ?? HostFilePicker;
            if (picker == null) { String(label, getter, setter); return; }

            ControlCount++;
            string id = $"{_id}_a{++_row}";
            using (_paper.Row($"{id}_r").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                .MinHeight(24f).Padding(6f + _indent * 12f, 6f, 0f, 0f).Gap(6f).Enter())
            {
                DrawLabel($"{id}_l", label);
                // Fixed-width browse button before the field: a stretched sibling in
                // Paper claims the whole row, which would push a trailing button out.
                POrigami.Button(_paper, $"{id}_b", "...", () =>
                {
                    picker(extensions ?? System.Array.Empty<string>(), picked =>
                    {
                        if (string.IsNullOrEmpty(picked)) return;
                        setter(picked);
                        Notify();
                    });
                }).Width(UnitValue.Pixels(30f)).Show();
                POrigami.TextField(_paper, $"{id}_t", getter() ?? "", v => { setter(v); Notify(); })
                    .Show();
            }
        }

        public void Vector2(string label, Func<object> getter, Action<object> setter)
            => VectorRow(label, getter, setter, 2);

        public void Vector3(string label, Func<object> getter, Action<object> setter)
            => VectorRow(label, getter, setter, 3);

        public void Color(string label, Func<object> getter, Action<object> setter) => Row(label, () =>
        {
            var value = getter();
            if (value is System.Drawing.Color dc)
            {
                POrigami.ColorField(_paper, NextId("col"),
                    new PVector.Color(dc.R / 255f, dc.G / 255f, dc.B / 255f, dc.A / 255f),
                    c => { setter(System.Drawing.Color.FromArgb(ToByte(c.A), ToByte(c.R), ToByte(c.G), ToByte(c.B))); Notify(); })
                    .Show();
            }
            else if (value is PVector.Color pc)
            {
                POrigami.ColorField(_paper, NextId("col"), pc,
                    c => { setter(c); Notify(); }).Show();
            }
            else
            {
                ReadOnly(label, value);
            }
        });

        private void VectorRow(string label, Func<object> getter, Action<object> setter, int size)
        {
            Row(label, () =>
            {
                var value = getter();
                string id = NextId("v");
                switch (value)
                {
                    case System.Numerics.Vector2 v2:
                        POrigami.Float2Field(_paper, id,
                            new PVector.Float2(v2.X, v2.Y),
                            v => { setter(new System.Numerics.Vector2(v.X, v.Y)); Notify(); }).Show();
                        break;
                    case System.Numerics.Vector3 v3:
                        POrigami.Float3Field(_paper, id,
                            new PVector.Float3(v3.X, v3.Y, v3.Z),
                            v => { setter(new System.Numerics.Vector3(v.X, v.Y, v.Z)); Notify(); }).Show();
                        break;
                    case PVector.Float2 p2:
                        POrigami.Float2Field(_paper, id, p2, v => { setter(v); Notify(); }).Show();
                        break;
                    case PVector.Float3 p3:
                        POrigami.Float3Field(_paper, id, p3, v => { setter(v); Notify(); }).Show();
                        break;
                    case PVector.Float4 p4:
                        POrigami.Float4Field(_paper, id, p4, v => { setter(v); Notify(); }).Show();
                        break;
                    default:
                        ReadOnly(label, value);
                        break;
                }
            });
        }

        public void Enum<T>(string label, Func<T> getter, Action<T> setter) where T : struct, Enum
        {
            var values = System.Enum.GetValues<T>();
            var names = System.Enum.GetNames<T>();
            Row(label, () =>
            {
                POrigami.Dropdown(_paper, NextId("e"), System.Array.IndexOf(values, getter()),
                    i => { if ((uint)i < (uint)values.Length) { setter(values[i]); Notify(); } },
                    names).Show();
            });
        }

        public void Asset<T>(string label, Func<T> getter, Action<T> setter) where T : class
            => ReadOnly(label, getter()?.ToString());

        public void Entity(string label, Func<object> getter, Action<object> setter)
            => ReadOnly(label, getter());

        // ── Layer 2 — layout ────────────────────────────────────────────────

        public void Section(string title) => Header(title);
        public void Separator() { ControlCount++; POrigami.Separator(_paper, $"{_id}_sep{_row}").Show(); }
        public void Indent() => _indent++;
        public void Unindent() => _indent = Math.Max(0, _indent - 1);
        public void SameLine() { }
        public void Spacing()
        {
            ControlCount++;
            _paper.Box($"{_id}_sp{_row}").Width(UnitValue.Stretch()).Height(4f);
        }

        // ── Layer 3 — stateful ──────────────────────────────────────────────

        public void Foldout(string label, Func<bool> getter, Action<bool> setter) => Header(label);
        public void TreeNode(string label) => Header(label);
        public void TabBar(string label) => Header(label);
        public void CollapsingHeader(string label) => Header(label);

        // ── Layer 4 — feedback ──────────────────────────────────────────────

        public void HelpBox(string message) => Note(message, POrigami.Current.Ink.C300);
        public void Warning(string message) => Note(message, POrigami.Current.Amber.C400);
        public void Error(string message) => Note(message, POrigami.Current.Red.C400);
        public void Tooltip(string text) { }
        public void Disabled(string label, bool disabled) { }

        public void ReadOnly(string label, object? value)
        {
            Row(label, () =>
            {
                string text = value?.ToString() ?? "-";
                var el = _paper.Box(NextId("ro")).Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                    .Padding(2f, 0f, 0f, 0f);
                if (_font != null)
                    el.Text(text, _font).FontSize(12f).TextColor(POrigami.Current.Ink.C400);
            });
        }

        // ── Layer 5 — interaction ───────────────────────────────────────────

        public void Button(string label, Action action)
        {
            ControlCount++;
            using (_paper.Row($"{_id}_btn{_row}").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                .Padding(6f + _indent * 12f, 6f, 2f, 0f).Enter())
            {
                POrigami.Button(_paper, $"{_id}_button{_row}", label, action).Show();
            }
        }

        public void Action(string label, Action action) => Button(label, action);

        public void Command(string label, string command) => Button(label, () => { });

        public void ContextMenu(string label) => Note(label, POrigami.Current.Ink.C300);

        // ── Layer 6 — collections ───────────────────────────────────────────

        public void List<T>(string label, Func<List<T>> getter, Action<List<T>> setter)
            => ReadOnly(label, $"{getter()?.Count ?? 0} items");

        public void Array<T>(string label, Func<T[]> getter, Action<T[]> setter)
            => ReadOnly(label, $"{getter()?.Length ?? 0} items");

        public void Dictionary<K, V>(string label, Func<Dictionary<K, V>> getter, Action<Dictionary<K, V>> setter)
            => ReadOnly(label, $"{getter()?.Count ?? 0} entries");

        // ── Layer 7 — advanced ──────────────────────────────────────────────

        public void ComponentReference(string label, Func<object> getter, Action<object> setter)
            => ReadOnly(label, getter());

        public void WorldReference(string label, Func<object> getter, Action<object> setter)
            => ReadOnly(label, getter());

        public void ObjectIRField(string label, Func<object> getter, Action<object> setter)
            => ReadOnly(label, getter());

        public void NetworkState(string label, Func<object> getter, Action<object> setter)
            => ReadOnly(label, getter());

        public void ReplicationFlags(string label, Func<object> getter, Action<object> setter)
            => ReadOnly(label, getter());

        // ── Auto-generation (no authored inspector) ─────────────────────────

        private static readonly HashSet<string> Excluded = new(StringComparer.Ordinal)
        {
            "Id", "EntityId", "Owner", "IsDirty", "Name", "Description", "IsPersistent",
            "Active", "Runtime", "IsInitialized", "IsPlaying", "Position", "Forward", "Up",
            "RenderType", "Transform", "WorldTransform", "LocalTransform", "IsWorldLocked",
            "Material", "Mesh", "MeshPoints", "Indices", "MeshPointsValue", "IndicesValue",
            "PrimaryTexture", "RX", "RY", "RZ", "Color", "Intensity", "BuildUI",
        };

        private void AutoGenerate(object target)
        {
            var type = target.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

            foreach (var prop in type.GetProperties(flags))
            {
                if (!prop.CanRead || !prop.CanWrite || prop.GetIndexParameters().Length != 0) continue;
                if (Excluded.Contains(prop.Name)) continue;
                if (prop.GetCustomAttribute<BsonIgnoreAttribute>() != null) continue;
                DrawMember(target, prop.Name, prop.PropertyType, () => prop.GetValue(target),
                    v => prop.SetValue(target, v));
            }

            foreach (var field in type.GetFields(flags))
            {
                if (field.IsStatic || field.IsInitOnly) continue;
                if (Excluded.Contains(field.Name)) continue;
                if (field.GetCustomAttribute<BsonIgnoreAttribute>() != null) continue;
                if (field.GetCustomAttribute<NonSerializedAttribute>() != null) continue;
                if (field.GetCustomAttribute<System.Runtime.Serialization.IgnoreDataMemberAttribute>() != null) continue;
                DrawMember(target, field.Name, field.FieldType, () => field.GetValue(target),
                    v => field.SetValue(target, v));
            }
        }

        private void DrawMember(object target, string name, Type type, Func<object?> getter, Action<object?> setter)
        {
            if (type == typeof(bool))
            {
                Bool(name, () => getter() is bool b && b, v => setter(v));
                return;
            }
            if (type == typeof(int) || type == typeof(short) || type == typeof(byte) || type == typeof(long))
            {
                Int(name, () => Convert.ToInt32(getter() ?? 0), v => setter(Convert.ChangeType(v, type)));
                return;
            }
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            {
                Float(name, () => Convert.ToSingle(getter() ?? 0f), v => setter(Convert.ChangeType(v, type)));
                return;
            }
            if (type == typeof(string))
            {
                String(name, () => getter() as string ?? "", v => setter(v));
                return;
            }
            if (type.IsEnum)
            {
                var values = System.Enum.GetValues(type);
                var names = System.Enum.GetNames(type);
                var current = getter();
                int index = System.Array.IndexOf(values, current);
                Row(name, () =>
                {
                    POrigami.Dropdown(_paper, NextId("e"), index,
                        i => { if ((uint)i < (uint)values.Length) { setter(values.GetValue(i)); Notify(); } },
                        names).Show();
                });
                return;
            }
            if (typeof(IList).IsAssignableFrom(type) && type != typeof(string))
            {
                ReadOnly(name, getter() is IList list ? $"{list.Count} items" : "0 items");
                return;
            }

            var value = getter();
            if (value != null)
            {
                var vt = value.GetType();
                if (vt == typeof(System.Numerics.Vector2) || vt == typeof(PVector.Float2))
                { Vector2(name, () => value, v => setter(v)); return; }
                if (vt == typeof(System.Numerics.Vector3) || vt == typeof(PVector.Float3))
                { Vector3(name, () => value, v => setter(v)); return; }
                if (vt == typeof(System.Drawing.Color) || vt == typeof(PVector.Color))
                { Color(name, () => value, v => setter(v)); return; }
            }
            ReadOnly(name, value);
        }

        private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
    }
}
