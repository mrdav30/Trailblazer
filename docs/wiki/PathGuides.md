# Path Guides Reference

This document is the focused reference for Trailblazer's guide layer:

- `NavigationGuideLease`
- remaining `IGuide` and `IWaypointGuide` contracts
- `FlowFieldGuide`
- `VolumeGuide`
- `TrailblazerGuideService`

Use this file when you already understand requests and want to understand how
computed path data is exposed to runtime consumers.

If you need the broader pathing model first, read `Pathing.md`.

Relevant code:

- `src/Trailblazer/Pathing/Search/Guide/IGuide.cs`
- `src/Trailblazer/Pathing/Search/Guide/IWaypointGuide.cs`
- `src/Trailblazer/Pathing/Search/Guide/TrailblazerGuideService.cs`
- `src/Trailblazer/Pathing/Search/AStar/NavigationGuideLease.cs`
- `src/Trailblazer/Pathing/Search/Guide/PathGuideFactory.cs`
- `src/Trailblazer/Pathing/Search/FlowField/FlowFieldGuide.cs`
- `src/Trailblazer/Pathing/Search/Volume/VolumeGuide.cs`
- `src/Trailblazer/Pathing/Search/FlowField/FlowFieldSurveyResult.cs`
- `src/Trailblazer/Pathing/Search/Volume/VolumeSurveyResult.cs`

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

The surface design split is:

- `PathQuery` describes complete immutable intent
- an immutable dependency-stamped payload stores reusable computed data
- `NavigationGuideLease` owns one payload reference and a guide-local cursor
- `TrailblazerWorldContext.Guides` returns explicit status plus the lease

The remaining flow/volume paths still use `IPathRequest`, cached
`SurveyResult`, and `IGuide`.

## 2. Survey Results Versus Guide Instances

This distinction matters a lot.

Graph surface A* caches immutable payloads, not leases. The remaining guide
factory caches flow/volume survey results, not guide objects.

That means:

- repeated equivalent surface queries can share immutable payload data
- each successful caller receives an exclusive waypoint cursor in its own lease
- equivalent remaining requests can reuse `FlowFieldSurveyResult` or
  `VolumeSurveyResult`

Practical consequence:

- dispose surface leases; return remaining guides with
  `context.Guides.ReturnGuide(...)`

## 3. Shared Contracts

### 3.1 `NavigationGuideLease`

`NavigationGuideLease` is the public graph-surface cursor. It exposes:

- `Status`
- `CurrentWaypointIndex`
- `WaypointCount`
- `TryGetCurrentWaypoint(...)`
- `TryAdvanceWaypoint()`
- `Dispose()`

Every cursor operation returns or exposes `NavigationGuideStatus`; dependency
changes can make an active lease `Stale`. `Dispose()` releases the payload
reference. Do not pass a graph lease to `ReturnGuide(...)`.

Acquisition reports one of `Success`, `Unsupported`, `NoMap`, `InvalidProfile`,
`InvalidStart`, `InvalidEnd`, `NoPath`, `BudgetExceeded`, `CostOverflow`,
`CapacityExceeded`, or `Stale`. A lease exists only for `Success`.

### 3.2 `IGuide`

`IGuide` is the smallest movement contract.

It exposes:

- `TryGetMovementDirection(Vector3d origin, out Vector3d direction)`
- `TryGetFallbackDirection(Vector3d from, out Vector3d fallbackDirection)`

Use it when the caller only needs a direction query and does not care about
waypoint progression.

The intended semantics are:

- `TryGetMovementDirection(...)` returns the best current heading the guide can
  provide
- `TryGetFallbackDirection(...)` is the recovery path when normal guidance fails
  or the caller is stuck

Important nuance:

- `IGuide` does not expose arrival state
- `IGuide` does not expose progress or completion indices
- consumers that need explicit waypoint progression should prefer
  `IWaypointGuide`

### 3.3 `IWaypointGuide`

`IWaypointGuide` extends `IGuide` for discrete waypoint trails.

It adds:

- `CurrentWaypointIndex`
- `GetIndex(Vector3d from)`
- `AdvanceWaypoint()`
- `GetCurrentWaypointDirection(Vector3d from)`

Use it when the caller wants to own waypoint progression explicitly.

Important nuance:

- `GetCurrentWaypointDirection(...)` uses `CurrentWaypointIndex`
- `TryGetMovementDirection(...)` on the concrete waypoint guides instead finds
  the nearest useful waypoint from the provided position

Those two paths are related but not equivalent.

For direct guide consumption:

- use `GetCurrentWaypointDirection(...)` if you are actively maintaining
  waypoint progress
- use `TryGetMovementDirection(...)` if you want a best-effort direction query
  without first managing the index yourself

Another important nuance:

- `AdvanceWaypoint()` does not clamp or validate bounds
- callers should only advance when their own arrival or closing-distance logic
  says it is safe

## 4. Concrete Guide Types

### 4.1 `FlowFieldGuide`

`FlowFieldGuide` is the vector-field guide for chart-backed flow fields.

It wraps either:

- `FlowFieldSurveyResult FlowMap`

or, during staged transition fallback:

- an internal `HybridRoutePlan`

Normal flow-field behavior:

- `Initialize(...)` stores the resolved `FlowMap`
- `TryGetMovementDirection(...)` samples the field through
  `FlowFieldSurveyor.SampleFlowVector(context, ...)`
- `FlowFieldContainsPosition(...)` tells you whether the current voxel is inside
  the field
- `TryGetFallbackDirection(...)` searches for a nearby flow anchor through
  `FlowFieldSurveyor.TryGetNearestFlowAnchor(context, ...)`

Staged behavior:

- `InitializeStaged(...)` switches the guide into staged-plan mode
- the public guide type stays `FlowFieldGuide`
- internally the guide advances through staged waypoints and staged sub-guides
- staged path segments can borrow `FlowFieldGuide` or `VolumeGuide` instances
  from the remaining guide factory

This is an important API guarantee:

- when a `FlowFieldPathRequest` falls back through authored transitions, callers
  still receive a `FlowFieldGuide`

Important nuance:

- `FlowFieldGuide` is the only public guide type with two execution modes
- `PathGuideFactory.ReturnGuide(...)` calls `ReleaseStagedResources(...)` so any
  borrowed staged sub-guides are returned correctly

### 4.2 `VolumeGuide`

`VolumeGuide` is the waypoint guide for raw voxel volume detours.

It wraps:

- `VolumeSurveyResult TrailMap`

Key behavior:

- `Initialize(...)` requires a valid volume survey result
- `ActiveWaypoints` exposes the raw volume waypoints
- `TryGetMovementDirection(...)` uses the nearest active waypoint from the
  current position
- `GetCurrentWaypointDirection(...)` uses `CurrentWaypointIndex`
- `TryGetFallbackDirection(...)` searches forward from the last fallback index

Important nuance:

- `Initialize(...)` starts `CurrentWaypointIndex` at `1` when the guide has more
  than one waypoint
- the starting waypoint is skipped immediately for indexed progression

## 5. Context Guide Service

`TrailblazerWorldContext.Guides` is the context-owned guide-layer entry point.
Graph queries are admitted against that context's immutable graph snapshot.
Remaining requests carry their owning `TrailblazerWorldContext`; the service
rejects cross-context use.

It is responsible for:

- routing requests to the correct guide type
- returning exact `NavigationGuideStatus` for graph surface queries
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

- `context.Guides.RequestGuide(PathQuery query, out NavigationGuideLease result)`
- `context.Guides.RequestGuide(IPathRequest request, out IGuide result)`
- `context.Guides.RequestGuide<T>(IPathRequest request, out T result)`
- `context.Guides.ReturnGuide(IGuide guide, bool dispose = false)`
- `context.Guides.InvalidateCacheFor(string chartKey)`
- `context.Guides.FlushCache(bool force = false)`

The internal `PathGuideFactory` performs the same routing and cache work after
`context.Guides` enters the owning context state.

Useful status properties:

- `TotalFlowGuideCount`
- `TotalVolumeGuideCount`
- `TotalHybridRoutePlanCount`
- `InUseFlowGuideCount`
- `InUseVolumeGuideCount`
- `InUseHybridRoutePlanCount`
- `IsPooling`
- `AnyInUse`

Important invalidation rule:

- `InvalidateCacheFor(string chartKey)` applies only to remaining FlowField,
  Volume, and route-plan results whose `ChartsUtilized` contains that chart key
- unrelated cached guides remain reusable when the invalidation does not match
  them
- graph surface leases validate exact graph dependency stamps instead of chart
  keys

### 5.2 Request Routing

Routing is type-based:

- `PathQuery` surface A* -> status plus `NavigationGuideLease`
- `FlowFieldPathRequest` -> `FlowFieldGuide`
- `VolumePathRequest` -> `VolumeGuide`

Important nuance:

- `RequestGuide<T>(...)` does a direct cast after routing
- callers should only use the generic overload when `T` matches the request type
  they are asking for

### 5.3 Surface A* Resolution

The service validates the supported surface query shape, admits endpoint/search
work under its finite budget, publishes one immutable result payload, and
returns a lease only with `NavigationGuideStatus.Success`. FlowField, volume,
and transition-enabled query shapes return `Unsupported`; there is no fallback
to a legacy surface provider.

### 5.4 Flow Field Resolution

`RequestFlowField(...)` behaves differently from A* in one important way:

- a flow field can exist and still be unusable for the current caller if the
  caller's `StartNode` is not inside `result.Fields`

The normal flow is:

1. try to reuse or build a direct `FlowFieldSurveyResult`
2. verify the current request's `StartNode` is covered by that field
3. if covered, wrap it in a fresh `FlowFieldGuide`
4. if not covered and traversal transitions are enabled, build a staged fallback
   `FlowFieldGuide`

Important guarantee:

- transition-aware fallback still returns `FlowFieldGuide`
- that fallback guide may internally borrow other guide types while remaining a
  `FlowFieldGuide` at the API boundary

### 5.5 Volume Resolution

`RequestVolume(...)` is the simplest path:

1. try to reuse or build a `VolumeSurveyResult`
2. wrap it in a fresh `VolumeGuide`

There is no chart-transition fallback layer above `VolumePathRequest`.

### 5.6 FlowField Staged Resolution

The remaining hybrid planner feeds `HybridRoutePlan` directly to a
`FlowFieldGuide`. It has no standalone guide type and no surface-A* conversion
path.

## 6. Cache And Lifetime Rules

Graph surface A* owns a dependency-stamped immutable payload cache. The
remaining guide factory uses one cache per survey-result or route-plan family:

- `_cachedFlowResults`
- `_cachedVolumeResults`
- `_cachedHybridRoutePlans`

These caches store survey results or route plans keyed by
`request.RequestCacheKey`. The key stores exact GridForge world, grid,
generation, voxel, and request-option identity; its hash code only selects a
bucket. The caches remain context-local as an additional ownership boundary.

Guide lifetime rules:

- a surface query returns an exclusive cursor lease over an immutable payload;
  disposing it releases that reference
- requesting a guide checks out the backing survey result
- returning a guide releases the backing survey result
- `dispose: true` removes the backing result from the cache instead of just
  releasing it

What `ReturnGuide(...)` actually does:

- `FlowFieldGuide` -> releases staged resources, then returns `FlowMap` when one
  exists
- `VolumeGuide` -> returns `TrailMap`

## 7. Invalidation And Maintenance

Cached guide data can stop being valid even if the caller's request object still
looks the same.

Invalidation sources include:

- chart ownership changes during `PathManager.InitializeChart(...)`
- chart unload through `PathManager.UnloadChart(...)`
- full pathing teardown through `context.Pathing.Reset()` or world-event
  teardown through `GridWorld.Reset()`
- request-hash changes caused by transition-registry versioning

Maintenance behavior:

- `CullExpiredGuides(...)` removes stale, unused cached results
- `TrailblazerWorldContext.Simulate()` culls that context's stale guides using
  the context frame count
- `FlushCache(force: false)` only clears caches when nothing is checked out
- `FlushCache(force: true)` clears everything

## 8. Usage Patterns

### 8.1 Minimal Direct Usage

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

### 8.2 Polymorphic Usage

```csharp
if (context.Guides.RequestGuide(request, out IGuide guide))
{
    if (guide is IWaypointGuide waypointGuide)
    {
        Vector3d heading = waypointGuide.GetCurrentWaypointDirection(origin);
    }
    else
    {
        guide.TryGetMovementDirection(origin, out Vector3d heading);
    }

    context.Guides.ReturnGuide(guide);
}
```

### 8.3 Waypoint Progression

For a graph surface lease, inspect `CurrentWaypointIndex`/`WaypointCount`, read
the current waypoint, and call `TryAdvanceWaypoint()`. Treat any non-success
status as terminal for that lease.

When consuming a remaining `IWaypointGuide` directly, the normal pattern is:

1. get indexed movement from `GetCurrentWaypointDirection(...)`
2. decide when the current waypoint counts as reached
3. call `AdvanceWaypoint()`

This is what `NavSteering` does for `IWaypointGuide`.

## 9. Common Gotchas

- A guide is not the cached object. The cached object is the underlying survey
  result.
- `TryGetMovementDirection(...)` and `GetCurrentWaypointDirection(...)` can
  produce different behavior on waypoint guides.
- `AdvanceWaypoint()` does not bounds-check.
- A graph surface lease starts indexed progression at waypoint `0` and checks
  bounds and dependency status when advancing.
- `VolumeGuide` starts indexed progression at waypoint `1` when possible.
- A `FlowFieldGuide` may be executing a staged hybrid route even though its
  public type is still `FlowFieldGuide`.
- `ReturnGuide(...)` matters even when guide instances themselves are freshly
  allocated, because the backing survey result is checked out from a cache.
- `RequestGuide<T>(...)` can throw if the caller asks for the wrong `T`.

## 10. AI And Contributor Notes

If you are modifying the guide layer, verify:

- which objects are cached and which are not
- whether a change affects `RequestCacheKey` assumptions
- whether a guide method uses nearest-waypoint lookup or indexed progression
- whether staged flow-field fallback still returns the public `FlowFieldGuide`
  shape
- whether borrowed staged guides are always returned

High-risk files:

- `src/Trailblazer/Pathing/Search/AStar/NavigationGuideLease.cs`
- `src/Trailblazer/Pathing/Search/Guide/PathGuideFactory.cs`
- `src/Trailblazer/Pathing/Search/FlowField/FlowFieldGuide.cs`
- `src/Trailblazer/Pathing/Search/Volume/VolumeGuide.cs`
- `src/Trailblazer/Pathing/Search/Survey/ReusableSurveyResultCache.cs`

Useful test entry points:

- `tests/Trailblazer.Tests/Pathing/Graph/TrailblazerGuideServiceTests.cs`
- `tests/Trailblazer.Tests/Pathing/Search/FlowField/FlowFieldSurveyor.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/Search/FlowField/FlowFieldTransitionFallback.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/Search/Volume/AerialSurveyor.Tests.cs`

## 11. Where To Read Next

- `Pathing.md` for the request and surveyor model
- `Transitions.md` for authored transition fallback and staged route planning
- `VolumeTraversal.md` for raw-volume traversal rules behind `VolumeGuide`
- `PathManager.md` for cache invalidation triggers caused by chart lifecycle
- `NavSteering.md` for the main runtime consumer of guides
- `Serialization.md` if you need current guide-restoration behavior during load
