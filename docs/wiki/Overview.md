# Trailblazer Overview

Trailblazer is a deterministic navigation library for lockstep simulations and
games. It is split into two layers that can be used together or independently:

- A pathing layer for graph-backed surface A* and Flow queries, chart-backed
  volume routes, and reusable guide data.
- A navigation layer for steering, turning, locomotion, and deterministic
  frame-by-frame movement.

This document is the high-level architecture guide for the current codebase.

See also:

- [ChartAuthoring](ChartAuthoring.md) for tokenized chart plus transition
  authoring
- [Pathing](Pathing.md) for a standalone guide to the `Trailblazer.Pathing`
  namespace
- [PathGuides](PathGuides.md) for the runtime guide and guide-factory layer
- [Transitions](Transitions.md) for authored handoffs between charts and raw
  volume
- [VolumeTraversal](VolumeTraversal.md) for raw-volume traversal media and host
  rules
- [HeightMaps](HeightMaps.md) for deterministic context-owned ground/contact Y
  sampling
- [NavigationCharts](NavigationCharts.md) for a deeper explanation of
  `NavigationChart` and the chart lifecycle
- [Serialization](Serialization.md) for Trailblazer's current serialization
  coverage and runtime behavior

## 1. Core Model

Trailblazer assumes:

- world-space math uses `FixedMathSharp` types such as `Fixed64`, `Vector3d`,
  and `FixedQuaternion`
- traversable space is represented by `GridForge` voxels
- surface A* and Flow are driven by immutable navigation maps and composed graph
  snapshots
- remaining volume and handoff paths still use chart and partition state until
  their owning cutover phases
- simulation advances in deterministic fixed steps through
  `TrailblazerWorldContext`
- runtime diagnostics flow through `TrailblazerLogger.Channel`, with verbose
  debug logging gated separately through `TrailblazerLogger.DebugChannel`

At a high level, the runtime loop is:

1. Publish navigation maps and a matching navigation-area policy.
2. Build a complete immutable `PathQuery` for surface A* or Flow, or use a
   remaining volume request where applicable.
3. Resolve surface intent through `TrailblazerWorldContext.Guides` into a
   disposable `NavigationGuideLease` or `NavigationFlowFieldLease`, or let
   `NavSteering` own that lifecycle.
4. Run `Navigator.Simulate()` on the fixed step.
5. Update traversal medium and surface state from your own collision/environment
   code.
6. Commit the frame with `Navigator.CommitFrameMotion()`.

## 2. World Representation

### 2.1 Navigation Maps And Remaining Charts

`NavigationMap` is the authored input to graph-backed surface A* and Flow. One
map binds to one exact grid generation, and the context publishes composed
immutable graph snapshots for query admission and dependency validation.

`NavigationChart` is the remaining chart-backed volume/handoff description. It
stores authored chart-cell data and exposes:

- `NavigationChart.From3D(...)` to build a chart from `bool[,,]` voxel data for
  one authored medium at a time, or from `NavigationChartCell[,,]` voxel data
  for mixed authored payloads
- `TraversalAuthoringMap.Build()` to build a `TraversalBuildResult` from
  tokenized `string[,,]` authoring input
- `TryGetCell(...)` to inspect the authored cell payload at a world position
- `TryWorldToIndex(...)` to map world positions into chart coordinates
- `IsWalkable(...)` to query a world-space position
- `GetWalkablePositions()` to enumerate every walkable voxel origin in world
  space

Important details:

- graph-backed surface A* and Flow do not read `NavigationChart`, `PathManager`,
  or chart partitions
- charts remain live for the retained volume/handoff branches described below

- the chart itself is data only; it does not become queryable by pathfinding
  until it is registered and initialized
- the chart uses flattened internal storage, but the public constructor/factory
  still works in 3D terms
- `NavigationChart.Interval` must match the owning context's `VoxelSize` at
  registration time so authored cells map one-to-one onto live GridForge voxels
- charts can author both solid and raw-volume traversal data; runtime gas and
  liquid routing still flow through `VolumePathRequest`
- world bounds are derived from `MinBounds`, `Interval`, and the source array
  size

### 2.2 TrailblazerWorldContext

`TrailblazerWorldContext` is the explicit owner for one `GridWorld` and its
deterministic simulation clock. It provides context construction,
attach/owned-world lifetime, independent frame rate and frame count,
context-local lifecycle hooks, pathing state, transitions, volume rules, graph
guide payloads, remaining guide caches, navigator ids, and movement-group state.

Graph-backed surface A* and Flow use each grid's topology and world-space
geometry; they do not derive costs from context-wide `VoxelSize`. Remaining
chart-backed volume/handoff branches retain their existing metric constraints
until cut over.

### 2.3 Pathing Service

`TrailblazerWorldContext.Pathing` is the context-local chart registry and live
partition coordinator. It turns registered `NavigationChart` data into
initialized voxel partitions, can apply a `TraversalBuildResult` in one step,
manages chart ownership and unload behavior, exposes effective-cell query
helpers, owns chart partition pools, and handles grid rebuild events for its
`GridWorld`.

Explicit handoff data between chart-backed traversal and raw-volume traversal is
registered through `TrailblazerWorldContext.Transitions`.

See also:

- [`ChartAuthoring`](ChartAuthoring.md)
- [`NavigationCharts`](NavigationCharts.md)
- [`PathManager`](PathManager.md)

### 2.4 Heightmaps

`TrailblazerWorldContext.Heightmaps` is the context-local registry for
deterministic ground/contact Y sampling from prebuilt heightmap data. Heightmaps
are separate from `NavigationChart` topology: they do not author walkability,
neighbors, guide caches, or traversal transitions. They answer the runtime
question "what environment Y exists at this X/Z contact query?"

Runtime heightmap storage is compressed through `SwiftShortArray2D` plus
`HeightmapCompression`. Use `HeightmapSurface.FromCompressed(...)` for baked or
serialized compact samples, and `HeightmapSurface.FromHeights(...)` only when
tests, generated maps, or tooling already have `Fixed64[,]` heights and want
Trailblazer to quantize them once.

Multi-level worlds register separate layers with vertical selection bands. For
example, a floor and an overhead platform can share X/Z coverage while selecting
by contact Y. Navigators can opt into heightmap grounding through
`ConfigureHeightmapGrounding(...)`, but concrete navigators must still call the
protected grounding helper from their own grounded traversal probing.

See also:

- [`HeightMaps.md`](HeightMaps.md)

## 3. Path Requests

Surface A* and Flow use `PathQuery`, an immutable value containing exact start/end
`NavigationEndpoint` values, `NavigationAgentProfile`, area-policy key,
`TraversalIntent`, algorithm, finite `NavigationWorkBudget`, and transition
intent. Endpoint positions are foot positions. Admission resolves these values
against one immutable graph snapshot; callers do not select an A* heuristic.

The legacy `IPathRequest` hierarchy remains only for retained volume consumers.
Its cache keys and mutable endpoint carriers are not part of the graph surface
API.

### 3.1 Surface `PathQuery`

Use `PathAlgorithm.AStar` with surface traversal and
`AllowTransitions == false`. Submit the query through:

```csharp
NavigationGuideStatus status = context.Guides.RequestGuide(
    query,
    out NavigationGuideLease? lease);
```

A lease exists only for `Success`. Dispose it when finished. Its status and
waypoint operations validate graph dependencies and can report `Stale`; finite
admission/search limits report `BudgetExceeded` rather than running unbounded.

### 3.2 Graph Flow `PathQuery`

Use `PathAlgorithm.FlowField` when many agents can share a graph-backed,
destination-centric field. Supply `FlowFieldQueryOptions` on the same immutable
`PathQuery` used by A*.

The Flow-specific option is:

- `ExtraIntegrationCost`, a non-negative exact cost included in payload identity

Acquire and sample the field through the context guide service:

```csharp
NavigationGuideStatus status = context.Guides.RequestFlowField(
    query,
    out NavigationFlowFieldLease? lease);
```

Direct acquisition requires surface-to-surface traversal and
`AllowTransitions == false`. Dispose successful leases after sampling with
`TrySample(actualFootPosition, budget, out heading)`.

Important cache behavior:

- the field family is based on exact destination and query identity, not the
  specific start node
- a single field can be reused by multiple agents as long as their current voxel
  exists in the generated field set
- when a compatible cached field does not cover a farther start voxel,
  Trailblazer publishes the deterministic longer prefix while active leases may
  still reference the smaller payload

### 3.3 VolumePathRequest

Use `VolumePathRequest` when a navigator should travel through 3D voxel
connectivity governed by authored or explicitly configured volume membership
instead of a chart-backed surface route.

Key traits:

- it resolves through raw `GridForge.Voxel` connectivity while still honoring
  authored `VolumeChartPartition` and `SolidChartPartition` ownership
- it can stay in direct 3D travel when the corridor is clear
- it can fall back to a cached volume waypoint guide when blockers force a
  detour
- it supports both authored gas-volume travel and constrained volumes such as
  liquid
- constrained volume membership can come from authored chart cells,
  `VolumeMediumRules`, or both
- gas requests fail until authored gas volume or a host gas rule is configured

Related support type:

- `TraversalTransitionRegistry` stores authored handoff points between
  chart-backed traversal and raw-volume traversal for hybrid routing

Factory helpers:

```csharp
VolumePathRequest.TryCreate(context, origin, destination, Fixed64.One, out var request);
```

### 3.4 Transition Fallback For Chart Requests

Chart-backed requests are being hardened to use explicit transitions without
forcing callers onto a separate public request family.

Current behavior:

- Direct graph A* and Flow guide-service calls reject
  `AllowTransitions == true` as `Unsupported`.
- Navigator-owned Flow queries may opt into the retained hybrid/volume handoff
  planner above the direct graph guide-service boundary.
- For navigator-owned volume-first travel, that same opt-in also enables bounded
  swim-exit handoffs from liquid volume into a follow-up chart request and
  bounded aerial landing handoffs into chart-backed follow-up travel.
- The staged route is resolved internally from the request plus the live
  `TraversalTransitionRegistry`.
- Search implementations stay single-mode; staged escalation happens above
  them.

No transition fallback routes a surface `PathQuery` through the deleted legacy
A* provider.

## 4. Search Results and Guides

Trailblazer separates search payloads from runtime movement consumption.

### 4.1 Search Payloads

Graph surface searches publish immutable dependency-stamped payloads:

- A* publishes ordered waypoint data behind `NavigationGuideLease`
- Flow performs reverse integration and publishes selected edges and integration
  costs behind `NavigationFlowFieldLease`

The retained `VolumeSurveyor` builds `VolumeSurveyResult` waypoint data for
chart-optional volume travel. It is not surface-search authority.

### 4.2 Guides

Guides expose movement directions to runtime systems through `IGuide`:

For the dedicated guide-layer reference, read [`PathGuides.md`](PathGuides.md).

```csharp
public interface IGuide
{
    bool TryGetMovementDirection(Vector3d origin, out Vector3d direction);
    bool TryGetFallbackDirection(Vector3d from, out Vector3d fallbackDirection);
}
```

Graph leases and the remaining guide type:

- `NavigationGuideLease` owns a graph surface payload reference and waypoint
  cursor
- `NavigationFlowFieldLease` owns a graph Flow payload reference and samples a
  heading from the actual foot position
- `VolumeGuide` implements `IWaypointGuide`

`IWaypointGuide` adds waypoint-specific operations:

```csharp
public interface IWaypointGuide : IGuide
{
    int CurrentWaypointIndex { get; }
    int GetIndex(Vector3d from);
    void AdvanceWaypoint();
    Vector3d GetCurrentWaypointDirection(Vector3d from);
}
```

Guide behavior in practice:

- `NavigationGuideLease` exposes dependency-validated waypoint sampling and
  advancement; it exposes no geometry-uncertified smoothing path
- `NavigationFlowFieldLease` exposes dependency-validated fixed-budget sampling;
  terminal sampling may require the Navigator-owned local recovery bridge

## 5. Guide Caching and Lifetime

`TrailblazerWorldContext.Guides` is the entry point for guide resolution, guide
return, cache invalidation, and cache diagnostics.

Supported operations:

- `RequestGuide(PathQuery query, out NavigationGuideLease result)` returns a
  `NavigationGuideStatus`
- `RequestFlowField(PathQuery query, out NavigationFlowFieldLease result)`
  returns a `NavigationGuideStatus`
- `RequestGuide(IPathRequest request, out IGuide result)`
- `RequestGuide<T>(IPathRequest request, out T result)`
- `ReturnGuide(IGuide guide, bool dispose = false)`
- `InvalidateCacheFor(string chartKey)`
- `FlushCache(bool force = false)`

Graph A* and Flow payload reuse validates exact dependency stamps. The remaining
volume branch still uses `ReusableSurveyResultCache<T>` for:

- cache lookup by `RequestCacheKey`
- reuse of valid survey results
- stale-entry eviction
- invalidation when charts unload or change ownership

Lifetime rules matter:

- dispose graph `NavigationGuideLease` and `NavigationFlowFieldLease` instances;
  do not pass them to `ReturnGuide(...)`
- if you request a guide directly, return it with
  `context.Guides.ReturnGuide(...)`
- `NavSteering` handles this automatically when it owns the guide lifecycle
- unloaded charts invalidate all cached results that reference them
- `InvalidateCacheFor(...)` is chart-targeted only for the remaining Volume
  cache; graph A* and Flow staleness comes from graph dependency stamps

## 6. Runtime Navigation Stack

The navigation layer is built from four main pieces.

The primary navigation controllers are split into partial files by
responsibility; the subsystem pages below list the current source layout.

### 6.1 Navigator

`Navigator` is the host-facing orchestration layer. It binds to one
`TrailblazerWorldContext`, owns transform and traversal state, composes
`NavSteering`, `NavTurning`, and `NavMotor`, coordinates the `Simulate()` /
`CommitFrameMotion()` lifecycle, and exposes an abstract `CheckTrekCondition()`
hook so each host provides traversal probing explicitly.

See also:

- [`Navigator.md`](Navigator.md)

### 6.2 NavSteering

`NavSteering` is the heading-generation layer. For guided surface travel it owns
immutable `PathQuery` intent plus a `NavigationGuideLease` or
`NavigationFlowFieldLease`, refreshing only the start foot position when
repathing. It also retains the volume request path, blends local steering
influences, and manages arrival and stop logic.

See also:

- [`NavSteering.md`](NavSteering.md)

### 6.3 NavTurning

`NavTurning` is the deterministic facing layer. It buffers turn requests,
promotes them into active target rotations, interpolates orientation over fixed
simulation steps, and optionally derives auto-turns from collision movement.

See also:

- [`NavTurning.md`](NavTurning.md)

### 6.4 NavMotor

`NavMotor` is the deterministic movement-execution layer. It consumes the
current `TrekRequest`, applies locomotion rules for ground, air, water, slopes,
jumps, controlled flight, slides, and platforms, and supports per-navigator
locomotion profiles so different navigators can install different movement
capabilities while sharing the same core motor pipeline. It then reconciles
traversal-state transitions after the host updates environment data for the
frame.

See also:

- [`NavMotor.md`](NavMotor.md)
- [`Gravity.md`](Gravity.md)

## 7. Deterministic Frame Flow

Create or attach a `TrailblazerWorldContext` once during application startup
after your host creates and populates the `GridWorld` instance Trailblazer
should use.

The fixed-step flow is usually:

```csharp
// Once during startup.
var world = new GridWorld();
world.TryAddGrid(
    new GridConfiguration(new Vector3d(-32, -8, -32), new Vector3d(32, 24, 32)),
    out _);

TrailblazerWorldContext context = TrailblazerWorldContext.Attach(world);
context.Simulate();
navigator.Simulate();
navigator.CommitFrameMotion();
context.LateSimulate();
```

What each stage does:

1. `context.Simulate()` advances that world's frame counters, flushes pending
   grid changes, culls expired guides, and runs ordered simulate hooks.
2. `Navigator.Simulate()` resolves heading, runs the motor, and updates turning.
3. Host code refreshes surface and medium data through the concrete navigator's
   `CheckTrekCondition()` implementation, typically by calling helpers such as
   `SetGroundContact(...)`, `SetAirborne(...)`, or `SetWaterContact(...)` from
   inside that override before commit.
4. `Navigator.CommitFrameMotion()` finalizes deltas, updates velocity and
   acceleration, and finalizes motor state.
5. `context.LateSimulate()` marks the visual accumulation boundary.

`context.Visualize()` exists for accumulation tracking on the visual side, but
it does not replace fixed-step simulation.

Important maintenance rule:

- hosts should keep the `TrailblazerWorldContext` handle and pass it to
  navigators, path requests, guide services, transition services, and
  volume-rule services
- `context.SetFrameRate(...)` requires a positive frame rate; zero or negative
  values are rejected
- if a value depends on `context.FrameRate`, do not freeze it in a one-time
  snapshot unless the code also refreshes it when the context frame rate
  changes; prefer reading the context live or recomputing from stored inputs

## 8. Direct Pathing Without Navigator

You can use the pathing layer without the full navigation stack:

For a pathing-first guide that does not assume `Navigator`, read
[`Pathing.md`](Pathing.md).

```csharp
NavigationGuideStatus status = context.Guides.RequestGuide(query, out NavigationGuideLease? lease);
if (status == NavigationGuideStatus.Success && lease != null)
{
    using (lease)
    {
        lease.TryGetCurrentWaypoint(out NavigationCellAddress address, out Vector3d footWaypoint);
    }
}
```

This is useful when:

- you already have your own movement controller
- you only need path queries or waypoint generation
- you want flow fields for RTS-style unit coordination without `Navigator`

## 9. Integration Checklist

Before runtime pathing works correctly:

1. Create or attach a `TrailblazerWorldContext` for the target `GridWorld`.
2. Publish navigation maps and area policies for surface graph queries.
3. Register and initialize charts only for remaining volume/handoff consumers.
4. Create and initialize your context-bound `Navigator` with an exact
   `NavigationAgentProfile`, or request guides
   directly.
5. Keep traversal state up to date through your concrete navigator's
   `CheckTrekCondition()` implementation.
6. Dispose graph leases, unload remaining charts, and clear remaining caches
   during teardown.

## 10. Common Gotchas

- A chart is not pathable until it has been initialized; `Register(chart)` does
  this by default unless you pass `initializeChart: false`.
- A surface `PathQuery` needs a published map, matching area-policy revision,
  exact profile, valid foot endpoints, and a nonzero finite work budget.
- If you request guides directly, forgetting `ReturnGuide(...)` will keep
  results checked out.
- `NeedsPath(...)` is a line trace over voxels, not a guarantee that a long
  route is globally optimal.
- `Navigator.CommitFrameMotion()` is required to apply accumulated deltas and
  refresh velocity state.
- Host code is still responsible for collision probing, surface detection, water
  detection, and other environment inputs.
- Graph mutations stale A* and Flow leases through dependency stamps; chart
  unloads invalidate remaining volume cache entries.

## 11. Where to Read Next

- [`../../README.md`](../../README.md) for package-level overview and
  quick-start examples
- [`Pathing.md`](Pathing.md) for standalone pathing integration and request
  guidance
- [`PathGuides.md`](PathGuides.md) for graph leases and the remaining volume
  guide cache
- [`Transitions.md`](Transitions.md) for authored chart and volume handoffs
- [`VolumeTraversal.md`](VolumeTraversal.md) for raw-volume traversal rules
- [`NavMotor.md`](NavMotor.md) for motor phase ordering
- [`Gravity.md`](Gravity.md) for the gravity model
- `src/Trailblazer/Runtime` for `TrailblazerWorldContext`, the deterministic
  clock, and lifecycle hooks
- `src/Trailblazer/Navigation/Navigator` for the host-facing navigator
  orchestration API
- `src/Trailblazer/Navigation` for steering, turning, motor flow, movement
  groups, and navigator guidance
- `src/Trailblazer/Pathing` for chart lifecycle, pathing state, grid-bridge
  integration, transition topology, volume rules, and search
- `src/Trailblazer/Pathing/Search/AStar` and `Search/Flow` for graph admission,
  payloads, and leases; the Hybrid/Volume folders remain later-phase code
- `src/Trailblazer/Traversal` for traversal-medium value objects shared by
  runtime, navigation, pathing, and transitions
