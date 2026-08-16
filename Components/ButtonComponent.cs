using System;
using MongoDB.Bson.Serialization.Attributes;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Networking;

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
        /// A code reference, never serialized — presses are forwarded as RPCs.
        /// </summary>
        [BsonIgnore]
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

        /// <summary>
        /// Remote-callable press handler: the raycast ButtonSystem invokes this locally and
        /// also broadcasts it, so every peer that has a matching element runs the same handler.
        /// </summary>
        [Remote]
        public void Press()
        {
            Pressed = true;
            try { OnPressed?.Invoke(); } catch { }
        }
    }
}
