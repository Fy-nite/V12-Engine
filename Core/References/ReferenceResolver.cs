using System;
using System.Collections.Generic;
using System.Linq;
using V12.Core.Core.Interfaces;

namespace V12.Core.References
{
    /// <summary>
    /// Resolves selector strings against a live world. This is the runtime
    /// counterpart to the SDK's XML-only link checker; both implement the same
    /// selector grammar in <see cref="ReferenceSelector"/>.
    /// </summary>
    public static class ReferenceResolver
    {
        public static IWorldElement? ResolveElement(World? world, string? selector)
        {
            if (world == null || string.IsNullOrWhiteSpace(selector)) return null;
            selector = selector!.Trim();

            if (ReferenceSelector.IsUidSelector(selector))
            {
                if (!Guid.TryParse(selector[1..], out var uid)) return null;
                return FindElementByUid(world, uid);
            }

            var segments = ReferenceSelector.SplitPath(selector);
            if (segments.Length == 0) return null;

            var (lastName, lastIndex) = ReferenceSelector.ParseSegment(segments[^1]);
            if (string.IsNullOrEmpty(lastName)) return null;

            var candidates = new List<IWorldElement>();
            foreach (var root in world.Root)
                CollectByName(root, lastName, candidates);

            if (lastIndex.HasValue)
                candidates = candidates.Where(c => SiblingIndex(world, c, lastName) == lastIndex.Value).ToList();

            candidates = candidates.Where(c => MatchesSuffixPath(world, c, segments)).ToList();
            return candidates.Count == 1 ? candidates[0] : null;
        }

        public static IComponent? ResolveComponent(World? world, string? selector)
        {
            if (world == null || string.IsNullOrWhiteSpace(selector)) return null;
            var (elementSelector, componentSelector) = ReferenceSelector.SplitComponent(selector);
            var element = ResolveElement(world, elementSelector);
            if (element == null || string.IsNullOrWhiteSpace(componentSelector)) return null;

            return ResolveComponent(element, componentSelector!);
        }

        public static IComponent? ResolveComponent(IWorldElement element, string qualifier)
        {
            if (element == null || string.IsNullOrWhiteSpace(qualifier)) return null;
            qualifier = qualifier.Trim();

            if (ReferenceSelector.IsUidSelector(qualifier))
            {
                if (!Guid.TryParse(qualifier[1..], out var uid)) return null;
                return element.Components.FirstOrDefault(c => c.Uid == uid);
            }

            var (name, index) = ReferenceSelector.ParseSegment(qualifier);
            if (string.IsNullOrEmpty(name)) return null;

            // A component name wins over a type name: authors can name two
            // MeshComponents "left"/"right" and address them unambiguously.
            var byName = element.Components
                .Where(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (byName.Count > 0)
                return index.HasValue && index.Value <= byName.Count ? byName[index.Value - 1] : byName[0];

            var byType = element.Components
                .Where(c => string.Equals(c.GetType().Name, name, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(c.GetType().FullName, name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (byType.Count == 0) return null;
            return index.HasValue && index.Value <= byType.Count ? byType[index.Value - 1] : byType[0];
        }

        public static IWorldElement? FindElementByUid(World world, Guid uid)
        {
            if (uid == Guid.Empty) return null;
            foreach (var root in world.Root)
            {
                var found = FindByUidRecursive(root, uid);
                if (found != null) return found;
            }
            return null;
        }

        private static IWorldElement? FindByUidRecursive(IWorldElement element, Guid uid)
        {
            if (element.Uid == uid) return element;
            foreach (var child in element.Children)
            {
                var found = FindByUidRecursive(child, uid);
                if (found != null) return found;
            }
            return null;
        }

        private static void CollectByName(IWorldElement element, string name, List<IWorldElement> results)
        {
            if (string.Equals(element.Name, name, StringComparison.OrdinalIgnoreCase))
                results.Add(element);

            foreach (var child in element.Children)
                CollectByName(child, name, results);
        }

        private static bool MatchesSuffixPath(World world, IWorldElement element, string[] segments)
        {
            var current = element;
            for (int i = segments.Length - 1; i >= 0; i--)
            {
                if (current == null) return false;

                var (name, index) = ReferenceSelector.ParseSegment(segments[i]);
                if (string.IsNullOrEmpty(name)
                    || !string.Equals(current.Name, name, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (index.HasValue && SiblingIndex(world, current, name) != index.Value)
                    return false;

                current = current.Parent;
            }
            return true;
        }

        private static int SiblingIndex(World world, IWorldElement element, string name)
        {
            var siblings = element.Parent?.Children ?? world.Root;
            int index = 0;
            foreach (var sibling in siblings)
            {
                if (!string.Equals(sibling.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                index++;
                if (ReferenceEquals(sibling, element)) return index;
            }
            return 0;
        }
    }
}
