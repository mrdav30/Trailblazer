# Traversal Transitions Reference

This document explains Trailblazer's authored transition system and the role of `TraversalTransitionRegistry` in chart-to-chart and chart-to-volume routing.

Use this file when you need to understand:

- why chart requests can cross gaps or media boundaries in some worlds and not others
- how to author and register jump, swim, takeoff, and landing handoffs
- how transition topology affects path reuse and deterministic planning

If you need the broader request model first, read `PATHING.MD`.
If you need raw-volume traversal rules, pair this with `VOLUMETRAVERSAL.MD`.

Relevant code:

- `src/Trailblazer/Pathing/Support/Transition/TraversalTransitionRegistry.cs`
- `src/Trailblazer/Pathing/Support/Transition/TraversalTransition.cs`
- `src/Trailblazer/Pathing/Support/Transition/TraversalTransitionAnchor.cs`
- `src/Trailblazer/Pathing/Support/Transition/TraversalTransitionType.cs`
- `src/Trailblazer/Pathing/Support/Transition/TraversalTransitionQuery.cs`
- `src/Trailblazer/Pathing/Hybrid/HybridRoutePlanner.cs`
- `src/Trailblazer/Pathing/Hybrid/HybridPathRequest.cs`

## 1. Why Transitions Matter

Charts provide the main authored surface structure, even though `NavigationChart` can also carry authored volume cells.

That is a good default for:

- floors
- roads
- ramps
- rooftops
- other stable walkable structure

But many real worlds also need explicit handoffs such as:

- jumpable gaps between disconnected chart islands
- shoreline entries into water
- shoreline exits back onto charts
- takeoff or landing links between chart space and open volume

Without authored transitions, chart-backed pathing only knows about chart connectivity. It cannot infer that a gap is jumpable or that a shoreline is a valid swim handoff just because those things look plausible to a human.

That is the job of `TraversalTransitionRegistry`.

## 2. Core Transition Model

### 2.1 `TraversalTransition`

`TraversalTransition` is the authored handoff record.

It contains:

- `Id`: stable identifier in the global registry
- `Type`: semantic handoff kind
- `Source`: where the handoff starts
- `Destination`: where the handoff ends
- `PathCostModifier`: additional authored path cost
- `IsBidirectional`: whether the handoff may be used in both directions

Current built-in transition types:

- `Custom`
- `Jump`
- `SwimEntry`
- `SwimExit`
- `Takeoff`
- `Landing`

Important nuance:

- transition type is descriptive metadata for planning and host meaning
- it does not by itself make a route valid
- the route segments on both sides still have to resolve successfully

### 2.2 `TraversalTransitionAnchor`

Each transition endpoint is a `TraversalTransitionAnchor`.

An anchor is defined by:

- a traversal space
- a canonical `GlobalVoxelIndex`
- an optional world-space point override inside that voxel

You can create anchors from either a resolved voxel index or a world position that resolves to one.

The built-in spaces are:

- `Chart(...)`
- `OpenVolume(...)`
- `WaterVolume(...)`

This is what lets one transition system cover:

- chart -> chart handoffs
- chart -> volume handoffs
- volume -> chart handoffs

Important nuance:

- anchors identify their traversal space directly
- world-position factory overloads resolve to a voxel index immediately
- volume route legs still use `VolumeTraversalMode`, but that is resolved from the anchor's traversal space only when a volume segment is actually planned
- anchors may optionally keep a more precise world-space point override while still resolving through a canonical voxel index

## 3. `TraversalTransitionRegistry`

`TraversalTransitionRegistry` is the global store for authored transitions.

It is responsible for:

- registering transitions by stable id
- tracking whether a registered transition is currently active after precedence is applied
- resolving authored anchor positions to real voxel indices
- exposing transition lookup APIs by id, position, and voxel
- tracking a monotonic `RegistryVersion` for cache invalidation

It is not responsible for:

- verifying chart ownership at registration time
- validating volume traversal rules at registration time
- forcing a route to exist where the surrounding chart or volume segments still fail

### 3.1 Registration Rules

`Register(...)` succeeds only when:

- the transition id is new
- both authored anchor voxel indices resolve to real voxels in the current grid setup

Manual registration also rejects an already-registered manual transition with the same effective semantics.

Important nuance:

- creating an anchor from world position resolves that position to a voxel index immediately
- registration validates that the stored anchor voxel indices still exist in the active grid setup
- if an anchor uses a point override, that override must still resolve inside the same voxel
- generated transitions registered through `PathManager.Register(TraversalBuildResult)` participate in precedence as generated transitions, not manual ones
- it does not verify that a chart anchor actually belongs to a live chart path segment
- it does not verify that a volume anchor's derived traversal mode is currently configured in `VolumeTraversalRules`

That means registration is necessary, but not sufficient, for a handoff to be usable.

### 3.2 Lookup APIs

The main public registry APIs are:

- `Register(...)`
- `IsRegistered(...)`
- `IsActive(...)`
- `TryGet(...)`
- `TryGetResolvedEndpoints(...)`
- `Unregister(...)`
- `GetOutgoingTransitions(...)`
- `GetIncomingTransitions(...)`
- `AllTransitions`

Useful mental model:

- `AllTransitions` is the active snapshot after precedence is applied
- outgoing and incoming queries are active voxel-scoped views
- `TryGet(...)` and `TryGetResolvedEndpoints(...)` can still reach registered inactive transitions by id
- `IsActive(...)` tells you whether planners and transition queries will currently use that transition
- `TryGetResolvedEndpoints(...)` is the bridge from authored anchors to actual voxel indices

### 3.3 Lifetime And Reset

`TraversalTransitionRegistry` is static and global.

Important lifecycle rules:

- `PathManager.Reset()` clears the registry
- `PathManager.UnloadChart(...)` unregisters generated transitions that originated from `TraversalBuildResult` for that chart
- manually registered transitions still remain global until explicit unregister or `PathManager.Reset()`
- topology changes outside normal reset flow can make registered endpoints stale
- if the world grid or authored handoff layout changes, hosts should unregister or rebuild transitions

The registry remarks in code are important here: endpoint voxels are resolved at registration time, not lazily every time a request is planned.

### 3.4 Precedence

When two registered transitions have the same effective semantics:

- manual transitions take precedence over generated transitions
- duplicate manual transitions are ignored and warned rather than replacing the existing manual transition
- lower-precedence generated transitions remain registered but inactive
- removing the winning manual transition can reactivate an equivalent generated transition automatically

Transitions that differ in effective point overrides, type, cost, or bidirectional shape are treated as distinct semantics and may coexist.

## 4. Deterministic Directed Queries

The registry stores authored transitions. Planning uses deterministic directed transition snapshots through `TraversalTransitionQuery`.

That query layer matters because:

- bidirectional transitions are expanded into reversible directed candidates
- planners often want grid-local discovery first
- the candidate list needs deterministic ordering

### 4.1 Bidirectional Expansion

If a transition is authored with `IsBidirectional = true`, the registry still stores one authored transition.

The query layer expands that into:

- the original directed transition
- a reversed directed transition for the opposite direction

Important nuance:

- this expansion happens in query results, not as duplicate registry entries
- the reversed directed transition keeps the same `Id`, `Type`, and `PathCostModifier`

### 4.2 Grid-Scoped Snapshots

The query layer exposes:

- `GetDirectedTransitions()`
- `GetDirectedTransitionsFromSourceGrid(int sourceGridIndex)`
- `GetDirectedTransitionsToDestinationGrid(int destinationGridIndex)`

`HybridRoutePlanner` uses grid-scoped snapshots first so it can search nearby authored options before falling back to full-world transition discovery.

### 4.3 Versioned Cache Refresh

`TraversalTransitionRegistry.RegistryVersion` is part of the transition story.

It is used to:

- invalidate directed-transition query snapshots
- invalidate request cache keys when chart requests opt into traversal transitions

Practical consequence:

- changing transition topology changes transition-aware request identity
- stale cached chart routes should not survive a registry change blindly

## 5. How Transitions Affect Path Planning

Transition-aware pathing is opt-in.

For chart-backed requests:

- `AStarPathRequest.AllowTraversalTransitions`
- `FlowFieldPathRequest.AllowTraversalTransitions`

When that opt-in is enabled, the planning story is:

1. try the normal direct chart route first
2. if that fails, try a single chart -> chart transition
3. if that still fails, try a chart -> volume -> chart transition pair

This is driven by `HybridRoutePlanner`.

Current staged route shapes are:

- chart direct
- chart -> transition -> chart
- chart -> transition -> volume -> transition -> chart

Important nuance:

- a transition does not replace chart or volume routing
- it stitches together multiple valid segments
- if either chart segment or the volume segment cannot be built, the overall staged route fails

## 6. Authoring Patterns

### 6.1 Jump Link Between Two Chart Islands

Use this when two chart regions should remain disconnected in normal voxel connectivity, but a specific authored gap should be traversable.

```csharp
TraversalTransitionRegistry.Register(new TraversalTransition(
    id: "bridge-gap-jump",
    type: TraversalTransitionType.Jump,
    source: TraversalTransitionAnchor.Chart(new Vector3d(10, 0, 4)),
    destination: TraversalTransitionAnchor.Chart(new Vector3d(14, 0, 4)),
    pathCostModifier: 3));
```

### 6.2 Shoreline Entry And Exit

Use this when surface pathing should hand off into constrained water traversal and later back onto a chart.

```csharp
TraversalTransitionRegistry.Register(new TraversalTransition(
    id: "shore-entry",
    type: TraversalTransitionType.SwimEntry,
    source: TraversalTransitionAnchor.Chart(new Vector3d(-1, 0, 0)),
    destination: TraversalTransitionAnchor.WaterVolume(new Vector3d(0, 0, 0))));

TraversalTransitionRegistry.Register(new TraversalTransition(
    id: "shore-exit",
    type: TraversalTransitionType.SwimExit,
    source: TraversalTransitionAnchor.WaterVolume(new Vector3d(4, 0, 0)),
    destination: TraversalTransitionAnchor.Chart(new Vector3d(5, 0, 0))));
```

### 6.3 Takeoff And Landing

Use `OpenVolume(...)` anchors when authored air handoffs should be available between chart-backed traversal and open-volume travel.

## 7. Why Complex Worlds Depend On This

Transitions let you keep the underlying world model honest.

Without them, you often have to choose between two bad options:

- over-connect charts and pretend every plausible handoff is always legal
- keep charts accurate but lose meaningful jump, swim, or landing routes

Transitions give you a third option:

- keep charts truthful
- author the exceptional handoffs explicitly
- let pathing use them only when the caller opted into transition-aware routing

This is especially important in worlds with:

- disconnected ledges
- water crossings
- mixed ground and open-air traversal
- layered traversal spaces that share the same voxel grid but not the same movement rules

## 8. Common Gotchas

- `AllowUnwalkableEndNode` is not the same as `AllowTraversalTransitions`.
- Registration only checks voxel existence and duplicate ids.
- A registered transition can still be unusable if its surrounding chart or volume segments cannot be built.
- Bidirectional behavior is query-time expansion, not two separate registry entries.
- Volume anchors depend on `VolumeTraversalRules` and the raw `VolumeTraversalMode` implied by their anchor space.
- `PathManager.Reset()` clears the registry.
- If world topology changes after registration, rebuild your transitions.

## 9. AI And Contributor Notes

If you are changing transition behavior, inspect these files together:

- `src/Trailblazer/Pathing/Support/Transition/TraversalTransitionRegistry.cs`
- `src/Trailblazer/Pathing/Support/Transition/TraversalTransitionQuery.cs`
- `src/Trailblazer/Pathing/Hybrid/HybridRoutePlanner.cs`
- `src/Trailblazer/Pathing/Support/Guide/PathGuideFactory.cs`

Useful tests:

- `tests/Trailblazer.Tests/Pathing/TraversalTransitionRegistry.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/TraversalTransitionQuery.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/AStarTransitionFallback.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/FlowFieldTransitionFallback.Tests.cs`

High-risk changes include:

- cache-version behavior
- directed-query ordering
- anchor interpretation
- route-shape expansion in `HybridRoutePlanner`

## 10. Where To Read Next

- `PATHING.MD` for request-level transition opt-in
- `VOLUMETRAVERSAL.MD` for raw-volume traversal rules used by volume anchors
- `CHARTS.MD` for the surface-side world model
- `PATHGUIDES.MD` for how transition-aware routes still surface as normal guide types
