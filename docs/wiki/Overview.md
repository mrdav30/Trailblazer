# Trailblazer Overview

Trailblazer is a deterministic navigation library for lockstep simulations and
games. It is split into two layers that can be used together or independently:

- A pathing layer for chart registration, path requests, A* pathfinding, flow
  fields, and reusable guide caching.
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
- pathfinding is driven by `NavigationChart` data and `SolidChartPartition`
  ownership
- simulation advances in deterministic fixed steps through
  `TrailblazerWorldContext`
- runtime diagnostics flow through `TrailblazerLogger.Channel`, with verbose
  debug logging gated separately through `TrailblazerLogger.DebugChannel`

At a high level, the runtime loop is:

1. Register and initialize one or more `NavigationChart` instances.
2. Build an `IPathRequest` directly for lower-level pathing, or let `Navigator`
   create one from a target position and guided-path settings.
3. Resolve that request into an `IGuide` through
   `TrailblazerWorldContext.Guides`, or let `NavSteering` do that for you.
4. Run `Navigator.Simulate()` on the fixed step.
5. Update traversal medium and surface state from your own collision/environment
   code.
6. Commit the frame with `Navigator.CommitFrameMotion()`.

## 2. World Representation

### 2.1 NavigationChart

`NavigationChart` is the pathable surface description of your world. It stores
authored chart-cell data and exposes:

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
context-local lifecycle hooks, pathing state, transitions, volume rules,
reachability snapshots, guide caches, navigator ids, and movement-group state.

`VoxelSize` is the representative cell edge reported by GridForge topology
metrics. The current pathing implementation requires every active grid in the
world to use dense rectangular-prism storage, cubic cells, and the same cell
edge. Hex, sparse, anisotropic, or conflicting metrics are rejected explicitly.
Supporting those layouts is a planned fast-follow that will replace this
compatibility seam with topology-aware pathfinding rather than silently applying
cubic assumptions.

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

All guide requests implement `IPathRequest`. Shared request state includes:

- `StartNode`
- `EndNode`
- `UnitSize`
- `AllowUnwalkableEndpoints`
- `MaxPathSearchRange`
- `HasValidEndpoints`
- `IsValid`
- `RequestCacheKey`
- `Context`

Shared request behavior is provided by `PathRequest`:

- `UpdateRequest(origin, destination, unitSize)` resolves start/end voxels,
  computes validation state, and clears `MaxPathSearchRange` when the new
  endpoints cannot be resolved
- `TrySetOrigin(...)` and `TrySetDestination(...)` update endpoints without
  recreating the request
- `TrySetUnitSize(...)` revalidates the request for a different agent footprint
- successful creation or endpoint reset derives `MaxPathSearchRange` from the
  request's `TrailblazerWorldContext`

`RequestCacheKey` is an exact value identity, not a persisted integer hash. It
compares complete GridForge world/grid generations, voxel indices, and the
request options that affect survey output; its hash code is used only for cache
bucket placement. Hybrid keys also capture ordered transition ids and the
relevant registry versions, plus the exact origin and target positions embedded
in their staged segment requests.

Use context-bound factories. Requests carry their owning
`TrailblazerWorldContext`, and guide resolution rejects requests from a
different context.

### 3.1 AStarPathRequest

Use `AStarPathRequest` when you want a concrete waypoint trail.

Additional configuration includes:

- `Heuristic` with `Manhattan`, `Octile`, or `Euclidean`; `Octile` is the
  closest fit for diagonal-enabled chart grids, while `Manhattan` supports
  axis-biased routing
- `MaxClimbHeight` for vertical step restrictions

Factory helpers:

```csharp
AStarPathRequest.TryCreate(context, origin, destination, Fixed64.One, out var request);
```

### 3.2 FlowFieldPathRequest

Use `FlowFieldPathRequest` when many agents can share a destination-centric
field.

Additional configuration includes:

- `MaxClimbHeight`, which restricts vertical step height during flood expansion
  and flow selection
- `ExtraFloodRange`, which controls how far the flood expands beyond the
  destination

Factory helpers:

```csharp
FlowFieldPathRequest.TryCreate(context, origin, destination, out var request);
```

One important difference from A*:

- the flow-field cache key is based on the exact destination voxel identity and
  configuration, not the specific start voxel
- a single field can be reused by multiple agents as long as their current voxel
  exists in the generated field set
- when a compatible cached field does not cover a farther start voxel,
  Trailblazer computes a covering result without resetting the smaller field
  while active guides may still reference it

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

- `AStarPathRequest` and `FlowFieldPathRequest` remain the public chart-backed
  request types.
- Setting `AllowTraversalTransitions` lets either request use internal staged
  fallback through authored `TraversalTransition` handoffs when direct chart
  routing is not enough.
- That fallback does not change the caller's intent: an `AStarPathRequest` still
  means "route this as A*," and a `FlowFieldPathRequest` still means "route this
  as FlowField."
- `Navigator` exposes the same policy for built-in guided travel through
  `ConfigureForGuidedTraversal(allowTraversalTransitions: true)`.
- For navigator-owned volume-first travel, that same opt-in also enables bounded
  swim-exit handoffs from liquid volume into a follow-up chart request and
  bounded aerial landing handoffs into chart-backed follow-up travel.
- The staged route is resolved internally from the request plus the live
  `TraversalTransitionRegistry`.
- Surveyors stay single-mode; staged escalation happens above them.

This means the public API story stays centered on normal request types even when
the resolved route temporarily switches through transition points or raw volume.

## 4. Surveyors and Guides

Trailblazer separates raw path computation from runtime movement consumption.

### 4.1 Surveyors

Surveyors build reusable results:

- `AStarSurveyor` expands `SolidChartPartition` nodes and produces waypoint
  trails
- `FlowFieldSurveyor` performs a reverse flood and produces directional field
  data
- `VolumeSurveyor` expands raw voxel neighbors and produces 3D waypoint trails
  for chart-optional volume travel

Both surveyors return concrete `SurveyResult` types:

- `AStarSurveyResult`
- `FlowFieldSurveyResult`
- `VolumeSurveyResult`

These results are what the cache stores and reuses.

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

Concrete guide types:

- `AStarGuide` implements `IWaypointGuide`
- `FlowFieldGuide` implements `IGuide`
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

- `AStarGuide` follows discrete waypoints and optionally exposes spline-smoothed
  movement
- `FlowFieldGuide` samples the local vector field, can recover by searching for
  a nearby flow anchor, and can internally execute staged transition-aware
  FlowField routes

## 5. Guide Caching and Lifetime

`TrailblazerWorldContext.Guides` is the entry point for guide resolution, guide
return, cache invalidation, and cache diagnostics.

Supported operations:

- `RequestGuide(IPathRequest request, out IGuide result)`
- `RequestGuide<T>(IPathRequest request, out T result)`
- `ReturnGuide(IGuide guide, bool dispose = false)`
- `InvalidateCacheFor(string chartKey)`
- `FlushCache(bool force = false)`

Internally, it uses `ReusableSurveyResultCache<T>` for:

- cache lookup by `RequestCacheKey`
- reuse of valid survey results
- stale-entry eviction
- invalidation when charts unload or change ownership

Lifetime rules matter:

- if you request a guide directly, return it with
  `context.Guides.ReturnGuide(...)`
- `NavSteering` handles this automatically when it owns the guide lifecycle
- unloaded charts invalidate all cached results that reference them
- `InvalidateCacheFor(...)` is chart-targeted, so unrelated cached A*,
  FlowField, and Volume guides remain reusable

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

`NavSteering` is the heading-generation layer. It consumes an `IPathRequest`,
decides between direct line-of-sight travel and guide-following, blends in local
steering influences, uses an internal movement-group coordinator to preserve
formation offsets for shared group sessions, and manages arrival, stop, and
repath logic for the active navigation session.

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
var request = AStarPathRequest.Create(context, origin, destination, Fixed64.One);

if (context.Guides.RequestGuide(request, out AStarGuide guide))
{
    if (guide.TryGetMovementDirection(origin, out Vector3d heading))
    {
        // Consume the heading in your own movement system.
    }

    context.Guides.ReturnGuide(guide);
}
```

This is useful when:

- you already have your own movement controller
- you only need path queries or waypoint generation
- you want flow fields for RTS-style unit coordination without `Navigator`

## 9. Integration Checklist

Before runtime pathing works correctly:

1. Create or attach a `TrailblazerWorldContext` for the target `GridWorld`.
2. Build `NavigationChart` data for the relevant walkable space.
3. Register the chart with `context.Pathing.Register(...)`.
4. If you registered with `initializeChart: false`, call
   `context.Pathing.InitializeChart(chart.Name)` before requesting guides or
   simulating navigators.
5. Create and initialize your context-bound `Navigator`, or request guides
   directly.
6. Keep traversal state up to date through your concrete navigator's
   `CheckTrekCondition()` implementation.
7. Unload charts or clear caches during teardown.

## 10. Common Gotchas

- A chart is not pathable until it has been initialized; `Register(chart)` does
  this by default unless you pass `initializeChart: false`.
- `IPathRequest.IsValid` depends on valid endpoints and a computed
  `MaxPathSearchRange`.
- If you request guides directly, forgetting `ReturnGuide(...)` will keep
  results checked out.
- `NeedsPath(...)` is a line trace over voxels, not a guarantee that a long
  route is globally optimal.
- `Navigator.CommitFrameMotion()` is required to apply accumulated deltas and
  refresh velocity state.
- Host code is still responsible for collision probing, surface detection, water
  detection, and other environment inputs.
- Chart unloads invalidate cached guides that reference those charts.

## 11. Where to Read Next

- [`../../README.md`](../../README.md) for package-level overview and
  quick-start examples
- [`Pathing.md`](Pathing.md) for standalone pathing integration and request
  guidance
- [`PathGuides.md`](PathGuides.md) for `IGuide`, `IWaypointGuide`, and
  context-owned guide caches
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
- `src/Trailblazer/Pathing/Search` for request, guide, survey, voxel-resolution,
  reachability, open-set, A*, FlowField, Hybrid, and Volume pathing code
- `src/Trailblazer/Traversal` for traversal-medium value objects shared by
  runtime, navigation, pathing, and transitions
