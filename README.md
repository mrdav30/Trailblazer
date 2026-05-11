# Trailblazer

**A deterministic, framework-agnostic pathfinding and navigation library for lockstep simulations and games.**

Trailblazer targets simulation-heavy projects that need predictable movement, fixed-point math, and reusable navigation primitives without depending on a specific engine.

The library combines:

- voxel-backed navigation charts
- A* waypoint pathfinding
- flow-field generation for shared destination movement
- cached reusable guide results
- steering, turning, and deterministic motor simulation
- extension points for host-owned traversal, and collision systems

## Status

Trailblazer is being prepared for alpha release. Current work is focused on API clarity, documentation, coverage, and performance hardening in the pathing and navigation hot paths.

## Features

- Deterministic fixed-point math through `FixedMathSharp`
- Engine-agnostic architecture with no required renderer or physics engine
- Dual pathing strategies: waypoint-based A* and destination-centric flow fields
- Chart registration and invalidation through `PathManager`
- Reusable guide caching through `PathGuideFactory`
- Runtime steering, group movement, stuck detection, repathing, and local avoidance hooks
- Deterministic turning and locomotion-aware movement through `NavTurning` and `NavMotor`
- Multi-targeted library build for `netstandard2.1` and `net8.0`

## What Trailblazer Includes

### Pathing Layer

- `NavigationChart` and `NavigationChartCell` for defining chart-backed surface space with optional per-cell cost and hint metadata
- `TraversalAuthoringMap`, `TraversalLegend`, and `TraversalBuildResult` for tokenized `string[,,]` authoring that can build and apply a chart plus generated transitions
- `TraversalTransition` and `TraversalTransitionRegistry` for explicit chart-to-chart and chart-to-volume handoff data
- `PathManager` for chart registration, initialization, unloading, effective-state queries, closest-active-transition queries, and path utilities
- `AStarPathRequest`, `FlowFieldPathRequest`, and `VolumePathRequest` for request configuration
- `AStarSurveyor` and `FlowFieldSurveyor` for raw path generation
- `PathGuideFactory` and `ReusableSurveyResultCache<T>` for guide reuse
- `AStarGuide`, `FlowFieldGuide`, and `VolumeGuide` for runtime direction queries

### Navigation Layer

- `Navigator` as the host-facing simulation coordinator
- `NavSteering` for headings, direct-path checks, guide following, and repathing
- `NavTurning` for deterministic facing updates
- `NavMotor` and locomotion handlers for movement state transitions, gravity, jumps, slopes, swimming, sliding, moving platforms, and per-object locomotion profiles

### Host Responsibilities

Trailblazer does not own your world simulation. Your game or simulation still supplies:

- `GridForge` world creation and grid registration through `GridWorld`
- traversal medium and contact information
- collision and environment probing
- object setup and traversal-state refresh
- any rendering, animation, or ECS integration

## Dependencies

Trailblazer is built around:

- `FixedMathSharp`
- `GridForge`
- `SwiftCollections`

These are part of the design, not incidental utilities. If you integrate Trailblazer directly from source, those packages will be restored automatically through the project file.

## Installation

The library project lives at:

- [`src/Trailblazer/Trailblazer.csproj`](src/Trailblazer/Trailblazer.csproj)

For local development today, reference the project directly:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/Trailblazer/src/Trailblazer/Trailblazer.csproj" />
</ItemGroup>
```

The package id is `Trailblazer`. Once the alpha package is published, installation can be done with:

```bash
dotnet add package Trailblazer
```

## Diagnostics

Trailblazer routes its runtime diagnostics through `TrailblazerLogger`.

- `TrailblazerLogger.MinimumLevel` controls whether warning and error diagnostics are emitted.
- `TrailblazerLogger.Channel` exposes no-unnecessary-work interpolated `Info`, `Warn`, `Error`, and dynamic `Log` helpers.
- `TrailblazerLogger.EnableDebugLogging` opts in to verbose trace-style messages on `TrailblazerLogger.DebugChannel`, which are off by default.
- `TrailblazerLogger.LogHandler` and `TrailblazerLogger.CustomFormatter` let hosts redirect or format Trailblazer diagnostics without modifying simulation code.

## Quick Start

### 1. Register a Navigation Chart

```csharp
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using Trailblazer;
using Trailblazer.Pathing;

var world = new GridWorld();
world.TryAddGrid(
    new GridConfiguration(new Vector3d(-8, -4, -8), new Vector3d(8, 4, 8)),
    out _);

TrailblazerManager.Initialize(world);

bool[,,] chartData = new bool[1, 3, 3]
{
    {
        { true, true, true },
        { true, true, true },
        { true, true, true }
    }
};

var chart = NavigationChart.From3D(
    name: "Arena",
    sourceMap: chartData,
    minBounds: Vector3d.Zero,
    interval: Fixed64.One);

PathManager.Register(chart);
```

Call `TrailblazerManager.Initialize(world)` once during startup to attach Trailblazer to the `GridWorld`
instance you want it to use. After that, convenience APIs such as `PathManager.Register(chart)` use the
configured world automatically. If you need to bind a world opportunistically before manager startup, use
the explicit overloads such as `PathManager.Register(world, chart)`.

Trailblazer is migrating toward explicit multi-world ownership through `TrailblazerWorldContext`.
Today, `TrailblazerManager.Initialize(world)` creates a default context for compatibility while the
static pathing APIs continue to route through the legacy facade. Hosts that need explicit clock and
world lifetime ownership can create a context directly with `TrailblazerWorldContext.Attach(world)` or
`TrailblazerWorldContext.CreateOwned(...)`; chart registries, guide caches, transitions, and navigator
state are being moved behind that context in follow-up phases.

`PathManager.Register(chart)` can initialize the chart by default. Pass `initializeChart: false` when you need to defer live partition activation until a later step. Initialization and registration order are live registration state, not authored chart data; use `PathManager.IsChartInitialized(...)` or `TryGetNavigationChartRegistration(...)` when integration code needs to inspect that state.

`NavigationChart.From3D(...)` also accepts `NavigationChartCell[,,]` when you need authored per-cell surface or volume traversal metadata, cost modifiers, mixed media such as `Solid | Liquid` or `Solid | Gas`, or transition hints. The `bool[,,]` overload emits solid cells by default, or a single authored gas/liquid medium when you pass `TraversalMedium.Gas` or `TraversalMedium.Liquid`. Raw 3D travel runs through `VolumePathRequest`, while chart-backed `AStarPathRequest` and `FlowFieldPathRequest` requests can opt into registered transition fallback through `AllowTraversalTransitions`.

When charts overlap on the same voxel, Trailblazer resolves one winning authored cell instead of merging them additively. Higher `NavigationChart.Priority` wins; same-priority ties fall back to later chart registration order. If one voxel should intentionally support both solid and volume traversal, author that explicitly in the cell payload or tokenized authoring input instead of relying on overlap.

Registered charts are mutable after registration through `PathManager.TryUpdateChartCell(...)` and `PathManager.ApplyChartUpdates(...)`. Initialized charts re-resolve only the touched voxels and keep the rest of the live pathing state intact. Any registered chart whose cells carry generated-transition media participates in the same managed transition lifecycle: local mutations refresh only the affected adjacent pairs, overlap masking suppresses inactive managed transitions without unregistering them, and unloading the chart removes its managed generated transitions entirely. Explicit manual transitions are lifecycle-managed too: Trailblazer keeps them registered, reevaluates them as local chart state changes, and suppresses them automatically when their endpoint media is no longer supported.

If you prefer tokenized setup for tests or lightweight host bootstrapping, `TraversalAuthoringMap` can parse a `string[,,]` using the built-in legend into a `TraversalBuildResult`, and `PathManager.Register(buildResult)` will register the chart, initialize any authored solid or volume partitions, and register generated explicit transitions in one step. Generated transitions inherit the owning chart priority, remain registered while inactive, and become active only when their supporting pair is valid in the current effective world state. The built-in legend and current generator rules are documented in `docs/wiki/AUTHORING.MD`.

### 2. Request a Guide Directly

If you already have your own movement controller, you can use just the pathing layer:

```csharp
using FixedMathSharp;
using Trailblazer.Pathing;

Vector3d origin = new(0, 0, 0);
Vector3d destination = new(2, 0, 2);

var request = AStarPathRequest.Create(origin, destination, Fixed64.One);

if (PathGuideFactory.RequestGuide(request, out AStarGuide guide))
{
    if (guide.TryGetMovementDirection(origin, out Vector3d heading))
    {
        // Use the heading in your own movement code.
    }

    PathGuideFactory.ReturnGuide(guide);
}
```

### 3. Use the Full Navigation Stack

If you want steering, turning, and locomotion support, create a concrete `Navigator` implementation and drive it from your fixed simulation loop:

```csharp
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using Trailblazer;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Pathing;

// Once during startup.
var world = new GridWorld();
world.TryAddGrid(
    new GridConfiguration(new Vector3d(-32, -8, -32), new Vector3d(32, 24, 32)),
    out _);

TrailblazerManager.Initialize(world);

var navigator = new MyNavigator();
navigator.Setup(new Vector3d(0, 0, 0), size: Fixed64.One);
navigator.Initialize(new TrekCondition
{
    Medium = TraversalMedium.Solid,
    SurfaceLevel = Fixed64.Zero,
    GroundState = new GroundCondition()
});

Vector3d target = new(10, 0, 10);
navigator.ConfigureForGuidedTraversal(
    pathAlgorithm: SolidPathAlgorithm.FlowField,
    allowTraversalTransitions: true,
    maxClimbHeight: Fixed64.One,
    flowFieldExtraFloodRange: FlowFieldPathRequest.DefaultExtraFloodRange);

navigator.ApplyGuidedTrekRequest(
    target,
    rate: TrekRate.Moderate);

TrailblazerManager.Simulate();
navigator.Simulate();
navigator.CommitFrameMotion();
TrailblazerManager.LateSimulate();
```

Call `TrailblazerManager.Initialize(world)` once during application startup before entering the fixed-step loop. Trailblazer will lazily initialize as a safety net if needed, but explicit world bootstrap is the intended host flow.

If several navigators should move as one formation, pass the same optional `groupId` to each `ApplyGuidedTrekRequest(...)` call.

Concrete navigator types should implement `CheckTrekCondition()` to populate ground, water, ceiling, and platform state during `CommitFrameMotion()`.

Call `navigator.ConfigureForGuidedTraversal(allowTraversalTransitions: true)` when built-in chart-guided travel should fall back through registered `TraversalTransition` handoffs instead of failing at chart boundaries. The same opt-in also allows bounded swim-exit style handoffs from liquid volume into a follow-up chart request when the requested target is chart-backed outside the active liquid volume, plus bounded aerial landing handoffs when an authored volume-to-chart landing route is a better fit than staying in gas-volume travel.

If a navigator should use a smaller locomotion set, override `CreateLocomotionProfile()` and return a custom profile such as `LocomotionProfile.CreateCoreOnly()`. Core locomotion always includes move, platform, and fall behavior.

## Choosing A Request Type

When you use `Navigator.ApplyGuidedTrekRequest(...)`, the navigator creates the concrete request internally from the current traversal medium plus the guided traversal defaults configured through `Navigator.ConfigureForGuidedTraversal(...)`.

Use `AStarPathRequest` when:

- a single unit needs a concrete trail of waypoints
- you want explicit path smoothing or waypoint progression
- you want per-request heuristic control
- you want optional transition-aware fallback through `AllowTraversalTransitions`

Use `FlowFieldPathRequest` when:

- many units can share the same destination
- you want local vector sampling rather than waypoint following
- you want to restrict per-step climb height while keeping destination-centric reuse
- you want optional transition-aware fallback while keeping a FlowField request surface
- you want destination-centric caching and group-friendly movement; paired `groupId` values can preserve relative offsets while the group stays cohesive

Use `VolumePathRequest` when:

- traversal should stay in raw voxel volume instead of chart-backed surface space
- movement needs gas or liquid routing without authored chart structure
- gas or liquid navigator guidance should stay volume-first but still be allowed to hand off into chart-backed traversal at authored exits or landing zones when guided traversal transitions are enabled

## Project Layout

| Path | Purpose |
| --- | --- |
| [`src/Trailblazer`](src/Trailblazer) | Main library source |
| [`src/Trailblazer/Main`](src/Trailblazer/Main) | Host-facing lifecycle entry points such as `Navigator` and `TrailblazerManager` |
| [`src/Trailblazer/Pathing`](src/Trailblazer/Pathing) | Charts, requests, search, guides, caching, and transitions |
| [`src/Trailblazer/Navigation`](src/Trailblazer/Navigation) | Steering, turning, motor, and movement groups flow |
| [`tests/Trailblazer.Tests`](tests/Trailblazer.Tests) | xUnit test suite |
| [`docs`](docs) | Architecture and subsystem notes |

## Documentation

Start with:

- [`OVERVIEW.md`](docs/wiki/OVERVIEW.md)
- [`PATHING.MD`](docs/wiki/PATHING.MD)
- [`PATHGUIDES.MD`](docs/wiki/PATHGUIDES.MD)
- [`TRANSITIONS.MD`](docs/wiki/TRANSITIONS.MD)
- [`VOLUMETRAVERSAL.MD`](docs/wiki/VOLUMETRAVERSAL.MD)
- [`PATHMANAGER.MD`](docs/wiki/PATHMANAGER.MD)
- [`NAVIGATOR.MD`](docs/wiki/NAVIGATOR.MD)
- [`NAVSTEERING.MD`](docs/wiki/NAVSTEERING.MD)
- [`NAVTURNING.MD`](docs/wiki/NAVTURNING.MD)
- [`NAVMOTOR.MD`](docs/wiki/NAVMOTOR.MD)
- [`GRAVITY.MD`](docs/wiki/GRAVITY.MD)

If you are integrating or extending the runtime, the key source entry points are:

- [`src/Trailblazer/Main/TrailblazerManager.cs`](src/Trailblazer/Main/TrailblazerManager.cs)
- [`src/Trailblazer/Pathing/PathManager.cs`](src/Trailblazer/Pathing/PathManager.cs)
- [`src/Trailblazer/Pathing/Search/PathGuideFactory.cs`](src/Trailblazer/Pathing/Search/PathGuideFactory.cs)
- [`src/Trailblazer/Main/Navigator.cs`](src/Trailblazer/Main/Navigator.cs)
- [`src/Trailblazer/Navigation/Steering/NavSteering.cs`](src/Trailblazer/Navigation/Steering/NavSteering.cs)
- [`src/Trailblazer/Navigation/Turning/NavTurning.cs`](src/Trailblazer/Navigation/Turning/NavTurning.cs)

## Testing and Validation

To restore, build, and run the full suite:

```bash
dotnet restore Trailblazer.slnx
dotnet build Trailblazer.slnx --configuration Release
dotnet test Trailblazer.slnx --configuration Release
```

For focused runs while iterating:

```bash
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~NavSteering
```

Note:

- the library project currently generates NuGet packages on build
- most tests rely on global static managers, so teardown and fixture discipline matter

## Benchmarks

The benchmark suite lives under [`tests/Trailblazer.Benchmarks`](tests/Trailblazer.Benchmarks).

It covers the layered path-request hot paths: raw A* and flow-field surveys, cold and warm guide
resolution, guide cache lifecycle, NavSteering steady-state and first-frame costs, transition-aware
routing, and volume-path request resolution.

List all available benchmark selections:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- list
```

Run all benchmarks:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- all
```

Run a specific group using an alias:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- a-star-path-request
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- nav-steering
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- guide-cache
```

Filter to specific benchmark methods within a run:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- a-star-path-request --filter '*Corridor*'
```

See [`tests/Trailblazer.Benchmarks/README.md`](tests/Trailblazer.Benchmarks/README.md) for the full command reference and benchmark design notes.

## Compatibility

- `netstandard2.1`
- `net8.0`
- Windows, Linux, and macOS host environments supported by .NET

---

## 🤝 Contributing

We welcome contributions! Please see our [CONTRIBUTING](https://github.com/mrdav30/Trailblazer/blob/main/CONTRIBUTING.md) guide for details on how to propose changes, report issues, and interact with the community.

---

## 👥 Contributors

- **mrdav30** - Lead Developer
- Contributions are welcome! Feel free to submit pull requests or report issues.

---

## 💬 Community & Support

For questions, discussions, or general support, join the official Discord community:

👉 **[Join the Discord Server](https://discord.gg/mhwK2QFNBA)**

For bug reports or feature requests, please open an issue in this repository.

We welcome feedback, contributors, and community discussion across all projects.

## License

This project is licensed under the MIT License.

See the following files for details:

- LICENSE – standard MIT license
- NOTICE – additional terms regarding project branding and redistribution
- COPYRIGHT – authorship information
