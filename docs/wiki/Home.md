# Trailblazer Wiki

Trailblazer is a deterministic, framework-agnostic navigation library for
lockstep simulations and games.

The library combines voxel-backed pathing with runtime navigation controllers.
Use the pathing layer directly when you only need graph-backed surface A*,
chart-backed flow fields, volume routes, and reusable guide data. Use the full navigation stack
when you also want steering, turning, locomotion-aware movement, and
deterministic frame-by-frame controller state.

Trailblazer is built around explicit `TrailblazerWorldContext` instances. A
context owns one `GridWorld`, the deterministic simulation clock, chart
registration, transitions, volume rules, guide caches, navigator ids, movement
groups, heightmaps, and runtime lifecycle hooks for that isolated world.

## What Trailblazer Provides

- Deterministic fixed-point navigation using `FixedMathSharp`
- Engine-agnostic pathing and movement primitives with no required renderer or
  physics engine
- Context-owned navigation maps plus remaining chart-backed flow/volume state
- Surface `PathQuery` waypoint leases, destination-centric flow fields, and
  raw-volume pathing
- Authored chart-to-chart and chart-to-volume traversal transitions
- Deterministic heightmap lookup for context-owned ground/contact Y sampling
- Runtime steering, direct-path checks, group movement, stuck detection,
  repathing, and local avoidance hooks
- Deterministic facing through `NavTurning`
- Locomotion-aware movement through `NavMotor`, including ground, air, liquid,
  slopes, jumps, controlled flight, slides, climbing, and moving platforms
- Explicit Chronicler serialization for the currently supported navigation
  branch
- Cross-target support for `netstandard2.1` and `net8.0`

## Who This Wiki Is For

- Library consumers wiring Trailblazer into a game, simulation, server, or tool
- Engine integrators building concrete `Navigator` subclasses and host traversal
  probes
- Contributors working on pathing, navigation, serialization, or deterministic
  runtime behavior
- Maintainers keeping API behavior, tests, and docs aligned

## Wiki Navigation

| Page                                     | Focus                                                                                            |
| ---------------------------------------- | ------------------------------------------------------------------------------------------------ |
| [Overview](Overview.md)                  | High-level architecture, runtime flow, and where the major systems fit                           |
| [Authoring](ChartAuthoring.md)           | Tokenized chart and generated-transition authoring                                               |
| [Navigation Charts](NavigationCharts.md) | `NavigationChart`, chart cells, registration, initialization, updates, and unload behavior       |
| [PathManager](PathManager.md)            | Context-local chart registry, partition ownership, effective-cell queries, and transition lookup |
| [Pathing](Pathing.md)                    | Graph surface queries, remaining flow/volume requests, guide resolution, and direct usage         |
| [Path Guides](PathGuides.md)             | `NavigationGuideLease` plus the remaining flow/volume guide and cache contracts                  |
| [Transitions](Transitions.md)            | Authored handoffs between chart-backed traversal and raw-volume traversal                        |
| [Volume Traversal](VolumeTraversal.md)   | Gas/liquid/raw-volume routing, medium rules, and volume request behavior                         |
| [Heightmaps](HeightMaps.md)              | Context-owned deterministic ground/contact Y sampling and navigator grounding helpers            |
| [Navigator](Navigator.md)                | Host-facing orchestration, frame flow, guided requests, occupancy, and traversal state           |
| [NavSteering](NavSteering.md)            | Heading generation, request lifecycle, line-of-sight checks, groups, repathing, and arrival      |
| [NavTurning](NavTurning.md)              | Deterministic facing, buffered turn requests, interpolation, and collision auto-turns            |
| [NavMotor](NavMotor.md)                  | Movement execution, traversal finalization, locomotion profiles, and host state refresh          |
| [Gravity](Gravity.md)                    | Vertical-force model, jump/fall/water/flight interactions, and motor expectations                |
| [Serialization](Serialization.md)        | Chronicler coverage, load-into-existing-instance behavior, and round-trip boundaries             |

## Quick Technical Snapshot

- Language: C# 11
- Main library: `src/Trailblazer`
- Test suite: `tests/Trailblazer.Tests`
- Benchmarks: `tests/Trailblazer.Benchmarks`
- Target frameworks: `netstandard2.1`, `net8.0`
- Test framework: xUnit v3
- Key packages: `FixedMathSharp`, `GridForge`, `SwiftCollections`, `Chronicler`
- Package id: `Trailblazer`
- Packaging note: `GeneratePackageOnBuild` is enabled, so Release library builds
  also emit NuGet packages

## The Core Mental Model

The library is easiest to reason about in this order:

1. Create or attach a `TrailblazerWorldContext` for a `GridWorld`.
2. Publish navigation maps and area policies for graph-backed surface routing.
3. Register and initialize `NavigationChart` state only for remaining
   flow/volume consumers.
4. Optionally register traversal transitions, volume medium rules, and heightmap
   layers on the same context.
5. Build a complete immutable `PathQuery` for surface A*, or a remaining
   flow/volume request where applicable.
6. Resolve surface queries into disposable `NavigationGuideLease` instances
   through `context.Guides`, or let `NavSteering` own the lease during guided
   movement.
7. Advance fixed-step runtime flow with `context.Simulate()`,
   `Navigator.Simulate()`, host traversal probing,
   `Navigator.CommitFrameMotion()`, and `context.LateSimulate()`.
8. Return guides, unload charts, reset contexts, or dispose owned worlds
   explicitly when a test, tool, or host world shuts down.

The most important architectural reality is that Trailblazer is context-scoped.
If behavior seems global, check whether it actually belongs to the active
`TrailblazerWorldContext`, its backing `GridWorld`, or an internal service that
entered context-owned state for the duration of the operation.

## Architecture At A Glance

| Type or Area                                       | Role                                                                                                                                                         |
| -------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `TrailblazerWorldContext`                          | Owns one world binding, deterministic frame clock, pathing services, guide caches, transitions, volume rules, heightmaps, navigator ids, and lifecycle hooks |
| `NavigationMap` / composed graph                   | Graph-backed surface topology and immutable query snapshot authority                                                                                        |
| `NavigationChart`                                  | Remaining flow/volume authoring data that maps onto live voxels after registration                                                                            |
| `PathManager`                                      | Context-local chart registry, partition coordinator, effective-cell query service, and cache-invalidation bridge                                             |
| `TraversalAuthoringMap`                            | Tokenized setup path for building chart data plus generated transitions from lightweight authoring input                                                     |
| `TraversalTransitionRegistry`                      | Stores explicit chart-to-chart and chart-to-volume handoff points for staged routing                                                                         |
| `PathQuery`                                        | Complete immutable graph-query intent, including endpoints, profile, policy, traversal, algorithm, and finite budget                                        |
| `FlowFieldPathRequest`                             | Destination-centric chart-backed request designed for shared fields                                                                                          |
| `VolumePathRequest`                                | Raw 3D voxel connectivity request for gas, liquid, and chart-optional travel                                                                                 |
| `TrailblazerGuideService`                          | Returns explicit status plus a disposable graph surface lease, and retains legacy flow/volume routing only for their remaining consumers                     |
| `NavigationGuideLease` / `FlowFieldGuide` / `VolumeGuide` | Graph waypoint cursor and remaining runtime direction providers                                                                                         |
| `Navigator`                                        | Host-facing coordinator for steering, turning, motor execution, frame deltas, traversal state, occupancy, and guided request construction                    |
| `NavSteering`                                      | Heading generator that manages requests, direct-path checks, guide following, movement groups, stuck handling, and arrival                                   |
| `NavTurning`                                       | Deterministic facing controller for buffered turn requests and collision auto-turns                                                                          |
| `NavMotor`                                         | Deterministic movement executor and traversal-state finalizer backed by locomotion profiles                                                                  |
| `HeightmapSurface` / `TrailblazerHeightmapService` | Context-owned deterministic ground/contact Y lookup data and registry                                                                                        |
| Chronicler serialization branch                    | Explicit snapshot/populate support for the currently covered navigation runtime state                                                                        |

## Repository Map

| Path                                   | Purpose                                                                                                             |
| -------------------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| `src/Trailblazer/Pathing`              | Navigation maps/graph queries plus remaining chart, flow, volume, transition, and cache code                       |
| `src/Trailblazer/Navigation/Navigator` | Host-facing navigator orchestration, guided request construction, occupancy, heightmap grounding, and serialization |
| `src/Trailblazer/Navigation/Steering`  | Steering request lifecycle, simulation, line of sight, movement groups, and steering serialization                  |
| `src/Trailblazer/Navigation/Turning`   | Deterministic turning and turning serialization                                                                     |
| `src/Trailblazer/Navigation/Motor`     | Movement execution, traversal finalization, locomotion, climbing, surface state, and motor serialization            |
| `src/Trailblazer/Heightmaps`           | Compressed heightmap surfaces, sampling, and context-owned registry support                                         |
| `src/Trailblazer/Support`              | Lifecycle hook helpers, transient-state utilities, and shared support types                                         |
| `src/Trailblazer/Traversal`            | Traversal-medium value objects shared by pathing and navigation                                                     |
| `src/Trailblazer/Diagnostics`          | Logging channels and diagnostics helpers                                                                            |
| `src/Trailblazer/Runtime`              | World context, deterministic clock, lifecycle hooks, and reset behavior                                             |
| `tests/Trailblazer.Tests/Pathing`      | Pathing, chart, transition, guide, surveyor, and cache coverage                                                     |
| `tests/Trailblazer.Tests/Navigation`   | Navigator, steering, turning, motor, locomotion, and serialization coverage                                         |
| `tests/Trailblazer.Tests/Support`      | Fixtures and factories for context, grid, chart, and runtime isolation                                              |

## Non-Negotiable Invariants

- Keep simulation math deterministic. Use `Fixed64`, `Vector3d`, and
  `FixedQuaternion` in runtime logic.
- Bind runtime state to an explicit `TrailblazerWorldContext` whenever possible.
- Keep `NavigationChart.Interval` aligned with the owning context's `VoxelSize`.
- Treat chart registration, initialization, partition ownership, guide caches,
  movement groups, and heightmaps as context-owned runtime state.
- Preserve deterministic ordering when traversal, cache keys, neighbor
  selection, or route scoring depend on iteration.
- Do not introduce hidden allocations, LINQ, or broad collection churn in
  per-frame or per-node hot paths.
- Dispose `NavigationGuideLease` instances and return remaining checked-out
  flow/volume guides unless `NavSteering` owns the active session.
- Keep `Navigator.Simulate()` and `Navigator.CommitFrameMotion()` paired with
  host traversal probing between them.
- Serialize authoritative runtime state explicitly; rebuild caches, host
  bindings, live guides, and movement-group coordinator state where practical.
- Reset shared test/runtime state with the existing fixtures before assuming a
  clean world.

## Build And Validation

Useful commands when working in the repository:

```bash
dotnet restore Trailblazer.slnx
dotnet build Trailblazer.slnx --configuration Release
dotnet test Trailblazer.slnx --configuration Release
```

For focused subsystem work, run the matching test area first:

```bash
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~NavSteering
```

Release-mode validation matters because deterministic pathing and navigation
behavior can be sensitive to target framework, optimization, and allocation
patterns.

## Recommended Reading Order

If you are new to the project, read in this order:

1. `README.md`
2. This `Home` page
3. `Overview.md`
4. `NavigationCharts.md`
5. `Pathing.md`
6. `PathGuides.md`
7. `Navigator.md`
8. `NavSteering.md`
9. `NavTurning.md`
10. `NavMotor.md`
11. `Serialization.md` if you are touching save/load behavior
12. The closest matching source and test files under `src/Trailblazer` and
    `tests/Trailblazer.Tests`

## Documentation Approach

The wiki is not trying to restate every public member. Its job is to make
Trailblazer understandable quickly: what the system does, where behavior lives,
which invariants matter, and where someone should go next.

This folder is synced directly to the GitHub wiki by
`.github/workflows/sync-wiki.yml` after successful main-branch CI. When
implementation structure or public behavior changes, update this `Home` page and
the closest subsystem page in the same change.
