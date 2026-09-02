# Trailblazer

![Trailblazer Icon](https://raw.githubusercontent.com/mrdav30/trailblazer/main/icon.png)

[![Build](https://github.com/mrdav30/Trailblazer/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/mrdav30/Trailblazer/actions/workflows/build-and-test.yml)
[![Branch Coverage](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fmrdav30.github.io%2FTrailblazer%2FSummary.json&query=%24.summary.branchcoverage&suffix=%25&label=branch%20coverage&color=brightgreen)](https://mrdav30.github.io/Trailblazer/coverage/)
[![NuGet](https://img.shields.io/nuget/v/Trailblazer.svg)](https://www.nuget.org/packages/Trailblazer)
[![NuGet Lean](https://img.shields.io/nuget/v/Trailblazer.Lean.svg?label=nuget%20lean)](https://www.nuget.org/packages/Trailblazer.Lean)
[![License](https://img.shields.io/github/license/mrdav30/Trailblazer.svg)](https://github.com/mrdav30/Trailblazer/blob/main/LICENSE)
[![API](https://img.shields.io/badge/docs-API-f4511e)](https://mrdav30.github.io/Trailblazer/)
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

Trailblazer targets `netstandard2.1` and `net8.0`. The standard package includes
JSON and MemoryPack serialization support.

## Request a route

After publishing the referenced GridForge-backed map and area policy, describe
the entire request with one immutable `PathQuery`:

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

## Choose a package

| Package                                                               | Use it when                                                                                           |
| --------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| [`Trailblazer`](https://www.nuget.org/packages/Trailblazer)           | Your LSF dependency graph uses the standard packages and you want JSON plus MemoryPack serialization. |
| [`Trailblazer.Lean`](https://www.nuget.org/packages/Trailblazer.Lean) | Your complete LSF dependency graph uses Lean packages and you do not need the MemoryPack transport.   |

Do not mix standard and Lean LSF packages in one dependency graph.

## Built on the LSF stack

- [FixedMathSharp](https://github.com/mrdav30/FixedMathSharp) supplies
  deterministic fixed-point math.
- [SwiftCollections](https://github.com/mrdav30/SwiftCollections) supplies
  low-allocation collections and storage.
- [GridForge](https://github.com/mrdav30/GridForge) supplies world topology,
  geometry, and physical cell identity.
- [Chronicler](https://github.com/mrdav30/Chronicler) supplies explicit state
  transfer and serialization.

## Learn more

- [Get started with a complete route](https://github.com/mrdav30/Trailblazer/wiki/GettingStarted)
- [Understand Trailblazer's ownership model](https://github.com/mrdav30/Trailblazer/wiki/Overview)
- [Author and publish navigation maps](https://github.com/mrdav30/Trailblazer/wiki/MapAuthoring)
- [Consume A\* and flow-field guides](https://github.com/mrdav30/Trailblazer/wiki/PathGuides)
- [Browse the API reference](https://mrdav30.github.io/Trailblazer/api/Trailblazer.html)
- [Migrate from v1 to v2](https://github.com/mrdav30/Trailblazer/blob/main/docs/MIGRATION.md)

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
[LSF Discord community](https://discord.gg/mhwK2QFNBA).

Trailblazer is available under the
[MIT License](https://github.com/mrdav30/Trailblazer/blob/main/LICENSE). See the
[notice](https://github.com/mrdav30/Trailblazer/blob/main/NOTICE) for the
repository's branding and redistribution terms.
