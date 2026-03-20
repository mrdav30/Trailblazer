# Navigation Chart Hardening

This document turns the initial brainstorming into a concrete implementation plan.

The main conclusion after reviewing `docs/CHARTS.MD`, `PathManager`, `NavigationChart`, `PathPartition`, the request factories, and the current tests is:

- charts should stay focused on authored traversal structure
- raw voxel volume should stay the default solution for open air and fully submerged water
- transitions between those two worlds need to become first-class

That means points 1 and 2 should be planned together rather than solved as separate features.

## Original Questions

1. Expand nav chart assignment to allow marking "air" and "water" voxels with a new partition type?
2. Handle guided destinations that are still inside a grid but beyond the current chart, especially when the agent can jump, swim, or fly?

## Current Read On The System

### What charts are today

`NavigationChart` is currently a data-only authored walkability map.

`PathManager.InitializeChart(...)` turns chart cells into live `PathPartition` ownership on `GridForge` voxels.

`AStarPathRequest` and `FlowFieldPathRequest` only work when endpoints resolve onto voxels that already have `PathPartition`.

### What volume traversal is today

`VolumePathRequest` already exists for chart-optional movement through raw voxel volume.

That system currently supports:

- `VolumeTraversalMode.Open` for unrestricted volume travel
- `VolumeTraversalMode.Water` for host-marked water volume through `VolumeTraversalRules`

### Important design signal from `CHARTS.MD`

`CHARTS.MD` explicitly positions charts as a good fit for stable surface traversal and a weaker fit for:

- free-flight space
- fully submerged water volumes
- short-lived or purely dynamic occupancy

That is a strong argument against immediately turning `NavigationChart` into the canonical representation for all mediums.

## Recommended Direction

Treat the hardening work as three distinct concerns:

1. Traversal structure
   Charts and partitions define intentional authored space.
2. Traversal medium
   Ground, water, and open air decide which broad movement model is valid.
3. Traversal transitions
   Jump points, ledge exits, water entry points, flight takeoff/landing zones, and other handoffs connect one model to another.

The recommendation is:

- keep `NavigationChart` surface-first
- keep open air and broad water volume on the raw-volume pathing side
- add richer chart metadata for authored costs and tags
- add explicit transition data instead of encoding transitions as a vague new partition type
- add a hybrid routing layer only after the metadata and transition model are in place

## Design Principles

### 1. Do not overload charts with every traversal problem

If a space is best described as "all clear voxels in this volume are traversable", use volume traversal.

If a space is best described as "these exact authored cells form a stable route or layer", use charts.

### 2. Treat mud/sludge/ice differently from water/air

Mud and sludge are usually path cost or locomotion modifiers, not separate topological mediums.

That means they should probably become chart metadata that feeds path cost or locomotion rules, not brand new partition families.

### 3. Treat transitions as edges, not cells

A jump link or water-entry point is usually not a different voxel identity.

It is a rule that says:

- from here
- this agent can hand off into another traversal mode or destination set

That is better modeled as explicit transition/link data than as "yet another partition type on the voxel."

### 4. Keep existing request types valid

The current `AStar`, `FlowField`, `Aerial`, and `Swim` flows should keep working unchanged while the new system is added behind opt-in APIs.

## Proposed Model

| Concern | Backing model | Notes |
| --- | --- | --- |
| Ground or authored surface traversal | `NavigationChart` + `PathPartition` | This remains the current core chart system. |
| Open air or broad water volume | `VolumePathRequest` + `VolumeTraversalRules` | This already matches how the codebase thinks about chart-optional movement. |
| Terrain flavor and penalties | chart cell metadata | Use for mud, sludge, shallow water penalties, ice bias, etc. |
| Cross-medium handoff | explicit transition/link records | Use for jump arcs, ledge drops, dive points, takeoff or landing zones, and chart-to-volume handoff. |

## Planned Phases

### (✅ DONE) Phase 0: Terminology And Invariants

Before changing code, lock down the vocabulary in docs and comments:

- surface traversal: chart-backed authored path space
- volume traversal: raw voxel traversal constrained by `VolumeTraversalRules`
- modifier: extra cost or locomotion influence without changing ownership model
- transition: an explicit handoff from one traversal model to another

Decisions to lock in early:

- air remains volume-first unless there is a concrete authored flight-lane need
- water remains volume-first for broad bodies of water
- mud/sludge stay modifier-driven unless they truly need unique topology
- jump/platform access is a transition problem, not a new chart family

### (✅ DONE) Phase 1: Harden Chart Payloads Without Breaking Existing Charts

Goal:
Allow charts to carry more than a single `bool` while preserving the current simple `From3D(bool[,,])` flow.

Recommended implementation:

1. Add a new chart cell payload type such as `NavigationChartCell` or `ChartCellData`.
2. Keep `NavigationChart.From3D(bool[,,])` as a compatibility helper that fills default payloads.
3. Add new builder overloads that accept structured cell data.
4. Include at least:
   - ownership or traversable flag
   - path cost modifier
   - optional flags or tags reserved for later transition work
5. Update `PathManager.InitializeChart(...)` so chart initialization can apply payload data to the live partition.

Important caution:

`PathPartition.PathCostModifier` is currently a single integer on the live partition.

If overlapping charts can contribute different semantics, we need an explicit merge rule before implementation:

- sum all costs
- take max cost
- apply a priority chart
- store per-owner contributions and resolve them later

That merge rule should be chosen before introducing layered chart metadata.

### (✅ DONE) Phase 2: Add Explicit Transition Data

Goal:
Handle destinations beyond chart space by giving the system real handoff points instead of relying on nearest-walkable snapping.

Recommended implementation:

1. Introduce a transition type such as `TraversalTransition`, `TraversalPortal`, or `TraversalLink`.
2. Each transition should define:
   - source voxel or world position
   - destination voxel, world position, or traversal mode
   - allowed movement capability such as jump, swim entry, takeoff, or landing
   - optional cost penalty
   - optional one-way or bidirectional behavior
3. Store transitions in a dedicated registry rather than burying them inside `PathPartition` immediately.
4. Allow transitions that connect:
   - chart to chart
   - chart to volume
   - volume to chart

Examples:

- a ledge jump from one ground chart region to another
- a shoreline entry point from ground chart into water volume
- a landing zone from aerial volume onto a chart-backed platform

This phase should not yet try to solve full multi-stage routing. It should only make transitions expressible and queryable.

Current implementation notes:

- `TraversalTransition`, `TraversalTransitionAnchor`, and `TraversalTransitionRegistry` are now in place
- transitions use stable ids and authored source or destination anchors
- registry registration resolves anchors onto the active voxel grid
- registry supports outgoing and incoming queries plus resolved-endpoint lookup for future hybrid routing
- `PathManager.Reset()` now clears transition state alongside other global pathing state

### Phase 3: Hybrid Request Resolution (✅ DONE)

Goal:
Make guided requests succeed when the destination is inside the grid but outside the current chart, as long as the agent has a valid transition path.

Recommended implementation:

1. Add a new higher-level request or planner type.
   Suggested names:
   - `HybridPathRequest`
   - `TraversalRouteRequest`
   - `MultiStagePathRequest`
2. Do not overload `AStarPathRequest` or `FlowFieldPathRequest` with hidden cross-medium behavior.
3. Route planning should work in layers:
   - try current single-mode request first
   - if chart resolution fails, search for a valid transition path
   - build a multi-segment route such as chart -> transition -> volume -> transition -> chart
4. Keep the first implementation narrow:
   - support a single transition hop or a single pair of entry/exit transitions
   - do not attempt arbitrary N-stage graph composition on the first pass

This gives a realistic first milestone for cases like:

- walk to a shoreline, swim through water volume, then exit onto another chart
- walk to a takeoff point, traverse open air volume, then land on a platform chart
- walk to a jump link, cross a gap, continue on another chart

Current implementation notes:

- `HybridPathRequest`, `HybridRoutePlanner`, and `HybridGuide` are now in place
- hybrid planning stays explicit instead of overloading `AStarPathRequest` or `FlowFieldPathRequest`
- the first pass supports direct chart paths, a single chart-to-chart transition hop, and a single chart -> volume -> chart bridge
- `PathGuideFactory` now composes hybrid guides from cached chart and volume segment guides
- steering serialization now records and restores hybrid requests and hybrid guide waypoint progress

### Phase 4: Navigator Integration

Goal:
Expose the new routing power without destabilizing current navigator behavior.

Recommended implementation:

1. Extend `NavigatorPathRequestFactory` to optionally build the hybrid request type.
2. Keep existing guided modes as-is:
   - `AStar`
   - `FlowField`
   - `Aerial`
   - `Swim`
3. Add a new opt-in mode only if needed, for example:
   - `GuidedPathMode.Hybrid`
   - or a separate navigator flag such as `AllowTraversalTransitions`
4. Keep the current deterministic request creation path when hybrid routing is disabled.

This preserves current behavior for callers that want strict control over the requested mode.

### Phase 5: Revisit Authored Air Or Water Charts Only If Still Needed

After the transition system exists, re-evaluate whether authored non-ground charts are still necessary.

Possible outcomes:

- they are unnecessary because raw volume plus transitions covers the real use cases
- authored water lanes are useful for specific deterministic routes
- authored flight lanes are useful for specific corridors

If that later work is still needed, it should likely be introduced as a more explicit traversal-layer concept, not by quietly stretching the current `bool` chart into a generic everything-map.

## Code Areas Likely To Change

### Chart and partition layer

- `src/Trailblazer/Pathing/Support/Chart/NavigationChart.cs`
- `src/Trailblazer/Pathing/PathManager.cs`
- `src/Trailblazer/Pathing/Support/Partition/PathPartition.cs`

### Request and routing layer

- `src/Trailblazer/Pathing/AStar/AStarPathRequest.cs`
- `src/Trailblazer/Pathing/FlowField/FlowFieldPathRequest.cs`
- `src/Trailblazer/Pathing/Volume/VolumePathRequest.cs`
- `src/Trailblazer/Navigation/Support/NavigatorPathRequestFactory.cs`
- `src/Trailblazer/Navigation/Steering/NavSteering.cs`

### Volume and transition rules

- `src/Trailblazer/Pathing/Support/VolumeTraversalRules.cs`
- new transition registry and support types under `src/Trailblazer/Pathing/Support` or a dedicated folder

### Serialization and caching

- `src/Trailblazer/Serialization/PathRequestRecord.cs`
- `src/Trailblazer/Pathing/Support/Guide/PathGuideFactory.cs`

If hybrid requests or transitions are added, cache keys and invalidation rules will need to include those new dependencies.

## Test Plan

The first implementation should add focused coverage for:

1. Chart payload compatibility
   - existing `bool[,,]` charts still behave identically
   - cost metadata initializes and unloads correctly
2. Overlap semantics
   - overlapping charts merge metadata deterministically
   - unload restores remaining ownership and cost state correctly
3. Transition registration
   - links can be added, queried, and removed without leaving shared state dirty
4. Hybrid path resolution
   - chart -> water -> chart
   - chart -> air -> chart
   - chart -> jump -> chart
5. Invalid destination handling
   - destination inside grid but outside chart resolves through transitions when valid
   - same request still fails when the required capability or transition is missing
6. Cache invalidation and serialization
   - route changes invalidate cached results
   - steering serialization restores any new request type safely

## Suggested Implementation Order

If this is done incrementally, the safest order is:

1. Add chart payload support with no routing changes.
2. Add cost modifier tests and overlap merge rules.
3. Add explicit transition types and registry APIs.
4. Add a narrow hybrid request planner for one-hop or entry-exit routes.
5. Integrate navigator opt-in for hybrid routing.
6. Re-evaluate whether authored water or air charts are still needed.

## Recommended First Milestone

If we want the smallest useful first slice, it should be:

1. structured chart cell metadata
2. explicit chart-to-volume transitions
3. a single hybrid route case: ground chart -> water volume -> ground chart

That milestone directly tests the architecture without forcing a broad refactor of every pathing mode at once.

## Working Recommendation

Do not start by adding a new generic partition type for air and water.

Start by:

- making chart cells richer
- making transitions explicit
- adding hybrid routing on top of the existing chart and volume systems

That keeps the design aligned with `CHARTS.MD`, preserves the current deterministic split between chart-backed and raw-volume traversal, and gives the system a clear way to handle destinations beyond chart space.
