# SyncValue\<T\> Usage Guide

`SyncValue<T>` is the engine's value-level replication side channel. It wraps a
single value, tracks its version and dirty state, and sends changed values in a
compact binary batch over `MessageType.SyncBatch`. It runs alongside the
`DirtyTracker` component/element path, not instead of it.

Use it when you have one hot or bulky value that changes on its own and you do
not want a full component snapshot for every change. The current in-tree
consumer is `MeshComponent`, which publishes mesh vertex and index buffers this
way.

> **Status:** `SyncValue<T>` is a working side channel, not a complete
> field-replication feature. The send path is wired into `GameRoot`, but the
> receive path is the consumer's responsibility: a `SyncValue` applies the new
> value to itself and raises `OnRemoteApplied`; it does not know how to write
> that value back into your component. See
> [Current limitations](#current-limitations) before using it for new state.

## When to use it

Good fit:

- A single value that is large or changes frequently, such as a generated mesh
  buffer.
- Data you want to ship separately from the component's BSON snapshot.
- A value that already has a stable key derived from the element and component.

Poor fit:

- Structural changes such as creating elements or adding components.
- State that needs reliable/unreliable lanes, authority epochs, schema hashes,
  or conflict resolution.
- A replacement for `DirtyTracker` component sync. BSON remains the structural
  and snapshot path.
- Values that must be present on a late-joining client before `WorldSync` has
  been applied.

## End-to-end flow

```text
SyncValue<T>.Set(value)
  -> value, version, and dirty flag update under a lock
  -> OnDirty fires
  -> DirtyTracker.SendDirtyUpdates()
       -> SyncManager.FlushDirty()
            -> SyncRegistry.DirtyValues()
            -> SyncBatchBuilder.BuildBatch()
            -> SyncManager.SendBytes(batch)
                 -> SyncNetworkBridge wraps it in MessageDTO(MessageType.SyncBatch)
                 -> NetworkCables transport
  -> remote SyncNetworkBridge.OnMessageReceived()
       -> SyncManager.ApplyIncoming(batch)
            -> SyncRegistry.TryGet(key)
            -> SyncValue<T>.ApplyRemotePayload()
                 -> TryApplyRemote(value, version)
                      -> OnRemoteApplied(value, version)
```

The bridge is created automatically by `GameRoot.SetupNetworking`. You only need
to wire it yourself if you are using the networking types outside a `GameRoot`.

## Quick start

### 1. Declare the synced value

Add a `SyncValue<T>` property and mark it `[Sync]`. You do not need
`[BsonIgnore]`: `BsonConfig` installs a convention that makes every
`ISyncValue` member non-serializable, so the component's BSON snapshot never
carries a second copy of the same data.

```csharp
using V12.Components;
using V12.Core.Networking;

public sealed class HealthComponent : ComponentBase
{
    private float _health = 100f;

    [Sync]
    public SyncValue<float>? HealthValue { get; set; }
}
```

`[Sync]` lives in `V12.Core.Networking`. It can also be written as
`[Sync("custom/key")]` to override the auto-generated key path for static keys.

### 2. Create the value on attach

`SyncAutoCreator.EnsureSyncValues` finds `[Sync]` properties of type
`SyncValue<T>` that are currently `null`, creates them, and lets the
`SyncValue<T>` constructor register them in `SyncRegistry`.

```csharp
public override void OnAttach(IWorldElement element)
{
    base.OnAttach(element);

    // The base path becomes part of every auto-created key.
    SyncAutoCreator.EnsureSyncValues(this, $"health/{Owner?.Id ?? 0}/{Id}");
}
```

The auto-created key is:

```text
{basePath}/{propertyName}
```

For the example above that is:

```text
health/{elementId}/{componentId}/HealthValue
```

The property name is part of the key. If you create a `SyncValue<T>` manually,
use the same key shape on both peers or the receiver will not find it.

You can also create the value yourself, especially when you need an initial
value or a custom key:

```csharp
HealthValue = new SyncValue<float>(
    $"health/{Owner?.Id ?? 0}/{Id}/HealthValue",
    _health,
    markDirtyInitially: true);
```

The constructor registers the value automatically. The `SyncRegistry` entry is
global and keyed by string, so the key must be unique across the process.

### 3. Publish from the authoritative side

Only the authority should push state. `SyncValue<T>` has no authority model of
its own, so the guard is the caller's job. `MeshComponent` uses the same pattern:

```csharp
public void PublishHealth()
{
    var root = GameRoot.Instance;
    if (root is null) return;

    var host = root.Registry?.Get<NetworkHost>("NetworkHost");
    var client = root.Registry?.Get<NetworkClient>("NetworkClient");
    if (client is not null && host is null) return; // pure client, do not echo

    HealthValue?.Set(_health);
}
```

`Set` only marks the value dirty when the new value differs from the current
one. Publishing an unchanged value is cheap and does not increment the version.

### 4. Apply remote values on the receiver

`SyncManager.ApplyIncoming` applies the value to the `SyncValue` object and
raises `OnRemoteApplied`. Nothing else happens automatically. Subscribe once,
on attach, and write the value back into your real state.

```csharp
public override void OnAttach(IWorldElement element)
{
    base.OnAttach(element);

    SyncAutoCreator.EnsureSyncValues(this, $"health/{Owner?.Id ?? 0}/{Id}");

    HealthValue!.OnRemoteApplied += (value, _) =>
    {
        if (MathF.Abs(_health - value) < 0.001f) return;

        // Write the backing field directly. Do not call the property setter
        // unless your setter is guarded against echo.
        _health = value;

        // Refresh derived state, UI, animation, etc. here.
    };
}
```

A remote apply clears the local dirty flag and updates the local version. If the
property setter calls `MarkDirty()` or publishes again, you create an echo loop.
Either write the field directly, or add an apply-scope guard as described in
[Current limitations](#current-limitations).

### 5. Let the transport flush

`DirtyTracker.SendDirtyUpdates` calls `SyncManager.FlushDirty()` on its send
tick, so a value set through `Set` goes out automatically while the tracker is
running. You can also flush explicitly:

```csharp
SyncManager.FlushDirty();
```

`FlushDirty` collects every dirty value, builds one batch, clears the dirty
flags, and calls `SyncManager.SendBytes`. If `SendBytes` is not set, or the send
throws, the flags have already been cleared, so that update is lost.

## Minimal value-only example

You do not need a component to use the channel directly. The value registers
itself when constructed:

```csharp
using System;
using V12.Core.Networking;

var health = new SyncValue<int>("player/1/health", 100);

health.OnValueChanged += (oldValue, newValue) =>
    Console.WriteLine($"health {oldValue} -> {newValue}");

health.OnRemoteApplied += (value, version) =>
{
    // The value is already applied to health.Value. Do not call Set here.
    Console.WriteLine($"remote health={value} v{version}");
};

health.Set(90); // dirty; the next FlushDirty sends it
```

You still need a transport and a flush. In a `GameRoot`, the tracker does the
flush; outside one, call `SyncManager.FlushDirty()` and feed incoming batches
through `SyncManager.ApplyIncoming`. Keep the instance reference for local
writes; the registry keeps it registered for the life of the process.

## Complete example

This example mirrors the `MeshComponent` pattern in a smaller component. It keeps
the BSON path intact for structural state and uses `SyncValue<float>` for a hot
value.

```csharp
using System;
using MongoDB.Bson.Serialization.Attributes;
using V12.Components;
using V12.Core;
using V12.Core.Core.Interfaces;
using V12.Core.Networking;
using V12.Core.NetworkCable;

public sealed class HealthComponent : ComponentBase
{
    private float _health = 100f;

    [Sync]
    public SyncValue<float>? HealthValue { get; set; }

    private string SyncPrefix => $"health/{Owner?.Id ?? 0}/{Id}";

    public float Health
    {
        get => _health;
        set
        {
            if (MathF.Abs(_health - value) < 0.001f) return;

            _health = value;
            MarkDirty();       // keeps the component BSON path in sync too
            PublishHealth();   // sends the value-level copy
        }
    }

    public override void OnAttach(IWorldElement element)
    {
        base.OnAttach(element);

        SyncAutoCreator.EnsureSyncValues(this, SyncPrefix);

        HealthValue!.OnRemoteApplied += (value, _) =>
        {
            if (MathF.Abs(_health - value) < 0.001f) return;
            _health = value;
            // Refresh caches, UI, or other derived state.
        };

        PublishHealth();
    }

    public void PublishHealth()
    {
        var root = GameRoot.Instance;
        if (root is null) return;

        var host = root.Registry?.Get<NetworkHost>("NetworkHost");
        var client = root.Registry?.Get<NetworkClient>("NetworkClient");
        if (client is not null && host is null) return;

        // Do not construct a second SyncValue here with a different key.
        // EnsureSyncValues already created it with the property-name suffix.
        HealthValue?.Set(_health);
    }
}
```

If the component is ported to typed field sync later, remove the `[Sync]`
`SyncValue` property and move the value into a `[Sync]` field with `SetSync`.
The two mechanisms should not both own the same value.

## Key rules

- **Keys are global and stable.** Two `SyncValue<T>` instances with the same key
  replace each other in `SyncRegistry`. The constructor registration uses
  `_map[key] = value`, so the later instance wins silently.
- **The property name is part of an auto-created key.** Use
  `SyncAutoCreator.EnsureSyncValues` with a stable prefix, and do not create a
  manual `SyncValue` with a different suffix for the same property.
- **The key is fixed at construction.** It does not rebind if the element id or
  component id changes later. Create the value after ids are assigned.
- **Both peers must use the same `T`.** The wire payload has no type tag. A
  mismatch either fails deserialization or produces the wrong value.
- **One authority per value.** `SyncValue` does not reject writes by role.
  Guard the publish path yourself.
- **Write the backing field on remote apply.** Do not re-enter the property
  setter unless it is guarded against echo.
- **Arrays are compared by reference.** `EqualityComparer<T>.Default` treats two
  different array instances as different even when their contents match, and
  treats a mutated-in-place array as unchanged. Publish a new array, or call
  `MarkDirty()` explicitly.
- **No unregister.** The registry has no removal API. Values created for
  destroyed components stay registered for the rest of the process.
- **No built-in full-state resend.** A late joiner needs the keys to exist
  before batches can apply. The current `GameRoot` flow sends a mesh sync batch
  before `WorldSync`, and the receiver drops unknown keys.

## API reference

### `SyncValue<T>`

File: `V12-Engine/Core/Networking/SyncValue.cs`

| Member | Description |
|---|---|
| `Key` | Global string key. |
| `Path` | Alias for `Key`. |
| `ValueType` | `typeof(T)`. |
| `Value` | Current value, read under the internal lock. |
| `Version` | Local `ulong` version counter. Increments on each accepted local change. |
| `IsDirty` | Whether the value has changed since the last `ClearDirty`. |
| `Set(T newValue)` | Updates the value when it differs, increments the version, marks dirty, and raises `OnValueChanged` and `OnDirty`. |
| `TryApplyRemote(T newValue, ulong version, Uri sender = null)` | Applies a remote value only when `version` is strictly greater than the local version. Clears dirty and raises `OnValueChanged` when the value changed and `OnRemoteApplied` in all accepted cases. |
| `MarkDirty()` | Marks dirty without changing the value. Raises `OnDirty` only on the transition from clean to dirty. |
| `ClearDirty()` | Clears the dirty flag. |
| `ToByteArray()` | Serializes `version` plus the value. |
| `ApplyRemotePayload(ReadOnlySpan<byte> payload, Uri sender = null)` | Deserializes and calls `TryApplyRemote`. |
| `SyncValue<T>.TryDeserialize(...)` | Static deserializer for a payload produced by `ToByteArray`. |

Events:

| Event | Signature | When |
|---|---|---|
| `OnValueChanged` | `(T old, T new)` | A local `Set` or an accepted remote apply changed the value. |
| `OnDirty` | `(SyncValue<T>)` | The value became dirty. |
| `OnRemoteApplied` | `(T value, ulong version)` | A remote payload was accepted by the version check. |

### Companion types

| Type | File | Role |
|---|---|---|
| `ISyncValue` | `ISyncValue.cs` | Non-generic interface used by the registry, manager, and batch builder. |
| `SyncAttribute` | `SyncAttribute.cs` | Marks a `SyncValue<T>` property for auto-creation. Optional static path. |
| `SyncAutoCreator` | `SyncAutoCreator.cs` | Reflection pass that creates null `[Sync]` `SyncValue<T>` properties. Properties only, not fields. |
| `SyncRegistry` | `SyncRegistry.cs` | Process-wide `ConcurrentDictionary<string, ISyncValue>`. Register, lookup, enumerate, filter dirty. |
| `SyncManager` | `SyncManager.cs` | Static `SendBytes` hook, `FlushDirty`, and `ApplyIncoming`. |
| `SyncBatchBuilder` | `SyncBatchBuilder.cs` | Binary batch writer and reader. |
| `SyncNetworkBridge` | `SyncNetworkBridge.cs` | Wires `SyncManager` to a `NetworkCables` instance. |
| `SyncSerializers` | `SyncSerializers.cs` | Serializer registry. Present, but not used by `SyncValue<T>` today. See below. |

## Wire format

### Batch

`SyncBatchBuilder.BuildBatch` writes:

```text
int32   itemCount
repeat itemCount times:
  int32   keyByteLength
  bytes   key (UTF-8)
  int32   payloadLength
  bytes   payload
```

### Value payload

`SyncValue<T>.ToByteArray` writes:

```text
uint64  version
value
```

The value encoding depends on `T`:

| Type | Encoding |
|---|---|
| `int` | `BinaryWriter.Write(int)` |
| `long` | `BinaryWriter.Write(long)` |
| `uint` | `BinaryWriter.Write(uint)` |
| `short` | `BinaryWriter.Write(short)` |
| `byte` | `BinaryWriter.Write(byte)` |
| `bool` | `BinaryWriter.Write(bool)` |
| `float` | `BinaryWriter.Write(float)` |
| `double` | `BinaryWriter.Write(double)` |
| `string` | `int32` length, `-1` for null, then UTF-8 bytes |
| Everything else | `int32` JSON length, then UTF-8 JSON from `System.Text.Json` |

There is no schema hash and no type tag on the wire. The key identifies the
value, and both sides are expected to agree on `T`.

`SyncSerializers` defines `TypeId` values for primitives and a registration API,
but `SyncValue<T>` does not consult it. Arrays such as `double[]` and `uint[]`,
which the mesh path uses, go through the JSON fallback. That is the largest
current source of avoidable bandwidth in this channel.

## Transport wiring

### With `GameRoot`

`GameRoot.SetupNetworking` creates and registers a `SyncNetworkBridge` against
the root's `NetworkCables` instance. The bridge:

- Sets `SyncManager.SendBytes` to enqueue `MessageDTO(MessageType.SyncBatch)`.
- Subscribes to `NetworkCables.OnMessageReceived`.
- Routes only `MessageType.SyncBatch` messages into `SyncManager.ApplyIncoming`.

`DirtyTracker.SendDirtyUpdates` then calls `SyncManager.FlushDirty()` on its
send tick. No extra wiring is required for normal engine use.

### Without `GameRoot`

Create the bridge yourself and keep it alive for the lifetime of the transport:

```csharp
using V12.Core.Networking;
using V12.Core.NetworkCable;

var bridge = new SyncNetworkBridge(
    NetworkCables.Default,
    "networkcables://myapp");

// On shutdown:
bridge.Dispose();
```

If you do not use the bridge, set `SyncManager.SendBytes` and call
`SyncManager.ApplyIncoming` yourself. The batch format is documented above.

`SyncManager.SendBytes` and `SyncRegistry` are static and process-wide. Two
`GameRoot` instances in one process, such as a host plus a client test harness,
share both. `SyncNetworkBridge.Dispose` clears `SendBytes` even if another
bridge installed it.

## Current limitations

These are the gaps identified during the SyncValue networking review. They are
the reason the channel is documented as a side channel rather than a general
component feature.

1. **The receive path is not wired for the only consumer.** `MeshComponent`
   creates and publishes `MeshPointsValue` and `IndicesValue`, but never
   subscribes to `OnRemoteApplied`. The client stores the value in the registry
   and keeps rendering locally generated mesh data. A new consumer must add the
   write-back hook itself.
2. **Global static state.** `SyncRegistry` and `SyncManager.SendBytes` are
   process-wide. Two roots in one process share keys and overwrite the send
   hook.
3. **No unregistration.** Values stay in `SyncRegistry` after their component is
   detached or destroyed. Old keys can collide with newly created components.
4. **Keys can be baked too early.** `MeshComponent.OnAttach` builds its prefix
   from `Owner.Id`. In the archive world-sync path, the server ids are assigned
   after the archive world was parsed and after `OnAttach` ran, so the client
   keys may not match the host keys.
5. **Late-join ordering is wrong.** `GameRoot.OnClientConnected` publishes and
   flushes the mesh batch before sending `WorldSync`. The receiver does not have
   the keys yet, so `ApplyIncoming` ignores the batch. There is no per-peer full
   resend after `WorldSync`.
6. **No type information on the wire.** A sender and receiver that disagree
   about `T` fail or produce garbage. `SyncSerializers` is not consulted.
7. **No authority guard.** `Set` always marks dirty. Every publisher must check
   whether it is the authority, as `MeshComponent.PublishMeshToSyncAuthoritative`
   does.
8. **Dirty scanning is O(registry size).** `SyncRegistry.DirtyValues()`
   enumerates every registered value on each send tick, even when nothing is
   dirty.
9. **Duplicate replication.** `MeshComponent` shape and dimension properties
   already travel through the BSON component batch, while the mesh buffers
   travel through this channel. That is intentional for the buffers today, but
   it means the same component has two replication paths.
10. **No lanes or settle keyframes.** Every value goes through the same
    `SyncBatch` message. There is no unreliable lane for continuous motion and
    no reliable settle packet for a dropped final update.
11. **Version-based stale-drop assumes one authority.** `TryApplyRemote` rejects
    versions that are not strictly greater. If authority for a value moves to a
    peer with a lower counter, valid updates are dropped.
12. **No compression.** The value payload is raw primitives or JSON. Large
    arrays are expensive, and the key string is repeated in every batch entry.
13. **Remote apply runs on the transport thread.** `SyncNetworkBridge` calls
    `ApplyIncoming` from `OnMessageReceived`, so `OnRemoteApplied` handlers run
    off the game thread. Marshal to the game thread before touching rendering,
    scene state, or anything else that expects single-threaded access.

## Where this is going

The design review for this channel concluded that the long-term shape is not a
`SyncValue<T>` wrapper per field. It is a component field sync service:

- `[Sync]` on backing fields, with `SetSync` handling equality, epsilon, and
  dirty marking.
- A cached schema per component type with stable field ids and a schema hash
  exchange at connect.
- An instance-based `SyncService` registered in `Registry`, with bind/unbind on
  component attach/detach and world replacement.
- Separate reliable and unreliable lanes, per-component sequence numbers, and a
  settle keyframe.
- Codecs selected from the declared CLR type, with no global BSON registration.
- A reliable sequenced operation log for structural changes. OT was rejected
  for runtime field state because the host is already authoritative.

That work is not implemented yet. Until it is, use `SyncValue<T>` only when the
separate value-level channel is genuinely what you want. For ordinary component
state, keep using `DirtyTracker` and the BSON component path.

## File map

| File | Purpose |
|---|---|
| `V12-Engine/Core/Networking/SyncValue.cs` | `SyncValue<T>` implementation and value serialization. |
| `V12-Engine/Core/Networking/ISyncValue.cs` | Non-generic interface. |
| `V12-Engine/Core/Networking/SyncAttribute.cs` | `[Sync]` marker. |
| `V12-Engine/Core/Networking/SyncAutoCreator.cs` | Auto-creates missing `SyncValue<T>` properties. |
| `V12-Engine/Core/Networking/SyncRegistry.cs` | Global key registry. |
| `V12-Engine/Core/Networking/SyncManager.cs` | Flush and apply pipeline. |
| `V12-Engine/Core/Networking/SyncBatchBuilder.cs` | Batch binary format. |
| `V12-Engine/Core/Networking/SyncNetworkBridge.cs` | `NetworkCables` transport bridge. |
| `V12-Engine/Core/Networking/SyncSerializers.cs` | Serializer registry, currently unused by `SyncValue<T>`. |
| `V12-Engine/Core/Networking/MessageType.cs` | `MessageType.SyncBatch`. |
| `V12-Engine/Components/MeshComponent.cs` | Reference consumer. |
| `V12-Engine/Core/GameRoot.cs` | Bridge creation, mesh publish on client connect. |
| `V12-Engine/Core/Networking/DirtyTracker.cs` | Calls `SyncManager.FlushDirty()` on the send tick. |
