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
the pathing layer directly for chart-backed A*, flow fields, volume routes, and
reusable guide data. Add the navigation layer when you also want steering,
turning, locomotion-aware movement, groups, heightmap grounding, and
frame-by-frame controller state.

The README is the front door. The deeper integration notes live in the
[wiki](docs/wiki/Home.md), starting with the
[architecture overview](docs/wiki/Overview.md).

## Why Trailblazer?

- Deterministic runtime math through `FixedMathSharp` types such as `Fixed64`,
  `Vector3d`, and `FixedQuaternion`.
- Voxel-backed world representation through `GridForge`, with explicit chart
  registration and context-owned runtime state.
- Three guide families: waypoint-oriented `AStarGuide`, destination-centric
  `FlowFieldGuide`, and raw-volume `VolumeGuide`.
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
2. Register `NavigationChart` data whose cell interval matches the context's
   representative cubic cell edge.
3. Request an `IGuide` directly, or let a `Navigator` create and manage guide
   requests.
4. Advance the context and navigators in your fixed-step simulation.
5. Feed host-owned collision, traversal, contacts, and platform state back into
   the navigator.

Trailblazer owns navigation state. Your host still owns rendering, animation,
entity lifetime, collision queries, environment probes, and any engine-specific
integration.

Trailblazer currently supports GridForge worlds whose active grids all use dense
rectangular-prism storage with one shared cubic cell edge. Hex, sparse,
anisotropic, or conflicting active-grid metrics fail fast at the context/request
boundary. Those topologies require a dedicated pathfinding path and are planned
as a fast-follow rather than being approximated by the cubic implementation.

## Quick Start

This example builds a tiny chart, requests an A* guide, and samples the first
movement direction. The [Pathing wiki](docs/wiki/Pathing.md) and
[Navigator wiki](docs/wiki/Navigator.md) cover complete integration flows.

```csharp
using FixedMathSharp;
using GridForge.Configuration;
using Trailblazer;
using Trailblazer.Pathing;

using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
context.World.TryAddGrid(
    new GridConfiguration(new Vector3d(-4, -1, -4), new Vector3d(8, 2, 8)),
    out _);

bool[,,] cells = new bool[1, 3, 3]
{
    {
        { true, true, true },
        { true, true, true },
        { true, true, true }
    }
};

NavigationChart chart = NavigationChart.From3D(
    name: "Arena",
    sourceMap: cells,
    minBounds: Vector3d.Zero,
    interval: context.VoxelSize);

context.Pathing.Register(chart);

Vector3d origin = new(0, 0, 0);
Vector3d destination = new(2, 0, 2);
AStarPathRequest? request = AStarPathRequest.Create(context, origin, destination, Fixed64.One);

if (request != null && context.Guides.RequestGuide(request, out AStarGuide? guide))
{
    try
    {
        if (guide.TryGetMovementDirection(origin, out Vector3d heading))
        {
            // Apply heading in your own movement code, or use Navigator for the full stack.
        }
    }
    finally
    {
        context.Guides.ReturnGuide(guide);
    }
}
```

## Main Systems

| Area            | What it does                                                                                                                                               | Start here                                                                                                      |
| --------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| World context   | Owns one `GridWorld`, the deterministic clock, pathing services, guide caches, transitions, heightmaps, movement groups, diagnostics, and lifecycle hooks. | [Overview](docs/wiki/Overview.md)                                                                               |
| Chart authoring | Builds surface and volume traversal data from `NavigationChart`, `NavigationChartCell`, or tokenized `TraversalAuthoringMap` input.                        | [ChartAuthoring](docs/wiki/ChartAuthoring.md), [NavigationCharts](docs/wiki/NavigationCharts.md)                |
| Pathing         | Resolves `AStarPathRequest`, `FlowFieldPathRequest`, and `VolumePathRequest` into reusable guides.                                                         | [Pathing](docs/wiki/Pathing.md), [PathGuides](docs/wiki/PathGuides.md)                                          |
| Transitions     | Describes explicit handoffs between charts and raw volume traversal, including generated transition data.                                                  | [Transitions](docs/wiki/Transitions.md), [VolumeTraversal](docs/wiki/VolumeTraversal.md)                        |
| Navigation      | Coordinates steering, turning, motor simulation, locomotion, occupancy, and host traversal state.                                                          | [Navigator](docs/wiki/Navigator.md), [NavSteering](docs/wiki/NavSteering.md), [NavMotor](docs/wiki/NavMotor.md) |
| Heightmaps      | Provides deterministic ground/contact Y sampling separate from chart walkability.                                                                          | [HeightMaps](docs/wiki/HeightMaps.md)                                                                           |
| Serialization   | Uses explicit Chronicler record/populate behavior for supported navigation state.                                                                          | [Serialization](docs/wiki/Serialization.md)                                                                     |

## Choosing A Request Type

Use `AStarPathRequest` when one agent needs a concrete waypoint trail or you
want explicit waypoint progression.

Use `FlowFieldPathRequest` when many agents can share a destination and sample
local movement vectors from one cached field.

Use `VolumePathRequest` when movement should route through raw 3D voxel
connectivity for gas, liquid, aerial, or chart-optional travel.

When using `Navigator.ApplyGuidedTrekRequest(...)`, the navigator chooses the
request family from the current traversal medium and the settings configured
through `Navigator.ConfigureForGuidedTraversal(...)`.

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

The benchmark suite measures path-request and navigation hot paths: raw A* and
flow-field surveys, cold and warm guide resolution, guide cache lifecycle,
steering steady-state costs, transition fallback, and volume routing.

List available benchmark selections:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- list
```

Run a specific group:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- guide-cache
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
