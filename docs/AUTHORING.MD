# Traversal Authoring Reference

This document explains the current tokenized authoring workflow built around `TraversalAuthoringMap`, the built-in legend, and `PathManager.Register(TraversalBuildResult)`.

Use this file when you need:

- a lightweight way to author dense traversal charts and explicit transition handoffs together
- the meaning of the built-in `string[,,]` legend tokens
- the current rules for generated chart-to-volume transitions

Relevant code:

- `src/Trailblazer/Pathing/Support/Authoring/TraversalAuthoringMap.cs`
- `src/Trailblazer/Pathing/Support/Authoring/TraversalLegend.cs`
- `src/Trailblazer/Pathing/Support/Authoring/TraversalLegendEntry.cs`
- `src/Trailblazer/Pathing/Support/Authoring/TraversalBuildResult.cs`
- `src/Trailblazer/Pathing/PathManager.cs`

## 1. Core Workflow

The current workflow is:

1. author a `string[,,]` map
2. build it into a `TraversalBuildResult`
3. register that result through `PathManager`

```csharp
string[,,] map =
{
    {
        { "L!" },
        { "W!" }
    }
};

TraversalBuildResult buildResult = new TraversalAuthoringMap(
    chartName: "Shoreline",
    sourceMap: map,
    minBounds: Vector3d.Zero,
    interval: Fixed64.One).Build();

PathManager.Register(buildResult);
```

`TraversalBuildResult` contains:

- the built `NavigationChart`
- the generated explicit `TraversalTransition[]`

`PathManager.Register(buildResult)`:

- registers the chart
- registers the generated transitions
- initializes authored surface and volume partitions by default

Registration is all-or-nothing. If any generated transition fails to register, the method rolls back the chart and any transitions it already added in that call.

## 2. Built-In Legend

`TraversalLegend.CreateBuiltIn()` currently maps these tokens:

| Token | Meaning |
| --- | --- |
| empty string | skip this cell |
| `.` | skip this cell |
| `X` | skip this cell |
| `L` | authored surface traversal cell |
| `O` | authored open-volume traversal cell |
| `W` | authored water-volume traversal cell |

Important nuance:

- `L` produces a surface cell that initializes a `PathPartition`
- empty / `.` / `X` contribute no chart surface and no transition anchor
- `O` and `W` produce authored volume cells that initialize `VolumePartition` state
- `O` and `W` are the built-in way to author valid open-volume and water-volume space; Trailblazer does not treat generic empty voxels as implicit volume by default

## 3. Transition Marker Rules

Append `!` to opt a token into generated transition authoring.

Examples:

- `L!`
- `O!`
- `W!`

Current marker behavior:

- generation is opt-in only
- only perpendicular neighbors are considered
- only chart-to-volume pairs are generated
- chart-to-chart auto-generation is not part of the current builder
- `O!` and `W!` still emit the same authored volume cells as `O` and `W`; the marker only opts them into transition generation

When a marked chart cell participates in generation, the builder also adds:

- `NavigationChartCellFlags.TransitionSourceHint`
- `NavigationChartCellFlags.TransitionDestinationHint`

## 4. Generated Transition Shapes

The built-in generator currently creates:

- `SwimEntry` and `SwimExit` for `L!` next to `W!`
- `Takeoff` and `Landing` for `L!` next to `O!`

Generated anchors use canonical voxel identity plus the corresponding traversal space:

- `Chart`
- `OpenVolume`
- `WaterVolume`

## 5. Custom Legends

You can provide a custom `TraversalLegend` to `TraversalAuthoringMap` when the built-in tokens are not enough.

Current rules for legend registration:

- tokens are trimmed before lookup
- legend tokens cannot include `!`
- `!` is reserved for transition-generation markers

## 6. Current Limits

This authoring layer intentionally stays small for now.

It does not automatically:

- configure supplemental `VolumeTraversalRules`
- generate diagonal handoffs
- generate chart-to-chart handoffs
- refresh generated transitions mid-simulation

For the chart lifecycle, read `PATHMANAGER.MD`.
For explicit handoff data, read `TRANSITIONS.MD`.
