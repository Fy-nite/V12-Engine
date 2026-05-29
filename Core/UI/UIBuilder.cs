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

        public void SetScale(float scale)
        {
            var s = Root.GetComponent<V12.Components.ScaleComponent>();
            if (s == null)
            {
                Root.AddComponent(new V12.Components.ScaleComponent(scale));
            }
            else
            {
                s.ScaleX = s.ScaleY = s.ScaleZ = scale;
            }
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
        public IWorldElement Button(IWorldElement? parent, string name, Action onClick)
        {
            var el = new Element(name);
            if (parent == null) parent = Root;
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

        public IWorldElement Rect(IWorldElement parent, string name, float width = 0f, float height = 0f, string backgroundColor = "")
        {
            var el = new Element(name);
            el.Components.Add(new RectComponent { Width = width, Height = height, BackgroundColor = backgroundColor });
            parent.AddChild(el);
            return el;
        }

        public IWorldElement Image(IWorldElement parent, string name, string source, bool preserveAspect = true)
        {
            var el = new Element(name);
            el.Components.Add(new ImageComponent { Source = source, PreserveAspect = preserveAspect });
            parent.AddChild(el);
            return el;
        }

        public IWorldElement Toggle(IWorldElement parent, string name, string label, bool initialState, Action<bool> onToggled)
        {
            var el = new Element(name);
            el.Components.Add(new ToggleComponent { Label = label, IsOn = initialState, OnToggled = onToggled });
            parent.AddChild(el);
            return el;
        }

        public IWorldElement Checkbox(IWorldElement parent, string name, string label, bool initialState, Action<bool> onChanged)
        {
            var el = new Element(name);
            el.Components.Add(new CheckboxComponent { Label = label, Checked = initialState, OnChanged = onChanged });
            parent.AddChild(el);
            return el;
        }

        public IWorldElement Slider(IWorldElement parent, string name, float min, float max, float value, Action<float> onChanged)
        {
            var el = new Element(name);
            el.Components.Add(new SliderComponent { Min = min, Max = max, Value = value, OnChanged = onChanged });
            parent.AddChild(el);
            return el;
        }

        public IWorldElement ProgressBar(IWorldElement parent, string name, float value, bool indeterminate = false)
        {
            var el = new Element(name);
            el.Components.Add(new ProgressBarComponent { Value = value, Indeterminate = indeterminate });
            parent.AddChild(el);
            return el;
        }

        public IWorldElement Icon(IWorldElement parent, string name, string iconId, float size = 16f)
        {
            var el = new Element(name);
            el.Components.Add(new IconComponent { Icon = iconId, Size = size });
            parent.AddChild(el);
            return el;
        }

        public IWorldElement InputField(IWorldElement parent, string name, string placeholder = "")
        {
            var el = new Element(name);
            el.Components.Add(new TextInputComponent(placeholder));
            parent.AddChild(el);
            return el;
        }

        ///// <summary>
        ///// Build world-elements for a UiElement tree using this builder.
        ///// Returns the created IWorldElement for the supplied ui root (child of parent).
        ///// </summary>
        //public IWorldElement BuildFromUiElement(V12.Core.UI.UiElement ui, IWorldElement parent)
        //{
        //    if (ui == null) throw new System.ArgumentNullException(nameof(ui));

        //    string name = !string.IsNullOrEmpty(ui.Name) ? ui.Name : ui.Id;
        //    IWorldElement created = null;

        //    switch (ui)
        //    {
        //        case V12.Core.UI.LabelElement lbl:
        //            created = Label(parent, name, lbl.Text);
        //            break;
        //        case V12.Core.UI.ButtonElement btn:
        //            created = Button(parent, name, btn.OnPressed ?? (() => { }));
        //            break;
        //        case V12.Core.UI.TextFieldElement tf:
        //            created = TextInput(parent, name, string.Empty);
        //            // wire commit if provided
        //            if (tf.OnCommit != null)
        //            {
        //                var comp = created.Components.Find(c => c is V12.Components.UI.TextInputComponent) as V12.Components.UI.TextInputComponent;
        //                if (comp != null) comp.OnChanged += s => tf.OnCommit(s);
        //            }
        //            break;
        //        case V12.Core.UI.RectElement rect:
        //            created = Rect(parent, name, rect.Width, rect.Height, rect.BackgroundColor ?? string.Empty);
        //            break;
        //        case V12.Core.UI.ImageElement img:
        //            created = Image(parent, name, img.Source, img.PreserveAspect);
        //            break;
        //        case V12.Core.UI.ToggleElement tog:
        //            created = Toggle(parent, name, tog.Label, tog.IsOn, tog.OnToggled ?? (_ => { }));
        //            break;
        //        case V12.Core.UI.CheckboxElement cb:
        //            created = Checkbox(parent, name, cb.Label, cb.Checked, cb.OnChanged ?? (_ => { }));
        //            break;
        //        case V12.Core.UI.SliderElement sld:
        //            created = Slider(parent, name, sld.Min, sld.Max, sld.Value, sld.OnChanged ?? (_ => { }));
        //            break;
        //        case V12.Core.UI.ProgressBarElement pb:
        //            created = ProgressBar(parent, name, pb.Value, pb.Indeterminate);
        //            break;
        //        case V12.Core.UI.IconElement ic:
        //            created = Icon(parent, name, ic.Icon, ic.Size);
        //            break;
        //        case V12.Core.UI.InputFieldElement inf:
        //            created = InputField(parent, name, inf.Placeholder ?? string.Empty);
        //            if (inf.OnCommit != null)
        //            {
        //                var comp = created.Components.Find(c => c is V12.Components.UI.TextInputComponent) as V12.Components.UI.TextInputComponent;
        //                if (comp != null) comp.OnChanged += s => inf.OnCommit(s);
        //            }
        //            break;
        //        case V12.Core.UI.HBoxElement _:
        //            created = HLayout(parent, name);
        //            break;
        //        case V12.Core.UI.VBoxElement _:
        //            created = VLayout(parent, name);
        //            break;
        //        default:
        //            // generic panel fallback
        //            created = new Element(name);
        //            parent.AddChild(created);
        //            break;
        //    }

        //    // recurse children
        //    foreach (var child in ui.Children)
        //        BuildFromUiElement(child, created);

        //    return created;
        //}
    }
}
