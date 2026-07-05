# ECS-Style Query API & World Architecture

## Overview

V12 uses a **multi-world** architecture. Instead of one flat scene, there are two
(or more) `World` instances active at the same time:

- **`PersistentWorld`** — Contains the Player, camera, and any other elements that
  must survive world switches (e.g. joining/leaving a headless server). Never
  touched by WorldSync.
- **`SelectedWorld`** — The current gameplay world (level geometry, entities,
  networked content). Replaced by WorldSync when connecting to a server.

Systems should **never** hard-code which world to search. Instead they use the
ECS-style query API on `GameRoot`, which automatically searches all active worlds.

---

## Query API Reference

All methods are on `GameRoot` (typically accessed as `_gameRoot` or `root` in
systems and components).

### Iterating Active Worlds

```csharp
public IEnumerable<World> ActiveWorlds { get; }
```

Yields `PersistentWorld` first, then `SelectedWorld` (if not null). Systems use
this when they need to process every element in every world:

```csharp
foreach (var world in _gameRoot.ActiveWorlds)
{
    // process world.Root, etc.
}
```

### Find an Element by Predicate

```csharp
// First match (recursive depth-first)
public IWorldElement? FindElement(Func<IWorldElement, bool> predicate)

// All matches (recursive)
public List<IWorldElement> FindElements(Func<IWorldElement, bool> predicate)
```

Examples:

```csharp
// Find any element named "Player"
var player = root.FindElement(e => e.Name == "Player");

// Find all elements with "Door" in the name
var doors = root.FindElements(e => e.Name != null && e.Name.Contains("Door"));
```

### Find Elements by Component Type

```csharp
// First element that has a component of type T
public IWorldElement? FindElementWithComponent<T>() where T : IComponent

// All elements that have a component of type T
public List<IWorldElement> FindElementsWithComponent<T>() where T : IComponent
```

Examples:

```csharp
// Find the player (looks for PlayerComponent in PersistentWorld + SelectedWorld)
var player = root.FindElementWithComponent<PlayerComponent>();

// Find all light sources
var lights = root.FindElementsWithComponent<PointLightComponent>();
```

### Find Components Directly

```csharp
// First component of type T found on any element
public T? FindComponent<T>() where T : IComponent

// All components of type T across all elements
public List<T> FindComponents<T>() where T : IComponent
```

Examples:

```csharp
// Get the first LocomotionComponent (usually the player's)
var locomotion = root.FindComponent<LocomotionComponent>();

// Get all audio sources
var audioSources = root.FindComponents<AudioSourceComponent>();
```

### Resolve Which World an Element Belongs To

```csharp
public World? GetWorldForElement(IWorldElement element)
```

Walks up to the root element and checks `_elementsById` in PersistentWorld and
all registered worlds. Used internally by `Element.AddChild`/`RemoveChild` to
lock and index the correct world.

```csharp
var world = root.GetWorldForElement(someElement);
world?.RemoveElement(someElement);
```

### Convenience Property

```csharp
public IWorldElement? Player => PersistentWorld.Root.FirstOrDefault(e => e.Name == "Player");
```

---

## Migration Guide: Old Way → New Way

| ❌ Old Pattern | ✅ New Pattern |
|---|---|
| `root.SelectedWorld?.Root?.FirstOrDefault(e => e.Name == "Player")` | `root.Player` or `root.FindElement(e => e.Name == "Player")` |
| `root.SelectedWorld.FindElementWithComponent<T>()` | `root.FindElementWithComponent<T>()` |
| `PersistentWorld.FindElementWithComponent<T>()` | `root.FindElementWithComponent<T>()` |
| `ProcessWorld(SelectedWorld, dt); ProcessWorld(PersistentWorld, dt);` | `foreach (var w in root.ActiveWorlds) ProcessWorld(w, dt);` |
| `FindPlayer(world)` with manual iteration | `root.FindElementWithComponent<PlayerComponent>()` |
| `FindElementByBody(world, body)` | `root.FindElement(e => e.GetComponent<PhysicsBodyComponent>()?.Body == body)` |

---

## Adding a New World to the Query

If you add another world that should be searched by all systems (e.g. a UI overlay
world), just add it to `ActiveWorlds` in `GameRoot.cs`:

```csharp
public IEnumerable<World> ActiveWorlds
{
    get
    {
        yield return PersistentWorld;
        if (SelectedWorld != null)
            yield return SelectedWorld;
        yield return MyNewWorld;          // <-- add here
    }
}
```

Every system that uses `ActiveWorlds`, `FindElement()`, `FindElements()`, or
`FindElementWithComponent<T>()` will automatically pick it up.

---

## How `Element.AddChild` Finds the Correct World

When a child is added to an element (e.g. `PlayerComponent.OnAttach` creates
`PlayerCamera3D` as a child of Player), `AddChild` needs to index the child
in the correct world's `_elementsById` dictionary.

It calls `GameRoot.GetWorldForElement(this)`, which:

1. Walks up the parent chain to find the root element.
2. Checks `PersistentWorld._elementsById` for the root's ID.
3. Checks each world in `Worlds` for the root's ID.
4. Falls back to `SelectedWorld`.

This ensures that children of elements in `PersistentWorld` are indexed there,
and children of elements in any other world are indexed in the correct world.

---

## Why This Matters

Before the query API, every system had its own ad-hoc approach:

- Some only searched `SelectedWorld` → missed the Player after world switches.
- Some searched both worlds with `if/else` chains → fragile and duplicated.
- `Element.AddChild` always used `SelectedWorld` → indexed camera in wrong world.

The query API centralises the iteration policy so systems are **world-agnostic**
and future world additions don't require changes across the entire codebase.

---

## Per-World Spawn Positions

Each `World` has a `SpawnPosition` (`Vector3`, default `(0, 1.5, 0)`) that
determines where the Player appears when entering that world.

### Automatic Teleport on World Switch

`GameRoot.SelectWorld()` calls `TeleportPlayerToSpawn()` after switching, so
the Player is automatically moved to the new world's spawn point. This means:

- Joining a headless server → Player spawns at the server world's spawn.
- Switching back to the local world → Player spawns at `TestWorld`'s spawn.
- `CreateWorld` → Player spawns at `(0, 1.5, 0)` by default.

### Setting Spawn Position

```csharp
// In code:
world.SpawnPosition = new Vector3(10f, 2f, -5f);

// From the headless REPL:
//   set-spawn        — sets spawn to current player position
//   spawn            — teleports player back to current world's spawn
//   reset-player     — same as spawn
```

### Implementing a SpawnPoint Component

For more advanced scenarios (multiple spawn points, team spawns, etc.), place
a `TransformComponent` on an element with a custom `SpawnPointComponent` tag,
then override the world's spawn on load:

```csharp
var spawnPoint = world.FindElementWithComponent<SpawnPointComponent>();
if (spawnPoint != null)
{
    var t = spawnPoint.GetComponent<TransformComponent>();
    world.SpawnPosition = new Vector3(t.X, t.Y, t.Z);
}
```
