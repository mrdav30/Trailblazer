# Trailblazer

![Trailblazer Icon](https://raw.githubusercontent.com/mrdav30/trailblazer/main/icon.png)

[![Build](https://github.com/mrdav30/Trailblazer/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/mrdav30/Trailblazer/actions/workflows/build-and-test.yml)
[![NuGet](https://img.shields.io/nuget/v/Trailblazer.svg)](https://www.nuget.org/packages/Trailblazer)
[![NuGet Lean](https://img.shields.io/nuget/v/Trailblazer.Lean.svg?label=nuget%20lean)](https://www.nuget.org/packages/Trailblazer.Lean)
[![License](https://img.shields.io/github/license/mrdav30/Trailblazer.svg)](https://github.com/mrdav30/Trailblazer/blob/main/LICENSE)

Trailblazer is deterministic, engine-agnostic navigation for lockstep games and
simulations. It combines immutable GridForge-backed navigation maps, fixed-point
geometry, bounded A* and flow-field search, and actionable traversal guides.

The same query model handles:

- grounded Solid travel;
- free-form Gas and Liquid travel;
- rectangular and pointy/flat hex grids;
- runtime map and overlay publication;
- explicit actions such as ladders, jumps, elevators, and teleporters;
- bounded procedural actions such as a duck taking off anywhere along a water
  surface.

Trailblazer uses
[FixedMathSharp](https://github.com/mrdav30/FixedMathSharp) for deterministic
math, [GridForge](https://github.com/mrdav30/GridForge) for world topology and
geometry, [SwiftCollections](https://github.com/mrdav30/SwiftCollections) for
low-allocation collections, and
[Chronicler](https://github.com/mrdav30/Chronicler) for explicit serialization.

## Why Trailblazer?

- **Deterministic:** fixed-point costs, stable ordering, explicit budgets, and
  immutable published snapshots.
- **Medium aware:** one search can move through Solid, Gas, and Liquid states.
- **Topology aware:** rectangular and hex-prism grids share GridForge's geometry
  authority rather than local neighbor formulas.
- **Dynamic:** addressed overlays can mine a cell, flood a map, or add/remove a
  ladder without rebuilding unrelated maps.
- **Actionable:** guides stop at semantic actions and advance only after the host
  explicitly completes the exact instruction.
- **Framework agnostic:** no Unity, Godot, Unreal, or other engine dependency.

## Install

Trailblazer targets `netstandard2.1` and `net8.0`.

```bash
dotnet add package Trailblazer
```

Use `Trailblazer.Lean` when the rest of your LSF stack uses its Lean package
variants. The standard package includes the MemoryPack transport; Lean omits
that transport and follows the Lean dependency chain. Keep the package family
consistent within one application.

```bash
dotnet add package Trailblazer.Lean
```

## Mental Model

```text
GridForge world
    + immutable NavigationMap bakes
    + addressed overlay transactions
    + NavigationAreaPolicy revisions
             |
             v
immutable medium-state graph snapshot
             |
       one PathQuery
       /           \
 A* step lease   Flow sample lease
       \           /
 ordinary movement or held transition instruction
```

A physical cell can support multiple media. Search state is the cell plus one
exact `TraversalMedium`; movement keeps that medium, while an authored semantic
transition may keep or change it.

## Quick Start

The world, maps, and area policy must already be published. Construct one exact
query for the agent's current foot position and current medium:

```csharp
var query = new PathQuery(
    new NavigationEndpoint(startFoot, "overworld"),
    new NavigationEndpoint(destinationFoot, "overworld"),
    agentProfile,
    areaPolicy.Key,
    new TraversalIntent(
        TraversalMedium.Solid,
        TraversalMedia.Solid | TraversalMedia.Liquid),
    PathAlgorithm.AStar,
    workBudget,
    allowTransitions: true);

NavigationGuideStatus status = context.Guides.RequestGuide(
    query,
    out NavigationGuideLease? acquired);

if (status == NavigationGuideStatus.Success)
{
    using NavigationGuideLease guide = acquired!.Value;
    while (guide.TryGetCurrentStep(out NavigationGuideStep step)
        == NavigationGuideStatus.Success)
    {
        if (step.HasTransition)
        {
            ExecuteAction(step.Transition);

            NavigationGuideStatus completion;
            do
            {
                completion = guide.CompletePendingTransition(step.Transition);
                if (completion == NavigationGuideStatus.CapacityExceeded)
                    WaitUntilNextFixedStep();
            }
            while (completion == NavigationGuideStatus.CapacityExceeded);

            if (completion == NavigationGuideStatus.Stale)
            {
                RequestFreshGuide();
                break;
            }

            if (completion != NavigationGuideStatus.Success)
            {
                HandleGuideFailure(completion);
                break;
            }
        }
        else
        {
            MoveToward(step.Position, step.Medium);
            if (guide.CurrentStepIndex == guide.StepCount - 1)
                break;

            NavigationGuideStatus advance = guide.TryAdvanceStep();
            if (advance != NavigationGuideStatus.Success)
            {
                HandleGuideFailure(advance);
                break;
            }
        }
    }
}
```

Use `PathAlgorithm.FlowField` and `RequestFlowField(...)` for many agents
sharing a destination. Flow sampling returns `NavigationFlowSample`; it uses
the same transition completion contract.

## Map Authoring

`NavigationMapBuilder` binds one stable map ID to one normalized GridForge
configuration. A map may have:

- one optional complete default `NavigationCell`;
- explicit cell entries overriding that default;
- explicit physical connections;
- explicit semantic transition definitions;
- bounded procedural transition rules.

The effective cell precedence is overlay, explicit bake, map default, then no
cell. Each winning cell is complete; media, capability, area, cost, clearance,
and flags never merge field by field.

Runtime changes are admitted as deterministic `NavigationMapCommitOperation`,
`NavigationOverlayCommitOperation`, or
`NavigationAreaPolicyCommitOperation` values. Their receipts become terminal
after fixed-step publication.

## Actions And Completion

`TraversalTransitionDefinition` authors one exact source-owned action.
`TraversalTransitionRule` authors one bounded reusable action over either the
same cell or a positive-face contact. Both carry explicit source/destination
media, required capabilities, `ActionCost`, type, and locomotion hints.

An A* step or Flow sample with `HasTransition == true` is a barrier:

1. move to the reported source action position;
2. let the host perform the action;
3. call `CompletePendingTransition(...)` with that exact instruction;
4. update the host's physical medium/state and continue the same lease.

Moving or removing the authored object stales held instructions through normal
graph publication. Completion never silently performs gameplay or physics.

## Main References

- [Overview](docs/wiki/Overview.md)
- [Map authoring](docs/wiki/ChartAuthoring.md)
- [Map publication and overlays](docs/wiki/PathManager.md)
- [Queries and algorithms](docs/wiki/Pathing.md)
- [Gas and Liquid travel](docs/wiki/VolumeTraversal.md)
- [Transitions](docs/wiki/Transitions.md)
- [Guides](docs/wiki/PathGuides.md)
- [Navigator](docs/wiki/Navigator.md)
- [Serialization](docs/wiki/Serialization.md)

## Build And Test

```bash
dotnet restore Trailblazer.slnx
dotnet build Trailblazer.slnx --configuration Release
dotnet test Trailblazer.slnx --configuration Release
```

## Compatibility

Trailblazer is under active development. Breaking changes are accepted when
they materially improve determinism, correctness, or the long-term public API.

## License

MIT. See [LICENSE](LICENSE).
