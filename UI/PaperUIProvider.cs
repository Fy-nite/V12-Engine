using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using V12.Components;
using V12.Components.UI;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Rendering;
using V12.UI;
using UIButton = V12.Components.UI.ButtonComponent;
using UISplitterOrientation = V12.Components.UI.SplitterOrientation;

namespace V12.UI.Paper
{
    /// <summary>
    /// Renderer-neutral IUIProvider. Widgets are ordinary V12 elements built
    /// from the engine's existing UI components; PaperUIRenderer draws the
    /// resulting component tree and routes input back through the components.
    /// </summary>
    public class PaperUIProvider : IUIProvider
    {
        private readonly Element _canvas;
        private readonly PaperPanel _desktop;
        private readonly PaperWindow _window;

        public PaperUIProvider(GameRoot root)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            _canvas = new Element("PaperUIRoot");
            _canvas.AddComponent(new CanvasComponent { ScreenSpace = true, AnchorX = 0f, AnchorY = 0f });
            Root.PersistentWorld.AddElement(_canvas);

            _desktop = new PaperPanel(this, "Desktop");
            _canvas.AddChild(_desktop.Element);
            _window = new PaperWindow(this, _desktop);
        }

        internal GameRoot Root { get; }
        public Element RootCanvas => _canvas;
        public IPanel RootPanel => _desktop;

        public void AttachRoot(IWidget root)
        {
            if (root is PaperWidget widget && widget.Element.Parent == null)
                _canvas.AddChild(widget.Element);
        }

        public PaperViewportHost CreateViewportPanel() => new PaperViewportHost(this, "GameView");

        internal void AttachOverlay(PaperWidget widget)
        {
            if (widget.Element.Parent == null) _canvas.AddChild(widget.Element);
            MarkDirty();
        }

        internal void DetachOverlay(PaperWidget widget)
        {
            if (widget.Element.Parent == _canvas) _canvas.RemoveChild(widget.Element);
            MarkDirty();
        }

        internal void MarkDirty() => V12.Core.GameRoot.Instance?.MarkRenderDirty();

        public IWindow CreateWindow(string title, WindowOptions options)
        {
            var panel = CreatePanel(title ?? "Window");
            return new PaperWindow(this, panel);
        }

        public string Palletname { get; set; } = "";
        public void SetPallet(string name) => Palletname = name ?? "";
        public IReadOnlyList<string> PalletNames => Array.Empty<string>();

        public IViewportHost CreateViewportHost() => CreateViewportPanel();

        public IButton CreateButton(string text, Action onClick)
        {
            var button = new PaperButton(this, text);
            button.SetOnClick(onClick);
            return button;
        }

        public ILabel CreateLabel(string text) => new PaperLabel(this, text);

        public ITextField CreateTextField(string placeholder, Action<string> onChanged)
        {
            var field = new PaperTextField(this, placeholder);
            field.SetOnChanged(onChanged);
            return field;
        }

        public IDropdown CreateDropdown(IEnumerable<string> options, Action<int> onSelected)
        {
            var dropdown = new PaperDropdown(this);
            dropdown.SetOptions(options);
            dropdown.SetOnSelected(onSelected);
            return dropdown;
        }

        public IVBox CreateVBox() => new PaperVBox(this);
        public IHBox CreateHBox() => new PaperHBox(this);
        public IScrollView CreateScrollView() => new PaperScrollView(this);
        public IDock CreateDock(string title, DockPosition position) => new PaperDock(this, title);
        public IPanel CreatePanel(string name) => new PaperPanel(this, name);
        public ITabControl CreateTabControl() => new PaperTabControl(this);
        public IToolbar CreateToolbar(string name) => new PaperToolbar(this, name);

        public IModal ShowModal(string title, string content)
        {
            var modal = new PaperModal(this, title, content);
            modal.Show();
            return modal;
        }

        public ITreeView CreateTreeView() => new PaperTreeView(this);
        public virtual ICodeEditor CreateCodeEditor() => new PaperCodeEditor(this);
        public IAssetGrid CreateAssetGrid() => new PaperAssetGrid(this);

        public ICheckbox CreateCheckbox(string text, bool value) => new PaperCheckbox(this, text, value);
        public IToggle CreateToggle(string text, bool toggled) => new PaperToggle(this, text, toggled);
        public ISlider CreateSlider(float min, float max, float step, float value) => new PaperSlider(this, min, max, step, value);
        public INumericField CreateNumericField(float value, string format = "F2") => new PaperNumericField(this, value, format);
        public ISplitter CreateSplitter(SplitterOrientation orientation) => new PaperSplitter(this, orientation);
        public IContextMenu CreateContextMenu() => new PaperContextMenu(this);
        public IFilePicker CreateFilePicker() => new PaperFilePicker(this);

        public void ConfigureLayout(IWidget widget, float preferredWidth = -1f, float preferredHeight = -1f, bool expand = false)
        {
            if (widget is not PaperWidget paper) return;
            var layout = paper.GetOrAdd<LayoutElementComponent>();
            if (preferredWidth >= 0f) layout.PreferredWidth = preferredWidth;
            if (preferredHeight >= 0f) layout.PreferredHeight = preferredHeight;
            paper.SetExpand(expand);
            MarkDirty();
        }

        public void ApplyVariation(IWidget widget, string variation)
        {
            if (widget is not PaperWidget paper) return;
            var style = paper.GetOrAdd<UIStyleComponent>();
            style.StyleHint = variation;
            MarkDirty();
        }

        public IWindow RootWindow => _window;
        public void ProcessFrame(double delta) { }
    }

    public abstract class PaperWidget : IWidget
    {
        protected PaperWidget(PaperUIProvider owner, string name)
        {
            Provider = owner;
            Element = new Element(name);
        }

        public PaperUIProvider Provider { get; }
        public Element Element { get; }
        public object NativeControl => Element;

        public virtual bool Visible
        {
            get => Element.Active;
            set
            {
                if (Element.Active == value) return;
                Element.Active = value;
                Provider.MarkDirty();
            }
        }

        protected void AddChildElement(Element child)
        {
            if (child.Parent == null) Element.AddChild(child);
            Provider.MarkDirty();
        }

        internal virtual void SetExpand(bool expand)
        {
            var v = Element.GetComponent<VLayoutComponent>();
            if (v != null) { v.Expand = expand; Provider.MarkDirty(); }
            var h = Element.GetComponent<HLayoutComponent>();
            if (h != null) { h.Expand = expand; Provider.MarkDirty(); }
        }

        internal T GetOrAdd<T>() where T : IComponent, new()
        {
            var existing = Element.GetComponent<T>();
            if (existing != null) return existing;
            var created = new T();
            Element.AddComponent(created);
            return created;
        }
    }

    public sealed class PaperWindow : PaperWidget, IWindow
    {
        public PaperWindow(PaperUIProvider owner, IWidget content) : base(owner, "Window")
        {
            if (content is PaperWidget widget) AddChildElement(widget.Element);
        }
        public string Title { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public void Close() => Visible = false;
    }

    public class PaperPanel : PaperWidget, IPanel
    {
        private float _minWidth;
        public PaperPanel(PaperUIProvider owner, string name) : base(owner, name)
        {
            Element.AddComponent(new VLayoutComponent { Spacing = 2f, Padding = 4f, Expand = true });
            Element.AddComponent(new UIStyleComponent { StyleHint = "panel" });
        }

        public string Name { get => Element.Name ?? ""; set => Element.Name = value; }
        public bool UseScrollableContent
        {
            set
            {
                if (!value || Element.GetComponent<ScrollComponent>() != null) return;
                Element.AddComponent(new ScrollComponent());
                Provider.MarkDirty();
            }
        }
        public float MinWidth
        {
            get => _minWidth;
            set
            {
                _minWidth = value;
                var layout = GetOrAdd<LayoutElementComponent>();
                layout.MinWidth = value;
                Provider.MarkDirty();
            }
        }

        public void AddChild(IWidget child)
        {
            if (child is PaperWidget widget) AddChildElement(widget.Element);
        }

        public void RemoveChild(IWidget child)
        {
            if (child is PaperWidget widget && widget.Element.Parent == Element)
            {
                Element.RemoveChild(widget.Element);
                Provider.MarkDirty();
            }
        }
    }

    public sealed class PaperVBox : PaperWidget, IVBox
    {
        public PaperVBox(PaperUIProvider owner) : base(owner, "VBox")
        {
            Element.AddComponent(new VLayoutComponent { Spacing = 2f });
        }

        public bool Expand
        {
            get => Element.GetComponent<VLayoutComponent>()?.Expand ?? false;
            set => SetExpand(value);
        }

        public void AddChild(IWidget child)
        {
            if (child is PaperWidget widget) AddChildElement(widget.Element);
        }

        public void RemoveChild(IWidget child)
        {
            if (child is PaperWidget widget && widget.Element.Parent == Element)
            {
                Element.RemoveChild(widget.Element);
                Provider.MarkDirty();
            }
        }
    }

    public sealed class PaperHBox : PaperWidget, IHBox
    {
        public PaperHBox(PaperUIProvider owner) : base(owner, "HBox")
        {
            Element.AddComponent(new HLayoutComponent { Spacing = 2f });
        }

        public bool Expand
        {
            get => Element.GetComponent<HLayoutComponent>()?.Expand ?? false;
            set => SetExpand(value);
        }

        public void AddChild(IWidget child)
        {
            if (child is PaperWidget widget) AddChildElement(widget.Element);
        }

        public void AddChild(IWidget child, bool expand)
        {
            AddChild(child);
            if (child is PaperWidget widget) widget.SetExpand(expand);
        }

        public void RemoveChild(IWidget child)
        {
            if (child is PaperWidget widget && widget.Element.Parent == Element)
            {
                Element.RemoveChild(widget.Element);
                Provider.MarkDirty();
            }
        }
    }

    public class PaperButton : PaperWidget, IButton
    {
        private readonly UIButton _button;
        private Action? _onClick;
        public PaperButton(PaperUIProvider owner, string text) : base(owner, "Button")
        {
            _button = new UIButton(text, () => { if (Enabled) _onClick?.Invoke(); });
            Element.AddComponent(_button);
        }
        public string Text { get => _button.Label; set { _button.Label = value ?? ""; Provider.MarkDirty(); } }
        public bool Enabled { get; set; } = true;
        public void SetOnClick(Action onClick) => _onClick = onClick;
    }

    public sealed class PaperLabel : PaperWidget, ILabel
    {
        private readonly LabelComponent _label;
        private float _minWidth;
        public PaperLabel(PaperUIProvider owner, string text) : base(owner, "Label")
        {
            _label = new LabelComponent(text);
            Element.AddComponent(_label);
        }
        public string Text { get => _label.Text; set { _label.Text = value ?? ""; Provider.MarkDirty(); } }
        public float MinWidth
        {
            get => _minWidth;
            set
            {
                _minWidth = value;
                var layout = GetOrAdd<LayoutElementComponent>();
                layout.MinWidth = value;
                Provider.MarkDirty();
            }
        }
    }

    public sealed class PaperTextField : PaperWidget, ITextField
    {
        private readonly TextInputComponent _input;
        public PaperTextField(PaperUIProvider owner, string placeholder) : base(owner, "TextField")
        {
            _input = new TextInputComponent(placeholder);
            Element.AddComponent(_input);
        }
        public string Text { get => _input.Value; set { _input.Value = value ?? ""; Provider.MarkDirty(); } }
        public string Placeholder { get => _input.Placeholder; set { _input.Placeholder = value ?? ""; Provider.MarkDirty(); } }
        public bool Enabled { get; set; } = true;
        public void SetOnChanged(Action<string> onChanged) => _input.OnChanged += value => onChanged?.Invoke(value);
    }

    public sealed class PaperDropdown : PaperWidget, IDropdown
    {
        private readonly DropdownComponent _dropdown;
        public PaperDropdown(PaperUIProvider owner) : base(owner, "Dropdown")
        {
            _dropdown = new DropdownComponent();
            Element.AddComponent(_dropdown);
        }
        public void SetOptions(IEnumerable<string> options)
        {
            _dropdown.Items = new List<string>(options ?? Array.Empty<string>());
            Provider.MarkDirty();
        }
        public int SelectedIndex { get => _dropdown.SelectedIndex; set { _dropdown.SelectedIndex = value; Provider.MarkDirty(); } }
        public void SetOnSelected(Action<int> onSelected) => _dropdown.OnSelected = onSelected;
    }

    public sealed class PaperCheckbox : PaperWidget, ICheckbox
    {
        private readonly CheckboxComponent _box;
        public PaperCheckbox(PaperUIProvider owner, string text, bool value) : base(owner, "Checkbox")
        {
            _box = new CheckboxComponent { Label = text, Checked = value };
            Element.AddComponent(_box);
        }
        public string Text { get => _box.Label; set { _box.Label = value ?? ""; Provider.MarkDirty(); } }
        public bool Checked { get => _box.Checked; set { _box.Checked = value; Provider.MarkDirty(); } }
        public bool Enabled { get; set; } = true;
        public void SetOnChanged(Action<bool> onChanged) => _box.OnChanged = onChanged;
    }

    public sealed class PaperToggle : PaperWidget, IToggle
    {
        private readonly ToggleComponent _toggle;
        public PaperToggle(PaperUIProvider owner, string text, bool toggled) : base(owner, "Toggle")
        {
            _toggle = new ToggleComponent { Label = text, IsOn = toggled };
            Element.AddComponent(_toggle);
        }
        public string Text { get => _toggle.Label; set { _toggle.Label = value ?? ""; Provider.MarkDirty(); } }
        public bool Toggled { get => _toggle.IsOn; set { _toggle.IsOn = value; Provider.MarkDirty(); } }
        public bool Enabled { get; set; } = true;
        public void SetOnToggled(Action<bool> onToggled) => _toggle.OnToggled = onToggled;
    }

    public sealed class PaperSlider : PaperWidget, ISlider
    {
        private readonly SliderComponent _slider;
        public PaperSlider(PaperUIProvider owner, float min, float max, float step, float value) : base(owner, "Slider")
        {
            _slider = new SliderComponent { Min = min, Max = max, Step = step, Value = value };
            Element.AddComponent(_slider);
        }
        public float Min { get => _slider.Min; set { _slider.Min = value; Provider.MarkDirty(); } }
        public float Max { get => _slider.Max; set { _slider.Max = value; Provider.MarkDirty(); } }
        public float Step { get => _slider.Step; set { _slider.Step = value; Provider.MarkDirty(); } }
        public float Value { get => _slider.Value; set { _slider.Value = value; Provider.MarkDirty(); } }
        public bool Enabled { get; set; } = true;
        public void SetOnChanged(Action<float> onChanged) => _slider.OnChanged = onChanged;
    }

    public sealed class PaperNumericField : PaperWidget, INumericField
    {
        private readonly TextInputComponent _input;
        private Action<float>? _onChanged;
        private float _value;
        public PaperNumericField(PaperUIProvider owner, float value, string format) : base(owner, "NumericField")
        {
            _value = value;
            Format = format;
            _input = new TextInputComponent();
            Element.AddComponent(_input);
            _input.Value = value.ToString(format, CultureInfo.InvariantCulture);
            _input.OnChanged += text =>
            {
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
                    Math.Abs(parsed - _value) > 0.00001f)
                {
                    _value = parsed;
                    _onChanged?.Invoke(parsed);
                }
            };
        }
        public float Value
        {
            get => _value;
            set
            {
                _value = value;
                _input.Value = value.ToString(Format, CultureInfo.InvariantCulture);
                Provider.MarkDirty();
            }
        }
        public string Format { get; set; } = "F2";
        public bool Enabled { get; set; } = true;
        public void SetOnChanged(Action<float> onChanged) => _onChanged = onChanged;
    }

    public sealed class PaperScrollView : PaperWidget, IScrollView
    {
        public PaperScrollView(PaperUIProvider owner) : base(owner, "Scroll")
        {
            Element.AddComponent(new ScrollComponent());
            Element.AddComponent(new VLayoutComponent { Spacing = 2f });
        }
        public void SetContent(IWidget content)
        {
            if (content is PaperWidget widget) AddChildElement(widget.Element);
        }
    }

    public sealed class PaperSplitter : PaperWidget, ISplitter
    {
        public PaperSplitter(PaperUIProvider owner, SplitterOrientation orientation) : base(owner, "Splitter")
        {
            Element.AddComponent(new SplitterComponent
            {
                Orientation = orientation == SplitterOrientation.SideBySide
                    ? UISplitterOrientation.Horizontal
                    : UISplitterOrientation.Vertical,
                Spacing = 4f,
                Expand = true,
            });
        }
        public void SetFirst(IWidget child) { if (child is PaperWidget w) AddChildElement(w.Element); }
        public void SetSecond(IWidget child) { if (child is PaperWidget w) AddChildElement(w.Element); }
        public float SplitRatio { get; set; } = 0.5f;
        public void SetInitialMinimumPane(int paneIndex) { }
        public void SetOnSplitChanged(Action<float> onSplitChanged) { }
    }

    public sealed class PaperTabControl : PaperWidget, ITabControl
    {
        private readonly TabsComponent _tabs;
        private readonly List<Element> _pages = new();
        public PaperTabControl(PaperUIProvider owner) : base(owner, "Tabs")
        {
            _tabs = new TabsComponent();
            Element.AddComponent(_tabs);
        }
        public void AddTab(string name, IWidget content)
        {
            int index = _tabs.Tabs.Count;
            _tabs.Tabs.Add(name);
            var page = new Element("Tab_" + name);
            page.AddComponent(new TabPageComponent { Index = index, Title = name });
            page.AddComponent(new VLayoutComponent { Spacing = 2f });
            Element.AddChild(page);
            if (content is PaperWidget widget) page.AddChild(widget.Element);
            _pages.Add(page);
            Provider.MarkDirty();
        }
        public void RemoveTab(string name)
        {
            int index = _tabs.Tabs.FindIndex(t => string.Equals(t, name, StringComparison.Ordinal));
            if (index < 0) return;
            _tabs.Tabs.RemoveAt(index);
            if (index < _pages.Count)
            {
                if (_pages[index].Parent == Element) Element.RemoveChild(_pages[index]);
                _pages.RemoveAt(index);
            }
            for (int i = 0; i < _pages.Count; i++)
            {
                var page = _pages[i].GetComponent<TabPageComponent>();
                if (page != null) page.Index = i;
            }
            Provider.MarkDirty();
        }
        public int SelectedTabIndex { get => _tabs.SelectedIndex; set { _tabs.SelectedIndex = value; Provider.MarkDirty(); } }
    }

    public sealed class PaperToolbar : PaperWidget, IToolbar
    {
        public PaperToolbar(PaperUIProvider owner, string name) : base(owner, name)
        {
            Element.AddComponent(new HLayoutComponent { Spacing = 4f, Padding = 2f });
            Element.AddComponent(new UIStyleComponent { StyleHint = "panel" });
            Element.AddComponent(new LayoutElementComponent { PreferredHeight = 34f });
        }
        public void AddItem(IWidget item) { if (item is PaperWidget w) AddChildElement(w.Element); }
        public void RemoveItem(IWidget item) { if (item is PaperWidget w && w.Element.Parent == Element) Element.RemoveChild(w.Element); }
    }

    public sealed class PaperDock : PaperWidget, IDock
    {
        public PaperDock(PaperUIProvider owner, string title) : base(owner, title)
        {
            Title = title;
            Element.AddComponent(new VLayoutComponent { Spacing = 2f });
        }
        public string Title { get; set; } = "";
        public void AddChild(IWidget child) { if (child is PaperWidget w) AddChildElement(w.Element); }
        public void RemoveChild(IWidget child) { if (child is PaperWidget w) Element.RemoveChild(w.Element); }
    }

    public sealed class PaperViewportHost : PaperWidget, IViewportHost, IPanel
    {
        private float _minWidth;
        public PaperViewportHost(PaperUIProvider owner, string name) : base(owner, name)
        {
            Element.AddComponent(new VLayoutComponent { Expand = true });
            Element.AddComponent(new ViewportComponent { RenderWorld = true, BackgroundColor = "#00000000" });
        }
        public string Name { get => Element.Name ?? ""; set => Element.Name = value; }
        public bool UseScrollableContent { set { } }
        public float MinWidth { get => _minWidth; set => _minWidth = value; }
        public void AddChild(IWidget child) { if (child is PaperWidget w) AddChildElement(w.Element); }
        public void RemoveChild(IWidget child) { if (child is PaperWidget w) Element.RemoveChild(w.Element); }
        public void SetRenderTarget(IRenderTarget target) { }
        public void ClearRenderTarget() { }
    }

    public sealed class PaperModal : PaperWidget, IModal
    {
        private readonly LabelComponent _title;
        private readonly LabelComponent _content;
        public PaperModal(PaperUIProvider owner, string title, string content) : base(owner, "Modal")
        {
            Element.AddComponent(new VLayoutComponent { Spacing = 6f, Padding = 12f });
            Element.AddComponent(new UIStyleComponent { StyleHint = "panel" });
            var titleEl = new Element("ModalTitle");
            _title = new LabelComponent(title) { FontSize = 16f };
            titleEl.AddComponent(_title);
            Element.AddChild(titleEl);
            var contentEl = new Element("ModalContent");
            _content = new LabelComponent(content);
            contentEl.AddComponent(_content);
            Element.AddChild(contentEl);
        }
        public string Title { get => _title.Text; set => _title.Text = value ?? ""; }
        public string Content { get => _content.Text; set => _content.Text = value ?? ""; }
        public void Show() => Provider.AttachOverlay(this);
        public void Close() => Provider.DetachOverlay(this);
    }

    public class PaperCodeEditor : PaperWidget, ICodeEditor
    {
        private readonly TextInputComponent _text;
        public PaperCodeEditor(PaperUIProvider owner) : base(owner, "CodeEditor")
        {
            _text = new TextInputComponent("code");
            Element.AddComponent(_text);
        }
        public string Text { get => _text.Value; set { _text.Value = value ?? ""; Provider.MarkDirty(); } }
        public string FilePath { get; set; } = "";
        public bool ReadOnly { get; set; }
        public void SetLanguage(string languageId) { }
        public void SetCompilerLanguage(global::Prowl.OrigamiUI.CodeLanguage language) { }
        public void SetOnChanged(Action<string> onChanged) => _text.OnChanged += value => onChanged?.Invoke(value);
        public void SetOnSaveRequested(Action onSave) { }
        public void Focus() { }
    }

    public sealed class PaperTreeItem : ITreeItem
    {
        internal PaperTreeItem(PaperTreeView owner, TreeItem model, PaperTreeItem? parent)
        {
            View = owner; Model = model; Parent = parent;
        }
        internal PaperTreeView View { get; }
        internal TreeItem Model { get; }
        internal PaperTreeItem? Parent { get; }
        public object? UserData => Model.UserData;
        public string Label { get => Model.Label; set { Model.Label = value ?? ""; View.Provider.MarkDirty(); } }
        public string? IconKey { get => Model.IconKey; set => Model.IconKey = value; }
        public string? TrailingIconKey { get => Model.TrailingIconKey; set => Model.TrailingIconKey = value; }
        public bool TrailingActive { get => Model.TrailingActive; set => Model.TrailingActive = value; }
        public bool Expanded { get => Model.Expanded; set { Model.Expanded = value; View.Provider.MarkDirty(); } }
        public bool Visible { get => Model.Visible; set { Model.Visible = value; View.Provider.MarkDirty(); } }
    }

    public sealed class PaperTreeView : PaperWidget, ITreeView
    {
        private readonly TreeComponent _tree;
        private readonly Dictionary<TreeItem, PaperTreeItem> _map = new();
        private Action<ITreeItem?>? _onSelection;
        private Action<ITreeItem, string>? _onRenamed;
        private Action<ITreeItem, ITreeItem?, TreeDropPosition>? _onDropped;
        private Func<ITreeItem, IContextMenu?>? _onContext;
        private Action<ITreeItem>? _onTrailing;
        private string _emptyMessage = "";

        public PaperTreeView(PaperUIProvider owner) : base(owner, "Tree")
        {
            _tree = new TreeComponent();
            Element.AddComponent(_tree);
            _tree.OnSelected = item =>
            {
                if (item != null && _map.TryGetValue(item, out var wrapper))
                    _onSelection?.Invoke(wrapper);
                else
                    _onSelection?.Invoke(null);
            };
        }

        public ITreeItem AddItem(ITreeItem? parent, string label, object? userData = null)
        {
            var parentItem = parent as PaperTreeItem;
            var model = new TreeItem { Label = label, UserData = userData };
            if (parentItem != null) parentItem.Model.Children.Add(model);
            else _tree.Items.Add(model);
            var item = new PaperTreeItem(this, model, parentItem);
            _map[model] = item;
            Provider.MarkDirty();
            return item;
        }

        public bool RemoveItem(ITreeItem item)
        {
            if (item is not PaperTreeItem paper) return false;
            if (paper.Parent != null) paper.Parent.Model.Children.Remove(paper.Model);
            else _tree.Items.Remove(paper.Model);
            _map.Remove(paper.Model);
            Provider.MarkDirty();
            return true;
        }

        public void Clear()
        {
            _tree.Items.Clear();
            _map.Clear();
            _tree.Select(null, false);
            Provider.MarkDirty();
        }

        public ITreeItem? SelectedItem => _tree.SelectedItem != null && _map.TryGetValue(_tree.SelectedItem, out var item) ? item : null;
        public void SetSelected(ITreeItem? item) => _tree.Select((item as PaperTreeItem)?.Model, false);
        public void ScrollTo(ITreeItem item) { }
        public void SetOnSelectionChanged(Action<ITreeItem?> onSelectionChanged) => _onSelection = onSelectionChanged;
        public void BeginRename(ITreeItem item) { }
        public void SetOnRenamed(Action<ITreeItem, string> onRenamed) => _onRenamed = onRenamed;
        public void SetDropEnabled(bool enabled) { }
        public void SetOnItemDropped(Action<ITreeItem, ITreeItem?, TreeDropPosition> onItemDropped) => _onDropped = onItemDropped;
        public void SetOnContextMenuRequested(Func<ITreeItem, IContextMenu?> onContextMenuRequested) => _onContext = onContextMenuRequested;
        public void SetOnTrailingIconClicked(Action<ITreeItem> onTrailingIconClicked) => _onTrailing = onTrailingIconClicked;
        public void SetEmptyMessage(string message) => _emptyMessage = message ?? "";
    }

    public sealed class PaperAssetGrid : PaperWidget, IAssetGrid
    {
        private readonly TableComponent _table;
        private readonly List<AssetGridEntry> _entries = new();
        private object? _selected;
        private Action<object?>? _onSelection;
        private Action<object>? _onActivated;

        public PaperAssetGrid(PaperUIProvider owner) : base(owner, "AssetGrid")
        {
            _table = new TableComponent();
            _table.Columns.Add(new UITableColumn { Header = "Name", Flex = 2f });
            _table.Columns.Add(new UITableColumn { Header = "Type" });
            _table.RowHeight = 22f;
            _table.OnSelected = index =>
            {
                if (index < 0 || index >= _entries.Count) return;
                _selected = _entries[index].UserData;
                _onSelection?.Invoke(_selected);
                _onActivated?.Invoke(_selected);
            };
            Element.AddComponent(_table);
        }

        public object? SelectedUserData => _selected;
        public void SetItems(IEnumerable<AssetGridEntry> items)
        {
            _entries.Clear();
            _entries.AddRange(items ?? Array.Empty<AssetGridEntry>());
            _table.Rows.Clear();
            foreach (var entry in _entries)
                _table.Rows.Add(new UITableRow { Cells = { entry.Name, entry.Type } });
            Provider.MarkDirty();
        }
        public void SetOnSelectionChanged(Action<object?> onSelectionChanged) => _onSelection = onSelectionChanged;
        public void SetOnItemActivated(Action<object> onItemActivated) => _onActivated = onItemActivated;
    }

    public sealed class PaperContextMenu : PaperWidget, IContextMenu
    {
        private readonly List<(string Label, Action Click, bool Enabled)> _items = new();
        public PaperContextMenu(PaperUIProvider owner) : base(owner, "ContextMenu")
        {
            Element.AddComponent(new VLayoutComponent { Spacing = 1f, Padding = 4f });
            Element.AddComponent(new UIStyleComponent { StyleHint = "panel" });
        }
        public void AddItem(string label, Action onClick, bool enabled = true, string icon = "") => _items.Add((label, onClick, enabled));
        public void AddSeparator() { }
        public void AddSubmenu(string label, IContextMenu submenu, string icon = "") { }
        public void Searchable(string placeholder = "Search...") { }
        public void Show(float screenX, float screenY) => RebuildAndShow();
        public void ShowAt(IWidget anchor) => RebuildAndShow();
        private void RebuildAndShow()
        {
            foreach (var child in Element.Children.ToArray()) Element.RemoveChild(child);
            foreach (var item in _items)
            {
                var button = new PaperButton(Provider, item.Label);
                button.Enabled = item.Enabled;
                button.SetOnClick(() => { Provider.DetachOverlay(this); item.Click(); });
                Element.AddChild(button.Element);
            }
            Provider.AttachOverlay(this);
        }
    }

    public sealed class PaperFilePicker : IFilePicker
    {
        private readonly PaperUIProvider _provider;
        private PaperPanel? _active;
        public PaperFilePicker(PaperUIProvider provider) => _provider = provider;
        public void ShowOpen(string title, string description, string[] extensions, Action<string?> onResult) => ShowPath(title, "", onResult);
        public void ShowFolder(string title, Action<string?> onResult) => ShowPath(title, "", onResult);
        public void ShowSave(string title, string description, string[] extensions, string suggestedFileName, Action<string?> onResult) => ShowPath(title, suggestedFileName, onResult);
        public void Cancel() { if (_active != null) _provider.DetachOverlay(_active); _active = null; }
        private void ShowPath(string title, string initial, Action<string?> onResult)
        {
            Cancel();
            var panel = new PaperPanel(_provider, "FileDialog");
            var field = new PaperTextField(_provider, "Path") { Text = initial };
            panel.AddChild(new PaperLabel(_provider, title));
            panel.AddChild(field);
            var row = new PaperHBox(_provider);
            var ok = new PaperButton(_provider, "OK");
            var cancel = new PaperButton(_provider, "Cancel");
            ok.SetOnClick(() => { _provider.DetachOverlay(panel); _active = null; onResult(field.Text); });
            cancel.SetOnClick(() => { _provider.DetachOverlay(panel); _active = null; onResult(null); });
            row.AddChild(ok);
            row.AddChild(cancel);
            panel.AddChild(row);
            _provider.AttachOverlay(panel);
            _active = panel;
        }
    }
}

