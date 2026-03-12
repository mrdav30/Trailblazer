# PathManager Reference

This document is the detailed reference for Trailblazer's global chart registry and pathing utility layer.

If you only need the high-level architecture, read `OVERVIEW.md`.
If you need guide caching and guide lifetime details, read the relevant pathing source alongside this file.

The code referenced here lives primarily in:

- `src/Trailblazer/Pathing/PathManager.cs`
- `src/Trailblazer/Pathing/Support/PathPartition.cs`
- `src/Trailblazer/Pathing/Support/TraversableVoxel.cs`
- `src/Trailblazer/Pathing/NavigationChart.cs`

## 1. What PathManager Is

`PathManager` is the global coordination layer for registered navigation charts and voxel-backed pathing utilities.

It is responsible for:

- registering charts by name
- initializing charts into live voxel partitions
- unloading chart ownership from existing partitions
- exposing walkable-neighbor queries
- providing straight-line path viability checks
- computing search-size hints for path requests
- maintaining pooled `PathPartition` instances
- triggering guide-cache cleanup through `Tick(...)`

It is not responsible for:

- building the chart data itself
- computing A* or flow-field results
- steering behavior
- movement execution

Those responsibilities belong to `NavigationChart`, the surveyors, `NavSteering`, and the navigation stack.

## 2. Core Design Model

The important design choice is that `PathManager` operates on live voxel partitions, not just on `NavigationChart` data.

The lifecycle is:

1. Build a `NavigationChart`.
2. `Register(...)` it with `PathManager`.
3. `InitializeChart(...)` to attach or reuse `PathPartition` instances on the relevant voxels.
4. Use the chart indirectly through path requests, surveyors, and guide resolution.
5. `UnloadChart(...)` when the chart should stop owning those voxels.

This means chart registration alone is not enough. A chart only becomes pathable after initialization.

Another important detail:

- multiple charts can share the same underlying partition if they overlap the same walkable voxel
- partition ownership is tracked per chart through `PathPartition.ChartOwners`

That ownership model is why initialization and unload logic are more complex than simple add/remove operations.

## 3. Public Surface

The main entry points are:

- `Register(NavigationChart chart)`
- `IsChartRegistered(string name)`
- `TryGetNavigationChart(string name, out NavigationChart chart)`
- `InitializeChart(string chartKey)`
- `InitializeAllCharts()`
- `UnloadChart(string chartKey)`
- `Reset()`
- `GetWalkableNeighbors(GlobalVoxelIndex idx)`
- `WalkableNeighborsOf(Voxel voxel)`
- `GetWalkablePerpendicularNeighbors(GlobalVoxelIndex idx)`
- `WalkablePerpendicularNeighborsOf(Voxel voxel)`
- `GetWalkableDiagonalNeighbors(GlobalVoxelIndex idx)`
- `WalkableDiagonalNeighborsOf(Voxel voxel)`
- `GetMaxSearchSize(Voxel start, Voxel end, out int maxSearchSize)`
- `NeedsPath(Vector3d startPos, Vector3d endPos, Fixed64 unitSize, bool allowUnwalkable = false)`

Important public state includes:

- `AllCharts`
- `DefaultMaxPathSearchRange`

Important internal infrastructure includes:

- `PartitionPool`
- `PartitionSetPool`
- `Tick(int currentFrame)`

## 4. Global State and Ownership

`PathManager` is static and global.

It owns:

- the chart registry `_navigationChartMap`
- the registry lock `_navigationChartMapLock`
- the reusable `PathPartition` pool
- the pooled partition-set helper used during initialization and unload

That means:

- the manager is process-global for the current simulation
- tests and teardown must clean it explicitly
- chart lifecycle mistakes can leak pathing state into later frames or tests

## 5. Chart Registration Lifecycle

### 5.1 Register(...)

`Register(...)` adds a chart to the global registry by its `Name`.

Behavior:

- returns `false` if a chart with the same name is already registered
- does not initialize partitions
- does not touch voxel state

This is only the first step of making a chart available.

### 5.2 IsChartRegistered(...) and TryGetNavigationChart(...)

These are the basic lookup APIs for chart identity and retrieval.

They both operate under the internal reader lock and are safe against concurrent registry access.

### 5.3 AllCharts

`AllCharts` returns a snapshot of registered chart values.

Important detail:

- it materializes `ToArray()` under the read lock
- callers get an enumerable snapshot, not a live view into the dictionary

That is why `InitializeAllCharts()` and `Reset()` can iterate it safely.

## 6. Initialization Lifecycle

### 6.1 InitializeChart(...)

`InitializeChart(...)` is the method that turns chart data into live pathing state.

It returns immediately when:

- `chartKey` is null or empty
- the chart cannot be found
- the chart is already initialized

When it runs, it:

1. rents a pooled `SwiftHashSet<PathPartition>` for all touched partitions
2. creates a temporary queue for any existing chart owners encountered on those voxels
3. iterates `chart.GetWalkablePositions()`
4. resolves the voxel from `GlobalGridManager`
5. attaches or reuses a `PathPartition`
6. records any existing owners for later cache invalidation
7. adds the current chart as an owner
8. rebinds neighbors for every touched partition
9. marks `chart.IsInitialized = true`
10. invalidates cached guides for any previously owning chart keys collected during the process

### 6.2 Partition Reuse

When a walkable voxel already has a `PathPartition`, initialization does not create a duplicate.

Instead it:

- reuses the existing partition
- adds the chart to its `ChartOwners`
- rebinding neighbors later ensures the adjacency model stays current

This is what lets overlapping charts coexist on the same voxels.

### 6.3 Neighbor Binding

Initialization always rebinds neighbors for every touched partition.

This matters because:

- a new chart can change which adjacent voxels now have path partitions
- clearance and directional traversal checks depend on up-to-date neighbor pointers

`PathPartition.BindNeighbors()` builds the cached neighbor array used later by surveyors and clearance checks.

### 6.4 Cache Invalidation on Initialize

If initialization discovers that a reused partition already belonged to some other chart, `PathManager` invalidates cached guides for those prior owners through `PathGuideFactory.InvalidateCacheFor(...)`.

This is a subtle but important correctness rule:

- changing chart ownership on live partitions can change path validity
- cached guide results must not survive that ownership change blindly

## 7. Unload Lifecycle

### 7.1 UnloadChart(...)

`UnloadChart(...)` removes one chart's ownership from all of its walkable voxels.

It returns immediately when:

- `chartKey` is null or empty
- the chart cannot be found
- the chart is not initialized

When it runs, it:

1. invalidates any cached guide results that reference the chart
2. walks all of the chart's walkable positions
3. resolves the attached partition
4. removes the chart as an owner
5. removes the partition from the voxel entirely if no owners remain
6. otherwise refreshes the partition and queues it for neighbor rebinding
7. rebinds neighbors on still-active partitions
8. removes the chart from the registry

### 7.2 Shared Partition Behavior

If multiple charts own the same partition:

- unloading one chart removes only that chart's ownership
- the partition stays attached as long as at least one owner remains

This is verified in the existing pathing map tests.

### 7.3 Important Nuance

The current implementation of `UnloadChart(...)` only unloads initialized charts.

That means:

- a registered but never-initialized chart causes `UnloadChart(...)` to return early
- if you need a total registry reset, use the normal teardown pattern described below

## 8. Clear and Teardown Behavior

### 8.1 Reset()

`Reset()` currently:

- clears the chart registry
- flushes guide caches if pooling is active

It will walk live voxels and remove partitions itself.

That means the accurate teardown pattern is:

1. `Reset()`

### 8.2 Tick(...)

`Tick(int currentFrame)` is an internal bridge from `TrailblazerManager` to guide-cache cleanup.

It currently forwards to:

- `PathGuideFactory.CullExpiredGuides(currentFrame)`

So while `PathManager` does not own guide caches directly, it does participate in their frame-based maintenance.

## 9. Neighbor Discovery APIs

`PathManager` exposes three families of neighbor queries.

### 9.1 All Walkable Neighbors

- `GetWalkableNeighbors(...)`
- `WalkableNeighborsOf(...)`

These return all traversable neighbors found in `SpatialAwareness.AllDirections`.

### 9.2 Orthogonal Neighbors

- `GetWalkablePerpendicularNeighbors(...)`
- `WalkablePerpendicularNeighborsOf(...)`

These restrict traversal to perpendicular directions only.

### 9.3 Diagonal Neighbors

- `GetWalkableDiagonalNeighbors(...)`
- `WalkableDiagonalNeighborsOf(...)`

These return diagonal neighbors, but only when edge-leg validation passes.

## 10. Diagonal Edge Validation

Diagonal traversal is filtered through `HasBlockedEdgeNeighbor(...)`.

For a multi-axis step, it:

1. decomposes the direction into single-axis legs
2. checks each leg neighbor voxel
3. rejects the diagonal if any leg voxel is missing, blocked, or lacks a `PathPartition`

This prevents diagonal corner cutting.

That is one of the core rules that keeps path validity aligned with the rest of the library's traversal model.

## 11. Utility Methods

### 11.1 GetMaxSearchSize(...)

`GetMaxSearchSize(...)` computes a search bound for path requests based on the sizes of the start and end grids.

Behavior:

- returns `false` if either grid cannot be resolved
- returns one grid size if both voxels are in the same grid
- otherwise returns the sum of both grid sizes

This is not a geometric distance estimate. It is a grid-size-derived search cap hint.

### 11.2 NeedsPath(...)

`NeedsPath(...)` is the fast direct-travel viability check.

It:

1. line-traces between `startPos` and `endPos` through `GridTracer.TraceLine(...)`
2. walks the voxels touched by that trace
3. requires a path if any voxel lacks a `PathPartition`
4. requires a path if any traversed partition is impassable for the given unit size

Important detail:

- this is a straight-line viability test
- it does not mean a route is impossible if it returns `true`
- it only means a path guide is needed instead of direct travel

### 11.3 `allowUnwalkable`

The `allowUnwalkable` argument affects the direct-travel check.

In the current implementation:

- the unit-size impassability check is skipped when `allowUnwalkable` is true

This is a request-policy input, not a global chart rule.

## 12. Relationship to PathPartition

`PathManager` is tightly coupled to `PathPartition`.

Key partition responsibilities that matter here:

- storing chart owners
- binding cached neighbors
- computing clearance
- enforcing unit-size impassability through `IsImpassable(...)`
- resetting cleanly when returned to the pool

This means `PathManager` is not just a registry. It is the layer that keeps live partition topology coherent for the rest of the pathing system.

## 13. Threading and Determinism Notes

The chart registry is guarded by `ReaderWriterLockSlim`.

That protects:

- registration lookups
- chart retrieval
- registry clears and removals

However, this does not make the whole pathing stack "fully concurrent" in a broad sense. Live voxel, partition, and guide-cache behavior still needs to be reasoned about carefully as shared mutable state.

From a determinism perspective:

- chart names must be stable
- initialization and unload order can matter because they affect cache invalidation and live partition ownership
- direct-travel checks depend on the current live partition topology, not just source chart data

## 14. Common Integration Pattern

The usual chart lifecycle is:

```csharp
var chart = NavigationChart.From3D("Arena", data, minBounds, Fixed64.One);

PathManager.Register(chart);
PathManager.InitializeChart(chart);

// ... use the chart through requests and guides ...

PathManager.UnloadChart(chart);
```

For multi-chart teardown in tests or world resets, the normal pattern is:

```csharp
PathManager.Reset();
```

## 15. Common Gotchas

### Registering a chart does not make it pathable

You still need `InitializeChart(...)`.

### Forgetting cache invalidation is tied to chart ownership

Initialization and unload can invalidate guide caches because chart ownership changes affect path validity.

### Assuming diagonal neighbors are freely traversable

They are not. The edge-leg check rejects diagonals that would cut through blocked or missing side voxels.

### Treating NeedsPath(...) as a full route solver

It is only a direct-line viability check.

### Ignoring shared ownership

Overlapping charts can legitimately share a partition. Unloading one chart does not necessarily remove that partition from the voxel.

## 16. Testing References

Direct chart lifecycle coverage currently lives in:

- `tests/Trailblazer.Tests/Pathing/PathingNavigationMap.Tests.cs`
- `tests/Trailblazer.Tests/Support/PathingFixture.cs`
- `tests/Trailblazer.Tests/Support/PathTestFactory.cs`

Those tests currently cover:

- registration lookup
- initialization attaching partitions
- unload behavior with shared ownership
- bounds handling
- idempotent initialization

Additional indirect coverage comes from the A* and flow-field surveyor tests, because both depend on live `PathManager` chart state.

If you change initialization, unload, neighbor binding, direct-travel checks, or teardown behavior, update those tests in the same pass.
