# Navigation Charts

This document is a deep dive into `NavigationChart`, the chart lifecycle managed by `PathManager`, and the role charts play in Trailblazer's world model.

`NavigationChart` is a dense authored traversal map. Solid traversal is its main chart-backed runtime use, but charts can also author raw-volume cells that initialize `VolumeChartPartition` state for `VolumePathRequest`.

If you only need the high-level architecture, start with `OVERVIEW.md`.
If you are working from the standalone pathing API inward, read `PATHING.MD` before this file.
If your chart world depends on jumps, water, or chart-to-volume handoffs, also read `TRANSITIONS.MD` and `VOLUMETRAVERSAL.MD`.
If you need the registry and lifecycle details, read `PATHMANAGER.MD` alongside this file.

Relevant code:

- `src/Trailblazer/Pathing/Chart/NavigationChart.cs`
- `src/Trailblazer/Pathing/PathManager.cs`
- `src/Trailblazer/Pathing/Partition/SolidChartPartition.cs`
- `src/Trailblazer/Pathing/Partition/VolumeChartPartition.cs`

## 1. Why Charts Matter

`NavigationChart` is the main way Trailblazer represents intentional authored traversal data.

That matters because Trailblazer is supposed to stay engine-agnostic. It should not know:

- what a "floor" looks like in Unity, Godot, MonoGame, Unreal, or a custom engine
- how your game stores level geometry
- whether your world came from a tilemap, a raycast bake, imported scene data, or procedural generation

Instead, Trailblazer asks the host to answer a simpler question:

`Which world-aligned voxel positions should contribute authored traversal data for this chart?`

That answer becomes a `NavigationChart`.

In practice, a chart is Trailblazer's agnostic representation of a game's navigable world-view. It is the bridge between host-specific scene data and Trailblazer's deterministic pathing/runtime systems.

## 2. What A Chart Actually Is

At the data level, a `NavigationChart` is:

- a named dense authored traversal map
- a world-space bounds definition
- a fixed interval between cells
- a flattened authored cell payload that can describe solid traversal, volume traversal, both, or neither

Structured cell payloads can optionally carry:

- solid traversal state
- volume traversal state
- surface and volume path cost modifiers
- transition hint flags

The chart itself is data-only. It does not pathfind, steer, or mutate live voxel connectivity on its own.

Important fields:

- `Name`: stable chart identity used by `PathManager`
- `MinBounds`: world-space origin of the chart volume
- `MaxBounds`: exclusive upper world bound
- `Interval`: spacing between chart cells
- `SizeX`, `SizeY`, `SizeZ`: chart dimensions
- `IsInitialized`: whether the chart has been pushed into live partition state

The core factory today is:

```csharp
NavigationChart chart = NavigationChart.From3D(
    "ArenaGround",
    sourceMap,
    minBounds: new Vector3d(0, 0, 0),
    interval: Fixed64.One);
```

The `bool[,,]` overload emits authored solid cells by default, or one authored volume medium when you pass `TraversalMedium.Gas` or `TraversalMedium.Liquid`.

When you need mixed traversal media or authored per-cell metadata instead of one uniform medium, use the `NavigationChartCell[,,]` overload of `NavigationChart.From3D(...)`. Explicit `Solid | Gas` and `Solid | Liquid` cells are the supported way to author one voxel with both affordances intentionally.

## 3. What Charts Are For

Charts are best for stable or semi-stable authored traversal structure.

Good fits:

- ground layers
- roads
- catwalks
- ramps
- bridges
- rooftops
- cave floors
- authored platform routes

Less ideal fits:

- short-lived dynamic occupancy
- temporary blockers like units, crates, or doors that change every frame
- highly procedural media that the host would rather classify entirely outside authored chart data

Those are better handled by other systems:

- raw-volume traversal for chart-optional 3D movement
- `GridForge.Voxel` blocked state / occupant state
- supplemental host-defined volume rules such as water partitions
- explicit handoff registration through `TraversalTransitionRegistry` when chart-backed traversal needs a later jump, swim, or flight transition

In other words, charts define navigable structure, not every transient rule in the world.

## 4. Why Trailblazer Uses Charts

Charts solve several important problems at once.

### 4.1 They Decouple Authoring From Runtime

The host can generate chart data however it wants, then hand Trailblazer a normalized representation.

Trailblazer does not care whether the data came from:

- a hand-authored array
- an editor bake
- collision sampling
- imported map metadata
- procedural terrain analysis

### 4.2 They Turn Raw Voxels Into Intentional Solid Traversal Space

Not every voxel in a grid should necessarily become part of surface pathfinding.

A chart lets the host say:

- these voxels are part of the walkable network
- these voxels are not

That keeps surface pathing intentional instead of assuming every open voxel should behave like a floor or traversable volume.

### 4.3 They Support Multiple Overlapping World Views

Trailblazer can register multiple charts, but overlap resolves to one winning authored cell per voxel instead of additively merging chart data.

That enables patterns like:

- one chart for ground units
- one chart for rooftop patrol routes
- one chart for an interior level slice
- one chart for a specialized traversal layer

The precedence rules are:

- higher `NavigationChart.Priority` wins
- same-priority ties fall back to later chart registration order

This ownership model is one of the reasons `PathManager.InitializeChart(...)` and `UnloadChart(...)` are more involved than a simple add/remove operation.

## 5. Chart Data Versus Live Pathing State

This distinction is extremely important.

`NavigationChart` is not the live runtime graph.

The runtime flow is:

1. Build or load chart data.
2. Register the chart with `PathManager`.
3. If you registered with `initializeChart: false`, initialize the chart.
4. `PathManager` attaches or reuses `SolidChartPartition` or `VolumeChartPartition` instances on the underlying `GridForge` voxels.
5. Surveyors and steering then query those live partitions.

Until a chart is initialized, it is just inert data.

That means:

- chart creation is cheap relative to full runtime activation
- a chart is not pathable until initialization has run
- `PathManager.Register(chart)` performs that initialization by default, while `initializeChart: false` keeps the chart inert until a later explicit activation step

Registered charts are not frozen, though.

`PathManager.TryUpdateChartCell(...)` and `PathManager.ApplyChartUpdates(...)` let the host mutate authored chart cells after registration.

For initialized charts, those APIs reuse the same resolved effective-cell pipeline as initialization and unload:

- only touched voxels are re-resolved
- solid neighbors are rebound only when solid topology changes
- cache invalidation stays chart-targeted and only fires when the live winning state changes

For inert charts registered with `initializeChart: false`, the mutation simply updates the authored data so a later initialization uses the new cell payloads.

Generated-transition lifecycle is now chart-driven rather than registration-path-driven:

- any registered chart carrying generated-transition media refreshes only the affected adjacent pairs when edited
- inert charts registered with `initializeChart: false` keep those managed generated transitions suppressed until initialization
- overlap masking can suppress managed generated transitions temporarily without unregistering them
- unloading the chart removes its managed generated transitions entirely

## 6. How Charts Relate To GridForge

Trailblazer pathing runs on top of `GridForge` voxels.

Charts do not replace the voxel grid. They annotate it.

The relationship is:

- `GridForge` provides the world-aligned voxel lattice
- `NavigationChart` says which positions should belong to a pathing surface or volume and what their traversal properties are
- `PathManager` turns those positions into `SolidChartPartition` or `VolumeChartPartition` ownership on real voxels

This design keeps Trailblazer agnostic while still letting the host keep full control over world sampling.

## 7. How Charts Relate To SolidChartPartition

`SolidChartPartition` is the live runtime object attached to a voxel when one or more charts claim it.

You can think of it this way:

- `NavigationChart` = authored or generated intent
- `SolidChartPartition` = live runtime pathing membership

One voxel can still record multiple chart owners for invalidation and unload bookkeeping, but the live solid behavior comes from the winning effective cell only. Overlap does not implicitly create mixed media.

## 8. How Charts Relate To Volume Pathing

Volume traversal is chart-aware, but it is not chart-routed the same way solid pathing is.

When the winning `NavigationChartCell` carries volume traversal data, `PathManager.InitializeChart(...)` attaches or reuses a `VolumeChartPartition` on the underlying voxel.

That live partition holds the authored runtime data volume traversal actually uses:

- which charts currently own that voxel's authored volume state
- which `TraversalMedium` values the voxel supports
- authored path cost modifiers for raw-volume travel

`VolumePathRequest` does not ask charts for a surface route.

Instead, raw-volume traversal:

1. resolves endpoints through `VolumeVoxelFinder`
2. validates raw voxel traversability and medium compatibility
3. searches through raw voxel connectivity with `VolumeSurveyor`

That means charts can author valid gas or liquid space without turning volume traversal into chart graph pathfinding.

`VolumeMediumRules` can also extend authored membership with host-defined gas or liquid logic, so a usable volume corridor can come from:

- authored `VolumeChartPartition` state
- host-defined `VolumeMediumRules`
- both at the same time

In practice, charts still matter to volume traversal. They just play a different role than they do for solid pathing: charts seed runtime volume membership and authored costs, while the raw-volume stack handles endpoint resolution and traversal through the space.

Use charts when:

- movement is fundamentally surface-based
- the host can define a meaningful walkable layer
- you want the normal chart-backed pathing flow

Use `VolumePathRequest` when:

- movement happens through open 3D volume
- there may be no navigation chart for that space
- traversal is based on raw voxel connectivity instead of chart ownership

Use transition-aware chart routing when:

- movement should stay chart-backed most of the time but must cross an authored jump, swim, or takeoff/landing handoff
- explicit `TraversalTransition` anchors should stitch chart segments to each other or to raw volume travel
- an `AStarPathRequest` or `FlowFieldPathRequest` should fall back through transitions instead of failing outright

Examples:

- walking on terrain: chart-backed pathing
- flying through authored or configured gas volume: volume pathing
- swimming through authored or host-marked liquid voxels: constrained volume pathing
- walking to a shoreline, swimming across, then exiting onto another chart: hybrid pathing

This split is a big part of what keeps Trailblazer flexible without forcing one world model onto every traversal medium.

## 9. Designing Good Charts

Good chart authoring usually follows a few rules.

### 9.1 Keep The Meaning Stable

A chart should mean something consistent, such as:

- walkable ground
- valid rooftop patrol surface
- interior navigation layer

Avoid charts whose meaning changes frame to frame.

### 9.2 Keep The Interval Aligned With Your Grid

Charts are most predictable when their `Interval` matches the world sampling assumptions used by your `GridForge` setup.

Misalignment between chart cell spacing and voxel spacing can cause confusing endpoint resolution and awkward gaps.

### 9.3 Use Charts For Structure, Not Occupancy

Do not try to encode every dynamic obstacle directly into the chart asset itself.

Generally:

- chart = static or semi-static traversable structure
- voxel blocked state / occupants = dynamic runtime state

### 9.4 Think In Traversal Intent

A chart is not just "where geometry exists." It is "where this class of agents is intended to navigate."

That distinction matters for ledges, stairs, roofs, decorative meshes, and stacked spaces.

## 10. Common Chart Creation Patterns

Trailblazer does not require one authoring workflow. A few common patterns fit well.

### 10.1 Author From A 3D Array

This is the simplest approach for tests, tile games, and data-driven maps.

```csharp
bool[,,] walkable = new bool[1, 6, 1]
{
    {
        { true },
        { true },
        { true },
        { true },
        { true },
        { true }
    }
};

NavigationChart chart = NavigationChart.From3D(
    "Lane",
    walkable,
    minBounds: Vector3d.Zero,
    interval: Fixed64.One);

PathManager.Register(chart);
```

### 10.2 Bake From Scene Geometry

In a 3D engine, the host can sample world positions and decide whether each sample belongs to the walkable surface layer.

For example:

- step across a region at fixed intervals
- raycast downward
- accept hits on a configured "ground" layer
- reject steep or invalid surfaces
- write accepted samples into the chart map

That is still engine-specific authoring, but the exported result is just a `NavigationChart`.

### 10.3 Build From Existing Level Metadata

Many games already know where agents can walk because the level format contains:

- tile flags
- floor cells
- room graphs
- road masks
- terrain classifications

In those cases, chart generation can often be done without physics queries at all.

## 11. Example: Unity-Style Editor Bake

One reasonable Unity workflow would look like this:

1. Define a bake region and interval.
2. For each sample column, cast a ray downward.
3. If the ray hits a valid navigation layer, mark the hit cell as walkable.
4. Convert the collected cells into a `bool[,,]`.
5. Build a `NavigationChart`.
6. Serialize or regenerate that chart at runtime, then register it and initialize later only if you opted out of default registration-time activation.

Very rough pseudo-code:

```csharp
for (int y = 0; y < sizeY; y++)
for (int x = 0; x < sizeX; x++)
for (int z = 0; z < sizeZ; z++)
{
    Vector3 sample = minBounds + new Vector3(x, y, z) * interval;

    if (Physics.Raycast(sample + Vector3.up * castHeight, Vector3.down, out RaycastHit hit, maxDistance, groundMask))
    {
        map[y, x, z] = true;
    }
}

NavigationChart chart = NavigationChart.From3D("Ground", map, minBounds, interval);
```

The exact bake logic is host-specific, but that is the point: Trailblazer should consume the result, not dictate how the bake happens.

## 12. Practical Lifecycle Example

Typical runtime flow:

```csharp
NavigationChart chart = NavigationChart.From3D(
    "ArenaGround",
    map,
    minBounds,
    interval);

PathManager.Register(chart);

// Later, path requests and navigators can use the live chart-backed pathing graph.
```

For teardown or rebuild:

```csharp
PathManager.UnloadChart(chart.Name);
```

Or, when resetting the whole simulation:

```csharp
PathManager.Reset();
```

## 13. Mental Model

If you only remember one thing, make it this:

`NavigationChart` is Trailblazer's engine-agnostic description of intentional navigable space.

It is not the whole world, not the live graph, and not a replacement for voxel runtime state.

It is the authored or generated statement:

`These world-aligned cells belong to a navigation surface.`

That simple contract is what lets Trailblazer stay deterministic, flexible, and agnostic across very different host environments.
