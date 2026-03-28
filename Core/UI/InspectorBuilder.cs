namespace V12.Core.UI
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using V12.Core.Core.Interfaces;

    public static class InspectorBuilder
    {
        private static readonly HashSet<string> _skipProps = new(StringComparer.Ordinal)
            { "Id", "EntityId", "Name", "Description" };

        public static UiElement BuildForElement(IWorldElement element, Action<IWorldElement>? onRemove = null)
        {
            var root = new VBoxElement { Name = "InspectorRoot" };
            root.Children.Add(new LabelElement { Text = element.Name ?? "(unnamed)" });
            var summary = new LabelElement { Text = $"{element.Components.Count} component(s)" };
            root.Children.Add(summary);

            foreach (var comp in element.Components)
            {
                root.Children.Add(BuildForComponent(comp));
            }

            // actions
            var actions = new HBoxElement();
            var remove = new ButtonElement { Text = "Remove Element" };
            remove.OnPressed = () => { try { onRemove?.Invoke(element); } catch { } };
            actions.Children.Add(remove);
            root.Children.Add(actions);

            return root;
        }

        public static UiElement BuildForComponent(IComponent comp)
        {
            var panel = new PanelElement { Name = comp.Name };
            panel.Children.Add(new LabelElement { Text = comp.Name });

            var props = comp.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var p in props)
            {
                if (!p.CanRead) continue;
                if (_skipProps.Contains(p.Name)) continue;
                if (!IsSimpleType(p.PropertyType)) continue;

                var value = "";
                try { value = p.GetValue(comp)?.ToString() ?? ""; } catch { }

                var row = new HBoxElement();
                row.Children.Add(new LabelElement { Text = p.Name });
                var tf = new TextFieldElement { Text = value };
                // On commit, try convert and set back into the component
                tf.OnCommit = (txt) =>
                {
                    try
                    {
                        var conv = ConvertFromString(txt, p.PropertyType);
                        if (conv != null) p.SetValue(comp, conv);
                        // if element exposes MarkDirty notify
                        var mi = comp.GetType().GetMethod("MarkDirty", BindingFlags.Public | BindingFlags.Instance);
                        mi?.Invoke(comp, null);
                    }
                    catch { }
                };
                row.Children.Add(tf);
                panel.Children.Add(row);
            }

            return panel;
        }

        private static bool IsSimpleType(Type t)
        {
            if (t == typeof(string) || t == typeof(bool) || t == typeof(int) || t == typeof(long) || t == typeof(float) || t == typeof(double) || t.IsEnum)
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
                    return Enum.Parse(targetType, text, true);
                return Convert.ChangeType(text, targetType, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { return null; }
        }
    }
}

