using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>Marks an element subtree as editor-transient: it renders and
    /// picks normally, but it is never saved (WorldML), never network-synced
    /// (DirtyTracker deltas, full-sync snapshots) and never replicated.
    /// The editor gizmo root carries this; the check covers descendants, so
    /// parts only need the marker on their root.</summary>
    public class EditorTransientComponent : ComponentBase
    {
        public EditorTransientComponent() { }

        /// <summary>True when <paramref name="el"/> carries the marker itself
        /// or sits under a marked ancestor.</summary>
        public static bool IsTransient(IWorldElement? el)
        {
            for (var cur = el; cur != null; cur = cur.Parent)
            {
                if (cur.GetComponent<EditorTransientComponent>() != null)
                    return true;
            }
            return false;
        }

        public override IWorldElement BuildUI() => throw new System.NotImplementedException();
    }
}
