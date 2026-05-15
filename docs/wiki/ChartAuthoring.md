# Traversal Authoring Reference

This document explains the current tokenized authoring workflow built around `TraversalAuthoringMap`, the built-in legend, and `PathManager.Register(TraversalBuildResult)`.

Use this file when you need:

- a lightweight way to author dense traversal charts and explicit transition handoffs together
- the meaning of the built-in `string[,,]` legend tokens
- the current rules for generated chart-to-volume transitions

Relevant code:

- `src/Trailblazer/Pathing/Authoring/TraversalAuthoringMap.cs`
- `src/Trailblazer/Pathing/Authoring/TraversalLegend.cs`
- `src/Trailblazer/Pathing/Authoring/TraversalLegendEntry.cs`
- `src/Trailblazer/Pathing/Authoring/TraversalBuildResult.cs`
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
        { "S!" },
        { "L!" }
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
- registers generated transitions as chart-managed handoffs that inherit the chart priority
- initializes authored solid and volume partitions by default
- keeps generated transitions registered but suppressed until their supporting authored pair is active
- ties generated transitions to that chart's lifetime, so unloading the chart unregisters them automatically

Registration is all-or-nothing. If any generated transition fails to register, the method rolls back the chart and any transitions it already added in that call.

## 2. Built-In Legend

`TraversalLegend.CreateBuiltIn()` currently maps these tokens:

| Token | Meaning |
| --- | --- |
| empty string | skip this cell |
| `.` | skip this cell |
| `X` | skip this cell |
| `S` | authored solid traversal cell |
| `SC` | authored climb-capable solid traversal cell |
| `G` | authored gas-volume traversal cell |
| `L` | authored liquid-volume traversal cell |
| `LC` | authored climb-capable solid-plus-liquid shoreline cell |
| `SG` | authored solid-plus-gas traversal cell |
| `SL` | authored solid-plus-liquid traversal cell |

Important nuance:

- `S` produces a solid cell that initializes a `SolidChartPartition`
- empty / `.` / `X` contribute no chart-backed solid traversal and no transition anchor
- `G` and `L` produce authored volume cells that initialize `VolumeChartPartition` state
- `LC` produces a solid-plus-liquid cell that also advertises climb-surface support for authored
  climb routing and liquid-exit climb intent
- `SG` and `SL` are the built-in explicit mixed-media cells; overlap is not used to synthesize those combinations
- `G` and `L` are the built-in way to author valid gas-volume and liquid-volume space; Trailblazer does not treat generic empty voxels as implicit volume by default

## 3. Transition Marker Rules

Append `!` to opt a token into generated transition authoring.

Examples:

- `S!`
- `SC!`
- `G!`
- `L!`
- `LC!`
- `SG!`
- `SL!`

Current marker behavior:

- generation is opt-in only
- only perpendicular neighbors are considered
- chart-to-volume and authored climb chart-to-chart pairs are generated
- `G!` / `L!` / `S!` / `SG!` / `SL!` still emit their authored chart cells; the marker only opts them into transition generation
- `SC!` marks a climb seam cell so generated climb routing may explicitly enter or exit authored climb topology from neighboring solid cells
- `LC!` marks a liquid shoreline cell so generated `SwimExit` handoffs can request climb intent when
  the exit lands on that shoreline
- `SG!` and `SL!` keep mixed-media meaning narrow: they participate only when there is one unambiguous solid-to-volume boundary pair

When a marked chart cell participates in generation, the builder also adds:

- `NavigationChartCellFlags.TransitionSourceHint`
- `NavigationChartCellFlags.TransitionDestinationHint`

## 4. Generated Transition Shapes

The built-in generator currently creates:

- `SwimEntry` and `SwimExit` for `S!` next to `L!`
- `SwimEntry` and `SwimExit` for `L!` next to `LC!`, with the generated `SwimExit` requesting climb intent
- `SwimEntry` and `SwimExit` for `S!` next to `SL!`, or `SL!` next to `L!`
- `Takeoff` and `Landing` for `S!` next to `G!`
- `Takeoff` and `Landing` for `S!` next to `SG!`, or `SG!` next to `G!`
- bidirectional `Climb` transitions for adjacent `SC` / `SC` and `SC` / `SC!` climb-surface pairs
- climb entry and exit `Climb` transitions for `S` next to `SC!`

For authored liquid exits, the marker semantics stay narrow:

- only generated `SwimExit` handoffs request climb intent
- the paired `SwimEntry` remains a normal water-entry transition
- `LC` still behaves like a regular solid-plus-liquid traversal cell for partition ownership and medium support

Ambiguous mixed-to-mixed boundaries such as `SG!` next to `SL!` do not auto-generate transitions.

Generated anchors use canonical voxel identity plus the corresponding traversal medium:

- `Solid`
- `Gas`
- `Liquid`

## 5. Custom Legends

You can provide a custom `TraversalLegend` to `TraversalAuthoringMap` when the built-in tokens are not enough.

Current rules for legend registration:

- tokens are trimmed before lookup
- legend tokens cannot include `!`
- `!` is reserved for transition-generation markers

## 6. Current Limits

This authoring layer intentionally stays small for now.

It does not automatically:

- configure supplemental `VolumeMediumRules`
- generate diagonal handoffs
- infer climb routing from every solid cell
- refresh generated transitions mid-simulation

For the chart lifecycle, read `PathManager.md`.
For explicit handoff data, read `Transitions.md`.
