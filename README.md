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
- Runtime steering, stuck detection, repathing, and local avoidance hooks
- Deterministic turning and locomotion-aware movement through `NavTurning` and `NavMotor`
- Multi-targeted library build for `netstandard2.1` and `net8.0`

## What Trailblazer Includes

### Pathing Layer

- `NavigationChart` for defining walkable space
- `PathManager` for chart registration, initialization, unloading, and path utilities
- `AStarPathRequest` and `FlowFieldPathRequest` for request configuration
- `AStarSurveyor` and `FlowFieldSurveyor` for raw path generation
- `PathGuideFactory` and `ReusableSurveyResultCache<T>` for guide reuse
- `AStarGuide` and `FlowFieldGuide` for runtime direction queries

### Navigation Layer

- `Navigator` as the host-facing simulation abstraction
- `NavSteering` for headings, direct-path checks, guide following, and repathing
- `NavTurning` for deterministic facing updates
- `NavMotor` and locomotion handlers for movement state transitions, gravity, jumps, slopes, swimming, sliding, and moving platforms

### Host Responsibilities

Trailblazer does not own your world simulation. Your game or simulation still supplies:

- global grid setup through `GridForge`
- traversal medium and contact information
- collision and environment probing
- concrete navigator implementations
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

var navigator = new MyNavigator();
navigator.Setup(new Vector3d(0, 0, 0), size: Fixed64.One);
navigator.Initialize(new TrekCondition(
    medium: TraversalMedium.Ground,
    surfaceLevel: Fixed64.Zero,
    surfaceCondition: GroundCondition.CreateEmpty()));

Vector3d target = new(10, 0, 10);
var request = FlowFieldPathRequest.Create(navigator.Position, target, navigator.Size);

navigator.ApplyGuidedTrekRequest(
    pathRequest: request,
    destination: target,
    rate: TrekRate.Moderate);

TrailblazerManager.Simulate();
navigator.Simulate();

// Refresh ground, water, ceiling, and platform state from your host world here.
navigator.SetTrekCondition(
    medium: TraversalMedium.Ground,
    surfaceLevel: Fixed64.Zero,
    surfaceCondition: GroundCondition.CreateEmpty(),
    updateMotorState: true);

navigator.CommitFrameMotion();
TrailblazerManager.LateSimulate();
```

## Choosing Between A* and Flow Fields

Use `AStarPathRequest` when:

- a single unit needs a concrete trail of waypoints
- you want explicit path smoothing or waypoint progression
- you want per-request heuristic control

Use `FlowFieldPathRequest` when:

- many units can share the same destination
- you want local vector sampling rather than waypoint following
- you want destination-centric caching and group-friendly movement

## Project Layout

| Path | Purpose |
| --- | --- |
| [`src/Trailblazer`](src/Trailblazer) | Main library source |
| [`src/Trailblazer/Pathing`](src/Trailblazer/Pathing) | Charts, requests, surveyors, guides, caching |
| [`src/Trailblazer/Navigation`](src/Trailblazer/Navigation) | Steering, turning, motor, navigator flow |
| [`tests/Trailblazer.Tests`](tests/Trailblazer.Tests) | xUnit test suite |
| [`docs`](docs) | Architecture and subsystem notes |

## Documentation

Start with:

- [`docs/OVERVIEW.md`](docs/OVERVIEW.md)
- [`docs/NAVMOTOR.MD`](docs/NAVMOTOR.MD)
- [`docs/GRAVITY.MD`](docs/GRAVITY.MD)

If you are integrating or extending the runtime, the key source entry points are:

- [`src/Trailblazer/TrailblazerManager.cs`](src/Trailblazer/TrailblazerManager.cs)
- [`src/Trailblazer/Pathing/PathManager.cs`](src/Trailblazer/Pathing/PathManager.cs)
- [`src/Trailblazer/Pathing/Support/PathGuideFactory.cs`](src/Trailblazer/Pathing/Support/PathGuideFactory.cs)
- [`src/Trailblazer/Navigation/Navigator.cs`](src/Trailblazer/Navigation/Navigator.cs)
- [`src/Trailblazer/Navigation/Steering/NavSteering.cs`](src/Trailblazer/Navigation/Steering/NavSteering.cs)

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

## Contributing

Contributions are welcome, especially in these areas:

- pathing performance and allocation reduction
- deterministic correctness and bug fixing
- API surface cleanup
- XML documentation and docs improvements
- coverage expansion for caching and integration edge cases

When changing behavior, update tests and documentation in the same pass.

## License

Trailblazer is licensed under the MIT License. See [`LICENSE.md`](LICENSE.md) for details.
