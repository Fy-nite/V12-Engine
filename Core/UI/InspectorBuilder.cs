//namespace V12.Core.UI
//{
//    using System;
//    using System.Collections.Generic;
//    using System.Linq;
//    using System.Reflection;
//    using V12.Core;
//    using V12.Core.Core.Interfaces;
//    using V12.Components.UI;

//    /// <summary>
//    /// Builds the complete, engine-agnostic inspector UI tree from a GameRoot / InspectorState.
//    /// Frontend renderers (Godot InspectorPanel, Unity InspectorController, …) call
//    /// <see cref="BuildFullInspector"/> to get a <see cref="UiElement"/> tree, then walk
//    /// that tree to produce native UI nodes.
//    /// </summary>
//    public static class InspectorBuilder
//    {
//        private static readonly HashSet<string> _skipProps = new(StringComparer.Ordinal)
//            { "Id", "EntityId", "Name", "Description" };

//        // ── Full inspector ─────────────────────────────────────────────────────

//        /// <summary>
//        /// Builds the entire inspector: title bar, world label, split container with
//        /// element list (left) and detail panel (right).
//        /// </summary>
//        public static UiElement BuildFullInspector(GameRoot root, InspectorState state)
//        {
//            var world = root?.SelectedWorld;
//            var outer = new VBoxElement { Name = "InspectorRoot" };

//            // ── Title bar ─────────────────────────────────────────────────────
//            var titleBar = new PanelElement { StyleHint = "titlebar", MinHeight = 34f };
//            var titleRow = new HBoxElement();
//            titleRow.Attributes["separation"] = 6;

//            var indent = new LabelElement { Text = "" };
//            indent.Attributes["minWidth"] = 8f;
//            titleRow.Children.Add(indent);

//            var title = new LabelElement { Text = "▶  V12 Inspector", StyleHint = "title", FontSize = 14 };
//            title.Attributes["expandFill"] = true;
//            titleRow.Children.Add(title);

//            var closeBtn = new ButtonElement { Text = "×", Flat = true };
//            closeBtn.OnPressed = () => state.RequestClose?.Invoke();
//            titleRow.Children.Add(closeBtn);

//            titleBar.Children.Add(titleRow);
//            outer.Children.Add(titleBar);

//            // ── World info ────────────────────────────────────────────────────
//            outer.Children.Add(new LabelElement
//            {
//                Text      = world != null ? $"  {world.WorldName}  ({world.Root.Count} element(s))" : "  No active world",
//                StyleHint = "accent",
//                MinHeight = 22f,
//            });

//            outer.Children.Add(new SeparatorElement());

//            // ── Split: element list | detail ──────────────────────────────────
//            var split = new SplitContainerElement { Horizontal = true, FirstChildMinSize = 130f };

//            var leftScroll = new ScrollContainerElement();
//            leftScroll.Attributes["expandFill"] = true;
//            leftScroll.Children.Add(BuildElementList(world, state));
//            split.Children.Add(leftScroll);

//            var rightScroll = new ScrollContainerElement();
//            rightScroll.Attributes["expandFill"] = true;
//            rightScroll.Children.Add(BuildDetail(world, state));
//            split.Children.Add(rightScroll);

//            outer.Children.Add(split);
//            return outer;
//        }

//        // ── Element list (left pane) ───────────────────────────────────────────

//        public static UiElement BuildElementList(World? world, InspectorState state)
//        {
//            var vbox = new VBoxElement { Name = "ElementList" };
//            vbox.Attributes["separation"] = 1;

//            // "New element" header row
//            var addRow = new HBoxElement();
//            addRow.Attributes["separation"]  = 4;
//            addRow.Attributes["minHeight"]   = 26f;

//            var nameField = new TextFieldElement { Text = state.NewElementName };
//            nameField.Attributes["expandFill"] = true;
//            nameField.OnCommit = txt => { state.NewElementName = txt; };
//            addRow.Children.Add(nameField);

//            var addBtn = new ButtonElement { Text = "+ Add" };
//            addBtn.OnPressed = () =>
//            {
//                if (world == null) return;
//                var name = (state.NewElementName ?? "NewElement").Trim();
//                if (string.IsNullOrEmpty(name)) name = "NewElement";
//                world.AddElement(new Element(name));
//                state.RequestRebuild?.Invoke();
//            };
//            addRow.Children.Add(addBtn);
//            vbox.Children.Add(addRow);

//            if (world == null) return vbox;

//            foreach (var el in world.Root)
//            {
//                var captured  = el;
//                var isSelected = state.SelectedElement == el;
//                var displayName = el.Name ?? "(unnamed)";

//                var btn = new ButtonElement
//                {
//                    Text      = isSelected ? $"► {displayName}" : displayName,
//                    Flat      = true,
//                    StyleHint = isSelected ? "selected" : null,
//                };
//                btn.Attributes["expandFill"] = true;
//                btn.OnPressed = () =>
//                {
//                    state.SelectedElement = captured;
//                    state.RequestRebuild?.Invoke();
//                };
//                vbox.Children.Add(btn);
//            }

//            return vbox;
//        }

//        // ── Detail pane (right) ────────────────────────────────────────────────

//        public static UiElement BuildDetail(World? world, InspectorState state)
//        {
//            var vbox = new VBoxElement { Name = "Detail" };
//            vbox.Attributes["separation"] = 2;

//            var el = state.SelectedElement;
//            if (world == null || el == null)
//            {
//                vbox.Children.Add(new LabelElement { Text = "  (no element selected)", StyleHint = "muted" });
//                return vbox;
//            }

//            // ── Name row ──────────────────────────────────────────────────────
//            var nameRow = new HBoxElement();
//            nameRow.Attributes["separation"] = 6;
//            nameRow.Attributes["minHeight"]  = 28f;

//            var nameLbl = new LabelElement { Text = "Name:", StyleHint = "muted" };
//            nameLbl.Attributes["minWidth"] = 80f;
//            nameRow.Children.Add(nameLbl);

//            var nameField = new TextFieldElement { Text = el.Name ?? "" };
//            nameField.Attributes["expandFill"] = true;
//            nameField.OnCommit = txt =>
//            {
//                try { el.Name = txt.Trim(); TryMarkDirty(el); state.RequestRebuild?.Invoke(); }
//                catch { }
//            };
//            nameRow.Children.Add(nameField);

//            var renameBtn = new ButtonElement { Text = "Rename" };
//            renameBtn.OnPressed = () => state.RequestRebuild?.Invoke();
//            nameRow.Children.Add(renameBtn);

//            var removeElBtn = new ButtonElement { Text = "Remove Element", Flat = true, StyleHint = "danger" };
//            removeElBtn.OnPressed = () =>
//            {
//                try
//                {
//                    world.RemoveElement(el);
//                    state.SelectedElement = null;
//                    state.RequestRebuild?.Invoke();
//                }
//                catch { }
//            };
//            nameRow.Children.Add(removeElBtn);
//            vbox.Children.Add(nameRow);

//            vbox.Children.Add(new LabelElement
//            {
//                Text      = $"{el.Components.Count} component(s)",
//                StyleHint = "muted",
//            });

//            // ── Add-component row ──────────────────────────────────────────────
//            var compTypes  = GetComponentTypes();
//            var addCompRow = new HBoxElement();
//            addCompRow.Attributes["separation"] = 6;
//            addCompRow.Attributes["minHeight"]  = 26f;

//            var picker = new OptionPickerElement { SelectedIndex = state.ComponentPickerIndex };
//            picker.Attributes["expandFill"] = true;
//            foreach (var t in compTypes) picker.Items.Add(t.Name);
//            picker.OnSelectionChanged = idx => { state.ComponentPickerIndex = idx; };
//            addCompRow.Children.Add(picker);

//            var addCompBtn = new ButtonElement { Text = "+ Component" };
//            addCompBtn.OnPressed = () =>
//            {
//                try
//                {
//                    var idx  = state.ComponentPickerIndex;
//                    if (idx < 0 || idx >= compTypes.Count) return;
//                    var inst = Activator.CreateInstance(compTypes[idx]) as IComponent;
//                    if (inst == null) return;
//                    el.Components.Add(inst);
//                    TryMarkDirty(el);
//                    state.RequestRebuild?.Invoke();
//                }
//                catch { }
//            };
//            addCompRow.Children.Add(addCompBtn);
//            vbox.Children.Add(addCompRow);
//            vbox.Children.Add(new SeparatorElement());

//            if (el.Components.Count == 0)
//            {
//                vbox.Children.Add(new LabelElement { Text = "  (no components)", StyleHint = "muted" });
//                return vbox;
//            }

//            foreach (var comp in el.Components)
//                vbox.Children.Add(BuildForComponent(comp, el, state));

//            return vbox;
//        }

//        // ── Component section ──────────────────────────────────────────────────

//        /// <summary>
//        /// Builds a component section: header panel + property rows (with live refresh
//        /// via <see cref="TextFieldElement.LiveValue"/>) + separator.
//        /// </summary>
//        public static UiElement BuildForComponent(IComponent comp,
//            IWorldElement? owner = null, InspectorState? state = null)
//        {
//            var section = new VBoxElement { Name = $"Comp_{comp.Name}" };

//            // Header
//            var header = new PanelElement { StyleHint = "compheader", MinHeight = 22f };
//            var headerRow = new HBoxElement();
//            headerRow.Attributes["fullRect"] = true;

//            var headerLbl = new LabelElement { Text = $"  {comp.Name}", StyleHint = "accent" };
//            headerLbl.Attributes["expandFill"] = true;
//            headerRow.Children.Add(headerLbl);

//            var remBtn = new ButtonElement { Text = "Remove", Flat = true, StyleHint = "danger" };
//            remBtn.OnPressed = () =>
//            {
//                try
//                {
//                    owner?.Components.Remove(comp);
//                    if (owner != null) TryMarkDirty(owner);
//                    state?.RequestRebuild?.Invoke();
//                }
//                catch { }
//            };
//            headerRow.Children.Add(remBtn);
//            header.Children.Add(headerRow);
//            section.Children.Add(header);

//            var props = comp.GetType()
//                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
//                .Where(p => p.CanRead && !_skipProps.Contains(p.Name) && IsSimpleType(p.PropertyType))
//                .ToArray();

//            if (props.Length == 0)
//            {
//                section.Children.Add(new LabelElement { Text = "  (no properties)", StyleHint = "muted" });
//            }
//            else
//            {
//                foreach (var prop in props)
//                {
//                    var capturedProp  = prop;
//                    var capturedComp  = comp;
//                    var capturedOwner = owner;

//                    var row = new HBoxElement();
//                    row.Attributes["separation"] = 4;

//                    var keyLbl = new LabelElement { Text = $"  {prop.Name}", StyleHint = "muted" };
//                    keyLbl.Attributes["expandFill"] = true;
//                    keyLbl.Attributes["minWidth"]   = 100f;
//                    row.Children.Add(keyLbl);

//                    string initial;
//                    try { initial = FormatValue(capturedProp.GetValue(capturedComp)); }
//                    catch { initial = "?"; }

//                    var tf = new TextFieldElement { Text = initial };
//                    tf.Attributes["expandFill"] = true;

//                    // Live refresh: renderer calls this at 10 Hz without rebuilding the tree
//                    tf.LiveValue = () =>
//                    {
//                        try { return FormatValue(capturedProp.GetValue(capturedComp)); }
//                        catch { return "?"; }
//                    };

//                    tf.OnCommit = txt =>
//                    {
//                        try
//                        {
//                            var conv = ConvertFromString(txt, capturedProp.PropertyType);
//                            if (conv != null) capturedProp.SetValue(capturedComp, conv);
//                            if (capturedOwner != null) TryMarkDirty(capturedOwner);
//                        }
//                        catch { }
//                    };

//                    row.Children.Add(tf);
//                    section.Children.Add(row);
//                }
//            }

//            section.Children.Add(new SeparatorElement());
//            return section;
//        }

//        // ── Backwards-compat entry point ──────────────────────────────────────

//        /// <summary>Builds a simple element inspector (no title bar / split layout).</summary>
//        public static UiElement BuildForElement(IWorldElement element, Action<IWorldElement>? onRemove = null)
//        {
//            var root  = new VBoxElement { Name = "InspectorRoot" };
//            root.Children.Add(new LabelElement { Text = element.Name ?? "(unnamed)", FontSize = 14 });
//            root.Children.Add(new LabelElement
//            {
//                Text      = $"{element.Components.Count} component(s)",
//                StyleHint = "muted",
//            });
//            foreach (var comp in element.Components)
//                root.Children.Add(BuildForComponent(comp, element));

//            if (onRemove != null)
//            {
//                var removeBtn = new ButtonElement { Text = "Remove Element", StyleHint = "danger" };
//                removeBtn.OnPressed = () => { try { onRemove(element); } catch { } };
//                root.Children.Add(removeBtn);
//            }
//            return root;
//        }

//        // ── Helpers ───────────────────────────────────────────────────────────

//        private static void TryMarkDirty(object target)
//        {
//            try
//            {
//                var mi = target.GetType().GetMethod("MarkDirty", BindingFlags.Public | BindingFlags.Instance);
//                mi?.Invoke(target, null);
//            }
//            catch { }
//        }

//        internal static string FormatValue(object? val) => val switch
//        {
//            null   => "null",
//            float  f => f.ToString("F3"),
//            double d => d.ToString("F3"),
//            bool   b => b ? "true" : "false",
//            _        => val?.ToString() ?? "null",
//        };

//        private static List<Type> GetComponentTypes()
//        {
//            try
//            {
//                return AppDomain.CurrentDomain.GetAssemblies()
//                    .Where(a => !a.IsDynamic)
//                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
//                    .Where(t => typeof(IComponent).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
//                    .OrderBy(t => t.Name)
//                    .ToList();
//            }
//            catch { return new List<Type>(); }
//        }

//        private static bool IsSimpleType(Type t)
//        {
//            if (t == typeof(string) || t == typeof(bool) || t == typeof(int) || t == typeof(long) ||
//                t == typeof(float) || t == typeof(double) || t.IsEnum)
//                return true;
//            var under = Nullable.GetUnderlyingType(t);
//            return under != null && IsSimpleType(under);
//        }

//        private static object? ConvertFromString(string text, Type targetType)
//        {
//            if (targetType == typeof(string)) return text;
//            try
//            {
//                if (targetType == typeof(bool))
//                    return text.Trim().ToLowerInvariant() is "true" or "1" or "yes";
//                if (targetType == typeof(float))
//                    return float.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
//                if (targetType == typeof(double))
//                    return double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
//                if (targetType == typeof(int))
//                    return int.Parse(text);
//                if (targetType == typeof(long))
//                    return long.Parse(text);
//                if (targetType.IsEnum)
//                    return Enum.Parse(targetType, text, ignoreCase: true);
//                return Convert.ChangeType(text, targetType, System.Globalization.CultureInfo.InvariantCulture);
//            }
//            catch { return null; }
//        }

//        // ── Builder that emits world-elements via IUIBuilder (engine-backed) ──

//        /// <summary>
//        /// Builds the inspector UI directly into an engine-provided <see cref="IUIBuilder"/>
//        /// as world-elements. Returns the root world-element created for the inspector.
//        /// This lets engine glue (e.g. Godot/Unity) construct the inspector using the
//        /// same V12 element system used for in-world UI.
//        /// </summary>
//        public static IWorldElement BuildFullInspector(IUIBuilder ui, GameRoot root, InspectorState state)
//        {
//            var world = root?.SelectedWorld;
            
//            var outer = ui.VLayout(ui.Root, "InspectorRoot");
//            outer.AddComponent(new LayoutElementComponent { PreferredWidth = 380f });

//            // Title row
//            var titleRow = ui.HLayout(outer, "TitleRow");
//            titleRow.AddComponent(new UIStyleComponent { StyleHint = "titlebar" });
//            titleRow.AddComponent(new LayoutElementComponent { PreferredHeight = 30f });
            
//            ui.Label(titleRow, "Indent", "");
//            var title = ui.Label(titleRow, "Title", "▶  V12 Inspector");
//            title.AddComponent(new UIStyleComponent { StyleHint = "title" });
            
//            ui.Rect(titleRow, "TitleSpacer", 0f, 0f, "");
//            ui.Button(titleRow, "Close", () => state.RequestClose?.Invoke());

//            // World info
//            var worldInfo = world != null
//                ? $"  {world.WorldName}  ({world.Root.Count} element(s))"
//                : "  No active world";
//            var worldLbl = ui.Label(outer, "WorldInfo", worldInfo);
//            worldLbl.AddComponent(new UIStyleComponent { StyleHint = "accent" });

//            // Split: left list | right detail
//            var split = ui.HLayout(outer, "Split");
//            split.AddComponent(new LayoutElementComponent { PreferredHeight = 400f });

//            // Left: element list
//            var left = ui.VLayout(split, "ElementList");
//            left.AddComponent(new LayoutElementComponent { PreferredWidth = 120f });

//            // New element row
//            var addRow = ui.HLayout(left, "AddRow");
//            ui.TextInput(addRow, "NewName", state.NewElementName ?? "");
//            ui.Button(addRow, "AddBtn", () =>
//            {
//                try
//                {
//                    if (world == null) return;
//                    var name = (state.NewElementName ?? "NewElement").Trim();
//                    if (string.IsNullOrEmpty(name)) name = "NewElement";
//                    world.AddElement(new Element(name));
//                    state.RequestRebuild?.Invoke();
//                }
//                catch { }
//            });

//            if (world != null)
//            {
//                foreach (var el in world.Root)
//                {
//                    BuildTreeElement(ui, left, el, state, 0);
//                }
//            }

//            // Right: detail pane
//            var right = ui.VLayout(split, "Detail");
//            right.AddComponent(new LayoutElementComponent { PreferredWidth = 250f });
            
//            var selected = state.SelectedElement;
//            if (world == null || selected == null)
//            {
//                var noSel = ui.Label(right, "NoSel", "  (no element selected)");
//                noSel.AddComponent(new UIStyleComponent { StyleHint = "muted" });
//                return outer;
//            }

//            // Name row
//            var nameRow = ui.HLayout(right, "NameRow");
//            ui.Label(nameRow, "NameLbl", "Name:");
//            ui.TextInput(nameRow, "NameField", selected.Name ?? "");
//            ui.Button(nameRow, "Rename", () => state.RequestRebuild?.Invoke());
//            ui.Button(nameRow, "RemoveEl", () =>
//            {
//                try { world.RemoveElement(selected); state.SelectedElement = null; state.RequestRebuild?.Invoke(); } catch { }
//            });

//            var countLbl = ui.Label(right, "CompCount", $"{selected.Components.Count} component(s)");
//            countLbl.AddComponent(new UIStyleComponent { StyleHint = "muted" });

//            // Add-component row
//            var addCompRow = ui.HLayout(right, "AddCompRow");
//            ui.Button(addCompRow, "AddComp", () =>
//            {
//                try
//                {
//                    var compTypes = GetComponentTypes();
//                    if (compTypes.Count == 0) return;
//                    var inst = Activator.CreateInstance(compTypes[0]) as IComponent;
//                    if (inst == null) return;
//                    selected.Components.Add(inst);
//                    TryMarkDirty(selected);
//                    state.RequestRebuild?.Invoke();
//                }
//                catch { }
//            });

//            if (selected.Components.Count == 0)
//            {
//                var noComps = ui.Label(right, "NoComps", "  (no components)");
//                noComps.AddComponent(new UIStyleComponent { StyleHint = "muted" });
//                return outer;
//            }

//            foreach (var comp in selected.Components)
//            {
//                BuildForComponent(ui, comp, selected, state, right);
//            }

//            return outer;
//        }

//        private static void BuildTreeElement(IUIBuilder ui, IWorldElement parent, IWorldElement element, InspectorState state, int depth)
//        {
//            var row = ui.HLayout(parent, $"Row_{element.Id}");
//            row.AddComponent(new LayoutElementComponent { PreferredHeight = 26f });

//            // Indent
//            if (depth > 0)
//            {
//                var indent = ui.Label(row, $"Indent_{element.Id}", new string(' ', depth * 2));
//                indent.AddComponent(new LayoutElementComponent { PreferredWidth = depth * 10f });
//            }

//            // Expand/Collapse Toggle
//            bool hasChildren = element.Children.Count > 0;
//            bool isExpanded = state.ExpandedElements.Contains(element.Id);
            
//            if (hasChildren)
//            {
//                string toggleText = isExpanded ? "▼" : "▶";
//                var tglBtn = ui.Button(row, $"Toggle_{element.Id}", () =>
//                {
//                    if (isExpanded) state.ExpandedElements.Remove(element.Id);
//                    else state.ExpandedElements.Add(element.Id);
//                    state.RequestRebuild?.Invoke();
//                });
//                var tglComp = tglBtn.GetComponent<ButtonComponent>();
//                if (tglComp != null) tglComp.Label = toggleText;
//                tglBtn.AddComponent(new LayoutElementComponent { PreferredWidth = 24f, PreferredHeight = 24f });
//            }
//            else
//            {
//                ui.Label(row, $"Empty_{element.Id}", "  ");
//            }

//            // Element Name / Selection
//            var isSelected = state.SelectedElement == element;
//            string name = element.Name ?? "(unnamed)";
//            var btn = ui.Button(row, $"Btn_{element.Id}", () =>
//            {
//                state.SelectedElement = element;
//                state.RequestRebuild?.Invoke();
//            });
//            var btnComp = btn.GetComponent<ButtonComponent>();
//            if (btnComp != null) btnComp.Label = name;
//            if (isSelected) btn.AddComponent(new UIStyleComponent { StyleHint = "selected" });

//            // Recurse children
//            if (isExpanded)
//            {
//                foreach (var child in element.Children)
//                {
//                    BuildTreeElement(ui, parent, child, state, depth + 1);
//                }
//            }
//        }

//        private static void BuildForComponent(IUIBuilder ui, IComponent comp, IWorldElement owner, InspectorState? state, IWorldElement container)
//        {
//            // Header row
//            var headerRow = ui.HLayout(container, $"CompHeader_{comp.Name}");
//            headerRow.AddComponent(new UIStyleComponent { StyleHint = "compheader" });
            
//            var headerLbl = ui.Label(headerRow, $"CompLbl_{comp.Name}", $"  {comp.Name}");
//            headerLbl.AddComponent(new UIStyleComponent { StyleHint = "accent" });
            
//            ui.Button(headerRow, $"Rem_{comp.Name}", () =>
//            {
//                try { owner.Components.Remove(comp); if (owner != null) TryMarkDirty(owner); state?.RequestRebuild?.Invoke(); } catch { }
//            });

//            // Properties
//            var props = comp.GetType()
//                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
//                .Where(p => p.CanRead && !_skipProps.Contains(p.Name) && IsSimpleType(p.PropertyType))
//                .ToArray();

//            if (props.Length == 0)
//            {
//                var noProps = ui.Label(container, $"NoProps_{comp.Name}", "  (no properties)");
//                noProps.AddComponent(new UIStyleComponent { StyleHint = "muted" });
//                return;
//            }

//            foreach (var prop in props)
//            {
//                var capturedProp = prop;
//                var capturedComp = comp;
//                var row = ui.HLayout(container, $"PropRow_{comp.Name}_{prop.Name}");
                
//                var propLbl = ui.Label(row, $"PropLbl_{prop.Name}", $"  {prop.Name}");
//                propLbl.AddComponent(new UIStyleComponent { StyleHint = "muted" });
                
//                string initial;
//                try { initial = FormatValue(capturedProp.GetValue(capturedComp)); } catch { initial = "?"; }
//                ui.TextInput(row, $"PropField_{prop.Name}", initial);
//            }
//        }
//    }
//}

