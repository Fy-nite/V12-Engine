using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>Marks an element as editor infrastructure rather than user
    /// scene content (the editor camera, the game-view viewport router).
    /// Editor-only elements render and behave normally, but panels that list
    /// the user's scene — the hierarchy tree — skip them so internal plumbing
    /// never clutters the outliner. Distinct from
    /// <see cref="NonPersistentComponent"/>: that governs SAVING, this governs
    /// EDITOR VISIBILITY, and a user element can be non-persistent while still
    /// needing to show in the hierarchy.</summary>
    public class EditorOnlyComponent : ComponentBase
    {
        public EditorOnlyComponent() { }

        /// <summary>True when <paramref name="el"/> carries the marker itself
        /// or sits under a marked ancestor.</summary>
        public static bool IsEditorOnly(IWorldElement? el)
        {
            for (var cur = el; cur != null; cur = cur.Parent)
            {
                if (cur.GetComponent<EditorOnlyComponent>() != null)
                    return true;
            }
            return false;
        }

        public override IWorldElement BuildUI() => throw new System.NotImplementedException();
    }
}
