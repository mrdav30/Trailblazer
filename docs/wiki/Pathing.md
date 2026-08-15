# Pathing Reference

This document is the standalone guide for Trailblazer's `Trailblazer.Pathing`
namespace.

It is intended for developers integrating Trailblazer pathfinding without the
higher-level `Navigation` stack.

If you only need the broad architecture, start with `Overview.md`. If you need
chart lifecycle details, pair this with `NavigationCharts.md` and
`PathManager.md`. If you want the dedicated guide and factory reference, read
`PathGuides.md`. If you need authored handoffs and volume rules, read
`Transitions.md` and `VolumeTraversal.md`.

Relevant code:

- `src/Trailblazer/Pathing/Query/PathQuery.cs`
- `src/Trailblazer/Pathing/Search/AStar/NavigationGuideLease.cs`
- `src/Trailblazer/Pathing/Search/Guide/TrailblazerGuideService.cs`
- `src/Trailblazer/Pathing/Search/FlowField/FlowFieldPathRequest.cs`
- `src/Trailblazer/Pathing/Search/Volume/VolumePathRequest.cs`
- `src/Trailblazer/Pathing/Search/Hybrid/HybridPathRequest.cs`
- `src/Trailblazer/Pathing/Search/Guide/PathGuideFactory.cs`
- `src/Trailblazer/Pathing/Search/FlowField/FlowFieldSurveyor.cs`
- `src/Trailblazer/Pathing/Search/Volume/VolumeSurveyor.cs`
- `src/Trailblazer/Pathing/Search/VoxelResolution/SolidVoxelFinder.cs`
- `src/Trailblazer/Pathing/Search/VoxelResolution/VolumeVoxelFinder.cs`
- `src/Trailblazer/Pathing/Transition/*`

## 1. What Pathing Is

`Trailblazer.Pathing` is the lower-level deterministic routing layer.

It is responsible for:

- chart registration and initialization through `PathManager`
- endpoint resolution from world positions into voxels
- graph-backed surface A* queries and chart-backed flow-field requests
- chart-optional raw-volume requests
- staged transition-aware fallback between chart, authored climb, and volume
  segments
- graph payload leases plus remaining reusable flow/volume survey caching

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
2. For surface A*, publish navigation maps and a matching navigation-area policy.
3. For remaining flow/volume requests, register and initialize the required
   `NavigationChart` state.
4. Optionally configure supplemental `context.VolumeRules` before creating
   constrained volume requests.
5. Create a complete `PathQuery` or a remaining flow/volume request.
6. Ask `context.Guides` for a guide.
7. Consume and dispose the graph lease, or return a remaining guide with
   `context.Guides.ReturnGuide(...)`.
8. On teardown, dispose or reset the context.

Chart registration alone is not enough. Chart-backed requests depend on live
`SolidChartPartition` ownership, and that only exists after initialization. Full
3D traversal requests also depend on authored or host-defined volume membership,
whether that comes from chart cells that create `VolumeChartPartition`
instances, `VolumeMediumRules`, or both.

## 3. Core Query Contract

Surface A* uses immutable `PathQuery` intent. It contains:

- exact start and end `NavigationEndpoint` values, including optional `MapId`
  filters and strict/nearest resolution policy
- one exact `NavigationAgentProfile` and its authoritative
  `KinematicBodyShape`
- a versioned `NavigationAreaPolicyKey`
- surface `TraversalIntent` and `PathAlgorithm.AStar`
- one finite `NavigationWorkBudget`
- `AllowTransitions`, which must be `false` for the current surface service

The remaining flow/volume paths still implement `IPathRequest`.

Their shared state includes:

- `Origin`: exact world-space start position supplied by the caller
- `TargetPosition`: exact world-space target position supplied by the caller
- `StartNode`: resolved voxel used as the current origin node
- `EndNode`: resolved voxel used as the destination node
- `UnitSize`: clearance size used during endpoint and traversal validation
- `AllowUnwalkableEndpoints`: endpoint-relaxation policy
- `MaxPathSearchRange`: grid-size-derived search cap used to mark the request
  valid
- `Context`: explicit world context used for endpoint resolution, mutation,
  survey expansion, and guide lookup

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
- `StartNode` and `EndNode` are the resolved pathing hooks into the rest of the
  system.
- `RequestCacheKey` is an exact immutable request identity used by the context-local
  guide cache. Its hash code only selects a bucket; equality still compares the
  complete key.
- Endpoint identities carry GridForge world id, world generation, grid id, grid
  generation, and voxel index. Grid rebuilds and id reuse therefore cannot
  alias stale cached routes.
- Volume and hybrid keys include their behavior-affecting request options.
  Hybrid transition ids remain ordered and ordinal, and hybrid keys retain the
  exact endpoint positions embedded in staged segment requests. Flow-field keys
  remain destination-centric so agents can share a compatible field.
- A request can have world positions stored and still be invalid if endpoints or
  search range cannot be resolved.

## 4. Remaining `PathRequest` Base Behavior

`PathRequest` is the shared base class for the remaining chart-backed flow
request.

It provides:

- endpoint resolution through `SolidVoxelFinder`
- size-aware endpoint revalidation
- search-range calculation through `PathManager.TryGetMaxSearchSize(...)`
- reusable origin and destination mutation without recreating the request object

The chart-backed endpoint rules are:

- direct endpoints must resolve to voxels that belong to a live
  `SolidChartPartition` or `VolumeChartPartition` with a valid medium
- `AllowUnwalkableEndpoints: true` lets blocked or non-chart endpoints relax to
  a nearby chart-traversable voxel
- larger `UnitSize` values can also trigger endpoint relaxation even when
  `AllowUnwalkableEndpoints` is false

That last rule matters. `AllowUnwalkableEndpoints` is not the only reason
endpoints can snap. Size validation can also move the request onto a nearby
valid voxel.

## 5. Request Families

### 5.1 Surface `PathQuery`

Use `PathQuery` with `PathAlgorithm.AStar` when one surface route needs an exact
waypoint trail. Callers supply the complete profile, policy revision, endpoint
rules, traversal intent, and finite work budget; the search selects its own
certified fixed-point heuristic.

```csharp
NavigationGuideStatus status = context.Guides.RequestGuide(
    query,
    out NavigationGuideLease? lease);

if (status == NavigationGuideStatus.Success && lease != null)
{
    using (lease)
    {
        lease.TryGetCurrentWaypoint(out NavigationCellAddress address, out Vector3d footWaypoint);
    }
}
```

The lease is guide-local mutable cursor state over immutable cached payload
data. It validates graph dependencies on acquisition, sampling, and advancement.
There is no public A* surveyor, request class, guide pool, caller-selected
heuristic, or chart reachability preflight.

### 5.2 `FlowFieldPathRequest`

Use `FlowFieldPathRequest` when multiple agents can share one
destination-centered field.

Key configuration:

- `MaxClimbHeight`
- `ExtraFloodRange`
- `AllowUnwalkableEndpoints`
- `AllowTraversalTransitions`

Enabling `AllowTraversalTransitions` allows staged fallback through authored
climb topology when direct flow routing cannot complete the route.

Use it when:

- many units are moving toward the same destination
- you want reuse centered on the destination instead of on a unique start-to-end
  pair

Cache-key behavior:

- ignores the start voxel on purpose
- includes `EndNode`, `UnitSize`, `AllowUnwalkableEndpoints`,
  `AllowTraversalTransitions`, `MaxClimbHeight`, `ExtraFloodRange`, and
  `MaxPathSearchRange`
- includes `TraversalTransitionRegistry.RegistryVersion` when transition
  fallback is enabled

Important nuance:

- the cached field may exist and still be unusable for the current caller if the
  caller's `StartNode` is not present in `result.Fields`
- `PathGuideFactory.RequestFlowField(...)` checks that coverage before it
  returns a usable `FlowFieldGuide`

### 5.3 `VolumePathRequest`

Use `VolumePathRequest` when traversal should run through raw voxel volume
rather than through chart partitions.

For the dedicated raw-volume rules reference, read
[`VolumeTraversal.md`](VolumeTraversal.md).

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
- traversal validates raw voxels directly and prefers authored
  `VolumeChartPartition` data when present
- constrained modes can be satisfied by authored volume cells,
  `VolumeMediumRules`, or both

Important nuance:

- `VolumePathRequest.TrySetUnitSize(...)` only updates the stored `UnitSize`
- it does not recompute endpoints or `MaxPathSearchRange`
- if unit size changes should trigger full revalidation, call
  `UpdateRequest(...)`

Example:

```csharp
var request = VolumePathRequest.Create(
    context,
    origin,
    destination,
    Fixed64.One,
    medium: TraversalMedium.Liquid);
```

### 5.4 `HybridPathRequest`

`HybridPathRequest` is the internal staged-routing adapter.

It is not a public surface-A* entry point. It remains only for the unported
FlowField transition fallback and its volume segments.

Current planned route shapes are:

- chart direct
- chart -> transition -> chart
- chart -> one or more authored climb transitions -> chart
- chart -> transition -> volume -> transition -> chart

Marked liquid-climb shoreline cells such as `LC!` fit inside that existing
shape. They do not add a new planner stage; they make the generated `SwimExit`
back onto the chart request climb intent so navigation can continue with climb
or mantle locomotion after the volume leg completes.

Important internal behavior:

- `HybridPathRequest.CreateFromFlowField(...)` preserves the caller's FlowField
  intent
- `RebuildPlan()` calls `HybridRoutePlanner`
- validity depends on both endpoints and a non-null `RoutePlan`
- its hash includes the directed transition ids in the chosen route plan

If you are documenting or modifying public API, treat `HybridPathRequest` as
internal infrastructure rather than a user-facing request family.

## 6. Endpoint Resolution Rules

Query admission is where world-space intent turns into graph nodes.

Surface `NavigationEndpoint` values support strict or bounded nearest-navigable
resolution over mapped instances. An explicit `MapId` filters candidates before
distance ranking, and the finite query budget bounds every lookup/candidate
probe.

Remaining chart-backed FlowField requests use `SolidVoxelFinder`:

- direct voxel first
- nearby chart-traversable neighbor second
- traced fallback or star search only when endpoint relaxation is allowed or
  size fallback is required

Volume requests use `VolumeVoxelFinder`:

- direct voxel first
- nearby traversable raw-volume neighbor second when endpoint relaxation is
  allowed or size fallback is required
- traced fallback after that under the same relaxation gate

Practical consequences:

- surface queries resolve only according to each endpoint's explicit policy
- remaining requests can snap endpoints to nearby valid voxels
- snapped volume endpoints still have to match the requested medium
- `AllowUnwalkableEndpoints` relaxes endpoint resolution only; it does not make
  the full route ignore walkability
- `AllowUnwalkableEndpoints` also does not imply transition fallback
- exact target points inside the destination voxel are preserved in
  `TargetPosition`

The tests in
`tests/Trailblazer.Tests/Pathing/Search/VoxelResolution/SolidVoxelFinder.Tests.cs`
are the best executable reference for these rules.

## 7. Transitions and Staged Fallback

The remaining chart-backed FlowField request can opt into authored transitions
through `AllowTraversalTransitions`.

For the dedicated authored-handoff reference, read
[`Transitions.md`](Transitions.md).

That opt-in means:

- direct flow-field routing is tried first
- if direct chart routing fails, `PathGuideFactory` can build a staged fallback
  using `HybridPathRequest`
- that staged fallback may include authored climb transitions generated from
  explicit climb topology
- the original FlowField request still describes the caller's intent

Surface `PathQuery` currently returns `Unsupported` when transitions are
enabled; it never falls back through this legacy staged route.

Important distinctions:

- `AllowUnwalkableEndpoints` is not transition fallback
- transition fallback is chart-request-only
- transition topology changes invalidate cache keys through
  `TraversalTransitionRegistry.RegistryVersion`

`TraversalTransitionRegistry` is the source of authored handoff data.

It:

- resolves anchors to voxels at registration time
- exposes outgoing and incoming transition queries
- is cleared by `context.Pathing.Reset()`

For liquid routes, transition fallback and `VolumePathRequest` depend on a valid
liquid medium existing, whether that comes from authored chart cells,
`VolumeMediumRules`, or both.

## 8. Guide Resolution and Caching

`TrailblazerWorldContext.Guides` is the request-to-guide bridge.

For the dedicated guide-layer contract and lifecycle reference, read
[`PathGuides.md`](PathGuides.md).

Routing by type:

- surface `PathQuery` -> `NavigationGuideStatus` plus
  `NavigationGuideLease` on success
- `FlowFieldPathRequest` -> `FlowFieldGuide`
- `VolumePathRequest` -> `VolumeGuide`
- `HybridPathRequest` -> internal FlowField staged route plan

Cache behavior:

- graph surface payloads use exact dependency-stamped cache identity owned by
  graph A* admission
- flow-field and volume survey results plus FlowField hybrid route plans retain
  their own `ReusableSurveyResultCache<T>` until their owning cutovers
- the cache key is the exact `request.RequestCacheKey`
- cached results are reused until invalidated or evicted
- stale entries are culled by `TrailblazerWorldContext.Simulate()` using the
  context frame count

Important lifecycle rule:

- dispose `NavigationGuideLease` directly
- requesting a guide directly checks a survey result out of the cache
- you should return it with `context.Guides.ReturnGuide(...)`

Invalidation sources:

- `PathManager.InitializeChart(...)` when overlapping ownership changes live
  partitions
- `PathManager.UnloadChart(...)`
- `context.Pathing.Reset()`
- transition-topology changes when the request key includes
  `TraversalTransitionRegistry.RegistryVersion`

## 9. Choosing The Right Request

Choose a surface `PathQuery` when:

- you need a concrete waypoint chain
- route identity should include both origin and destination
- route legality depends on an exact agent profile, area policy, and bounded
  waypoint-style graph search

Choose `FlowFieldPathRequest` when:

- many agents share one destination
- you want destination-centric reuse
- the caller can tolerate that field coverage is checked at guide-request time

Choose `VolumePathRequest` when:

- movement should not depend on chart ownership
- the space is gas or liquid volume rather than chart-backed solid traversal
- you are integrating pathing without the navigation layer for swim or aerial
  systems

Choose transition-aware chart requests when:

- movement should stay chart-first
- authored jump, takeoff, landing, or swim handoffs should extend chart routing
- you do not want to expose hybrid routing as a separate public request type

## 10. Integration Patterns

### 10.1 Minimal Graph Surface Pathing

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

### 10.2 Shared Destination Flow Field

```csharp
var request = FlowFieldPathRequest.Create(context, origin, destination, Fixed64.One);
request.MaxClimbHeight = Fixed64.One;
request.ExtraFloodRange = 12;

if (context.Guides.RequestGuide(request, out FlowFieldGuide guide))
{
    // Multiple agents can request equivalent destination fields.
    context.Guides.ReturnGuide(guide);
}
```

### 10.3 Transition-Aware FlowField Pathing

```csharp
var request = FlowFieldPathRequest.Create(context, origin, destination, Fixed64.One);
request.AllowTraversalTransitions = true;
```

In this mode, transitions are still data owned by `context.Transitions`, not by
the request itself.

## 11. Common Gotchas

- `context.Pathing.Register(chart)` initializes by default. Only deferred
  registration with `initializeChart: false` requires a later
  `InitializeChart(...)` call.
- `context.Pathing.Register(buildResult)` initializes the built chart by default
  after registering its generated transitions.
- `IsValid` depends on resolved endpoints and a positive `MaxPathSearchRange`.
- `AllowUnwalkableEndpoints` relaxes endpoint resolution. It does not guarantee
  the full route can ignore blocked space.
- `AllowTraversalTransitions` is a separate opt-in from
  `AllowUnwalkableEndpoints`.
- `FlowFieldPathRequest` caches by destination-style identity, not unique
  start-to-end identity.
- `TraversalMedium.Liquid` fails until authored liquid volume exists,
  `VolumeMediumRules` provides liquid membership, or both.
- `TraversalMedium.Gas` fails until authored gas volume exists,
  `VolumeMediumRules` provides gas membership, or both.
- `context.Pathing.Reset()` also clears transition registry state and volume
  traversal rules.
- Dispose graph leases; return remaining flow/volume guides through
  `ReturnGuide(...)`.

## 12. AI And Contributor Notes

If you are changing `Trailblazer.Pathing`, read in this order:

1. this file
2. `NavigationCharts.md`
3. `PathManager.md`
4. `PathQuery` and graph admission for surface A*, or the concrete remaining
   flow/volume request
5. `TrailblazerGuideService`
6. the matching search/provider
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

- `tests/Trailblazer.Tests/Pathing/Search/VoxelResolution/SolidVoxelFinder.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/Graph/TrailblazerGuideServiceTests.cs`
- `tests/Trailblazer.Tests/Pathing/Search/FlowField/FlowFieldTransitionFallback.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/Transition/Registry/TraversalTransitionRegistry.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/Manager/PathingNavigationMap.Tests.cs`

## 13. Where To Read Next

- `Overview.md` for the whole library architecture
- `PathGuides.md` for graph leases and remaining guide families
- `Transitions.md` for authored chart and volume handoffs
- `VolumeTraversal.md` for raw-volume traversal rules and modes
- `NavigationCharts.md` for authored surface-space design
- `PathManager.md` for chart lifecycle and utility APIs
- `src/Trailblazer/Pathing` for implementation details
- `src/Trailblazer/Runtime` for `TrailblazerWorldContext`, clock state, and
  lifecycle hooks
- `src/Trailblazer/Navigation/Navigator` for `Navigator` and guided request
  construction
- `src/Trailblazer/Navigation` only if you also need steering, turning, or motor
  flow
