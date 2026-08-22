# Trailblazer Wiki

Trailblazer provides deterministic, engine-agnostic navigation for lockstep
simulations. Its public pathing model is intentionally small: publish immutable
map truth, create one `PathQuery`, consume an A* or Flow guide, and explicitly
complete any semantic action the guide selects.

## Start Here

1. [Overview](Overview.md) for the complete runtime model.
2. [Navigation maps](NavigationCharts.md) and
   [map authoring](ChartAuthoring.md) for world semantics.
3. [Runtime publication](PathManager.md) for maps, policies, and overlays.
4. [Pathing](Pathing.md) for `PathQuery`, endpoints, budgets, and algorithms.
5. [Guides](PathGuides.md) for movement and action consumption.

## Topic Index

| Topic | Reference |
| --- | --- |
| Solid, Gas, and Liquid cell semantics | [Navigation maps](NavigationCharts.md) |
| Token import and host materialization | [Map authoring](ChartAuthoring.md) |
| Map replacement, cell overlays, and receipts | [Runtime publication](PathManager.md) |
| A* and Flow query construction | [Pathing](Pathing.md) |
| Free-form Gas/Liquid movement | [Volume traversal](VolumeTraversal.md) |
| Ladders, jumps, takeoff, and teleporters | [Transitions](Transitions.md) |
| A* steps and Flow samples | [Path guides](PathGuides.md) |
| High-level steering | [NavSteering](NavSteering.md) |
| Simulation-facing controller | [Navigator](Navigator.md) |
| JSON and MemoryPack state | [Serialization](Serialization.md) |

## Core Invariants

- Simulation math uses FixedMathSharp, not floating point.
- GridForge owns topology, prisms, portals, and covered-body geometry.
- One immutable `NavigationMap` binds one stable map ID to one normalized grid.
- Runtime mutations are explicit addressed publications, not hidden callbacks.
- Query intent contains one exact start medium and a nonempty target-media mask.
- Work and retained memory are bounded before search begins.
- A guide action advances only after exact explicit completion.
- Hosts own terrain classification, animation, physics, and action execution.

## Repository Map

| Path | Purpose |
| --- | --- |
| `src/Trailblazer/Pathing/Map` | Map, cell, connection, transition, and overlay authoring |
| `src/Trailblazer/Pathing/Graph` | Immutable graph composition and traversal evaluation |
| `src/Trailblazer/Pathing/Search` | Query admission, A*, Flow, guides, and internal rays |
| `src/Trailblazer/Navigation` | Navigator, steering, turning, motor, and locomotion |
| `src/Trailblazer/Serialization` | Chronicler transports and record helpers |
| `tests/Trailblazer.Tests` | Deterministic behavior and regression coverage |

## Build

```bash
dotnet restore Trailblazer.slnx
dotnet build Trailblazer.slnx --configuration Release
dotnet test Trailblazer.slnx --configuration Release
```

The code is the final authority when a historical design note disagrees with a
public API reference.
