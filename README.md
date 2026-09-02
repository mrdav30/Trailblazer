# Trailblazer

![Trailblazer Icon](https://raw.githubusercontent.com/mrdav30/trailblazer/main/icon.png)

[![Build](https://github.com/mrdav30/Trailblazer/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/mrdav30/Trailblazer/actions/workflows/build-and-test.yml)
[![NuGet](https://img.shields.io/nuget/v/Trailblazer.svg)](https://www.nuget.org/packages/Trailblazer)
[![NuGet Lean](https://img.shields.io/nuget/v/Trailblazer.Lean.svg?label=nuget%20lean)](https://www.nuget.org/packages/Trailblazer.Lean)
[![License](https://img.shields.io/github/license/mrdav30/Trailblazer.svg)](https://github.com/mrdav30/Trailblazer/blob/main/LICENSE)

Trailblazer is deterministic, engine-agnostic navigation for lockstep games and
simulations. One immutable map and query model covers grounded movement,
free-form Gas/Liquid movement, rectangular and hex-prism grids, dynamic
overlays, A*, flow fields, and explicit actions such as ladders or takeoff.

It uses [FixedMathSharp](https://github.com/mrdav30/FixedMathSharp) for fixed-point
math, [GridForge](https://github.com/mrdav30/GridForge) for world topology and
geometry, [SwiftCollections](https://github.com/mrdav30/SwiftCollections) for
low-allocation storage, and [Chronicler](https://github.com/mrdav30/Chronicler)
for explicit serialization.

## Install

Trailblazer targets `netstandard2.1` and `net8.0`.

```bash
dotnet add package Trailblazer
```

Use `Trailblazer.Lean` when the rest of the LSF dependency stack also uses Lean
packages. Lean omits the MemoryPack transport.

```bash
dotnet add package Trailblazer.Lean
```

## Model

```text
GridForge world
  + immutable NavigationMap bakes
  + addressed overlay transactions
  + NavigationAreaPolicy revisions
                 |
                 v
       immutable medium-state graph
                 |
            one PathQuery
            /           \
       A* step lease   Flow sample lease
            \           /
       movement or held action instruction
```

A search state is one addressed cell plus one exact `TraversalMedium`.
Ordinary movement retains that medium. An authored transition may retain it or
change it, and a guide advances across the action only after the host completes
the exact instruction.

## Quick Start

First publish the referenced GridForge-backed map and area policy. The following
C# fragment assumes the shown positions, profile, policy key, and finite budget
have already been created:

```csharp
PathQuery query = new(
    new NavigationEndpoint(startFoot, "overworld"),
    new NavigationEndpoint(destinationFoot, "overworld"),
    profile,
    areaPolicyKey,
    new TraversalIntent(
        TraversalMedium.Solid,
        TraversalMedia.Solid | TraversalMedia.Liquid),
    PathAlgorithm.AStar,
    budget,
    allowTransitions: true);

NavigationGuideStatus status = context.Guides.RequestGuide(
    query,
    out NavigationGuideLease? acquired);

if (status == NavigationGuideStatus.Success)
{
    using NavigationGuideLease guide = acquired!.Value;
    // Consume steps using the completion-safe loop in PathGuides.md.
}
```

Use `PathAlgorithm.FlowField` with `RequestFlowField(...)` when many agents
share a destination. Both algorithms consume the same graph, costs,
dependencies, and action-completion contract.

## Core Rules

- Maps bind stable IDs to normalized GridForge configurations.
- Effective cell precedence is overlay, explicit bake, map default, then no
  cell; each winning `NavigationCell` is complete.
- Queries explicitly provide agent geometry, start medium, target media, area
  policy, algorithm, work budget, and transition permission.
- Runtime changes publish at deterministic fixed-step boundaries through
  `TrailblazerWorldContext.Pathing`.
- Hosts own terrain/material classification, physics, animation, and action
  execution.

## Documentation

- [Getting started](docs/wiki/GettingStarted.md)
- [Overview](docs/wiki/Overview.md)
- [Navigation maps](docs/wiki/NavigationMaps.md)
- [Map authoring](docs/wiki/MapAuthoring.md)
- [Map publication and overlays](docs/wiki/MapPublication.md)
- [Queries and algorithms](docs/wiki/Pathing.md)
- [Path guides](docs/wiki/PathGuides.md)
- [Transitions](docs/wiki/Transitions.md)
- [Gas and Liquid travel](docs/wiki/VolumeTraversal.md)
- [Navigator](docs/wiki/Navigator.md)
- [Serialization](docs/wiki/Serialization.md)
- [Troubleshooting](docs/wiki/Troubleshooting.md)
- [v1 to v2 migration](docs/MIGRATION.md)

## Build And Test

```bash
dotnet restore Trailblazer.slnx --property:Configuration=Release
dotnet build Trailblazer.slnx --configuration Release --no-restore
dotnet test Trailblazer.slnx --configuration Release --no-build
```

Repeat with `ReleaseLean` to validate the Lean package family.

## License

MIT. See [LICENSE](LICENSE).
