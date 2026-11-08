# Trailblazer

![Trailblazer Icon](https://raw.githubusercontent.com/mrdav30/trailblazer/main/icon.png)

[![Build](https://github.com/mrdav30/Trailblazer/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/mrdav30/Trailblazer/actions/workflows/build-and-test.yml)
[![Coverage](https://mrdav30.github.io/Trailblazer/coverage/badge_branchcoverage.svg)](https://mrdav30.github.io/Trailblazer/coverage/)
[![NuGet](https://img.shields.io/nuget/v/Trailblazer.svg)](https://www.nuget.org/packages/Trailblazer)
[![License](https://img.shields.io/github/license/mrdav30/Trailblazer.svg)](https://github.com/mrdav30/Trailblazer/blob/main/LICENSE)
[![Documentation](https://img.shields.io/badge/docs-guides%20%26%20API-f4511e)](https://mrdav30.github.io/Trailblazer/)
[![Discord](https://img.shields.io/badge/discord-join%20community-5865F2?logo=discord&logoColor=white)](https://discord.gg/mhwK2QFNBA)

**Deterministic navigation and character control for lockstep simulations and
games.**

Trailblazer gives lockstep games and simulations one explicit navigation model
for grounded, flying, and swimming agents. Build immutable navigation maps over
GridForge worlds, query them with deterministic A\* or flow fields, and turn the
result into movement or host-owned actions such as ladders, jumps, and
teleporters.

## Why Trailblazer?

- **Every peer follows the same world.** Fixed-point costs, canonical ordering,
  finite work budgets, and immutable published graphs keep queries reproducible.
- **One model covers several kinds of travel.** Solid, Gas, and Liquid movement
  share maps, queries, policies, dependencies, guides, and caches.
- **World changes are explicit.** Publish map revisions and addressed overlays
  at deterministic fixed-step boundaries.
- **Time supports long-running worlds.** Long frame counts and wide elapsed
  timestamps share one context-owned clock; movement keeps fixed-point steps.
- **Actions stay semantic.** A guide can hold a transition instruction while
  your host owns animation, physics, and gameplay execution.
- **Controllers are optional.** Use pathing by itself or compose Navigator,
  steering, turning, locomotion, occupancy, and deterministic heightmaps.
- **The engine stays outside.** Trailblazer has no Unity, Godot, Unreal, or
  renderer dependency.

## Install

```bash
dotnet add package Trailblazer
```

Trailblazer targets `netstandard2.1` and `net10.0`. The standard package
includes JSON and MemoryPack serialization support.

## Request a route

This excerpt assumes your host has created the context, published the referenced
map and area policy, and supplied the endpoints, profile, and budget. Describe
the request with one immutable `PathQuery`.

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
    // Consume ordinary steps and complete held actions over fixed frames.
}
```

Use `PathAlgorithm.FlowField` with `RequestFlowField(...)` when many agents
share a destination. Both algorithms consume the same graph, costs,
dependencies, and action-completion contract.

These request methods finish synchronously. For large cold requests during
gameplay, `context.Guides.Schedule(query, out request)` spreads acquisition over
fixed frames. See the
[scheduling guide](https://mrdav30.github.io/Trailblazer/guides/Pathing.html#spreading-acquisition-across-fixed-frames)
for finite capacity, result ownership and latency tradeoffs. Navigator hosts can
opt into the same queue through
`navigator.Steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled)`;
see
[steering acquisition](https://mrdav30.github.io/Trailblazer/guides/NavSteering.html#scheduled-acquisition-and-moving-origins).

## Choose a package

| Package                                                               | Use it when                                                                                           |
| --------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| [`Trailblazer`](https://www.nuget.org/packages/Trailblazer)           | Your LSF dependency graph uses the standard packages and you want JSON plus MemoryPack serialization. |
| [`Trailblazer.Lean`](https://www.nuget.org/packages/Trailblazer.Lean) | Your complete LSF dependency graph uses Lean packages and you do not need the MemoryPack transport.   |

Do not mix standard and Lean LSF packages in one dependency graph.

## Gravitas integration

For collision-backed 3D movement, the optional `Trailblazer.Gravitas` and
`Trailblazer.Gravitas.Lean` adapters connect Navigator to host-owned Gravitas
bodies without coupling the core libraries. See the
[integration guide](https://mrdav30.github.io/Trailblazer/guides/Gravitas.html).
For coordinated source development, see the
[adapter README](https://github.com/mrdav30/Trailblazer/blob/main/src/Trailblazer.Gravitas/README.md).

## Part of the LSF ecosystem

Trailblazer supplies navigation and character control in the Lockstep Simulation
Framework (LSF).

- [FixedMathSharp](https://github.com/mrdav30/FixedMathSharp) supplies
  deterministic fixed-point math.
- [SwiftCollections](https://github.com/mrdav30/SwiftCollections) supplies
  low-allocation collections and storage.
- [GridForge](https://github.com/mrdav30/GridForge) supplies world topology,
  geometry, and physical cell identity.
- [Chronicler](https://github.com/mrdav30/Chronicler) supplies explicit state
  transfer and serialization.
- [Gravitas](https://github.com/mrdav30/Gravitas) supplies deterministic physics
  for the optional collision-backed controller adapter.

Shared logging and exception guards come from the independent
`Chronicler.Diagnostics` package. See the
[migration guide](https://github.com/mrdav30/Trailblazer/blob/main/docs/MIGRATION.md)
for the updated imports and guard names.

## Learn more

- [Get started with a complete route](https://mrdav30.github.io/Trailblazer/guides/GettingStarted.html)
- [Understand Trailblazer's ownership model](https://mrdav30.github.io/Trailblazer/guides/Overview.html)
- [Author and publish navigation maps](https://mrdav30.github.io/Trailblazer/guides/MapAuthoring.html)
- [Consume A\* and flow-field guides](https://mrdav30.github.io/Trailblazer/guides/PathGuides.html)
- [Browse the API reference](https://mrdav30.github.io/Trailblazer/api/Trailblazer.html)
- [Migration guide](https://github.com/mrdav30/Trailblazer/blob/main/docs/MIGRATION.md)

## Development

```bash
dotnet restore Trailblazer.slnx --property:Configuration=Release
dotnet build Trailblazer.slnx --configuration Release --no-restore
dotnet test Trailblazer.slnx --configuration Release --no-build
```

Repeat with `ReleaseLean` before changing package, serialization, or public API
behavior. See the
[contributor guide](https://github.com/mrdav30/Trailblazer/blob/main/CONTRIBUTING.md)
for the full workflow.

## Community and license

Open an [issue](https://github.com/mrdav30/Trailblazer/issues) for bugs and
feature requests, or join the
[LSF Discord community](https://discord.gg/mhwK2QFNBA). See
[support](https://github.com/mrdav30/Trailblazer/blob/main/.github/SUPPORT.md)
for help and
[security](https://github.com/mrdav30/Trailblazer/blob/main/SECURITY.md) for
private vulnerability reports.

Trailblazer is available under the
[MIT License](https://github.com/mrdav30/Trailblazer/blob/main/LICENSE). See the
[notice](https://github.com/mrdav30/Trailblazer/blob/main/NOTICE) for the
repository's branding and redistribution terms.
