using V12.Core.Core.Interfaces;

namespace V12.Components
{
    /// <summary>Tags a transient gizmo part with the canonical handle id it
    /// belongs to (see <c>V12.Core.UI.TransformGizmo</c>: 0/1/2 = axis,
    /// <c>CenterHandle</c> = uniform centre). Lets any host map a picked
    /// element back to its handle without a side table.</summary>
    public class GizmoHandleComponent : ComponentBase
    {
        public int HandleId { get; set; } = -1;

        public GizmoHandleComponent() { }
        public GizmoHandleComponent(int handleId) { HandleId = handleId; }

        public override IWorldElement BuildUI() => throw new System.NotImplementedException();
    }
}
