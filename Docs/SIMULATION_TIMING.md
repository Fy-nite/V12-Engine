# Simulation Timing and Deterministic Random

V12 gameplay now runs on a fixed-step clock. Hosts still call
`GameRoot.Update(realDeltaTime)` once per rendered frame, but components and
services always receive a constant `FixedDeltaTime`.

## Quick start

```csharp
// Called once per rendered frame by the host.
gameRoot.Update((float)deltaTime);
```

The default step is 60 Hz (`1f / 60f`). A long frame can run several catch-up
steps, up to `MaxStepsPerFrame`. A stalled frame is clamped by
`MaxFrameDeltaTime` and the remaining backlog is dropped instead of spiralling.

## GameRoot API

| Member | Meaning |
|---|---|
| `FixedDeltaTime` | Fixed simulation step size. Default `1f / 60f`. |
| `SimulationPaused` | Real time advances, but no fixed steps run. |
| `TimeScale` | Scales real time before accumulation. `1` is normal speed. |
| `SimulationTick` | Number of fixed steps executed. |
| `SimulationTime` | Total simulated time, advanced only by fixed steps. |
| `InterpolationAlpha` | Fraction of a step left for render interpolation. |
| `OnFixedUpdate` | Raised after every fixed step, after worlds and services update. |
| `Random` | Seeded `DeterministicRandom` for gameplay. |
| `SetSimulationSeed(ulong)` | Reset the random sequence. |
| `ResetSimulation(ulong)` | Reset the clock and random sequence together. |

`GameRoot.FixedUpdate(fixedDeltaTime)` is public if a tool or test wants to run
exactly one step without the accumulator.

## Determinism rules

The gameplay-facing checklist lives in
[`BUILDING_GAMES.md`](../../Docs/BUILDING_GAMES.md#2-determinism-rules). The
short version: use `GameRoot.Random`, avoid wall-clock time, keep world order
stable, defer structural changes, and capture `SimulationTick`,
`SimulationTime`, and `Random.State` together for replay checkpoints.

## Current scope

The clock is deterministic and the random source is seeded. Full world
snapshot/restore and automatic render interpolation are separate features.
`InterpolationAlpha` is exposed now so renderers and host loops can interpolate
between the previous and current fixed state when that layer lands.

## Tests

`V12.Architecture.Tests/FixedStepTests.cs` covers:

- one fixed step for a 60 Hz frame,
- variable frame accumulation,
- max-step backlog dropping,
- pause and time scale,
- deterministic random replay and state restore,
- `GameRoot.Update` feeding components a constant delta.
