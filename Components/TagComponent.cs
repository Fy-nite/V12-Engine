using System;
using System.Collections.Generic;
using System.Linq;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>
    /// Comma-separated string tags attached to an element for query / filtering purposes.
    /// Example XML:  &lt;TagComponent Tags="player,team1,controllable" /&gt;
    /// </summary>
    public class TagComponent : ComponentBase
    {
        private string _tags = string.Empty;

        public override string Name        => "Tag";
        public override string Description => "Comma-separated entity tags";

        /// <summary>Raw comma-separated tag string. Whitespace around commas is trimmed.</summary>
        public string Tags
        {
            get => _tags;
            set
            {
                var normalised = NormaliseTags(value);
                if (_tags != normalised) { _tags = normalised; MarkDirty(); }
            }
        }

        public TagComponent() { }
        public TagComponent(params string[] tags) { _tags = NormaliseTags(string.Join(",", tags)); }

        public bool HasTag(string tag) =>
            _tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                 .Any(t => string.Equals(t.Trim(), tag.Trim(), StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<string> GetTags() =>
            _tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                 .Select(t => t.Trim())
                 .ToList();

        public void AddTag(string tag)
        {
            if (!HasTag(tag))
                Tags = string.IsNullOrEmpty(_tags) ? tag.Trim() : _tags + "," + tag.Trim();
        }

        public void RemoveTag(string tag)
        {
            Tags = string.Join(",",
                _tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                     .Where(t => !string.Equals(t.Trim(), tag.Trim(), StringComparison.OrdinalIgnoreCase)));
        }

        private static string NormaliseTags(string? raw) =>
            string.Join(",",
                (raw ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(t => t.Length > 0));

        public override string ToString() => $"Tag([{_tags}])";
    }
}
