# Pathing Reference

This document is the standalone guide for Trailblazer's `Trailblazer.Pathing` namespace.

It is intended for developers integrating Trailblazer pathfinding without the higher-level `Navigation` stack.

If you only need the broad architecture, start with `OVERVIEW.md`.
If you need chart lifecycle details, pair this with `CHARTS.MD` and `PATHMANAGER.MD`.
If you want the dedicated guide and factory reference, read `PATHGUIDES.MD`.
If you need authored handoffs and volume rules, read `TRANSITIONS.MD` and `VOLUMETRAVERSAL.MD`.

Relevant code:

- `src/Trailblazer/Pathing/Support/Request/IPathRequest.cs`
- `src/Trailblazer/Pathing/Support/Request/PathRequest.cs`
- `src/Trailblazer/Pathing/AStar/AStarPathRequest.cs`
- `src/Trailblazer/Pathing/FlowField/FlowFieldPathRequest.cs`
- `src/Trailblazer/Pathing/Volume/VolumePathRequest.cs`
- `src/Trailblazer/Pathing/Hybrid/HybridPathRequest.cs`
- `src/Trailblazer/Pathing/Support/Guide/PathGuideFactory.cs`
- `src/Trailblazer/Pathing/AStar/AStarSurveyor.cs`
- `src/Trailblazer/Pathing/FlowField/FlowFieldSurveyor.cs`
- `src/Trailblazer/Pathing/Volume/VolumeSurveyor.cs`
- `src/Trailblazer/Pathing/VoxelFinder/VoxelFinder.cs`
- `src/Trailblazer/Pathing/VoxelFinder/RawVoxelFinder.cs`
- `src/Trailblazer/Pathing/Support/Transition/*`

## 1. What Pathing Is

`Trailblazer.Pathing` is the lower-level deterministic routing layer.

It is responsible for:

- chart registration and initialization through `PathManager`
- endpoint resolution from world positions into voxels
- chart-backed A* and flow-field requests
- chart-optional raw-volume requests
- staged transition-aware fallback between chart and volume segments
- reusable survey result caching through `PathGuideFactory`

It is not responsible for:

- steering blend logic
- turning
- locomotion
- per-frame movement execution
- host-side environment probing

Those responsibilities belong to `Trailblazer.Navigation`.

## 2. Standalone Runtime Flow

When you use `Trailblazer.Pathing` directly, the normal flow is:

1. Set up `GridForge` global grids.
2. For chart-backed requests, build one or more `NavigationChart` instances.
3. Register each chart with `PathManager.Register(...)`.
4. Call `PathManager.InitializeChart(...)` or `InitializeAllCharts()`.
5. For raw water-volume requests, configure `VolumeTraversalRules` before creating the request.
6. Create an `IPathRequest`.
7. Ask `PathGuideFactory` for a guide.
8. Consume the guide in your own movement or planning system.
9. Return the guide with `PathGuideFactory.ReturnGuide(...)`.
10. On teardown, clear shared state with `PathManager.Reset()`.

Chart registration alone is not enough. Chart-backed requests depend on live `PathPartition` ownership, and that only exists after initialization.

## 3. Core Request Contract

All request types implement `IPathRequest`.

Shared state:

- `Origin`: exact world-space start position supplied by the caller
- `TargetPosition`: exact world-space target position supplied by the caller
- `StartNode`: resolved voxel used as the current origin node
- `EndNode`: resolved voxel used as the destination node
- `UnitSize`: clearance size used during endpoint and traversal validation
- `AllowUnwalkableEndNode`: endpoint-relaxation policy
- `MaxPathSearchRange`: grid-size-derived search cap used to mark the request valid

Shared validation flags:

- `HasOrigin`
- `HasDestination`
- `HasValidEndpoints`
- `IsValid`
- `HasZeroDisplacement`

Shared lifecycle methods:

- `UpdateRequest(origin, destination, unitSize)`
- `TrySetOrigin(...)`
- `TrySetDestination(...)`
- `TrySetUnitSize(...)`

Important model rules:

- `Origin` and `TargetPosition` are exact points, not snapped voxel origins.
- `StartNode` and `EndNode` are the resolved pathing hooks into the rest of the system.
- `RequestCacheKey` is the request hash and is the cache key used by `PathGuideFactory`.
- A request can have world positions stored and still be invalid if endpoints or search range cannot be resolved.

## 4. `PathRequest` Base Behavior

`PathRequest` is the shared base class for chart-backed requests.

It provides:

- endpoint resolution through `VoxelFinder`
- size-aware endpoint revalidation
- search-range calculation through `PathManager.TryGetMaxSearchSize(...)`
- reusable origin and destination mutation without recreating the request object

The chart-backed endpoint rules are:

- direct endpoints must resolve to voxels that belong to a live `PathPartition`
- `AllowUnwalkableEndNode: true` lets blocked or non-chart endpoints relax to a nearby chart-traversable voxel
- larger `UnitSize` values can also trigger endpoint relaxation even when `AllowUnwalkableEndNode` is false

That last rule matters. `AllowUnwalkableEndNode` is not the only reason endpoints can snap. Size validation can also move the request onto a nearby valid voxel.

## 5. Request Families

### 5.1 `AStarPathRequest`

Use `AStarPathRequest` when you want a concrete waypoint trail for one route.

Key configuration:

- `Heuristic`
- `MaxClimbHeight`
- `AllowUnwalkableEndNode`
- `AllowTraversalTransitions`

Use it when:

- one agent or one route needs a specific trail
- you want explicit waypoint progression
- path reuse is keyed by both start and end voxels

Cache-key behavior:

- includes both `StartNode` and `EndNode`
- includes `UnitSize`, `AllowUnwalkableEndNode`, `AllowTraversalTransitions`, `Heuristic`, `MaxClimbHeight`, and `MaxPathSearchRange`
- also includes `TraversalTransitionRegistry.RegistryVersion` when transition fallback is enabled

Example:

```csharp
var request = AStarPathRequest.Create(origin, destination, Fixed64.One);
if (PathGuideFactory.RequestGuide(request, out AStarGuide guide))
{
    // Consume guide.ActiveWaypoints or guide.TryGetMovementDirection(...)
    PathGuideFactory.ReturnGuide(guide);
}
```

### 5.2 `FlowFieldPathRequest`

Use `FlowFieldPathRequest` when multiple agents can share one destination-centered field.

Key configuration:

- `ExtraFloodRange`
- `AllowUnwalkableEndNode`
- `AllowTraversalTransitions`

Use it when:

- many units are moving toward the same destination
- you want reuse centered on the destination instead of on a unique start-to-end pair

Cache-key behavior:

- ignores the start voxel on purpose
- includes `EndNode`, `UnitSize`, `AllowUnwalkableEndNode`, `AllowTraversalTransitions`, `ExtraFloodRange`, and `MaxPathSearchRange`
- includes `TraversalTransitionRegistry.RegistryVersion` when transition fallback is enabled

Important nuance:

- the cached field may exist and still be unusable for the current caller if the caller's `StartNode` is not present in `result.Fields`
- `PathGuideFactory.RequestFlowField(...)` checks that coverage before it returns a usable `FlowFieldGuide`

### 5.3 `VolumePathRequest`

Use `VolumePathRequest` when traversal should run through raw voxel volume rather than through chart partitions.

For the dedicated raw-volume rules reference, read [`VOLUMETRAVERSAL.MD`](VOLUMETRAVERSAL.MD).

Key configuration:

- `Heuristic`
- `AllowUnwalkableEndNode`
- `TraversalMode`

Current traversal modes:

- `Open`
- `Water`

Use it when:

- there is no meaningful `NavigationChart` for the space
- movement is fundamentally 3D
- traversal should stay inside a host-defined volume such as water

Key behavior differences from chart-backed requests:

- endpoints resolve through `RawVoxelFinder`, not `VoxelFinder`
- traversal validates raw voxels directly and only uses `PathPartition` if one happens to exist
- non-open modes depend on `VolumeTraversalRules` being configured first

Important nuance:

- `VolumePathRequest.TrySetUnitSize(...)` only updates the stored `UnitSize`
- it does not recompute endpoints or `MaxPathSearchRange`
- if unit size changes should trigger full revalidation, call `UpdateRequest(...)`

Example:

```csharp
VolumeTraversalRules.SetWaterVoxelPartition<MyWaterPartition>();

var request = VolumePathRequest.Create(
    origin,
    destination,
    Fixed64.One,
    traversalMode: VolumeTraversalMode.Water);
```

### 5.4 `HybridPathRequest`

`HybridPathRequest` is the internal staged-routing adapter.

It is not the normal public entry point. External callers should usually create:

- `AStarPathRequest`
- `FlowFieldPathRequest`
- `VolumePathRequest`

`HybridPathRequest` exists so chart-backed requests can stay public and simple while still falling back through authored transitions.

Current planned route shapes are:

- chart direct
- chart -> transition -> chart
- chart -> transition -> volume -> transition -> chart

Important internal behavior:

- `HybridPathRequest.CreateFromAStar(...)` and `CreateFromFlowField(...)` preserve the caller's original intent
- `RebuildPlan()` calls `HybridRoutePlanner`
- validity depends on both endpoints and a non-null `RoutePlan`
- its hash includes the directed transition ids in the chosen route plan

If you are documenting or modifying public API, treat `HybridPathRequest` as internal infrastructure rather than a user-facing request family.

## 6. Endpoint Resolution Rules

The request object is where world-space intent turns into pathing nodes.

Chart-backed requests use `VoxelFinder`:

- direct voxel first
- nearby chart-traversable neighbor second
- traced fallback or star search only when endpoint relaxation is allowed or size fallback is required

Volume requests use `RawVoxelFinder`:

- direct voxel first
- nearby traversable raw-volume neighbor second
- traced fallback after that

Practical consequences:

- requests can snap endpoints to nearby valid voxels
- `AllowUnwalkableEndNode` relaxes endpoint resolution only; it does not make the full route ignore walkability
- `AllowUnwalkableEndNode` also does not imply transition fallback
- exact target points inside the destination voxel are preserved in `TargetPosition`

The tests in `tests/Trailblazer.Tests/Pathing/VoxelFinder.Tests.cs` are the best executable reference for these rules.

## 7. Transitions and Staged Fallback

Chart-backed requests can opt into authored transitions through `AllowTraversalTransitions`.

For the dedicated authored-handoff reference, read [`TRANSITIONS.MD`](TRANSITIONS.MD).

That opt-in means:

- direct A* or flow-field routing is tried first
- if direct chart routing fails, `PathGuideFactory` can build a staged fallback using `HybridPathRequest`
- the original public request type still describes the caller's intent

Important distinctions:

- `AllowUnwalkableEndNode` is not transition fallback
- transition fallback is chart-request-only
- transition topology changes invalidate cache keys through `TraversalTransitionRegistry.RegistryVersion`

`TraversalTransitionRegistry` is the source of authored handoff data.

It:

- resolves anchors to voxels at registration time
- exposes outgoing and incoming transition queries
- is cleared by `PathManager.Reset()`

For water routes, transition fallback and `VolumePathRequest` both still depend on `VolumeTraversalRules` being configured.

## 8. Guide Resolution and Caching

`PathGuideFactory` is the public request-to-guide bridge.

For the dedicated guide-layer contract and lifecycle reference, read [`PATHGUIDES.MD`](PATHGUIDES.MD).

Routing by type:

- `AStarPathRequest` -> `AStarGuide`
- `FlowFieldPathRequest` -> `FlowFieldGuide`
- `VolumePathRequest` -> `VolumeGuide`
- `HybridPathRequest` -> internal `HybridGuide`

Cache behavior:

- A*, flow-field, and volume survey results each use their own `ReusableSurveyResultCache<T>`
- the cache key is `request.RequestCacheKey`
- cached results are reused until invalidated or evicted
- stale entries are culled by `PathManager.Tick()` through `TrailblazerManager`

Important lifecycle rule:

- requesting a guide directly checks a survey result out of the cache
- you should return it with `PathGuideFactory.ReturnGuide(...)`

Invalidation sources:

- `PathManager.InitializeChart(...)` when overlapping ownership changes live partitions
- `PathManager.UnloadChart(...)`
- `PathManager.Reset()`
- transition-topology changes when the request hash includes `TraversalTransitionRegistry.RegistryVersion`

## 9. Choosing The Right Request

Choose `AStarPathRequest` when:

- you need a concrete waypoint chain
- route identity should include both origin and destination
- you care about climb limits and waypoint-style path following

Choose `FlowFieldPathRequest` when:

- many agents share one destination
- you want destination-centric reuse
- the caller can tolerate that field coverage is checked at guide-request time

Choose `VolumePathRequest` when:

- movement should not depend on chart ownership
- the space is open 3D or mode-specific volume
- you are integrating pathing without the navigation layer for swim or aerial systems

Choose transition-aware chart requests when:

- movement should stay chart-first
- authored jump, takeoff, landing, or swim handoffs should extend chart routing
- you do not want to expose hybrid routing as a separate public request type

## 10. Integration Patterns

### 10.1 Minimal Chart-Backed Pathing

```csharp
NavigationChart chart = NavigationChart.From3D("Ground", data, minBounds, Fixed64.One);
PathManager.Register(chart);
PathManager.InitializeChart(chart.Name);

var request = AStarPathRequest.Create(origin, destination, Fixed64.One);
if (PathGuideFactory.RequestGuide(request, out AStarGuide guide))
{
    if (guide.TryGetMovementDirection(origin, out Vector3d heading))
    {
        // Use heading in your own system.
    }

    PathGuideFactory.ReturnGuide(guide);
}
```

### 10.2 Shared Destination Flow Field

```csharp
var request = FlowFieldPathRequest.Create(origin, destination, Fixed64.One);
request.ExtraFloodRange = 12;

if (PathGuideFactory.RequestGuide(request, out FlowFieldGuide guide))
{
    // Multiple agents can request equivalent destination fields.
    PathGuideFactory.ReturnGuide(guide);
}
```

### 10.3 Transition-Aware Chart Pathing

```csharp
var request = AStarPathRequest.Create(origin, destination, Fixed64.One);
request.AllowTraversalTransitions = true;
```

In this mode, transitions are still data owned by `TraversalTransitionRegistry`, not by the request itself.

## 11. Common Gotchas

- `PathManager.Register(...)` does not make a chart pathable. You must initialize it.
- `IsValid` depends on resolved endpoints and a positive `MaxPathSearchRange`.
- `AllowUnwalkableEndNode` relaxes endpoint resolution. It does not guarantee the full route can ignore blocked space.
- `AllowTraversalTransitions` is a separate opt-in from `AllowUnwalkableEndNode`.
- `FlowFieldPathRequest` caches by destination-style identity, not unique start-to-end identity.
- `VolumeTraversalMode.Water` fails until `VolumeTraversalRules` has a water rule.
- `PathManager.Reset()` also clears transition registry state and volume traversal rules.
- If you request guides directly and forget `ReturnGuide(...)`, results stay checked out.

## 12. AI And Contributor Notes

If you are changing `Trailblazer.Pathing`, read in this order:

1. this file
2. `CHARTS.MD`
3. `PATHMANAGER.MD`
4. the concrete request type you are touching
5. `PathGuideFactory`
6. the matching surveyor
7. the matching tests in `tests/Trailblazer.Tests/Pathing`

High-risk areas:

- cache-key changes
- endpoint snapping rules
- `MaxPathSearchRange` validity rules
- chart initialization and unload invalidation
- transition registry versioning
- raw-volume traversal mode configuration

When docs and code disagree:

- trust the code and tests
- then update the docs in the same change

Good pathing-focused test entry points:

- `tests/Trailblazer.Tests/Pathing/VoxelFinder.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/AStarTransitionFallback.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/FlowFieldTransitionFallback.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/TraversalTransitionRegistry.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/PathingNavigationMap.Tests.cs`

## 13. Where To Read Next

- `OVERVIEW.md` for the whole library architecture
- `PATHGUIDES.MD` for `IGuide`, `IWaypointGuide`, and `PathGuideFactory`
- `TRANSITIONS.MD` for authored chart and volume handoffs
- `VOLUMETRAVERSAL.MD` for raw-volume traversal rules and modes
- `CHARTS.MD` for authored surface-space design
- `PATHMANAGER.MD` for chart lifecycle and utility APIs
- `src/Trailblazer/Pathing` for implementation details
- `src/Trailblazer/Navigation` only if you also need steering or runtime movement
