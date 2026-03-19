# Trailblazer Overview

Trailblazer is a deterministic navigation library for lockstep simulations and games. It is split into two layers that can be used together or independently:

- A pathing layer for chart registration, path requests, A* pathfinding, flow fields, and reusable guide caching.
- A navigation layer for steering, turning, locomotion, and deterministic frame-by-frame movement.

This document is the high-level architecture guide for the current codebase.

See also:

- `CHARTS.MD` for a deeper explanation of `NavigationChart` and the chart lifecycle
- `SERIALIZATION.MD` for Trailblazer's current serialization coverage and runtime behavior
- `../src/Trailblazer/Serialization/README.md` for the reusable Chronicler API reference
- `../src/Trailblazer/Serialization/MIGRATION.MD` for the planned extraction strategy for moving Chronicler into its own project

## 1. Core Model

Trailblazer assumes:

- world-space math uses `FixedMathSharp` types such as `Fixed64`, `Vector3d`, and `FixedQuaternion`
- traversable space is represented by `GridForge` voxels
- pathfinding is driven by `NavigationChart` data and `PathPartition` ownership
- simulation advances in deterministic fixed steps through `TrailblazerManager`

At a high level, the runtime loop is:

1. Register and initialize one or more `NavigationChart` instances.
2. Build an `IPathRequest` directly for lower-level pathing, or let `Navigator` create one from a target position and guided-path settings.
3. Resolve that request into an `IGuide` through `PathGuideFactory`, or let `NavSteering` do that for you.
4. Run `Navigator.Simulate()` on the fixed step.
5. Update traversal medium and surface state from your own collision/environment code.
6. Commit the frame with `Navigator.CommitFrameMotion()`.

## 2. World Representation

### 2.1 NavigationChart

`NavigationChart` is the pathable surface description of your world. It stores authored chart-cell data and exposes:

- `NavigationChart.From3D(...)` to build a chart from `bool[,,]` or `NavigationChartCell[,,]` voxel data
- `TryGetCell(...)` to inspect the authored cell payload at a world position
- `TryWorldToIndex(...)` to map world positions into chart coordinates
- `IsWalkable(...)` to query a world-space position
- `GetWalkablePositions()` to enumerate every walkable voxel origin in world space

Important details:

- the chart itself is data only; it does not become queryable by pathfinding until it is registered and initialized
- the chart uses flattened internal storage, but the public constructor/factory still works in 3D terms
- charts remain surface-first; open air and broad water traversal still use `VolumePathRequest`
- world bounds are derived from `MinBounds`, `Interval`, and the source array size

### 2.2 PathManager

`PathManager` is the global chart registry and live partition coordinator. It turns registered `NavigationChart` data into initialized voxel partitions, manages chart ownership and unload behavior, exposes neighbor and direct-travel utilities, and participates in guide-cache maintenance.

Explicit handoff data between chart-backed traversal and raw-volume traversal is registered separately through `TraversalTransitionRegistry`.

See also:

- [`CHARTS.MD`](CHARTS.MD)
- [`PATHMANAGER.MD`](PATHMANAGER.MD)

## 3. Path Requests

All guide requests implement `IPathRequest`. Shared request state includes:

- `StartNode`
- `EndNode`
- `UnitSize`
- `AllowUnwalkable`
- `MaxPathSearchRange`
- `HasValidEndpoints`
- `IsValid`
- `RequestCacheKey`

Shared request behavior is provided by `PathRequest`:

- `UpdateRequest(origin, destination, unitSize)` resolves start/end voxels and computes validation state
- `TrySetOrigin(...)` and `TrySetDestination(...)` update endpoints without recreating the request
- `TrySetUnitSize(...)` revalidates the request for a different agent footprint
- successful creation or endpoint reset derives `MaxPathSearchRange` using `PathManager.TryGetMaxSearchSize(...)`

### 3.1 AStarPathRequest

Use `AStarPathRequest` when you want a concrete waypoint trail.

Additional configuration includes:

- `Heuristic` with `Manhattan`, `Octile`, or `Euclidean`
- `MaxClimbHeight` for vertical step restrictions
- `AllowUnwalkable` for target-edge cases

Factory helpers:

```csharp
AStarPathRequest.TryCreate(origin, destination, out var request);
```

### 3.2 FlowFieldPathRequest

Use `FlowFieldPathRequest` when many agents can share a destination-centric field.

Additional configuration includes:

- `ExtraFloodRange`, which controls how far the flood expands beyond the destination

Factory helpers:

```csharp
FlowFieldPathRequest.TryCreate(origin, destination, out var request);
```

One important difference from A*:

- the flow-field cache key is based on destination and configuration, not the specific start voxel
- a single field can be reused by multiple agents as long as their current voxel exists in the generated field set

### 3.3 VolumePathRequest

Use `VolumePathRequest` when a navigator should travel through 3D grid volume that may not have a navigation chart.

Key traits:

- it resolves against raw `GridForge.Voxel` connectivity instead of `PathPartition` ownership
- it can stay in direct 3D travel when the corridor is clear
- it can fall back to a cached volume waypoint guide when blockers force a detour
- it supports both unrestricted open-volume travel and host-defined constrained volumes such as water
- Trailblazer does not assign water voxels itself; hosts configure that through `VolumeTraversalRules`

Related support type:

- `TraversalTransitionRegistry` stores authored handoff points between chart-backed traversal and raw-volume traversal for later hybrid-routing work

Factory helpers:

```csharp
VolumePathRequest.TryCreate(origin, destination, Fixed64.One, out var request);
```

## 4. Surveyors and Guides

Trailblazer separates raw path computation from runtime movement consumption.

### 4.1 Surveyors

Surveyors build reusable results:

- `AStarSurveyor` expands `PathPartition` nodes and produces waypoint trails
- `FlowFieldSurveyor` performs a reverse flood and produces directional field data
- `VolumeSurveyor` expands raw voxel neighbors and produces 3D waypoint trails for chart-optional volume travel

Both surveyors return concrete `SurveyResult` types:

- `AStarSurveyResult`
- `FlowFieldSurveyResult`
- `VolumeSurveyResult`

These results are what the cache stores and reuses.

### 4.2 Guides

Guides expose movement directions to runtime systems through `IGuide`:

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
    Vector3d GetMovementDirection(Vector3d from);
}
```

Guide behavior in practice:

- `AStarGuide` follows discrete waypoints and optionally exposes spline-smoothed movement
- `FlowFieldGuide` samples the local vector field and can recover by searching for a nearby flow anchor

## 5. Guide Caching and Lifetime

`PathGuideFactory` is the main entry point for guide resolution.

Supported operations:

- `RequestGuide(IPathRequest request, out IGuide result)`
- `RequestGuide<T>(IPathRequest request, out T result)`
- `RequestAStar(AStarPathRequest request)`
- `RequestFlowField(FlowFieldPathRequest request)`
- `RequestVolume(VolumePathRequest request)`
- `ReturnGuide(IGuide guide, bool dispose = false)`
- `InvalidateCacheFor(string chartKey)`
- `CullExpiredGuides(int currentFrame)`
- `FlushCache(bool force = false)`

Internally, it uses `ReusableSurveyResultCache<T>` for:

- cache lookup by `RequestCacheKey`
- reuse of valid survey results
- stale-entry eviction
- invalidation when charts unload or change ownership

Lifetime rules matter:

- if you request a guide directly, return it with `PathGuideFactory.ReturnGuide(...)`
- `NavSteering` handles this automatically when it owns the guide lifecycle
- unloaded charts invalidate all cached results that reference them

## 6. Runtime Navigation Stack

The navigation layer is built from four main pieces.

### 6.1 Navigator

`Navigator` is the host-facing orchestration layer. It owns transform and traversal state, composes `NavSteering`, `NavTurning`, and `NavMotor`, coordinates the `Simulate()` / `CommitFrameMotion()` lifecycle, and exposes an abstract `CheckTrekCondition()` hook so each host provides traversal probing explicitly.

See also:

- [`NAVIGATOR.MD`](NAVIGATOR.MD)

### 6.2 NavSteering

`NavSteering` is the heading-generation layer. It consumes an `IPathRequest`, decides between direct line-of-sight travel and guide-following, blends in local steering influences, uses an internal movement-group coordinator to preserve formation offsets for shared group sessions, and manages arrival, stop, and repath logic for the active navigation session.

See also:

- [`NAVSTEERING.MD`](NAVSTEERING.MD)

### 6.3 NavTurning

`NavTurning` is the deterministic facing layer. It buffers turn requests, promotes them into active target rotations, interpolates orientation over fixed simulation steps, and optionally derives auto-turns from collision movement.

See also:

- [`NAVTURNING.MD`](NAVTURNING.MD)

### 6.4 NavMotor

`NavMotor` is the deterministic movement-execution layer. It consumes the current `TrekRequest`, applies locomotion rules for ground, air, water, slopes, jumps, controlled flight, slides, and platforms, and supports per-navigator locomotion profiles so different navigators can install different movement capabilities while sharing the same core motor pipeline. It then reconciles traversal-state transitions after the host updates environment data for the frame.

See also:

- [`NAVMOTOR.MD`](NAVMOTOR.MD)
- [`GRAVITY.MD`](GRAVITY.MD)

## 7. Deterministic Frame Flow

Call `TrailblazerManager.Initialize()` once during application startup before entering the fixed-step loop.

The fixed-step flow is usually:

```csharp
// Once during startup.
TrailblazerManager.Initialize();
TrailblazerManager.Simulate();
navigator.Simulate();
navigator.CommitFrameMotion();
TrailblazerManager.LateSimulate();
```

What each stage does:

1. `TrailblazerManager.Simulate()` advances frame counters and then runs ordered internal simulate hooks such as `PathManager.Tick()`.
2. `Navigator.Simulate()` resolves heading, runs the motor, and updates turning.
3. Host code refreshes surface and medium data through the concrete navigator's `CheckTrekCondition()` implementation, typically by calling helpers such as `SetGroundContact(...)`, `SetAirborne(...)`, or `SetWaterContact(...)` from inside that override before commit.
4. `Navigator.CommitFrameMotion()` finalizes deltas, updates velocity and acceleration, and finalizes motor state.
5. `TrailblazerManager.LateSimulate()` marks the visual accumulation boundary.

`TrailblazerManager.Visualize()` exists for accumulation tracking on the visual side, but it does not replace fixed-step simulation.

Important maintenance rule:

- when a subsystem needs frame-step, reset, or frame-rate-change maintenance, register an ordered internal `TrailblazerManager` lifecycle hook instead of hard-wiring that subsystem into the manager
- hosts should prefer explicit `TrailblazerManager.Initialize()` during startup rather than relying on lazy first-use initialization
- if a value depends on `TrailblazerManager.FrameRate`, do not freeze it in a one-time snapshot unless the code also refreshes it from the frame-rate-change hook; prefer reading the manager live or recomputing from stored inputs

## 8. Direct Pathing Without Navigator

You can use the pathing layer without the full navigation stack:

```csharp
var request = AStarPathRequest.Create(origin, destination, Fixed64.One);

if (PathGuideFactory.RequestGuide(request, out AStarGuide guide))
{
    if (guide.TryGetMovementDirection(origin, out Vector3d heading))
    {
        // Consume the heading in your own movement system.
    }

    PathGuideFactory.ReturnGuide(guide);
}
```

This is useful when:

- you already have your own movement controller
- you only need path queries or waypoint generation
- you want flow fields for RTS-style unit coordination without `Navigator`

## 9. Integration Checklist

Before runtime pathing works correctly:

1. Set up `GridForge` global grids.
2. Build `NavigationChart` data for the relevant walkable space.
3. Register the chart with `PathManager.Register(...)`.
4. Call `PathManager.InitializeChart(chart.Name)`.
5. Create and initialize your `Navigator`, or request guides directly.
6. Keep traversal state up to date through your concrete navigator's `CheckTrekCondition()` implementation.
7. Unload charts or clear caches during teardown.

## 10. Common Gotchas

- A chart is not pathable until `InitializeChart(...)` has run.
- `IPathRequest.IsValid` depends on valid endpoints and a computed `MaxPathSearchRange`.
- If you request guides directly, forgetting `ReturnGuide(...)` will keep results checked out.
- `NeedsPath(...)` is a line trace over voxels, not a guarantee that a long route is globally optimal.
- `Navigator.CommitFrameMotion()` is required to apply accumulated deltas and refresh velocity state.
- Host code is still responsible for collision probing, surface detection, water detection, and other environment inputs.
- Chart unloads invalidate cached guides that reference those charts.

## 11. Where to Read Next

- [`../README.md`](../README.md) for package-level overview and quick-start examples
- [`NAVMOTOR.MD`](NAVMOTOR.MD) for motor phase ordering
- [`GRAVITY.MD`](GRAVITY.MD) for the gravity model
- `src/Trailblazer/Pathing` for core pathing logic
- `src/Trailblazer/Navigation` for steering, turning, and motor flow
