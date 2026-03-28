namespace V12.Core.UI
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using V12.Core;
    using V12.Core.Core.Interfaces;

    /// <summary>
    /// Builds the complete, engine-agnostic inspector UI tree from a GameRoot / InspectorState.
    /// Frontend renderers (Godot InspectorPanel, Unity InspectorController, …) call
    /// <see cref="BuildFullInspector"/> to get a <see cref="UiElement"/> tree, then walk
    /// that tree to produce native UI nodes.
    /// </summary>
    public static class InspectorBuilder
    {
        private static readonly HashSet<string> _skipProps = new(StringComparer.Ordinal)
            { "Id", "EntityId", "Name", "Description" };

        // ── Full inspector ─────────────────────────────────────────────────────

        /// <summary>
        /// Builds the entire inspector: title bar, world label, split container with
        /// element list (left) and detail panel (right).
        /// </summary>
        public static UiElement BuildFullInspector(GameRoot root, InspectorState state)
        {
            var world = root?.SelectedWorld;
            var outer = new VBoxElement { Name = "InspectorRoot" };

            // ── Title bar ─────────────────────────────────────────────────────
            var titleBar = new PanelElement { StyleHint = "titlebar", MinHeight = 34f };
            var titleRow = new HBoxElement();
            titleRow.Attributes["separation"] = 6;

            var indent = new LabelElement { Text = "" };
            indent.Attributes["minWidth"] = 8f;
            titleRow.Children.Add(indent);

            var title = new LabelElement { Text = "▶  V12 Inspector", StyleHint = "title", FontSize = 14 };
            title.Attributes["expandFill"] = true;
            titleRow.Children.Add(title);

            var closeBtn = new ButtonElement { Text = "×", Flat = true };
            closeBtn.OnPressed = () => state.RequestClose?.Invoke();
            titleRow.Children.Add(closeBtn);

            titleBar.Children.Add(titleRow);
            outer.Children.Add(titleBar);

            // ── World info ────────────────────────────────────────────────────
            outer.Children.Add(new LabelElement
            {
                Text      = world != null ? $"  {world.WorldName}  ({world.Root.Count} element(s))" : "  No active world",
                StyleHint = "accent",
                MinHeight = 22f,
            });

            outer.Children.Add(new SeparatorElement());

            // ── Split: element list | detail ──────────────────────────────────
            var split = new SplitContainerElement { Horizontal = true, FirstChildMinSize = 130f };

            var leftScroll = new ScrollContainerElement();
            leftScroll.Attributes["expandFill"] = true;
            leftScroll.Children.Add(BuildElementList(world, state));
            split.Children.Add(leftScroll);

            var rightScroll = new ScrollContainerElement();
            rightScroll.Attributes["expandFill"] = true;
            rightScroll.Children.Add(BuildDetail(world, state));
            split.Children.Add(rightScroll);

            outer.Children.Add(split);
            return outer;
        }

        // ── Element list (left pane) ───────────────────────────────────────────

        public static UiElement BuildElementList(World? world, InspectorState state)
        {
            var vbox = new VBoxElement { Name = "ElementList" };
            vbox.Attributes["separation"] = 1;

            // "New element" header row
            var addRow = new HBoxElement();
            addRow.Attributes["separation"]  = 4;
            addRow.Attributes["minHeight"]   = 26f;

            var nameField = new TextFieldElement { Text = state.NewElementName };
            nameField.Attributes["expandFill"] = true;
            nameField.OnCommit = txt => { state.NewElementName = txt; };
            addRow.Children.Add(nameField);

            var addBtn = new ButtonElement { Text = "+ Add" };
            addBtn.OnPressed = () =>
            {
                if (world == null) return;
                var name = (state.NewElementName ?? "NewElement").Trim();
                if (string.IsNullOrEmpty(name)) name = "NewElement";
                world.AddElement(new Element(name));
                state.RequestRebuild?.Invoke();
            };
            addRow.Children.Add(addBtn);
            vbox.Children.Add(addRow);

            if (world == null) return vbox;

            foreach (var el in world.Root)
            {
                var captured  = el;
                var isSelected = state.SelectedElement == el;
                var displayName = el.Name ?? "(unnamed)";

                var btn = new ButtonElement
                {
                    Text      = isSelected ? $"► {displayName}" : displayName,
                    Flat      = true,
                    StyleHint = isSelected ? "selected" : null,
                };
                btn.Attributes["expandFill"] = true;
                btn.OnPressed = () =>
                {
                    state.SelectedElement = captured;
                    state.RequestRebuild?.Invoke();
                };
                vbox.Children.Add(btn);
            }

            return vbox;
        }

        // ── Detail pane (right) ────────────────────────────────────────────────

        public static UiElement BuildDetail(World? world, InspectorState state)
        {
            var vbox = new VBoxElement { Name = "Detail" };
            vbox.Attributes["separation"] = 2;

            var el = state.SelectedElement;
            if (world == null || el == null)
            {
                vbox.Children.Add(new LabelElement { Text = "  (no element selected)", StyleHint = "muted" });
                return vbox;
            }

            // ── Name row ──────────────────────────────────────────────────────
            var nameRow = new HBoxElement();
            nameRow.Attributes["separation"] = 6;
            nameRow.Attributes["minHeight"]  = 28f;

            var nameLbl = new LabelElement { Text = "Name:", StyleHint = "muted" };
            nameLbl.Attributes["minWidth"] = 80f;
            nameRow.Children.Add(nameLbl);

            var nameField = new TextFieldElement { Text = el.Name ?? "" };
            nameField.Attributes["expandFill"] = true;
            nameField.OnCommit = txt =>
            {
                try { el.Name = txt.Trim(); TryMarkDirty(el); state.RequestRebuild?.Invoke(); }
                catch { }
            };
            nameRow.Children.Add(nameField);

            var renameBtn = new ButtonElement { Text = "Rename" };
            renameBtn.OnPressed = () => state.RequestRebuild?.Invoke();
            nameRow.Children.Add(renameBtn);

            var removeElBtn = new ButtonElement { Text = "Remove Element", Flat = true, StyleHint = "danger" };
            removeElBtn.OnPressed = () =>
            {
                try
                {
                    world.RemoveElement(el);
                    state.SelectedElement = null;
                    state.RequestRebuild?.Invoke();
                }
                catch { }
            };
            nameRow.Children.Add(removeElBtn);
            vbox.Children.Add(nameRow);

            vbox.Children.Add(new LabelElement
            {
                Text      = $"{el.Components.Count} component(s)",
                StyleHint = "muted",
            });

            // ── Add-component row ──────────────────────────────────────────────
            var compTypes  = GetComponentTypes();
            var addCompRow = new HBoxElement();
            addCompRow.Attributes["separation"] = 6;
            addCompRow.Attributes["minHeight"]  = 26f;

            var picker = new OptionPickerElement { SelectedIndex = state.ComponentPickerIndex };
            picker.Attributes["expandFill"] = true;
            foreach (var t in compTypes) picker.Items.Add(t.Name);
            picker.OnSelectionChanged = idx => { state.ComponentPickerIndex = idx; };
            addCompRow.Children.Add(picker);

            var addCompBtn = new ButtonElement { Text = "+ Component" };
            addCompBtn.OnPressed = () =>
            {
                try
                {
                    var idx  = state.ComponentPickerIndex;
                    if (idx < 0 || idx >= compTypes.Count) return;
                    var inst = Activator.CreateInstance(compTypes[idx]) as IComponent;
                    if (inst == null) return;
                    el.Components.Add(inst);
                    TryMarkDirty(el);
                    state.RequestRebuild?.Invoke();
                }
                catch { }
            };
            addCompRow.Children.Add(addCompBtn);
            vbox.Children.Add(addCompRow);
            vbox.Children.Add(new SeparatorElement());

            if (el.Components.Count == 0)
            {
                vbox.Children.Add(new LabelElement { Text = "  (no components)", StyleHint = "muted" });
                return vbox;
            }

            foreach (var comp in el.Components)
                vbox.Children.Add(BuildForComponent(comp, el, state));

            return vbox;
        }

        // ── Component section ──────────────────────────────────────────────────

        /// <summary>
        /// Builds a component section: header panel + property rows (with live refresh
        /// via <see cref="TextFieldElement.LiveValue"/>) + separator.
        /// </summary>
        public static UiElement BuildForComponent(IComponent comp,
            IWorldElement? owner = null, InspectorState? state = null)
        {
            var section = new VBoxElement { Name = $"Comp_{comp.Name}" };

            // Header
            var header = new PanelElement { StyleHint = "compheader", MinHeight = 22f };
            var headerRow = new HBoxElement();
            headerRow.Attributes["fullRect"] = true;

            var headerLbl = new LabelElement { Text = $"  {comp.Name}", StyleHint = "accent" };
            headerLbl.Attributes["expandFill"] = true;
            headerRow.Children.Add(headerLbl);

            var remBtn = new ButtonElement { Text = "Remove", Flat = true, StyleHint = "danger" };
            remBtn.OnPressed = () =>
            {
                try
                {
                    owner?.Components.Remove(comp);
                    if (owner != null) TryMarkDirty(owner);
                    state?.RequestRebuild?.Invoke();
                }
                catch { }
            };
            headerRow.Children.Add(remBtn);
            header.Children.Add(headerRow);
            section.Children.Add(header);

            var props = comp.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && !_skipProps.Contains(p.Name) && IsSimpleType(p.PropertyType))
                .ToArray();

            if (props.Length == 0)
            {
                section.Children.Add(new LabelElement { Text = "  (no properties)", StyleHint = "muted" });
            }
            else
            {
                foreach (var prop in props)
                {
                    var capturedProp  = prop;
                    var capturedComp  = comp;
                    var capturedOwner = owner;

                    var row = new HBoxElement();
                    row.Attributes["separation"] = 4;

                    var keyLbl = new LabelElement { Text = $"  {prop.Name}", StyleHint = "muted" };
                    keyLbl.Attributes["expandFill"] = true;
                    keyLbl.Attributes["minWidth"]   = 100f;
                    row.Children.Add(keyLbl);

                    string initial;
                    try { initial = FormatValue(capturedProp.GetValue(capturedComp)); }
                    catch { initial = "?"; }

                    var tf = new TextFieldElement { Text = initial };
                    tf.Attributes["expandFill"] = true;

                    // Live refresh: renderer calls this at 10 Hz without rebuilding the tree
                    tf.LiveValue = () =>
                    {
                        try { return FormatValue(capturedProp.GetValue(capturedComp)); }
                        catch { return "?"; }
                    };

                    tf.OnCommit = txt =>
                    {
                        try
                        {
                            var conv = ConvertFromString(txt, capturedProp.PropertyType);
                            if (conv != null) capturedProp.SetValue(capturedComp, conv);
                            if (capturedOwner != null) TryMarkDirty(capturedOwner);
                        }
                        catch { }
                    };

                    row.Children.Add(tf);
                    section.Children.Add(row);
                }
            }

            section.Children.Add(new SeparatorElement());
            return section;
        }

        // ── Backwards-compat entry point ──────────────────────────────────────

        /// <summary>Builds a simple element inspector (no title bar / split layout).</summary>
        public static UiElement BuildForElement(IWorldElement element, Action<IWorldElement>? onRemove = null)
        {
            var root  = new VBoxElement { Name = "InspectorRoot" };
            root.Children.Add(new LabelElement { Text = element.Name ?? "(unnamed)", FontSize = 14 });
            root.Children.Add(new LabelElement
            {
                Text      = $"{element.Components.Count} component(s)",
                StyleHint = "muted",
            });
            foreach (var comp in element.Components)
                root.Children.Add(BuildForComponent(comp, element));

            if (onRemove != null)
            {
                var removeBtn = new ButtonElement { Text = "Remove Element", StyleHint = "danger" };
                removeBtn.OnPressed = () => { try { onRemove(element); } catch { } };
                root.Children.Add(removeBtn);
            }
            return root;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void TryMarkDirty(object target)
        {
            try
            {
                var mi = target.GetType().GetMethod("MarkDirty", BindingFlags.Public | BindingFlags.Instance);
                mi?.Invoke(target, null);
            }
            catch { }
        }

        internal static string FormatValue(object? val) => val switch
        {
            null   => "null",
            float  f => f.ToString("F3"),
            double d => d.ToString("F3"),
            bool   b => b ? "true" : "false",
            _        => val?.ToString() ?? "null",
        };

        private static List<Type> GetComponentTypes()
        {
            try
            {
                return AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic)
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                    .Where(t => typeof(IComponent).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                    .OrderBy(t => t.Name)
                    .ToList();
            }
            catch { return new List<Type>(); }
        }

        private static bool IsSimpleType(Type t)
        {
            if (t == typeof(string) || t == typeof(bool) || t == typeof(int) || t == typeof(long) ||
                t == typeof(float) || t == typeof(double) || t.IsEnum)
                return true;
            var under = Nullable.GetUnderlyingType(t);
            return under != null && IsSimpleType(under);
        }

        private static object? ConvertFromString(string text, Type targetType)
        {
            if (targetType == typeof(string)) return text;
            try
            {
                if (targetType == typeof(bool))
                    return text.Trim().ToLowerInvariant() is "true" or "1" or "yes";
                if (targetType == typeof(float))
                    return float.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
                if (targetType == typeof(double))
                    return double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
                if (targetType == typeof(int))
                    return int.Parse(text);
                if (targetType == typeof(long))
                    return long.Parse(text);
                if (targetType.IsEnum)
                    return Enum.Parse(targetType, text, ignoreCase: true);
                return Convert.ChangeType(text, targetType, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { return null; }
        }
    }
}

