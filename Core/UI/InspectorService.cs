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

        public UiElement? BuildForSelectedElement()
        {
            var w = _root.SelectedWorld;
            if (w == null) return null;
            // return inspector for the first selected element if any
            var el = w.Root.Count > 0 ? w.Root[0] : null;
            if (el == null) return null;
            return InspectorBuilder.BuildForElement(el, (element) => { try { w.RemoveElement(element); } catch { } });
        }

        public void Dispose() { }
    }
}

