using System;
using System.Collections.Generic;
using V12.Components.UI;
using V12.Core.Core.Interfaces;

namespace V12.Core.UI
{
    public class UIBuilder : IUIBuilder
    {
        public IWorldElement Root { get; set; }

        public UIBuilder()
        {
            Root = new Element("CanvasRoot");
            Root.Components.Add(new CanvasComponent());
        }

        // ── Layout helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Creates a child element with an <see cref="HLayoutComponent"/> and
        /// appends it to <paramref name="parent"/>.
        /// Returns the new element so callers can nest further children into it.
        /// </summary>
        public IWorldElement HLayout(IWorldElement parent, string name, float spacing = 4f, float padding = 0f)
        {
            var el = new Element(name);
            el.Components.Add(new HLayoutComponent { Spacing = spacing, Padding = padding });
            parent.AddChild(el);
            return el;
        }

        /// <summary>
        /// Creates a child element with a <see cref="VLayoutComponent"/> and
        /// appends it to <paramref name="parent"/>.
        /// Returns the new element so callers can nest further children into it.
        /// </summary>
        public IWorldElement VLayout(IWorldElement parent, string name, float spacing = 4f, float padding = 0f)
        {
            var el = new Element(name);
            el.Components.Add(new VLayoutComponent { Spacing = spacing, Padding = padding });
            parent.AddChild(el);
            return el;
        }

        // ── Widget helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Creates a child element with a <see cref="ButtonComponent"/> and
        /// appends it to <paramref name="parent"/>.
        /// </summary>
        public IWorldElement Button(IWorldElement parent, string name, Action onClick)
        {
            var el = new Element(name);
            el.Components.Add(new ButtonComponent(name, onClick));
            parent.AddChild(el);
            return el;
        }

        /// <summary>
        /// Creates a child element with a <see cref="LabelComponent"/> and
        /// appends it to <paramref name="parent"/>.
        /// </summary>
        public IWorldElement Label(IWorldElement parent, string name, string text)
        {
            var el = new Element(name);
            el.Components.Add(new LabelComponent(text));
            parent.AddChild(el);
            return el;
        }

        /// <summary>
        /// Creates a child element with a <see cref="TextInputComponent"/> and
        /// appends it to <paramref name="parent"/>.
        /// </summary>
        public IWorldElement TextInput(IWorldElement parent, string name, string placeholder = "")
        {
            var el = new Element(name);
            el.Components.Add(new TextInputComponent(placeholder));
            parent.AddChild(el);
            return el;
        }
    }
}
