# Pathing Reference

This document is the standalone guide for Trailblazer's `Trailblazer.Pathing` namespace.

It is intended for developers integrating Trailblazer pathfinding without the higher-level `Navigation` stack.

If you only need the broad architecture, start with `OVERVIEW.md`.
If you need chart lifecycle details, pair this with `CHARTS.MD` and `PATHMANAGER.MD`.
If you want the dedicated guide and factory reference, read `PATHGUIDES.MD`.
If you need authored handoffs and volume rules, read `TRANSITIONS.MD` and `VOLUMETRAVERSAL.MD`.

Relevant code:

- `src/Trailblazer/Pathing/Search/Support/Request/IPathRequest.cs`
- `src/Trailblazer/Pathing/Search/Support/Request/PathRequest.cs`
- `src/Trailblazer/Pathing/Search/AStar/AStarPathRequest.cs`
- `src/Trailblazer/Pathing/Search/FlowField/FlowFieldPathRequest.cs`
- `src/Trailblazer/Pathing/Search/Volume/VolumePathRequest.cs`
- `src/Trailblazer/Pathing/Search/Hybrid/HybridPathRequest.cs`
- `src/Trailblazer/Pathing/Search/PathGuideFactory.cs`
- `src/Trailblazer/Pathing/Search/AStar/AStarSurveyor.cs`
- `src/Trailblazer/Pathing/Search/FlowField/FlowFieldSurveyor.cs`
- `src/Trailblazer/Pathing/Search/Volume/VolumeSurveyor.cs`
- `src/Trailblazer/Pathing/Search/Support/VoxelFinder/SolidVoxelFinder.cs`
- `src/Trailblazer/Pathing/Search/Support/VoxelFinder/VolumeVoxelFinder.cs`
- `src/Trailblazer/Pathing/Transition/*`

## 1. What Pathing Is

`Trailblazer.Pathing` is the lower-level deterministic routing layer.

It is responsible for:

- chart registration and initialization through `PathManager`
- endpoint resolution from world positions into voxels
- chart-backed A* and flow-field requests
- chart-optional raw-volume requests
- staged transition-aware fallback between chart, authored climb, and volume segments
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

1. Create or attach a `TrailblazerWorldContext` for the `GridWorld`.
2. For chart-backed requests, build one or more `NavigationChart` instances.
3. Register each chart with `context.Pathing.Register(...)`.
4. Call `context.Pathing.InitializeChart(...)` or `InitializeAllCharts()` if initialization was deferred.
5. Optionally configure supplemental `context.VolumeRules` before creating constrained volume requests.
6. Create an `IPathRequest` with a context-bound factory.
7. Ask `context.Guides` for a guide.
8. Consume the guide in your own movement or planning system.
9. Return the guide with `context.Guides.ReturnGuide(...)`.
10. On teardown, dispose or reset the context.

Chart registration alone is not enough. Chart-backed requests depend on live `SolidChartPartition` ownership, and that only exists after initialization.  Full 3D traversal requests also depend on authored or host-defined volume membership, whether that comes from chart cells that create `VolumeChartPartition` instances, `VolumeMediumRules`, or both.

## 3. Core Request Contract

All request types implement `IPathRequest`.

Shared state:

- `Origin`: exact world-space start position supplied by the caller
- `TargetPosition`: exact world-space target position supplied by the caller
- `StartNode`: resolved voxel used as the current origin node
- `EndNode`: resolved voxel used as the destination node
- `UnitSize`: clearance size used during endpoint and traversal validation
- `AllowUnwalkableEndpoints`: endpoint-relaxation policy
- `MaxPathSearchRange`: grid-size-derived search cap used to mark the request valid
- `Context`: explicit world context used for endpoint resolution, mutation, survey expansion, and guide lookup

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
- `RequestCacheKey` is the request hash and is the cache key used by the context-local guide cache.
- The request key does not carry a world id; equivalent keys in different contexts stay isolated
  because each context owns its own caches.
- A request can have world positions stored and still be invalid if endpoints or search range cannot be resolved.

## 4. `PathRequest` Base Behavior

`PathRequest` is the shared base class for chart-backed requests.

It provides:

- endpoint resolution through `SolidVoxelFinder`
- size-aware endpoint revalidation
- search-range calculation through `PathManager.TryGetMaxSearchSize(...)`
- reusable origin and destination mutation without recreating the request object

The chart-backed endpoint rules are:

- direct endpoints must resolve to voxels that belong to a live `SolidChartPartition` or `VolumeChartPartition` with a valid medium
- `AllowUnwalkableEndpoints: true` lets blocked or non-chart endpoints relax to a nearby chart-traversable voxel
- larger `UnitSize` values can also trigger endpoint relaxation even when `AllowUnwalkableEndpoints` is false

That last rule matters. `AllowUnwalkableEndpoints` is not the only reason endpoints can snap. Size validation can also move the request onto a nearby valid voxel.

## 5. Request Families

### 5.1 `AStarPathRequest`

Use `AStarPathRequest` when you want a concrete waypoint trail for one route.

Key configuration:

- `Heuristic`
- `MaxClimbHeight`
- `AllowUnwalkableEndpoints`
- `AllowTraversalTransitions`

When `AllowTraversalTransitions` is enabled, authored climb topology generated from `SC` / `SC!`
tokens participates in the same staged fallback surface as jump, swim, and aerial transitions.

Use it when:

- one agent or one route needs a specific trail
- you want explicit waypoint progression
- path reuse is keyed by both start and end voxels

Cache-key behavior:

- includes both `StartNode` and `EndNode`
- includes `UnitSize`, `AllowUnwalkableEndpoints`, `AllowTraversalTransitions`, `Heuristic`, `MaxClimbHeight`, and `MaxPathSearchRange`
- also includes `TraversalTransitionRegistry.RegistryVersion` when transition fallback is enabled

Example:

```csharp
TrailblazerWorldContext context = TrailblazerManager.DefaultContext;
var request = AStarPathRequest.Create(context, origin, destination, Fixed64.One);
if (context.Guides.RequestGuide(request, out AStarGuide guide))
{
    // Consume guide.ActiveWaypoints or guide.TryGetMovementDirection(...)
    context.Guides.ReturnGuide(guide);
}
```

### 5.2 `FlowFieldPathRequest`

Use `FlowFieldPathRequest` when multiple agents can share one destination-centered field.

Key configuration:

- `MaxClimbHeight`
- `ExtraFloodRange`
- `AllowUnwalkableEndpoints`
- `AllowTraversalTransitions`

As with `AStarPathRequest`, enabling `AllowTraversalTransitions` also allows staged fallback
through authored climb topology when direct chart routing cannot complete the route.

Use it when:

- many units are moving toward the same destination
- you want reuse centered on the destination instead of on a unique start-to-end pair

Cache-key behavior:

- ignores the start voxel on purpose
- includes `EndNode`, `UnitSize`, `AllowUnwalkableEndpoints`, `AllowTraversalTransitions`, `MaxClimbHeight`, `ExtraFloodRange`, and `MaxPathSearchRange`
- includes `TraversalTransitionRegistry.RegistryVersion` when transition fallback is enabled

Important nuance:

- the cached field may exist and still be unusable for the current caller if the caller's `StartNode` is not present in `result.Fields`
- `PathGuideFactory.RequestFlowField(...)` checks that coverage before it returns a usable `FlowFieldGuide`

### 5.3 `VolumePathRequest`

Use `VolumePathRequest` when traversal should run through raw voxel volume rather than through chart partitions.

For the dedicated raw-volume rules reference, read [`VOLUMETRAVERSAL.MD`](VOLUMETRAVERSAL.MD).

Key configuration:

- `Heuristic`
- `AllowUnwalkableEndpoints`
- `Medium`

Current supported media:

- `Gas`
- `Liquid`

Use it when:

- there is no meaningful `NavigationChart` for the space
- movement is fundamentally 3D
- traversal should stay inside an authored or host-defined volume such as liquid

Key behavior differences from chart-backed requests:

- endpoints resolve through `VolumeVoxelFinder`, not `SolidVoxelFinder`
- traversal validates raw voxels directly and prefers authored `VolumeChartPartition` data when present
- constrained modes can be satisfied by authored volume cells, `VolumeMediumRules`, or both

Important nuance:

- `VolumePathRequest.TrySetUnitSize(...)` only updates the stored `UnitSize`
- it does not recompute endpoints or `MaxPathSearchRange`
- if unit size changes should trigger full revalidation, call `UpdateRequest(...)`

Example:

```csharp
var request = VolumePathRequest.Create(
    origin,
    destination,
    Fixed64.One,
    medium: TraversalMedium.Liquid);
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
- chart -> one or more authored climb transitions -> chart
- chart -> transition -> volume -> transition -> chart

Marked liquid-climb shoreline cells such as `LC!` fit inside that existing shape. They do not add a new
planner stage; they make the generated `SwimExit` back onto the chart request climb intent so navigation
can continue with climb or mantle locomotion after the volume leg completes.

Important internal behavior:

- `HybridPathRequest.CreateFromAStar(...)` and `CreateFromFlowField(...)` preserve the caller's original intent
- `RebuildPlan()` calls `HybridRoutePlanner`
- validity depends on both endpoints and a non-null `RoutePlan`
- its hash includes the directed transition ids in the chosen route plan

If you are documenting or modifying public API, treat `HybridPathRequest` as internal infrastructure rather than a user-facing request family.

## 6. Endpoint Resolution Rules

The request object is where world-space intent turns into pathing nodes.

Chart-backed requests use `SolidVoxelFinder`:

- direct voxel first
- nearby chart-traversable neighbor second
- traced fallback or star search only when endpoint relaxation is allowed or size fallback is required

Volume requests use `VolumeVoxelFinder`:

- direct voxel first
- nearby traversable raw-volume neighbor second when endpoint relaxation is allowed or size fallback is required
- traced fallback after that under the same relaxation gate

Practical consequences:

- requests can snap endpoints to nearby valid voxels
- snapped volume endpoints still have to match the requested medium
- `AllowUnwalkableEndpoints` relaxes endpoint resolution only; it does not make the full route ignore walkability
- `AllowUnwalkableEndpoints` also does not imply transition fallback
- exact target points inside the destination voxel are preserved in `TargetPosition`

The tests in `tests/Trailblazer.Tests/Pathing/SolidVoxelFinder.Tests.cs` are the best executable reference for these rules.

## 7. Transitions and Staged Fallback

Chart-backed requests can opt into authored transitions through `AllowTraversalTransitions`.

For the dedicated authored-handoff reference, read [`TRANSITIONS.MD`](TRANSITIONS.MD).

That opt-in means:

- direct A* or flow-field routing is tried first
- if direct chart routing fails, `PathGuideFactory` can build a staged fallback using `HybridPathRequest`
- that staged fallback may include authored climb transitions generated from explicit climb topology
- the original public request type still describes the caller's intent

Important distinctions:

- `AllowUnwalkableEndpoints` is not transition fallback
- transition fallback is chart-request-only
- transition topology changes invalidate cache keys through `TraversalTransitionRegistry.RegistryVersion`

`TraversalTransitionRegistry` is the source of authored handoff data.

It:

- resolves anchors to voxels at registration time
- exposes outgoing and incoming transition queries
- is cleared by `PathManager.Reset()`

For liquid routes, transition fallback and `VolumePathRequest` depend on a valid liquid medium existing, whether that comes from authored chart cells, `VolumeMediumRules`, or both.

## 8. Guide Resolution and Caching

`TrailblazerWorldContext.Guides` is the primary request-to-guide bridge.
`PathGuideFactory` remains as the default-context compatibility facade.

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
- stale entries are culled by `TrailblazerWorldContext.Simulate()` using the context frame count

Important lifecycle rule:

- requesting a guide directly checks a survey result out of the cache
- you should return it with `context.Guides.ReturnGuide(...)`

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
- the space is gas or liquid volume rather than chart-backed solid traversal
- you are integrating pathing without the navigation layer for swim or aerial systems

Choose transition-aware chart requests when:

- movement should stay chart-first
- authored jump, takeoff, landing, or swim handoffs should extend chart routing
- you do not want to expose hybrid routing as a separate public request type

## 10. Integration Patterns

### 10.1 Minimal Chart-Backed Pathing

```csharp
NavigationChart chart = NavigationChart.From3D("Ground", data, minBounds, Fixed64.One);
TrailblazerWorldContext context = TrailblazerManager.DefaultContext;
context.Pathing.Register(chart);

var request = AStarPathRequest.Create(context, origin, destination, Fixed64.One);
if (context.Guides.RequestGuide(request, out AStarGuide guide))
{
    if (guide.TryGetMovementDirection(origin, out Vector3d heading))
    {
        // Use heading in your own system.
    }

    context.Guides.ReturnGuide(guide);
}
```

### 10.2 Shared Destination Flow Field

```csharp
TrailblazerWorldContext context = TrailblazerManager.DefaultContext;
var request = FlowFieldPathRequest.Create(context, origin, destination, Fixed64.One);
request.MaxClimbHeight = Fixed64.One;
request.ExtraFloodRange = 12;

if (context.Guides.RequestGuide(request, out FlowFieldGuide guide))
{
    // Multiple agents can request equivalent destination fields.
    context.Guides.ReturnGuide(guide);
}
```

### 10.3 Transition-Aware Chart Pathing

```csharp
TrailblazerWorldContext context = TrailblazerManager.DefaultContext;
var request = AStarPathRequest.Create(context, origin, destination, Fixed64.One);
request.AllowTraversalTransitions = true;
```

In this mode, transitions are still data owned by `TraversalTransitionRegistry`, not by the request itself.

## 11. Common Gotchas

- `PathManager.Register(...)` does not make a chart pathable. You must initialize it.
- `PathManager.Register(NavigationChart)` does not make a chart pathable. You must initialize it.
- `PathManager.Register(TraversalBuildResult)` initializes the built chart by default after registering its generated transitions.
- `IsValid` depends on resolved endpoints and a positive `MaxPathSearchRange`.
- `AllowUnwalkableEndpoints` relaxes endpoint resolution. It does not guarantee the full route can ignore blocked space.
- `AllowTraversalTransitions` is a separate opt-in from `AllowUnwalkableEndpoints`.
- `FlowFieldPathRequest` caches by destination-style identity, not unique start-to-end identity.
- `TraversalMedium.Liquid` fails until authored liquid volume exists, `VolumeMediumRules` provides liquid membership, or both.
- `TraversalMedium.Gas` fails until authored gas volume exists, `VolumeMediumRules` provides gas membership, or both.
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

- `tests/Trailblazer.Tests/Pathing/SolidVoxelFinder.Tests.cs`
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
- `src/Trailblazer/Main` for `Navigator` and `TrailblazerManager`
- `src/Trailblazer/Navigation` only if you also need steering, turning, or motor flow
