namespace V12.Core.UI
{
    using System;
    using V12.Core;
    using V12.Core.Core.Interfaces;

    public class InspectorService : IGameService, IDisposable
    {
        private readonly GameRoot _root;

        public InspectorService(GameRoot root)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
        }

        public void Initialize() { }
        public void Update(float deltaTime) { }

        /// <summary>Builds the full inspector tree for use by a frontend renderer.</summary>
        public UiElement BuildFullInspector(InspectorState state)
            => InspectorBuilder.BuildFullInspector(_root, state);

        /// <summary>
        /// Convenience: builds a simple element inspector for the first element in the
        /// selected world (backwards-compat; prefer BuildFullInspector for full UI).
        /// </summary>
        public UiElement? BuildForSelectedElement()
        {
            var w  = _root.SelectedWorld;
            var el = w?.Root.Count > 0 ? w.Root[0] : null;
            if (el == null) return null;
            return InspectorBuilder.BuildForElement(el,
                e => { try { w!.RemoveElement(e); } catch { } });
        }

        public void Dispose() { }
    }
}
