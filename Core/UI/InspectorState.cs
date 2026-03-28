namespace V12.Core.UI
{
    using System;
    using V12.Core.Core.Interfaces;

    /// <summary>
    /// Mutable state bag shared between InspectorBuilder and the frontend renderer.
    /// The renderer creates one instance and wires RequestRebuild / RequestClose before
    /// the first build; callbacks inside the tree update the state and call RequestRebuild
    /// when they need the UI to redraw.
    /// </summary>
    public class InspectorState
    {
        /// <summary>The currently selected world element (right-panel detail target).</summary>
        public IWorldElement? SelectedElement { get; set; }

        /// <summary>Text in the "new element name" field (left panel).</summary>
        public string NewElementName { get; set; } = "NewElement";

        /// <summary>Selected index in the "add component" option-picker.</summary>
        public int ComponentPickerIndex { get; set; }

        /// <summary>Called by button/field callbacks when the full UI needs to rebuild.</summary>
        public Action? RequestRebuild { get; set; }

        /// <summary>Called when the inspector close button is pressed.</summary>
        public Action? RequestClose { get; set; }
    }
}

