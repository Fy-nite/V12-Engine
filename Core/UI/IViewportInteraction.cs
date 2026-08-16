using System;
using V12.Core.Core.Interfaces;

namespace V12.Core.UI
{
    /// <summary>
    /// Editor-side interaction with UI-embedded 3D viewports: picking elements
    /// with the mouse and driving the translation gizmo.
    ///
    /// Defined in the core V12 assembly (not the host renderer assembly) so an
    /// embedding editor can resolve it through the registry regardless of how
    /// many copies of the host assembly the runtime loads.
    /// </summary>
    public interface IViewportInteraction
    {
        /// <summary>Register the callback invoked when the user picks an element
        /// in a 3D viewport (the editor's Select).</summary>
        void SetSelectHandler(Action<IWorldElement?> handler);

        /// <summary>Show the translation gizmo around <paramref name="el"/> in the
        /// viewport it renders in (null hides it).</summary>
        void SetGizmoTarget(IWorldElement? el);

        /// <summary>Register the element the editor's orbit camera should drive
        /// (a V12 element with a CameraComponent). Pass null to stop driving.</summary>
        void SetEditorCamera(IWorldElement? cameraElement);

        /// <summary>Set the active transform-gizmo mode (translate / rotate /
        /// scale). Rebuilds the gizmo handles around the current target.</summary>
        void SetGizmoMode(GizmoMode mode);

        /// <summary>
        /// Open a host file dialog (Save or Open mode). Returns immediately;
        /// <paramref name="onComplete"/> is invoked on the main thread with the
        /// chosen path, or null when the user cancels. The host renders the
        /// dialog (Godot's embedded FileDialog) above the editor UI.
        /// </summary>
        void ShowFileDialog(bool save, string title, string initialPath, Action<string?> onComplete);
    }
}
