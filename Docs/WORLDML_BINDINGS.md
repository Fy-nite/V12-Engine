# WorldML and the V12 Binding Systems

This guide documents the WorldML scene pipeline and the three different systems
that V12 calls "binding". They are easy to confuse because they share a word,
but they solve different problems and have different lifetimes.

| Layer | Namespace / files | What it connects | Lifetime |
|---|---|---|---|
| WorldML | `V12.WorldML` (`WorldML/`) | XML text to a live element/component tree | load time |
| Property binding | `V12.Core.Binding` (`Core/Binding/`) | a component property to a callback or a `Bindable<T>` | runtime, per world |
| Scene binding | `V12.Basic.Scene` (`Scene/SceneBinder.cs`) | C# handlers to elements by name, path, tag, or predicate | runtime, per world set |
| Script binding | `V12.Bindings` (`libs/V12.Bindings/`) | engine API to Contract / C# / ObjektRT scripts | script or pak lifetime |

The important consequence: WorldML does **not** contain binding declarations.
There is no `<Bind>` element and no `binding=` attribute. Property and scene
bindings are created from C# after the world is loaded and address runtime
objects, not XML nodes.

---

## 1. WorldML pipeline

```
XML string / file
    -> XmlDocument
    -> WorldMLParser.Parse
    -> Element tree (Element + IComponent instances)
    -> World.AddElement / AddChild
    -> WorldMLSerializer.Serialize for saving
```

### 1.1 Files

| File | Role |
|---|---|
| `WorldML/WorldMLParser.cs` | XML to live `Element`/`IComponent` graph. Supports three dialects and templates. |
| `WorldML/WorldMLSerializer.cs` | Live graph back to WorldML XML. Used by the Studio scene save. |
| `WorldML/WorldTemplateProvider.cs` | Loads template XML from inline `<Templates>`, a directory, or a pak. |
| `Core/WorldLoader.cs` | Loads the legacy `.v12world` ZIP layout (`world.xml` / `main.xml`, `templates/`). |
| `Core/Networking/WorldXmlHotReloader.cs` | Watches `WorldXmlSourceComponent` files and re-parses them on the main thread. |
| `Components/WorldXmlSourceComponent.cs` | Marks an element as a hot-reload XML source. |

`WorldML.tex` in this folder is the existing format reference. This document
focuses on the parser/serializer behaviour that matters when you bind code to a
loaded scene.

### 1.2 Root document

```xml
<?xml version="1.0" encoding="utf-8"?>
<World name="Arena">
  <!-- elements and components -->
</World>
```

- The root may be `<World>` or any single element. `name` or `WorldName` sets
  the root element name; it defaults to `"World"`.
- If the input is a fragment (multiple roots, no wrapper), the parser retries
  with an implicit `<World>...</World>` wrapper.
- The parser returns an `Element` for the root, not the `World` class. Callers
  such as `SceneLoader` create a `World` and add the parsed root to it.
- `<Templates>` is collected before elements are parsed, but only when the
  parser's `TemplateProvider` is a `WorldTemplateProvider`.

### 1.3 Elements

```xml
<Element name="Boss" description="Arena boss">
  <Element name="Head">
    <Component type="TransformComponent" x="0" y="2" z="0" />
  </Element>
</Element>
```

Parsing rules:

- `name` and `description` are element attributes.
- Every other attribute on an element is treated as a template override, not as
  a component property.
- A child node is a component when its tag is `Component` **or ends with**
  `Component` (case-insensitive). Everything else is parsed as a nested
  `<Element>`.
- Because of the suffix rule, a custom component can use the terse form
  `<SpinComponent Speed="2" />`, and a nested element whose name ends in
  `Component` must be written with an explicit `<Element>` wrapper to avoid
  being treated as a component.

### 1.4 Three component dialects

WorldML accepts three equivalent ways to describe the same component.

**1. Canonical type attribute**

```xml
<Component type="TransformComponent" x="0" y="2" z="0" />
```

**2. Terse tag name**

```xml
<TransformComponent x="0" y="2" z="0" />
```

**3. Legacy `Type` child plus value children**

```xml
<Component>
  <Type>TransformComponent</Type>
  <X>0</X>
  <Y>2</Y>
  <Z>0</Z>
</Component>
```

Type resolution:

1. `type` or `typename` attribute.
2. A `<Type>` child element.
3. The node's own local name.
4. Scan every loaded, non-dynamic assembly for a type that implements
   `IComponent` and whose simple name or full name matches case-insensitively.
   The first match wins.

If no type is found the parser logs a warning and skips the component. If the
assembly is not loaded yet, the same warning appears. Load component assemblies
before parsing.

### 1.5 Setting component properties

After the component instance is created through its parameterless constructor:

1. The `name` attribute is assigned to `ComponentBase.Name`.
2. All other attributes on the component node are applied as properties.
3. `<Property name="X" value="1" />` child nodes are applied afterwards and
   override attributes.
4. Direct leaf child elements such as `<X>1</X>` are applied as properties
   (legacy dialect). A child with nested element children is skipped.
5. Deferred references are resolved after every component on the element has
   been added.

Property matching is case-insensitive. Only public, writable **properties** are
set. Public fields are not populated by this parser.

Conversion rules in `SetPropertyIfExists` / `ConvertToType`:

- `string` is copied as-is.
- Enums use `Enum.Parse(..., ignoreCase: true)`.
- Nullable types map empty text to `null`, otherwise convert to the underlying
  type.
- Everything else uses `Convert.ChangeType` with the invariant culture.
- A conversion failure is logged and ignored; parsing continues.
- Complex values such as `Vector3`, `Quaternion`, and arrays are not parsed
  from a single attribute string. Expose scalar properties, use
  `TransformComponent`'s `X/Y/Z`, use `<LocalTransform>`, or add a dedicated
  parser payload (as `MeshComponent` does for custom vertices).

Deferred references use a component name:

```xml
<Component type="MeshComponent" name="mesh_data" Shape="Box" />
<Component type="MeshRenderer" Mesh="mesh_data" />
```

When the target property is an interface or an `IComponent` type (and is not
`string`), the text is stored and resolved after all components on that element
exist. Resolution matches `comp.Name` case-insensitively and requires the type
to be assignable to the property.

### 1.6 Special payloads

- `SvgComponent` captures the raw inner XML of its node as `SvgContent`.
- `MeshComponent` with `Shape="Custom"` reads `<vert>` / `<v>` and `<tri>` /
  `<i>` / `<index>` children into `CustomMeshPoints` and `CustomIndices`.

### 1.7 Transforms and precedence

An element has a `LocalTransform` (`Position`, `Rotation`, `Scale`) and may also
have a `TransformComponent`. Both forms are accepted:

```xml
<Element name="Crate">
  <!-- Canonical component form -->
  <Component type="TransformComponent" x="4" y="0.5" z="0" rotationX="0" rotationY="45" rotationZ="0" />
</Element>

<Element name="Crate">
  <!-- SDK value-block form -->
  <LocalTransform>
    <Position>4,0.5,0</Position>
    <Rotation>0,0.3827,0,0.9239</Rotation>
    <Scale>1,1,1</Scale>
  </LocalTransform>
</Element>
```

- `<LocalTransform>` fields accept `x,y,z`, `(x, y, z)`, `[x; y; z]`, and
  whitespace separators. Rotation is `x,y,z,w` and is normalized; all-zero
  rotation is treated as identity.
- A `TransformComponent` always wins over a `<LocalTransform>` block: the block
  is not applied when a `TransformComponent` was created. The parser compares
  the block against `Element.LocalTransform` only to decide whether to log a
  note; it does not merge the two.
- The parser does not synthesize a `TransformComponent` from a
  `<LocalTransform>` block. The serializer does the reverse synthesis when
  saving.

### 1.8 Templates

Inline templates:

```xml
<World name="Arena">
  <Templates>
    <Template name="crate">
      <Element name="CrateBody">
        <Component type="TransformComponent" x="${x}" y="${y}" z="${z}" />
        <Component type="MeshComponent" name="mesh_data" Shape="Box"
                   Width="1" Height="1" Depth="1" />
        <Component type="MeshRenderer" Mesh="mesh_data" />
      </Element>
    </Template>
  </Templates>

  <Element name="BossCrate" template="crate" x="0" y="2" z="0" />
</World>
```

The element that carries `template="crate"` keeps its own name (`BossCrate`)
and receives the template's elements as children (`CrateBody`).

- `template="name"` expands the template before the element's own children are
  processed.
- `<Instance template="name" ... />` expands a template as a child of the
  current element.
- Overrides are every attribute except `name`, `description`, and `template`.
  Those three are reserved element attributes, so they cannot be used as
  `${...}` override keys. Use other attribute names, or make the template
  produce a fixed element name.
- `${key}` is replaced in attribute values and text. Keys are word characters
  only (`\w+`, so letters, digits, and underscore). A missing key becomes the
  literal string `"0"`.
- Inline templates only work when the parser has a `WorldTemplateProvider`
  instance. A plain `new WorldMLParser()` ignores `<Templates>`.
- `WorldTemplateProvider.LoadFromDirectory` loads each `*.xml` as a document.
  Its name comes from the root `name` attribute, falling back to the file name.
  `AddDocument` does the same for pak-provided documents.

`SceneLoader` takes its template provider from the `GameRoot` registry
(`ITemplateProvider`). If nothing is registered there, pass a provider to the
parser yourself or register one before loading.

### 1.9 Loading a scene

`V12.Basic.Scene.SceneLoader`:

```csharp
var world = root.LoadSceneFromXml(xml, worldName: "Arena", select: true);
var world = root.LoadSceneFromFile("arena.worldml", select: true);
var world = root.LoadSceneFromResource("MyGame.Scenes.Arena.xml", select: true);
```

It parses, creates a `World`, adds the parsed root element, adds the world to
`GameRoot.Worlds`, and optionally selects it.

`WorldLoader.LoadFromArchive` handles the older `.v12world` ZIP layout. It
extracts to a temp directory, mounts assets under `v12://{worldName}`, loads
`templates/`, and parses `world.xml` or `main.xml`. It is marked obsolete in
favour of `V12.Pak`'s `.v12pak` loader.

### 1.10 Saving and round-trip rules

`WorldMLSerializer.Serialize(roots, worldName)` emits:

```xml
<World name="Arena">
  <Element name="Crate">
    <Component type="TransformComponent" x="4" y="0.5" z="0" rotationX="0" rotationY="45" rotationZ="0" />
    <Component type="MeshComponent" Shape="Box" Width="1" Height="1" Depth="1" />
  </Element>
</World>
```

What is written:

- Only public, writable properties.
- Only "simple" values: `string`, enums, `bool`, `float`, `double`, `int`,
  `uint`, `long`, `short`, `byte`, and nullables of those. `Vector3`,
  `Quaternion`, arrays, and component references are skipped.
- Floats and doubles use `0.###` with the invariant culture.
- Booleans are written as `True` / `False`.
- Null and empty strings are omitted.
- `TransformComponent` is emitted when persistent; otherwise a synthesized
  `TransformComponent` is emitted from `LocalTransform`, plus a
  `ScaleComponent` when scale is not `(1,1,1)`.

What is never written:

- Runtime ids (`Id`, `EntityId`), owners, `Active`, `IsDirty`, `IsPersistent`.
- Render, mesh, material, and transform interface properties.
- Transform and light aliases that duplicate the canonical fields.
- Audio, interaction, and script runtime state.
- Components with `IsPersistent == false`.
- Element subtrees marked by `NonPersistentComponent` (editor gizmos).
- Properties marked `[BsonIgnore]`.

Round-trip notes:

- Component `name` attributes are not written: `ComponentBase.Name` is in the
  excluded set and `EmitComponent` is always called with a null name override.
  A named reference such as `Mesh="mesh_data"` therefore relies on the
  component's own string companion or on `OnAttach`'s type-based fallback after
  a round-trip.
- Component references survive through a string companion property where the
  component provides one. `MeshRenderer.Mesh` is `[BsonIgnore]`; the serializer
  writes `MeshTargetName`, and `OnAttach` re-links it on load, falling back to
  the first compatible component when the name no longer matches.
- A component that exposes only fields, or only complex properties, will save
  as an empty component.
- Transform rotation is written as Euler `rotationX/Y/Z` (degrees), not as a
  quaternion, even if the live component used per-axis values.
- As written, the serializer maps its extracted yaw to `rotationX` and pitch
  to `rotationY`, while `TransformComponent` reads `rotationX` as pitch (`RX`)
  and `rotationY` as yaw (`RY`). Verify non-axis-aligned rotations after a
  save/load round-trip, or set orientation through the component's Euler
  fields.

### 1.11 Hot reload

Attach `WorldXmlSourceComponent` to an element, point `FilePath` at a WorldML
file, and create a `WorldXmlHotReloader` for the world:

```csharp
var reloader = new WorldXmlHotReloader(world);
reloader.Initialize();

// Each frame, on the main thread:
reloader.Update();
```

- `FileSystemWatcher` enqueues changes; a 200 ms debounce collapses bursts.
- `Update()` parses and replaces the watched element's children on the calling
  thread. Call it from the main/game thread.
- Reloaded elements are new objects with **new runtime ids**. Any property
  binding or cached element id that pointed at the old children is stale.
- `WorldXmlSourceComponent.AutoReload = false` performs a one-shot load with no
  watcher.

---

## 2. Property bindings: `V12.Core.Binding`

These are live, typed references to a component property. They are useful for
UI, inspectors, gameplay glue, and computed values.

### 2.1 Binding key

Every binding has a stable key:

```
world://{worldId}/entity/{elementId}/component/{componentId}/property/{propertyName}
```

Example:

```
world://Arena/entity/42/component/8/property/CurrentHealth
```

- `worldId` is `World.WorldName`.
- `elementId` and `componentId` are runtime ids, not names.
- The URI parser requires at least the seven slash-separated parts shown
  above. It reads the property name from part index 6 and ignores extra
  trailing parts. A world name containing `/` will break the split.
- Keys are used for de-duplication. Creating the same key twice returns the
  existing binding and adds the new callback to it.

### 2.2 Entry points

```csharp
using V12.Core.Binding;
using V12.Components;
using V12.Basic.Components;

// Strongly typed, one-way.
IBinding hp = Bind.To<HealthComponent, float>(
    boss, c => c.CurrentHealth, value => Console.WriteLine(value));

// Strongly typed, two-way.
var x = Bind.ToTwoWay<TransformComponent, float>(boss, c => c.X);
x.SetValue(10f);

// Dynamic, scripting/serialization style.
IBinding b = Bind.To("world://Arena/entity/42/component/8/property/X");

// String parameters, one-way and two-way.
IBinding b2 = Bind.To<float>("Arena", 42, 8, "TransformComponent", "X");
var b3 = Bind.ToTwoWay<float>("Arena", 42, 8, "TransformComponent", "X");

// Computed. Sources must be Bindable<T> (or cast an IBinding) to read Value.
// boss and clone are two elements that both carry a HealthComponent.
var hpA = Bind.ToTwoWay<HealthComponent, float>(boss, c => c.CurrentHealth);
var hpB = Bind.ToTwoWay<HealthComponent, float>(clone, c => c.CurrentHealth);
IBinding total = Bind.Computed(() => hpA.Value + hpB.Value, hpA, hpB);

// Refresh helpers.
Bind.RefreshAll();
Bind.RefreshElement(elementId);
```

Use `Bind.Current` to get the `BindingContext` for the selected world. It throws
when no world is selected, and lazily creates `World.Bindings`.

Strongly typed overloads are the safe choice. They extract the property name
from the expression, compile the getter (and setter for two-way), and store the
component's assembly-qualified type.

### 2.3 `Bindable<T>`

`Bindable<T>` is the concrete two-way binding:

| Member | Meaning |
|---|---|
| `Value` | Cached value. Reads the source on first access. |
| `HasValue` | Whether a value has been read at least once. |
| `SetValue(T)` | Writes the property and fires `OnChanged`. Throws for one-way bindings. |
| `Refresh()` | Re-reads the property and fires `OnChanged` only when the value changed. |
| `IsValid` | Whether the element and component still exist. |
| `Key`, `ValueType`, `OnChanged` | Standard `IBinding` surface. |

`IBinding` is the non-generic interface (`Key`, `ValueType`, `IsValid`,
`Refresh`, `OnChanged`, `Dispose`).

### 2.4 Change propagation

- `BindingContext.RegisterBinding` subscribes to the component's `OnDirty`
  event.
- When the component calls `MarkDirty()`, the binding calls `Refresh()`.
- `Refresh()` compares the new value with `EqualityComparer<T>.Default` and
  only raises `OnChanged` when it differs.
- `SetValue()` writes the property and raises `OnChanged` itself. It caches the
  value you passed, not a re-read of the property, so a setter that clamps or
  ignores input can leave `Value` temporarily different from the component. A
  following dirty refresh corrects the cache. If the property setter also calls
  `MarkDirty()`, the dirty handler may `Refresh()` first, so a single
  `SetValue` can produce two notifications.

`BindingContext` keeps two indexes: a key dictionary and a per-element list.
`RefreshElement` uses the per-element list. Computed bindings are only in the
key dictionary, so `RefreshElement` does not refresh them; `RefreshAll` does.

### 2.5 Computed bindings

```csharp
// boss and clone are two elements that both carry a HealthComponent.
var a = Bind.ToTwoWay<HealthComponent, float>(boss, c => c.CurrentHealth);
var b = Bind.ToTwoWay<HealthComponent, float>(clone, c => c.CurrentHealth);
var combined = Bind.Computed(() => a.Value + b.Value, a, b);
```

`ComputedBinding<T>` recomputes when any source binding in the same
`BindingContext` raises `OnChanged`. It only subscribes to sources already
registered in that context, so a binding created in another world or outside
the context will not drive the computed value.

### 2.6 Lifecycle and threading

- A `BindingContext` lives on `World.Bindings`. It is created lazily and is not
  automatically disposed when a world is removed. Dispose it yourself if you
  create long-lived bindings.
- `Dispose()` clears the binding's event subscribers, but the component's
  `OnDirty` handler is not unsubscribed. Until the component is collected, it
  retains the binding. Recreate bindings only when needed, and prefer
  `SceneBinder` for long-lived scene glue.
- `Bindable.GetComponent` resolves the element through
  `GameRoot.Instance.SelectedWorld._elementsById`, then finds the first
  component whose type matches the stored component type. The component id in
  the key is used for uniqueness and dirty subscription, not for selecting
  among several components of the same type. This has two consequences:
  - The binding reads from whichever world is selected **at read time**, not
    necessarily the world that was selected when it was created.
  - Only elements present in that world's id index are found. Root elements
    added with `World.AddElement` are indexed; nested elements are indexed when
    they are attached while a world is resolvable. A scene parsed before its
    world is registered can leave descendants out of the target world's index.
    Bind after the world is selected, or use `SceneBinder` / `World.Find` for
    nested scene elements.
- `IsValid` becomes false when the element or component disappears. `Refresh`
  then does nothing. The binding entry remains in the context.
- The context is not thread-safe. `MarkDirty` can run on any thread, so create,
  refresh, and dispose bindings on the main thread.

### 2.7 Type lookup caveat for string and URI overloads

`BindingContext.BuildSource` and `BuildGetter` resolve component types by simple
name using only the assembly that defines `ComponentBase` (`V12`). As a result:

- `Bind.To<T>(worldId, elementId, componentId, componentName, propertyName)`
  only works for components in the `V12` assembly.
- `Bind.To(uri)` finds the component by id, but then routes through the same
  name lookup. It fails for components defined in `V12.Basic` or game
  assemblies.
- Strongly typed `Bind.To<TEntity, TProperty>(entity, ...)` and
  `Bind.ToTwoWay<TEntity, TProperty>(entity, ...)` work for any loaded
  component type.

Prefer the strongly typed overloads whenever you have a compile-time type.

### 2.8 WorldML interaction

- WorldML does not write binding URIs. Binding keys are runtime data only.
- `WorldMLSerializer` never writes element or component ids, so a saved scene
  cannot preserve a `world://...` binding. Bind again after loading.
- Hot reload replaces children with new ids. Re-run the binding setup after a
  reload, or bind by name/path/tag with `SceneBinder`.

---

## 3. Scene bindings: `V12.Basic.Scene.SceneBinder`

`SceneBinder` attaches C# callbacks to scene elements. It is the recommended
way to connect code to an editor-authored WorldML scene because it matches by
stable authored data (name, path, tag) instead of runtime ids.

### 3.1 Entry points

```csharp
using V12.Basic.Scene;
using V12.Basic.Components;
using V12.Components;

var worldBinder = world.Bind(); // one world
var rootBinder = root.Bind();   // every active world (persistent + selected)

worldBinder
    .On("Boss", e => e.GetComponent<HealthComponent>().MaxHealth = 500f)
    .OnAll("Crate", e => e.GetComponent<MeshComponent>().Shape = MeshShape.Box)
    .OnPath("Player/CameraPitch/Camera3D", e => Console.WriteLine(e.Name))
    .OnTag("enemy", e => e.AddComponent(new HealthComponent(50f)))
    .OnWhere(e => e.Name?.StartsWith("Door") == true, e => Open(e))
    .On<HealthComponent>("Boss", h => h.IsInvincible = true)
    .Auto();
```

| Method | Matches | Default multiplicity |
|---|---|---|
| `On(name, ...)` | first element whose name matches | one |
| `OnAll(name, ...)` | every element whose name matches | many |
| `OnPath("A/B/C", ...)` | element whose ancestor chain ends in A/B/C | one |
| `OnTag(tag, ...)` | every element with a `TagComponent` containing the tag | many |
| `OnWhere(predicate, ...)` | every element matching the predicate | many |
| `On<T>(name, ..., addIfMissing)` | first named element, optionally adding `T` | one |

Names and tags are case-insensitive. `OnPath` walks from the candidate element
up its parents, so the path can stop below the scene root.

### 3.2 Execution model

- A rule records the element ids it has applied to and does not apply twice.
- `once: true` makes a rule apply once globally, including `OnAll` and
  `OnTag` rules.
- `Apply()` scans every element in every active world and applies every
  matching rule.
- `Refresh()` is an alias for `Apply()`.
- `Auto()` subscribes to `World.ElementAdded`, calls `Apply()` immediately, and
  then applies rules to new root elements and their parsed subtrees.
- `Clear()` unsubscribes and removes rules. `Dispose()` unsubscribes.
- `ThrowOnUnmatched()` turns the "no element matched" warning into an exception
  on the next `Apply()`.

`Auto()` only subscribes to the worlds that are active at the moment it is
called. If you select a different world later, call `world.Bind()...Auto()`
again or create a new binder for that world.

`Auto()` listens to `World.ElementAdded`, which is raised by
`World.AddElement`. Adding a child through `Element.AddChild` raises
`Element.ChildAdded`, not `World.ElementAdded`, so runtime child additions are
not seen automatically. Call `Refresh()` after such additions.

### 3.3 Query helpers

`SceneQueryExtensions` provides name-based access to a loaded world:

```csharp
IWorldElement? boss = world.Find("Boss");                 // recursive, case-insensitive
List<IWorldElement> crates = world.FindAll("Crate");
IWorldElement required = world.Require("Boss");           // throws when missing
IWorldElement? cam = world.FindByPath("Player/CameraPitch/Camera3D");
List<IWorldElement> enemies = world.FindByTag("enemy");
HealthComponent? hp = world.FindComponent<HealthComponent>("Boss");
foreach (var element in world.Enumerate()) { }
```

The same helpers exist as extensions on `GameRoot`, searching every active
world. Lookups are linear scans, which is fine for scene-sized trees.

### 3.4 Recommended pattern

```csharp
var world = root.LoadSceneFromFile("arena.worldml", select: true);

world.Bind()
     .On("Boss", e => e.GetComponent<HealthComponent>().MaxHealth = 500f)
     .OnTag("enemy", e => e.AddComponent(new HealthComponent(50f)))
     .OnWhere(e => e.Name == "ExitDoor", e => e.GetComponent<ButtonControlComponent>().OnPressed = ExitLevel)
     .Auto();
```

This survives re-saving and hot reloading better than id-based bindings,
because it re-resolves by authored names and tags.

---

## 4. Script bindings: `V12.Bindings`

The fourth use of "binding" is the script interop layer. This is not the same
mechanism as `V12.Core.Binding`.

- `[ClassBinding("V12.World")]` and `[MethodBinding]` expose C# APIs to
  Contract (`.ct`) scripts.
- `V12.World`, `V12.Components`, `V12.Assets`, `V12.Registry`, `V12.Script`,
  and `V12.Log` are the standard binding modules.
- `ContractV12Host` compiles a `.ct` script, runs `Main`, forwards
  `OnUpdate(float)`, and forwards input events. Reload recompiles and despawns
  the previous run's root elements.
- `ContractComponent` wraps a Contract component type as a normal V12
  `ComponentBase`, driving `on_attach`, `on_update`, and `on_detach`.
- `CSharpScriptCompiler` / `CSharpScriptHost` compile `.cs` component sources;
  `CSharpComponentRegistry` registers them for use by the editor and by
  WorldML.
- `ObjektRTScriptRuntime` / `ObjektRTGamepak` load compiled `.orbt` modules;
  `ScriptTickService` drives per-frame hooks.

WorldML interacts with this layer only through component type names. A C#
script component registered in `CSharpComponentRegistry` can be used in
WorldML like any other component:

```xml
<Component type="SpinComponent" Speed="2" />
```

Contract components are created through `ContractComponentRegistry`, not by the
WorldML parser's parameterless-constructor path.

See `docs/CSharpComponents.md` at the repository root and the Contract
documentation under `libs/Contract/` (from the V12.Basic root) for the full
scripting workflow.

---

## 5. Choosing the right layer

| You want to... | Use |
|---|---|
| Describe a scene declaratively | WorldML |
| Save/load an editor scene | `WorldMLSerializer` + `WorldMLParser` |
| React to a property change | `V12.Core.Binding` (`Bind.To`, `Bindable<T>`) |
| Push UI input back into a component | `Bind.ToTwoWay` or write the property directly |
| Bind code to authored elements by name/tag/path | `V12.Basic.Scene.SceneBinder` |
| Expose an API to a gameplay script | `[ClassBinding]` / `[MethodBinding]` in `V12.Bindings` |
| Hot-reload a scene file | `WorldXmlSourceComponent` + `WorldXmlHotReloader` |

---

## 6. Known limitations and gotchas

| Area | Gotcha |
|---|---|
| WorldML ids | Element and component ids are generated at runtime and are not serialized. Binding URIs do not survive save/load or hot reload. |
| WorldML properties | The parser sets public writable properties only, not fields. |
| WorldML types | Type lookup scans loaded assemblies and takes the first simple-name match. Use full names if two assemblies share a component name. |
| WorldML errors | Unknown components warn and are skipped; property conversion errors are logged and ignored. Parsing continues. |
| Templates | Inline `<Templates>` are ignored unless `TemplateProvider` is a `WorldTemplateProvider`. |
| Transform | `TransformComponent` wins over `<LocalTransform>`. A `<LocalTransform>` block without a component does not create one. |
| Serializer | Complex properties (`Vector3`, `Quaternion`, arrays, interfaces) are not written. Use string companion fields for references. |
| Serializer names | Component `name` attributes are not written. Named component references can fall back to the wrong component after a round-trip. |
| Serializer | `IsPersistent = false` components and `NonPersistentComponent` subtrees are omitted. |
| Property binding scope | Bindings resolve against `GameRoot.Instance.SelectedWorld` at read time, not the world they were created from. |
| Property binding index | Bindings look elements up in the selected world's id index. Nested elements parsed before the world is registered may not be indexed there. |
| Duplicate component types | Bindings resolve by component **type**, not by the component id in the key. They read the first component of that type on the element. |
| String/URI bindings | Component types are resolved only from the `V12` assembly. Use strongly typed bindings for `V12.Basic` and game components. |
| Computed bindings | Only sources already registered in the same `BindingContext` trigger recomputation. `IsValid` is always true. |
| SetValue | Caches the requested value, not the property's post-setter value. Can fire `OnChanged` twice when the property setter also calls `MarkDirty()`. |
| Binding lifetime | `Dispose()` clears callbacks but does not unsubscribe the component's `OnDirty` handler. |
| SceneBinder auto | `Auto()` subscribes only to worlds active at that moment and only to `World.ElementAdded`. Call `Refresh()` after `AddChild`, or re-create the binder after selecting another world. |
| Hot reload | Replaces children with new objects and ids. Re-run scene bindings after a reload. |

---

## 7. Source map

| Topic | Source |
|---|---|
| XML parsing, dialects, templates, transforms | `libs/V12/WorldML/WorldMLParser.cs` |
| Saving | `libs/V12/WorldML/WorldMLSerializer.cs` |
| Template storage | `libs/V12/WorldML/WorldTemplateProvider.cs` |
| Legacy archive loading | `libs/V12/Core/WorldLoader.cs` |
| Hot reload | `libs/V12/Core/Networking/WorldXmlHotReloader.cs`, `libs/V12/Components/WorldXmlSourceComponent.cs` |
| Property bindings | `libs/V12/Core/Binding/Bind.cs`, `Bindable.cs`, `BindingContext.cs`, `PropertyBindingSource.cs`, `IBinding.cs` |
| Scene loading and binding | `Scene/SceneLoader.cs`, `Scene/SceneBinder.cs`, `Scene/SceneQueryExtensions.cs` |
| Script bindings | `libs/V12.Bindings/` |
