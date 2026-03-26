# Trailblazer

**A deterministic, framework-agnostic pathfinding and navigation library for lockstep simulations and games.**

Trailblazer targets simulation-heavy projects that need predictable movement, fixed-point math, and reusable navigation primitives without depending on a specific engine.

The library combines:

- voxel-backed navigation charts
- A* waypoint pathfinding
- flow-field generation for shared destination movement
- cached reusable guide results
- steering, turning, and deterministic motor simulation
- extension points for host-owned traversal, collision, and animation systems

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
- `PathManager` for chart registration, initialization, unloading, and path utilities
- `AStarPathRequest`, `FlowFieldPathRequest`, and `VolumePathRequest` for request configuration
- `AStarSurveyor` and `FlowFieldSurveyor` for raw path generation
- `PathGuideFactory` and `ReusableSurveyResultCache<T>` for guide reuse
- `AStarGuide`, `FlowFieldGuide`, and `VolumeGuide` for runtime direction queries

### Navigation Layer

- `Navigator` as the host-facing simulation coordinator
- `NavSteering` for headings, direct-path checks, guide following, and repathing
- `NavTurning` for deterministic facing updates
- `NavMotor` and locomotion handlers for movement state transitions, gravity, jumps, slopes, swimming, sliding, moving platforms, and per-navigator locomotion profiles

### Host Responsibilities

Trailblazer does not own your world simulation. Your game or simulation still supplies:

- global grid setup through `GridForge`
- traversal medium and contact information
- collision and environment probing
- navigator setup and traversal-state refresh
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

## Quick Start

### 1. Register a Navigation Chart

```csharp
using FixedMathSharp;
using Trailblazer.Pathing;

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
PathManager.InitializeChart(chart.Name);
```

`NavigationChart.From3D(...)` also accepts `NavigationChartCell[,,]` when you need authored per-cell surface or volume traversal metadata, cost modifiers, or transition hints. Raw 3D travel still runs through `VolumePathRequest`, while chart-backed `AStarPathRequest` and `FlowFieldPathRequest` requests can opt into registered transition fallback through `AllowTraversalTransitions`.

If you prefer tokenized setup for tests or lightweight host bootstrapping, `TraversalAuthoringMap` can parse a `string[,,]` using the built-in legend into a `TraversalBuildResult`, and `PathManager.Register(buildResult)` will register the chart, initialize any authored solid or volume partitions, and register generated explicit transitions in one step. The built-in legend and current generator rules are documented in `docs/AUTHORING.MD`.

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
using Trailblazer;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Pathing;

// Once during startup.
TrailblazerManager.Initialize();

var navigator = new MyNavigator();
navigator.Setup(new Vector3d(0, 0, 0), size: Fixed64.One);
navigator.Initialize(new TrekCondition
{
    Medium = TraversalMedium.Solid,
    SurfaceLevel = Fixed64.Zero,
    GroundState = new GroundCondition()
});

Vector3d target = new(10, 0, 10);
navigator.GuidedPathMode = GuidedPathMode.FlowField;
navigator.GuidedAllowTraversalTransitions = true;
navigator.GuidedFlowFieldExtraFloodRange = FlowFieldPathRequest.DefaultExtraFloodRange;

navigator.ApplyGuidedTrekRequest(
    target,
    rate: TrekRate.Moderate);

TrailblazerManager.Simulate();
navigator.Simulate();
navigator.CommitFrameMotion();
TrailblazerManager.LateSimulate();
```

Call `TrailblazerManager.Initialize()` once during application startup before entering the fixed-step loop. Trailblazer will lazily initialize as a safety net if needed, but explicit bootstrap is the intended host flow.

If several navigators should move as one formation, pass the same optional `groupId` to each `ApplyGuidedTrekRequest(...)` call.

Concrete navigator types should implement `CheckTrekCondition()` to populate ground, water, ceiling, and platform state during `CommitFrameMotion()`.

Set `navigator.GuidedAllowTraversalTransitions = true;` when built-in chart-guided travel should fall back through registered `TraversalTransition` handoffs instead of failing at chart boundaries. The same opt-in also allows bounded swim-exit style handoffs from liquid volume into a follow-up chart request when the requested target is chart-backed outside the active liquid volume, plus bounded aerial landing handoffs when an authored volume-to-chart landing route is a better fit than staying in gas-volume travel.

If a navigator should use a smaller locomotion set, override `CreateLocomotionProfile()` and return a custom profile such as `LocomotionProfile.CreateMoveAndFallOnly()`.

## Choosing A Request Type

When you use `Navigator.ApplyGuidedTrekRequest(...)`, the navigator creates the concrete request internally based on `GuidedPathMode` and its guided-path defaults.

Use `AStarPathRequest` when:

- a single unit needs a concrete trail of waypoints
- you want explicit path smoothing or waypoint progression
- you want per-request heuristic control
- you want optional transition-aware fallback through `AllowTraversalTransitions`

Use `FlowFieldPathRequest` when:

- many units can share the same destination
- you want local vector sampling rather than waypoint following
- you want optional transition-aware fallback while keeping a FlowField request surface
- you want destination-centric caching and group-friendly movement; paired `groupId` values can preserve relative offsets while the group stays cohesive

Use `VolumePathRequest` when:

- traversal should stay in raw voxel volume instead of chart-backed surface space
- movement needs gas or liquid routing without authored chart structure
- navigator-owned `Swim` and `Aerial` guidance should stay volume-first but still be allowed to hand off into chart-backed traversal at authored exits or landing zones when `GuidedAllowTraversalTransitions` is enabled

## Project Layout

| Path | Purpose |
| --- | --- |
| [`src/Trailblazer`](src/Trailblazer) | Main library source |
| [`src/Trailblazer/Main`](src/Trailblazer/Main) | Host-facing lifecycle entry points such as `Navigator` and `TrailblazerManager` |
| [`src/Trailblazer/Pathing`](src/Trailblazer/Pathing) | Charts, requests, search, guides, caching, and transitions |
| [`src/Trailblazer/Navigation`](src/Trailblazer/Navigation) | Steering, turning, motor, movement groups, and animation flow |
| [`tests/Trailblazer.Tests`](tests/Trailblazer.Tests) | xUnit test suite |
| [`docs`](docs) | Architecture and subsystem notes |

## Documentation

Start with:

- [`docs/OVERVIEW.md`](docs/OVERVIEW.md)
- [`docs/PATHING.MD`](docs/PATHING.MD)
- [`docs/PATHGUIDES.MD`](docs/PATHGUIDES.MD)
- [`docs/TRANSITIONS.MD`](docs/TRANSITIONS.MD)
- [`docs/VOLUMETRAVERSAL.MD`](docs/VOLUMETRAVERSAL.MD)
- [`docs/PATHMANAGER.MD`](docs/PATHMANAGER.MD)
- [`docs/NAVIGATOR.MD`](docs/NAVIGATOR.MD)
- [`docs/NAVSTEERING.MD`](docs/NAVSTEERING.MD)
- [`docs/NAVTURNING.MD`](docs/NAVTURNING.MD)
- [`docs/NAVMOTOR.MD`](docs/NAVMOTOR.MD)
- [`docs/GRAVITY.MD`](docs/GRAVITY.MD)

If you are integrating or extending the runtime, the key source entry points are:

- [`src/Trailblazer/Main/TrailblazerManager.cs`](src/Trailblazer/Main/TrailblazerManager.cs)
- [`src/Trailblazer/Pathing/PathManager.cs`](src/Trailblazer/Pathing/PathManager.cs)
- [`src/Trailblazer/Pathing/Support/Guide/PathGuideFactory.cs`](src/Trailblazer/Pathing/Support/Guide/PathGuideFactory.cs)
- [`src/Trailblazer/Main/Navigator.cs`](src/Trailblazer/Main/Navigator.cs)
- [`src/Trailblazer/Navigation/Steering/NavSteering.cs`](src/Trailblazer/Navigation/Steering/NavSteering.cs)
- [`src/Trailblazer/Navigation/Turning/NavTurning.cs`](src/Trailblazer/Navigation/Turning/NavTurning.cs)

## Testing and Validation

To restore, build, and run the full suite:

```bash
dotnet restore Trailblazer.sln
dotnet build Trailblazer.sln --configuration Release
dotnet test Trailblazer.sln --configuration Release
```

For focused runs while iterating:

```bash
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~NavSteering
```

Note:

- the library project currently generates NuGet packages on build
- most tests rely on global static managers, so teardown and fixture discipline matter

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
