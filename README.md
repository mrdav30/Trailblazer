# Trailblazer

![Trailblazer Icon](https://raw.githubusercontent.com/mrdav30/trailblazer/main/icon.png)

[![Build](https://github.com/mrdav30/Trailblazer/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/mrdav30/Trailblazer/actions/workflows/build-and-test.yml)
[![Coverage](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fmrdav30.github.io%2FTrailblazer%2FSummary.json&query=%24.summary.linecoverage&suffix=%25&label=coverage&color=brightgreen)](https://mrdav30.github.io/Trailblazer/)
[![NuGet](https://img.shields.io/nuget/v/Trailblazer.svg)](https://www.nuget.org/packages/Trailblazer)
[![NuGet Lean](https://img.shields.io/nuget/v/Trailblazer.Lean.svg?label=nuget%20lean)](https://www.nuget.org/packages/Trailblazer.Lean)
[![License](https://img.shields.io/github/license/mrdav30/Trailblazer.svg)](https://github.com/mrdav30/Trailblazer/blob/main/LICENSE)
[![Frameworks](https://img.shields.io/badge/frameworks-netstandard2.1%20%7C%20net8.0-512BD4.svg)](https://github.com/mrdav30/Trailblazer)

**Deterministic pathfinding and navigation for lockstep simulations and games.**

Trailblazer gives simulation-heavy .NET projects a fixed-point navigation stack
without tying them to a renderer, physics engine, ECS, or game framework. Use
the pathing layer directly for graph-backed surface A* and flow fields,
chart-backed volume routes, and reusable guide data. Add the navigation layer when
you also want steering,
turning, locomotion-aware movement, groups, heightmap grounding, and
frame-by-frame controller state.

The README is the front door. The deeper integration notes live in the
[wiki](docs/wiki/Home.md), starting with the
[architecture overview](docs/wiki/Overview.md).

## Why Trailblazer?

- Deterministic runtime math through `FixedMathSharp` types such as `Fixed64`,
  `Vector3d`, and `FixedQuaternion`.
- Voxel-backed world representation through `GridForge`, with context-owned
  navigation maps and remaining chart-backed volume/handoff state.
- Graph-backed surface waypoints through `NavigationGuideLease`,
  destination-centric fields through `NavigationFlowFieldLease`, and retained
  raw-volume routes through `VolumeGuide`.
- Authored transitions between chart surfaces and raw gas/liquid/volume
  traversal.
- Full navigation stack with `Navigator`, `NavSteering`, `NavTurning`, and
  `NavMotor`.
- Locomotion profiles for grounded movement, falls, jumps, slopes, swimming,
  controlled flight, climbing, slides, and moving platforms.
- Context-local caches, movement groups, heightmaps, diagnostics, and
  serialization boundaries.
- Multi-targeted builds for `netstandard2.1` and `net8.0`.

## Install

```bash
dotnet add package Trailblazer
```

Trailblazer targets `netstandard2.1` and `net8.0`.

### Package Variants

Trailblazer is published in two build variants so you can choose between
built-in `MemoryPack` support and a leaner dependency set:

- `Trailblazer`: Includes `MemoryPack` and depends on the standard
  `FixedMathSharp`, `FixedMathSharp.Chronicler`, `SwiftCollections`,
  `SwiftCollections.FixedMathSharp`, `GridForge`, and `Chronicler.Core`
  packages. This is the best default choice for most .NET applications,
  especially if you want the MemoryPack-backed Chronicler transport available
  out of the box.
- `Trailblazer.Lean`: Excludes the `MemoryPack` package, swaps to
  `FixedMathSharp.Lean`, `FixedMathSharp.Chronicler.Lean`,
  `SwiftCollections.Lean`, `SwiftCollections.FixedMathSharp.Lean`,
  `GridForge.Lean`, and `Chronicler.Core.Lean`, and omits MemoryPack-specific
  source files. Choose
  this when you do not need built-in MemoryPack serialization, when you prefer a
  different serializer, or when you want the leanest dependency surface.

Both variants expose the same core pathing and navigation API. The main
difference is whether `MemoryPack` and the standard dependency chain are
included.

Install via NuGet:

- Standard package:

  ```bash
  dotnet add package Trailblazer
  ```

- Lean package:

  ```bash
  dotnet add package Trailblazer.Lean
  ```

If you build from source, the repository provides matching release
configurations:

- `Release` builds the standard `Trailblazer` package.
- `ReleaseLean` builds the `Trailblazer.Lean` package.

For local development against the repository, reference the project directly:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/Trailblazer/src/Trailblazer/Trailblazer.csproj" />
</ItemGroup>
```

## Mental Model

Trailblazer is easiest to approach as a small pipeline:

1. Create or attach a `TrailblazerWorldContext` for a `GridWorld`.
2. Publish a `NavigationMap` and matching `NavigationAreaPolicy` for graph-backed
   surface routing. Register `NavigationChart` data only for the remaining
   volume/handoff paths that still consume it.
3. Submit a complete immutable `PathQuery`, or let a `Navigator` own its guide
   session.
4. Advance the context and navigators in your fixed-step simulation.
5. Feed host-owned collision, traversal, contacts, and platform state back into
   the navigator.

Trailblazer owns navigation state. Your host still owns rendering, animation,
entity lifetime, collision queries, environment probes, and any engine-specific
integration.

Surface graph A* and Flow routing support rectangular and hex topology without
deriving path cost from a context-wide voxel size. Remaining chart-backed
volume/handoff paths retain their existing constraints until their owning
cutover phases.

## Quick Start

After publishing a navigation map and its area policy, construct one complete
surface query and own the returned lease. The [Pathing wiki](docs/wiki/Pathing.md)
and [Navigator wiki](docs/wiki/Navigator.md) cover the surrounding setup.

```csharp
using FixedMathSharp;
using Trailblazer;
using Trailblazer.Pathing;

using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
Vector3d origin = new(0, 0, 0);
Vector3d destination = new(2, 0, 2);
var profile = new NavigationAgentProfile(
    new KinematicBodyShape(Fixed64.Half, Fixed64.One, Fixed64.Zero),
    Fixed64.One,
    Fixed64.One,
    Fixed64.FromFraction(1, 4),
    TraversalMedia.Solid,
    TraversalCapability.None);
var query = new PathQuery(
    new NavigationEndpoint(origin, "Arena"),
    new NavigationEndpoint(destination, "Arena"),
    profile,
    new NavigationAreaPolicyKey("default", revision: 1),
    new TraversalIntent(TraversalDomain.Surface, TraversalMedium.Solid, TraversalDomain.Surface),
    PathAlgorithm.AStar,
    new NavigationWorkBudget(1024, 64, 4096, 16384, 4096, 0, 0, 0, 0, 0, 0),
    allowTransitions: false);

NavigationGuideStatus status = context.Guides.RequestGuide(query, out NavigationGuideLease? lease);
if (status == NavigationGuideStatus.Success && lease != null)
{
    using (lease)
    {
        if (lease.TryGetCurrentWaypoint(out NavigationCellAddress address, out Vector3d footWaypoint)
            == NavigationGuideStatus.Success)
        {
            // Consume the waypoint in your own movement code, or pass the query to Navigator.
        }
    }
}
```

## Main Systems

| Area            | What it does                                                                                                                                               | Start here                                                                                                      |
| --------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| World context   | Owns one `GridWorld`, the deterministic clock, pathing services, guide caches, transitions, heightmaps, movement groups, diagnostics, and lifecycle hooks. | [Overview](docs/wiki/Overview.md)                                                                               |
| Chart authoring | Builds the remaining volume/handoff traversal data from `NavigationChart`, `NavigationChartCell`, or tokenized `TraversalAuthoringMap` input.              | [ChartAuthoring](docs/wiki/ChartAuthoring.md), [NavigationCharts](docs/wiki/NavigationCharts.md)                |
| Pathing         | Resolves graph-backed surface A* and Flow `PathQuery` values plus the remaining volume request family.                                                      | [Pathing](docs/wiki/Pathing.md), [PathGuides](docs/wiki/PathGuides.md)                                          |
| Transitions     | Describes explicit handoffs between charts and raw volume traversal, including generated transition data.                                                  | [Transitions](docs/wiki/Transitions.md), [VolumeTraversal](docs/wiki/VolumeTraversal.md)                        |
| Navigation      | Coordinates steering, turning, motor simulation, locomotion, occupancy, and host traversal state.                                                          | [Navigator](docs/wiki/Navigator.md), [NavSteering](docs/wiki/NavSteering.md), [NavMotor](docs/wiki/NavMotor.md) |
| Heightmaps      | Provides deterministic ground/contact Y sampling separate from chart walkability.                                                                          | [HeightMaps](docs/wiki/HeightMaps.md)                                                                           |
| Serialization   | Uses explicit Chronicler record/populate behavior for supported navigation state.                                                                          | [Serialization](docs/wiki/Serialization.md)                                                                     |

## Choosing A Request Type

Use `PathQuery` with `PathAlgorithm.AStar` when one agent needs a concrete
surface waypoint trail. A successful request returns a disposable
`NavigationGuideLease`; every acquisition and cursor operation reports a
`NavigationGuideStatus`.

Use `PathQuery` with `PathAlgorithm.FlowField` and `FlowFieldQueryOptions` when
many agents can share a destination and sample local movement vectors from one
cached graph field. `context.Guides.RequestFlowField(...)` returns a disposable
`NavigationFlowFieldLease`.

Use `VolumePathRequest` when movement should route through raw 3D voxel
connectivity for gas, liquid, aerial, or chart-optional travel.

For guided surface travel, pass that exact query to
`Navigator.ApplyGuidedTrekRequest(PathQuery, ...)`. The query's profile must
match the navigator's configured `NavigationProfile`, and its start must equal
the navigator's derived foot position.

## Repository Map

| Path                                                           | Purpose                                                                 |
| -------------------------------------------------------------- | ----------------------------------------------------------------------- |
| [`src/Trailblazer`](src/Trailblazer)                           | Main library source                                                     |
| [`src/Trailblazer/Runtime`](src/Trailblazer/Runtime)           | World context, deterministic clock, lifecycle hooks, and reset behavior |
| [`src/Trailblazer/Pathing`](src/Trailblazer/Pathing)           | Charts, requests, transitions, search, guides, caches, and voxel lookup |
| [`src/Trailblazer/Navigation`](src/Trailblazer/Navigation)     | Navigator, steering, turning, motor, locomotion, and movement groups    |
| [`src/Trailblazer/Heightmaps`](src/Trailblazer/Heightmaps)     | Compressed deterministic ground/contact Y sampling                      |
| [`tests/Trailblazer.Tests`](tests/Trailblazer.Tests)           | xUnit v3 test suite                                                     |
| [`tests/Trailblazer.Benchmarks`](tests/Trailblazer.Benchmarks) | BenchmarkDotNet performance suite                                       |
| [`docs/wiki`](docs/wiki)                                       | Architecture, subsystem, and integration documentation                  |

## Build And Test

```bash
dotnet restore Trailblazer.slnx
dotnet build Trailblazer.slnx --configuration Release
dotnet test Trailblazer.slnx --configuration Release
```

For focused work, run the matching test area first:

```bash
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~NavSteering
```

Release builds generate NuGet packages because `GeneratePackageOnBuild` is
enabled.

## Benchmarks

The benchmark suite measures path-request and navigation hot paths: graph
surface A*, graph Flow reverse integration, prefix promotion, contention,
invalidation, warm sampling, guide lifecycle, and volume routing.

List available benchmark selections:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- list
```

Run a specific group:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- navigation-flow-field
```

See the [benchmark README](tests/Trailblazer.Benchmarks/README.md) for the full
command reference and suite design notes.

## Documentation

Start with the [wiki home](docs/wiki/Home.md) if you are evaluating the project,
or jump straight into:

- [Overview](docs/wiki/Overview.md) for the runtime model
- [Pathing](docs/wiki/Pathing.md) for guide requests and direct pathing usage
- [Navigator](docs/wiki/Navigator.md) for full-stack movement integration
- [Serialization](docs/wiki/Serialization.md) for Chronicler behavior and load
  boundaries

The wiki is intentionally more detailed than this README. If behavior changes,
keep code, tests, README, and the relevant wiki page aligned.

## Compatibility

- `netstandard2.1`
- `net8.0`
- Windows, Linux, and macOS host environments supported by .NET

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) before
opening a pull request, and prefer focused changes with release-mode validation.

For issues, feature requests, or questions, use the repository issue tracker.
Community discussion is also available on the official
[Discord server](https://discord.gg/mhwK2QFNBA).

## License

Trailblazer is licensed under the MIT License. See [LICENSE](LICENSE),
[NOTICE](NOTICE), and [COPYRIGHT](COPYRIGHT) for the project terms and
attribution details.
