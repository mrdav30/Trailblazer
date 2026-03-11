# Trailblazer Overview

Trailblazer is a deterministic navigation library for lockstep simulations and games. It is split into two layers that can be used together or independently:

- A pathing layer for chart registration, path requests, A* pathfinding, flow fields, and reusable guide caching.
- A navigation layer for steering, turning, locomotion, and deterministic frame-by-frame movement.

This document is the high-level architecture guide for the current codebase.

## 1. Core Model

Trailblazer assumes:

- world-space math uses `FixedMathSharp` types such as `Fixed64`, `Vector3d`, and `FixedQuaternion`
- traversable space is represented by `GridForge` voxels
- pathfinding is driven by `NavigationChart` data and `PathPartition` ownership
- simulation advances in deterministic fixed steps through `TrailblazerManager`

At a high level, the runtime loop is:

1. Register and initialize one or more `NavigationChart` instances.
2. Build an `IPathRequest` for an origin, destination, and unit size.
3. Resolve that request into an `IGuide` through `PathGuideFactory`, or let `NavSteering` do that for you.
4. Run `Navigator.Simulate()` on the fixed step.
5. Update traversal medium and surface state from your own collision/environment code.
6. Commit the frame with `Navigator.CommitFrameMotion()`.

## 2. World Representation

### 2.1 NavigationChart

`NavigationChart` is the pathable description of your world. It stores a 3D walkability map and exposes:

- `NavigationChart.From3D(...)` to build a chart from `bool[,,]` voxel data
- `TryWorldToIndex(...)` to map world positions into chart coordinates
- `IsWalkable(...)` to query a world-space position
- `GetWalkablePositions()` to enumerate every walkable voxel origin in world space

Important details:

- the chart itself is data only; it does not become queryable by pathfinding until it is registered and initialized
- the chart uses a flattened internal map, but the public constructor/factory still works in 3D terms
- world bounds are derived from `MinBounds`, `Interval`, and the source array size

### 2.2 PathManager

`PathManager` is the global pathing registry and utility layer. It owns:

- chart registration via `Register(...)`
- chart activation via `InitializeChart(...)` or `InitializeAllCharts()`
- chart teardown via `UnloadChart(...)`, `UnloadAllCharts()`, and `ClearAll()`
- `PathPartition` pooling and neighbor binding
- neighbor discovery and straight-line viability checks

Key helpers:

- `TryGetNavigationChart(...)`
- `AllCharts`
- `NeedsPath(startPos, endPos, unitSize, allowUnwalkable)`
- `GetMaxSearchSize(startVoxel, endVoxel, out int maxSearchSize)`

`NeedsPath(...)` is the first cheap decision point. If it returns `false`, a direct move is viable and a full guide may be unnecessary.

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

- `TryPrepare(origin, destination, unitSize)` resolves start/end voxels and computes validation state
- `TrySetOrigin(...)` and `TrySetDestination(...)` update endpoints without recreating the request
- `TrySetUnitSize(...)` revalidates the request for a different agent footprint
- `Validate()` derives `MaxPathSearchRange` using `PathManager.GetMaxSearchSize(...)`

### 3.1 AStarPathRequest

Use `AStarPathRequest` when you want a concrete waypoint trail.

Additional configuration includes:

- `Heuristic` with `Manhattan`, `Octile`, or `Euclidean`
- `MaxClimbHeight` for vertical step restrictions
- `AllowUnwalkable` for target-edge cases

Factory helpers:

```csharp
var request = AStarPathRequest.Create(origin, destination);
var empty = AStarPathRequest.CreateEmpty();
```

### 3.2 FlowFieldPathRequest

Use `FlowFieldPathRequest` when many agents can share a destination-centric field.

Additional configuration includes:

- `ExtraFloodRange`, which controls how far the flood expands beyond the destination

Factory helpers:

```csharp
var request = FlowFieldPathRequest.Create(origin, destination);
var empty = FlowFieldPathRequest.CreateEmpty();
```

One important difference from A*:

- the flow-field cache key is based on destination and configuration, not the specific start voxel
- a single field can be reused by multiple agents as long as their current voxel exists in the generated field set

## 4. Surveyors and Guides

Trailblazer separates raw path computation from runtime movement consumption.

### 4.1 Surveyors

Surveyors build reusable results:

- `AStarSurveyor` expands `PathPartition` nodes and produces waypoint trails
- `FlowFieldSurveyor` performs a reverse flood and produces directional field data

Both surveyors return concrete `SurveyResult` types:

- `AStarSurveyResult`
- `FlowFieldSurveyResult`

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

`Navigator` is the high-level coordinator and host-facing abstraction. It owns:

- transform state such as `Position`, `Rotation`, `Velocity`, and `Acceleration`
- traversal state such as `FrameCondition` and `FrameRequest`
- controller instances for `NavSteering`, `NavTurning`, and `NavMotor`
- occupancy tracking against the voxel grid
- optional animation integration through `INavAnimationHandler`

Key lifecycle methods:

- `Setup(...)`
- `Initialize(TrekCondition condition)`
- `ApplyInputTrekRequest(...)`
- `ApplyGuidedTrekRequest(...)`
- `Simulate()`
- `CommitFrameMotion()`
- `SetTrekCondition(...)`
- `Reset()`

`Navigator` is abstract because each host project must provide environment-specific traversal probing in `CheckTrekCondition()`.

### 6.2 NavSteering

`NavSteering` decides where the navigator wants to move.

It is responsible for:

- accepting an `IPathRequest`
- determining whether a direct line-of-sight path is sufficient
- resolving or refreshing a guide through `PathGuideFactory`
- advancing waypoints or following flow vectors
- stuck detection and repath attempts
- group steering and local avoidance blending
- arrival and stop events

Notable state:

- `Destination`
- `CurrentRequest`
- `TrailGuide`
- `ShouldMove`
- `HasLineOfSightPath`
- `IsAtDestination`
- `IsStuck`

### 6.3 NavTurning

`NavTurning` manages deterministic facing updates.

It:

- buffers requested turn directions
- interpolates rotation toward a target quaternion
- supports collision-triggered auto-turn logic
- exposes `NeedsTurn(...)`, `RequestTurnDirection(...)`, and `SimulateTurn(...)`

### 6.4 NavMotor

`NavMotor` applies the actual deterministic movement logic for the frame.

It coordinates:

- locomotion state through `LocomotionHandler`
- ground, air, and water traversal
- gravity and other environmental forces
- jump handling
- platform velocity transfer and movement
- frame locking so movement is only applied once per simulation tick

The locomotion subsystem lives under `Navigation/Motor/Locomotions` and includes:

- `MoveLocomotion`
- `JumpLocomotion`
- `FallLocomotion`
- `PlatformLocomotion`
- `SlideLocomotion`
- `SwimLocomotion`

See also:

- [`NAVMOTOR.MD`](NAVMOTOR.MD)
- [`GRAVITY.MD`](GRAVITY.MD)

## 7. Deterministic Frame Flow

The fixed-step flow is usually:

```csharp
TrailblazerManager.Simulate();
navigator.Simulate();

// Your host game updates terrain contact, water state, ceilings, moving platforms, etc.
navigator.SetTrekCondition(
    medium: TraversalMedium.Ground,
    surfaceLevel: Fixed64.Zero,
    surfaceCondition: GroundCondition.CreateEmpty(),
    updateMotorState: true);

navigator.CommitFrameMotion();
TrailblazerManager.LateSimulate();
```

What each stage does:

1. `TrailblazerManager.Simulate()` advances frame counters and performs cache maintenance through `PathManager.Tick(...)`.
2. `Navigator.Simulate()` resolves heading, runs the motor, and updates turning.
3. Host code refreshes surface and medium data based on the game world's current state.
4. `Navigator.CommitFrameMotion()` finalizes deltas, updates velocity and acceleration, and finalizes motor state.
5. `TrailblazerManager.LateSimulate()` marks the visual accumulation boundary.

`TrailblazerManager.Visualize()` exists for accumulation tracking on the visual side, but it does not replace fixed-step simulation.

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
6. Keep traversal state up to date through `SetTrekCondition(...)` if using `Navigator`.
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
