# Path Guides Reference

This document is the focused reference for Trailblazer's guide layer:

- `IGuide`
- `IWaypointGuide`
- `AStarGuide`
- `FlowFieldGuide`
- `VolumeGuide`
- `HybridGuide`
- `PathGuideFactory`

Use this file when you already understand requests and want to understand how computed path data is exposed to runtime consumers.

If you need the broader pathing model first, read `PATHING.MD`.

Relevant code:

- `src/Trailblazer/Pathing/Support/Guide/IGuide.cs`
- `src/Trailblazer/Pathing/Support/Guide/IWaypointGuide.cs`
- `src/Trailblazer/Pathing/Support/Guide/PathGuideFactory.cs`
- `src/Trailblazer/Pathing/AStar/AStarGuide.cs`
- `src/Trailblazer/Pathing/FlowField/FlowFieldGuide.cs`
- `src/Trailblazer/Pathing/Volume/VolumeGuide.cs`
- `src/Trailblazer/Pathing/Hybrid/HybridGuide.cs`
- `src/Trailblazer/Pathing/AStar/AStarSurveyResult.cs`
- `src/Trailblazer/Pathing/FlowField/FlowFieldSurveyResult.cs`
- `src/Trailblazer/Pathing/Volume/VolumeSurveyResult.cs`

## 1. What A Guide Is

A guide is the runtime-facing view of a resolved path.

Requests and surveyors answer:

- which route data should exist
- how that route should be computed
- whether that result can be reused

Guides answer:

- what direction should the caller move right now
- what fallback direction should be used if normal guidance fails
- for waypoint guides, which waypoint is currently active

The main design split is:

- `IPathRequest` describes path intent
- `SurveyResult` stores reusable computed data
- `IGuide` exposes movement directions to consumers
- `PathGuideFactory` bridges requests into guide instances

## 2. Survey Results Versus Guide Instances

This distinction matters a lot.

`PathGuideFactory` caches survey results, not guide objects.

That means:

- repeated equivalent requests can reuse cached `AStarSurveyResult`, `FlowFieldSurveyResult`, or `VolumeSurveyResult`
- each request still receives a fresh guide wrapper
- guide objects themselves are lightweight runtime views over reusable result data

Practical consequence:

- return guides with `PathGuideFactory.ReturnGuide(...)` so the backing survey result can be released back to the cache

## 3. Shared Contracts

### 3.1 `IGuide`

`IGuide` is the smallest movement contract.

It exposes:

- `TryGetMovementDirection(Vector3d origin, out Vector3d direction)`
- `TryGetFallbackDirection(Vector3d from, out Vector3d fallbackDirection)`

Use it when the caller only needs a direction query and does not care about waypoint progression.

The intended semantics are:

- `TryGetMovementDirection(...)` returns the best current heading the guide can provide
- `TryGetFallbackDirection(...)` is the recovery path when normal guidance fails or the caller is stuck

Important nuance:

- `IGuide` does not expose arrival state
- `IGuide` does not expose progress or completion indices
- consumers that need explicit waypoint progression should prefer `IWaypointGuide`

### 3.2 `IWaypointGuide`

`IWaypointGuide` extends `IGuide` for discrete waypoint trails.

It adds:

- `CurrentWaypointIndex`
- `GetIndex(Vector3d from)`
- `AdvanceWaypoint()`
- `GetCurrentWaypointDirection(Vector3d from)`

Use it when the caller wants to own waypoint progression explicitly.

Important nuance:

- `GetCurrentWaypointDirection(...)` uses `CurrentWaypointIndex`
- `TryGetMovementDirection(...)` on the concrete waypoint guides instead finds the nearest useful waypoint from the provided position

Those two paths are related but not equivalent.

For direct guide consumption:

- use `GetCurrentWaypointDirection(...)` if you are actively maintaining waypoint progress
- use `TryGetMovementDirection(...)` if you want a best-effort direction query without first managing the index yourself

Another important nuance:

- `AdvanceWaypoint()` does not clamp or validate bounds
- callers should only advance when their own arrival or closing-distance logic says it is safe

## 4. Concrete Guide Types

### 4.1 `AStarGuide`

`AStarGuide` is the waypoint guide for chart-backed A* trails.

It wraps:

- `AStarSurveyResult TrailMap`

Important state:

- `CurrentWaypointIndex`
- `UseSplineSmoothing`
- `ActiveWaypoints`

Key behavior:

- `Initialize(...)` requires a valid `AStarSurveyResult`
- `ActiveWaypoints` normally returns `TrailMap.Waypoints`
- when `UseSplineSmoothing` is enabled and there are at least four waypoints, `ActiveWaypoints` lazily switches to `AStarSurveyor.CatmullSmooth(...)`
- `TryGetMovementDirection(...)` uses the nearest active waypoint from the current position
- `GetCurrentWaypointDirection(...)` uses `CurrentWaypointIndex`
- `TryGetFallbackDirection(...)` searches forward from the last fallback index

Other useful members:

- `HasArrived()`
- `TryGetWaypointAt(...)`

Important nuance:

- `Initialize(...)` sets `CurrentWaypointIndex` to `0`
- that means consumers using `GetCurrentWaypointDirection(...)` directly should expect to manage the first progression step themselves

### 4.2 `FlowFieldGuide`

`FlowFieldGuide` is the vector-field guide for chart-backed flow fields.

It wraps either:

- `FlowFieldSurveyResult FlowMap`

or, during staged transition fallback:

- an internal `HybridRoutePlan`

Normal flow-field behavior:

- `Initialize(...)` stores the resolved `FlowMap`
- `TryGetMovementDirection(...)` samples the field through `FlowFieldSurveyor.SampleFlowVector(...)`
- `FlowFieldContainsPosition(...)` tells you whether the current voxel is inside the field
- `TryGetFallbackDirection(...)` searches for a nearby flow anchor through `FlowFieldSurveyor.TryGetNearestFlowAnchor(...)`

Staged behavior:

- `InitializeStaged(...)` switches the guide into staged-plan mode
- the public guide type stays `FlowFieldGuide`
- internally the guide advances through staged waypoints and staged sub-guides
- staged path segments can borrow `AStarGuide` or `VolumeGuide` instances from `PathGuideFactory`

This is an important API guarantee:

- when a `FlowFieldPathRequest` falls back through authored transitions, callers still receive a `FlowFieldGuide`

Important nuance:

- `FlowFieldGuide` is the only public guide type with two execution modes
- `PathGuideFactory.ReturnGuide(...)` calls `ReleaseStagedResources(...)` so any borrowed staged sub-guides are returned correctly

### 4.3 `VolumeGuide`

`VolumeGuide` is the waypoint guide for raw voxel volume detours.

It wraps:

- `VolumeSurveyResult TrailMap`

Key behavior:

- `Initialize(...)` requires a valid volume survey result
- `ActiveWaypoints` exposes the raw volume waypoints
- `TryGetMovementDirection(...)` uses the nearest active waypoint from the current position
- `GetCurrentWaypointDirection(...)` uses `CurrentWaypointIndex`
- `TryGetFallbackDirection(...)` searches forward from the last fallback index

Important nuance:

- `Initialize(...)` starts `CurrentWaypointIndex` at `1` when the guide has more than one waypoint
- unlike `AStarGuide`, the starting waypoint is skipped immediately for indexed progression

### 4.4 `HybridGuide`

`HybridGuide` is the internal waypoint guide used for explicit `HybridPathRequest` resolution.

It wraps:

- a flattened `AStarWaypoint[]`

It behaves similarly to `VolumeGuide`:

- indexed waypoint progression
- nearest-waypoint lookup for `TryGetMovementDirection(...)`
- forward-search fallback

Important limitations:

- `HybridGuide` is internal
- it does not wrap a reusable cached survey result
- `PathGuideFactory.ReturnGuide(...)` has no cache-release action for it

In practice, `HybridGuide` exists mainly for internal guide composition and serialization restoration paths rather than for public consumption.

## 5. `PathGuideFactory`

`PathGuideFactory` is the guide-layer entry point.

It is responsible for:

- routing requests to the correct guide type
- resolving or reusing cached survey results
- applying transition-aware fallback when supported
- releasing borrowed survey results back into the caches
- invalidating or evicting stale cached results

It is not responsible for:

- building requests
- updating waypoint progression for callers
- steering blend logic
- chart lifecycle

### 5.1 Public Surface

The main entry points are:

- `RequestGuide(IPathRequest request, out IGuide result)`
- `RequestGuide<T>(IPathRequest request, out T result)`
- `RequestAStar(AStarPathRequest request)`
- `RequestFlowField(FlowFieldPathRequest request)`
- `RequestVolume(VolumePathRequest request)`
- `ReturnGuide(IGuide guide, bool dispose = false)`
- `InvalidateCacheFor(string chartKey)`
- `CullExpiredGuides(int currentFrame)`
- `FlushCache(bool force = false)`

Useful status properties:

- `ActiveAStarGuideCount`
- `ActiveFlowGuideCount`
- `ActiveVolumeGuideCount`
- `IsPooling`
- `AnyInUse`

Important invalidation rule:

- `InvalidateCacheFor(string chartKey)` invalidates cached A*, FlowField, and Volume results whose `ChartsUtilized` contains that chart key
- unrelated cached guides remain reusable when the invalidation does not match them

### 5.2 Request Routing

Routing is type-based:

- `AStarPathRequest` -> `AStarGuide`
- `FlowFieldPathRequest` -> `FlowFieldGuide`
- `VolumePathRequest` -> `VolumeGuide`
- `HybridPathRequest` -> internal `HybridGuide`

Important nuance:

- `RequestGuide<T>(...)` does a direct cast after routing
- callers should only use the generic overload when `T` matches the request type they are asking for

### 5.3 A* Resolution

`RequestAStar(...)` asks the A* cache for a reusable `AStarSurveyResult`.

The normal flow is:

1. try to reuse or build a direct `AStarSurveyResult`
2. if direct A* succeeds, wrap it in a fresh `AStarGuide`
3. if direct A* fails and traversal transitions are enabled, build a flattened transition-aware fallback result
4. wrap that result in a fresh `AStarGuide`

Important guarantee:

- transition-aware chart fallback still returns `AStarGuide`, not `HybridGuide`

### 5.4 Flow Field Resolution

`RequestFlowField(...)` behaves differently from A* in one important way:

- a flow field can exist and still be unusable for the current caller if the caller's `StartNode` is not inside `result.Fields`

The normal flow is:

1. try to reuse or build a direct `FlowFieldSurveyResult`
2. verify the current request's `StartNode` is covered by that field
3. if covered, wrap it in a fresh `FlowFieldGuide`
4. if not covered and traversal transitions are enabled, build a staged fallback `FlowFieldGuide`

Important guarantee:

- transition-aware fallback still returns `FlowFieldGuide`
- that fallback guide may internally borrow other guide types while remaining a `FlowFieldGuide` at the API boundary

### 5.5 Volume Resolution

`RequestVolume(...)` is the simplest path:

1. try to reuse or build a `VolumeSurveyResult`
2. wrap it in a fresh `VolumeGuide`

There is no chart-transition fallback layer above `VolumePathRequest`.

### 5.6 Explicit Hybrid Resolution

`RequestHybrid(...)` is private and only used for explicit internal `HybridPathRequest` handling.

It:

- reads the planned `HybridRoutePlan`
- borrows sub-guides as needed to flatten staged chart and volume segments into one waypoint list
- returns borrowed sub-guides before returning
- builds a `HybridGuide` over the flattened waypoint result

Important nuance:

- the temporary borrowed guides used during flattening are returned inside `TryBuildFlattenedHybridWaypoints(...)`
- the final `HybridGuide` is not backed by a cached survey result

## 6. Cache And Lifetime Rules

`PathGuideFactory` uses one cache per reusable survey-result family:

- `_cachedAStarResults`
- `_cachedFlowResults`
- `_cachedVolumeResults`

These caches store survey results keyed by `request.RequestCacheKey`.

Guide lifetime rules:

- requesting a guide checks out the backing survey result
- returning a guide releases the backing survey result
- `dispose: true` removes the backing result from the cache instead of just releasing it

What `ReturnGuide(...)` actually does:

- `AStarGuide` -> returns `TrailMap`
- `FlowFieldGuide` -> releases staged resources, then returns `FlowMap` when one exists
- `VolumeGuide` -> returns `TrailMap`
- `HybridGuide` -> no cache action

That last case is intentional in the current design because `HybridGuide` has no cached survey-result backing to release.

## 7. Invalidation And Maintenance

Cached guide data can stop being valid even if the caller's request object still looks the same.

Invalidation sources include:

- chart ownership changes during `PathManager.InitializeChart(...)`
- chart unload through `PathManager.UnloadChart(...)`
- full teardown through `PathManager.Reset()`
- request-hash changes caused by transition-registry versioning

Maintenance behavior:

- `CullExpiredGuides(...)` removes stale, unused cached results
- `PathManager.Tick()` calls it using `TrailblazerManager.FrameCount`
- `FlushCache(force: false)` only clears caches when nothing is checked out
- `FlushCache(force: true)` clears everything

## 8. Usage Patterns

### 8.1 Minimal Direct Usage

```csharp
var request = AStarPathRequest.Create(origin, destination, Fixed64.One);

if (PathGuideFactory.RequestGuide(request, out AStarGuide guide))
{
    guide.TryGetMovementDirection(origin, out Vector3d heading);
    PathGuideFactory.ReturnGuide(guide);
}
```

### 8.2 Polymorphic Usage

```csharp
if (PathGuideFactory.RequestGuide(request, out IGuide guide))
{
    if (guide is IWaypointGuide waypointGuide)
    {
        Vector3d heading = waypointGuide.GetCurrentWaypointDirection(origin);
    }
    else
    {
        guide.TryGetMovementDirection(origin, out Vector3d heading);
    }

    PathGuideFactory.ReturnGuide(guide);
}
```

### 8.3 Waypoint Progression

When consuming a waypoint guide directly, the normal pattern is:

1. get indexed movement from `GetCurrentWaypointDirection(...)`
2. decide when the current waypoint counts as reached
3. call `AdvanceWaypoint()`

This is what `NavSteering` does for `IWaypointGuide`.

## 9. Common Gotchas

- A guide is not the cached object. The cached object is the underlying survey result.
- `TryGetMovementDirection(...)` and `GetCurrentWaypointDirection(...)` can produce different behavior on waypoint guides.
- `AdvanceWaypoint()` does not bounds-check.
- `AStarGuide` starts indexed progression at waypoint `0`.
- `VolumeGuide` and `HybridGuide` start indexed progression at waypoint `1` when possible.
- A `FlowFieldGuide` may be executing a staged hybrid route even though its public type is still `FlowFieldGuide`.
- `ReturnGuide(...)` matters even when guide instances themselves are freshly allocated, because the backing survey result is checked out from a cache.
- `RequestGuide<T>(...)` can throw if the caller asks for the wrong `T`.

## 10. AI And Contributor Notes

If you are modifying the guide layer, verify:

- which objects are cached and which are not
- whether a change affects `RequestCacheKey` assumptions
- whether a guide method uses nearest-waypoint lookup or indexed progression
- whether staged flow-field fallback still returns the public `FlowFieldGuide` shape
- whether borrowed staged guides are always returned

High-risk files:

- `src/Trailblazer/Pathing/Support/Guide/PathGuideFactory.cs`
- `src/Trailblazer/Pathing/FlowField/FlowFieldGuide.cs`
- `src/Trailblazer/Pathing/AStar/AStarGuide.cs`
- `src/Trailblazer/Pathing/Volume/VolumeGuide.cs`
- `src/Trailblazer/Pathing/Hybrid/HybridGuide.cs`
- `src/Trailblazer/Pathing/Support/ReusableSurveyResultCache.cs`

Useful test entry points:

- `tests/Trailblazer.Tests/Pathing/AStarPathSurveryor.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/FlowFieldSurveyor.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/AStarTransitionFallback.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/FlowFieldTransitionFallback.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/AerialSurveyor.Tests.cs`

## 11. Where To Read Next

- `PATHING.MD` for the request and surveyor model
- `TRANSITIONS.MD` for authored transition fallback and staged route planning
- `VOLUMETRAVERSAL.MD` for raw-volume traversal rules behind `VolumeGuide`
- `PATHMANAGER.MD` for cache invalidation triggers caused by chart lifecycle
- `NAVSTEERING.MD` for the main runtime consumer of guides
- `SERIALIZATION.MD` if you need current guide-restoration behavior during load
