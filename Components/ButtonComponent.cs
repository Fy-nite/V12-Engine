using System;
using V12.Core;
using V12.Core.Core.Interfaces;

namespace V12.Components
{
    public class ButtonComponent : ComponentBase
    {
        private string _label = "Button";
        private bool _pressed;

        public override string Name => "Button";

        public string Label
        {
            get => _label;
            set { if (_label != value) { _label = value; MarkDirty(); } }
        }

        /// <summary>
        /// Invoked when the button is pressed via raycast interact.
        /// </summary>
        public Action? OnPressed { get; set; }

        public bool Pressed
        {
            get => _pressed;
            set { if (_pressed != value) { _pressed = value; MarkDirty(); } }
        }

        public override IWorldElement BuildUI()
        {
            return new Element();
        }
    }
}
