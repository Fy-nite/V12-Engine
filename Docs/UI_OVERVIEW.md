# V12 UI System Overview

This document describes how V12's UI components work and how they are rendered by a host application (e.g. the Locus renderer).

---

## Architecture Philosophy

V12 separates **UI data** from **UI rendering**:

| Layer | Responsibility | Location |
|-------|---------------|----------|
| **Components** | Declarative data — *what* to show | `V12.Components.UI.*` |
| **CanvasComponent** | Root of a UI surface — defines mode and sizing | `V12.Components.UI.CanvasComponent` |
| **Host renderer** | Reads the element tree + components, draws pixels | External (e.g. Locus `WorldCanvasSystem`) |

Components **describe** UI — they do not draw anything themselves. The host walks the V12 element tree, finds elements bearing UI components, and renders them via whatever backend it uses (Paper, ImGui, etc.).

> **Update — the renderer no longer has to walk the tree.** V12 now captures the UI
> (`GameRoot.CaptureUI()` → `UIFrame`) and **pushes** it to a registered `IUIRenderer`
> (`ApplyUI`) each dirty frame, keyed by element id (`UINode.Id`). The host reconciles its own
> retained controls from that snapshot; walking the live tree (the model described below)
> remains an option, not a requirement. See `Core/Interfaces/Renderer/IUIRenderer.cs` and
> `Core/Rendering/UIFrame.cs`.

---

## The One UI Primitive: `CanvasComponent`

Every UI surface starts with an element that has a `CanvasComponent` attached. This is the only entry point the host looks for.

```csharp
var canvas = new Element("MyUI");
canvas.AddComponent(new CanvasComponent());
world.AddElement(canvas);

// Children are UI widgets:
var btn = new Element("Button");
btn.AddComponent(new ButtonComponent { Label = "Click" });
canvas.AddChild(btn);
```

Children of the canvas element do **not** need their own `CanvasComponent` — they are automatically rendered as part of their parent canvas. If a child *does* have one, it becomes an independent canvas with its own mode.

---

## Rendering Modes

### World-Space (default)

`ScreenSpace = false` — the canvas renders onto a 3D surface in the world.

```csharp
canvas.AddComponent(new CanvasComponent());
// or explicitly:
canvas.AddComponent(new CanvasComponent { ScreenSpace = false });
```

- The host creates a `RenderTexture`, renders UI components into it via Paper, and applies it as a material on a 3D quad
- The quad is positioned and scaled using the element's `TransformComponent` and `ScaleComponent`
- Useful for in-world monitors, screens, holograms, etc.
- Handles input via raycasting against the 3D quad

### Screen-Space (overlay)

`ScreenSpace = true` — the canvas renders directly to the screen as an overlay, exactly like a HUD or menu.

```csharp
canvas.AddComponent(new CanvasComponent
{
    ScreenSpace = true,
    Width = 320,
    Height = 240,
});
```

- No 3D quad or off-screen texture is created
- UI children render directly into the host's screen-space GUI during its overlay phase
- Input handling is automatic — clicks route through the host's GUI input
- Sizing: use `Width`/`Height` (pixels), or set to `0` for auto-stretch
- Positioning: `AnchorX`/`AnchorY` (0–1 normalized) reserved for future anchor layouts

---

## UI Components Reference

All UI components live under `V12.Components.UI` and inherit from `ComponentBase`.

| Component | Properties | Description |
|-----------|-----------|-------------|
| **CanvasComponent** | `ScreenSpace`, `Width`, `Height`, `AnchorX`, `AnchorY` | Root of a UI surface. Required to mark an element as a canvas. |
| **LabelComponent** | `Text`, `FontSize` | Non-interactive text label. |
| **ButtonComponent** | `Label`, `OnClick` | Clickable button. |
| **ToggleComponent** | `Label`, `IsOn`, `OnToggled` | On/off toggle switch. |
| **CheckboxComponent** | `Label`, `Checked`, `OnChanged` | Checkbox with label. |
| **SliderComponent** | `Value`, `Min`, `Max`, `Step`, `OnChanged` | Draggable slider control. |
| **ProgressBarComponent** | `Value`, `Indeterminate` | Read-only progress indicator. |
| **TextInputComponent** | `Placeholder`, `Value`, `OnChanged` | Single-line text input field. |
| **ImageComponent** | `Source`, `PreserveAspect` | Image display. |
| **IconComponent** | `Icon`, `Size` | Icon display. |
| **RectComponent** | `Width`, `Height`, `BackgroundColor`, `CornerRadius` | Simple coloured rectangle. |
| **HLayoutComponent** | `Spacing`, `Padding` | Horizontal layout container. |
| **VLayoutComponent** | `Spacing`, `Padding` | Vertical layout container. |
| **LayoutElementComponent** | `MinWidth`, `PreferredWidth`, `FlexibleWidth`, `MinHeight`, `PreferredHeight`, `FlexibleHeight` | Flexible sizing hints for layout containers. |
| **UIStyleComponent** | `StyleHint`, `Flat`, `Attributes` | Styling hints for the host renderer. |

---

## Complete Example

```csharp
// Create a world
var world = root.CreateWorld("UISample");

// ── World-space canvas ───────────────────────────
var worldCanvas = new Element("Monitor");
worldCanvas.AddComponent(new TransformComponent(2f, 2f, 0f));
worldCanvas.AddComponent(new ScaleComponent(2f, 2f, 1f));
worldCanvas.AddComponent(new CanvasComponent());
world.AddElement(worldCanvas);

var title = new Element("Title");
title.AddComponent(new LabelComponent { Text = "Status Monitor", FontSize = 18f });
worldCanvas.AddChild(title);

var statusBtn = new Element("StatusBtn");
var btn = new ButtonComponent { Label = "Refresh" };
btn.OnClick = () => Console.WriteLine("Refreshing...");
statusBtn.AddComponent(btn);
worldCanvas.AddChild(statusBtn);

// ── Screen-space HUD ─────────────────────────────
var hud = new Element("HUD");
hud.AddComponent(new CanvasComponent
{
    ScreenSpace = true,
    Width = 300,
    Height = 200,
    AnchorX = 0.02f,
    AnchorY = 0.02f,
});
world.AddElement(hud);

var fpsLabel = new Element("FPS");
fpsLabel.AddComponent(new LabelComponent { Text = "FPS: 60", FontSize = 14f });
hud.AddChild(fpsLabel);

var hpBar = new Element("HP");
hpBar.AddComponent(new ProgressBarComponent { Value = 0.75f });
hud.AddChild(hpBar);
```

---

## Host Rendering Pipeline (Locus Example)

The Locus renderer processes UI in this order each frame:

```
1. V12 worker tick → GameRoot.Update()
2. CaptureFrame() → immutable snapshot
3. LocusRenderer.ConsumeSnapshots()
   → reconciles Prowl scene graph
4. WorldCanvasSystem.SyncFromV12()
   → walks V12 element tree, finds CanvasComponent-bearing elements
   → creates/updates/destroys WorldCanvas instances
5. EndGui(paper) → screen-space canvases
   → WorldCanvasSystem.RenderScreenSpace(paper)
   → each screen-space WorldCanvas renders its children into the host Paper
6. EndRender() → world-space canvases
   → WorldCanvasSystem.RenderAll()
   → each world-space WorldCanvas renders into its own RenderTexture
   → texture appears on a 3D quad in the scene
```

---

## Why CanvasComponent Instead of IWidget?

V12's `IWidget` / `IUIProvider` interface family (`IButton`, `ILabel`, `IPanel`, etc.) is a general abstraction for retained-mode widget trees. It was originally designed to wrap native UI frameworks (Godot Control nodes, Unity UGUI, etc.).

The **CanvasComponent** approach was chosen as the primary UI path because:

1. **Data-driven**: UI is described by components on elements — the same model as everything else in V12. No separate widget tree to manage.
2. **Dual-mode**: One primitive supports both world-space 3D surfaces and screen-space overlays with a single flag.
3. **Snapshot-friendly**: The element tree is captured in `FrameSnapshot` just like meshes and lights, so UI state is automatically threaded.
4. **Composable**: UI elements can have V12 components (physics, scripts, etc.) alongside their UI components.

The `IWidget` interfaces remain in the engine for hosts that want to wrap a native widget toolkit, but the recommended path is `CanvasComponent`.
