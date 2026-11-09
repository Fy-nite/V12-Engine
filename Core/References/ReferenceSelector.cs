using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using V12.Core.Core.Interfaces;

namespace V12.Core.References
{
    /// <summary>
    /// Parses and builds the selector syntax used by <see cref="ElementRef"/>
    /// and <see cref="ComponentRef"/>.
    ///
    /// Elements:
    ///   Door B                 unique name anywhere in the scene
    ///   Doors/Door B           suffix path (matches SceneBinder.OnPath semantics)
    ///   Door B[2]              second sibling named Door B
    ///   @9f3a...               explicit uid pin
    ///
    /// Components:
    ///   Door B#PortalComponent first component of that type on the element
    ///   Door B#frame           component by its name attribute
    ///   Door B#MeshRenderer[2] second component of that type
    /// </summary>
    public static class ReferenceSelector
    {
        public const char ComponentSeparator = '#';
        public const char UidPrefix = '@';

        private static readonly char[] PathSeparators = { '/', '\\' };

        /// <summary>True when the value is an explicit uid pin rather than a path/name.</summary>
        public static bool IsUidSelector(string? selector)
            => !string.IsNullOrWhiteSpace(selector) && selector![0] == UidPrefix;

        /// <summary>Split a component selector into its element part and qualifier.</summary>
        public static (string ElementSelector, string? ComponentSelector) SplitComponent(string? selector)
        {
            if (string.IsNullOrWhiteSpace(selector)) return ("", null);
            int index = selector!.IndexOf(ComponentSeparator);
            if (index < 0) return (selector, null);
            return (selector[..index], selector[(index + 1)..]);
        }

        /// <summary>
        /// Build a selector for an element. Uses the bare name when it is unique
        /// in the tree; otherwise emits a full path with [n] disambiguation so
        /// the saved reference cannot become ambiguous after a reload.
        /// </summary>
        public static string Build(IWorldElement? element)
        {
            if (element == null) return "";

            var root = element.GetRoot();
            if (!string.IsNullOrWhiteSpace(element.Name)
                && CountByName(root, element.Name!) == 1)
                return EscapeSegment(element.Name!);

            var parts = new List<string>();
            for (var current = element; current != null; current = current.Parent)
            {
                var name = string.IsNullOrWhiteSpace(current.Name) ? "Element" : current.Name!;
                var siblings = current.Parent?.Children ?? new List<IWorldElement> { current };
                int sameName = siblings.Count(s =>
                    string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
                int index = 0;
                foreach (var sibling in siblings)
                {
                    if (!string.Equals(sibling.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                    index++;
                    if (ReferenceEquals(sibling, current)) break;
                }

                parts.Add(EscapeSegment(name) + (sameName > 1 ? $"[{index}]" : ""));
            }

            parts.Reverse();
            return string.Join('/', parts);
        }

        private static int CountByName(IWorldElement element, string name)
        {
            int count = string.Equals(element.Name, name, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            foreach (var child in element.Children)
                count += CountByName(child, name);
            return count;
        }

        /// <summary>Build a readable selector for a component.</summary>
        public static string Build(IComponent? component)
        {
            if (component == null) return "";
            var owner = component is V12.Components.ComponentBase cb ? cb.Owner : null;
            var element = Build(owner);
            if (string.IsNullOrEmpty(element)) return "";

            var qualifier = !string.IsNullOrWhiteSpace(component.Name)
                ? EscapeSegment(component.Name!)
                : component.GetType().Name;
            return $"{element}{ComponentSeparator}{qualifier}";
        }

        /// <summary>Parse the trailing "[n]" index from a path segment.</summary>
        public static (string Name, int? Index) ParseSegment(string segment)
        {
            if (string.IsNullOrEmpty(segment)) return ("", null);
            if (segment[^1] != ']') return (UnescapeSegment(segment), null);

            // Parse the index on the escaped text: a literal '[' in a name is
            // encoded as %5B, so a raw '[' can only be an index bracket.
            int open = segment.LastIndexOf('[');
            if (open < 0 || open == segment.Length - 2) return (UnescapeSegment(segment), null);
            if (!int.TryParse(segment[(open + 1)..^1], out var index) || index < 1)
                return (UnescapeSegment(segment), null);

            return (UnescapeSegment(segment[..open]), index);
        }

        /// <summary>Split a suffix path into its segments.</summary>
        public static string[] SplitPath(string selector)
            => selector.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>Percent-escape characters that would otherwise be selector syntax.</summary>
        public static string EscapeSegment(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '%': sb.Append("%25"); break;
                    case '/': sb.Append("%2F"); break;
                    case '\\': sb.Append("%5C"); break;
                    case '#': sb.Append("%23"); break;
                    case '@': sb.Append("%40"); break;
                    case '[': sb.Append("%5B"); break;
                    case ']': sb.Append("%5D"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Reverse <see cref="EscapeSegment"/>.</summary>
        public static string UnescapeSegment(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('%') < 0) return value;
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '%' && i + 2 < value.Length
                    && byte.TryParse(value.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out var hex))
                {
                    sb.Append((char)hex);
                    i += 2;
                    continue;
                }
                sb.Append(value[i]);
            }
            return sb.ToString();
        }
    }
}
