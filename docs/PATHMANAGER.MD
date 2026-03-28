# PathManager Reference

This document is the detailed reference for Trailblazer's global chart registry and pathing utility layer.

If you only need the high-level architecture, read `OVERVIEW.md`.
If you want the pathing-first request and guide model, read `PATHING.MD`.
If you need a deeper explanation of what charts are and why they exist, read `CHARTS.MD`.
If you need the tokenized chart-plus-transition builder flow, read `AUTHORING.MD`.
If you need guide caching and guide lifetime details, read the relevant pathing source alongside this file.

The code referenced here lives primarily in:

- `src/Trailblazer/Pathing/PathManager.cs`
- `src/Trailblazer/Pathing/Partition/SolidChartPartition.cs`
- `src/Trailblazer/Pathing/Partition/TraversableVoxel.cs`
- `src/Trailblazer/Pathing/Chart/NavigationChart.cs`

## 1. What PathManager Is

`PathManager` is the global coordination layer for registered navigation charts and voxel-backed pathing utilities.

It is responsible for:

- registering charts by name
- initializing charts into live voxel partitions
- unloading chart ownership from existing partitions
- exposing walkable-neighbor queries
- providing straight-line path viability checks
- computing search-size hints for path requests
- maintaining pooled `SolidChartPartition` instances
- maintaining pooled `VolumeChartPartition` instances
- triggering guide-cache cleanup through `Tick(...)`
- clearing global transition and volume-rule state during `Reset()`

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
3. If you registered with `initializeChart: false`, call `InitializeChart(...)` to attach or reuse `SolidChartPartition` and `VolumeChartPartition` instances on the relevant voxels.
4. Use the chart indirectly through path requests, surveyors, and guide resolution.
5. `UnloadChart(...)` when the chart should stop owning those voxels.

This means a chart only becomes pathable after initialization. `Register(chart)` now initializes by default, but callers can still defer live activation with `initializeChart: false`.

Another important detail:

- multiple charts can share the same underlying runtime partition when they overlap the same authored voxel
- partition ownership is tracked per chart through `SolidChartPartition.ChartOwners` and `VolumeChartPartition.ChartOwners`

That ownership model is why initialization and unload logic are more complex than simple add/remove operations.

## 3. Public Surface

The main entry points are:

- `Register(NavigationChart chart, bool initializeChart = true)`
- `Register(TraversalBuildResult buildResult, bool initializeChart = true)`
- `IsChartRegistered(string name)`
- `TryGetNavigationChart(string name, out NavigationChart chart)`
- `InitializeChart(string chartKey)`
- `InitializeAllCharts()`
- `TryUpdateChartCell(string chartName, int x, int y, int z, NavigationChartCell cell)`
- `TryUpdateChartCell(string chartName, Vector3d worldPosition, NavigationChartCell cell)`
- `ApplyChartUpdates(string chartName, IReadOnlyList<NavigationChartCellUpdate> updates)`
- `UnloadChart(string chartKey)`
- `Reset()`
- `GetMaxSearchSize(Voxel start, Voxel end, out int maxSearchSize)`
- `NeedsPath(Vector3d startPos, Vector3d endPos, Fixed64 unitSize, bool allowUnwalkableEndpoints = false)`

Important public state includes:

- `AllCharts`
- `DefaultMaxPathSearchRange`

Important internal infrastructure includes:

- `PartitionPool`
- `VolumeChartPartitionPool`
- `PartitionSetPool`
- `Tick(int currentFrame)`

## 4. Global State and Ownership

`PathManager` is static and global.

It owns:

- the chart registry `_navigationChartMap`
- the registry lock `_navigationChartMapLock`
- the reusable `SolidChartPartition` pool
- the reusable `VolumeChartPartition` pool
- the pooled partition-set helper used during initialization and unload

That means:

- the manager is process-global for the current simulation
- tests and teardown must clean it explicitly
- chart lifecycle mistakes can leak pathing state into later frames or tests
- transition registry state also resets through `PathManager.Reset()`

## 5. Chart Registration Lifecycle

### 5.1 Register(...)

`PathManager` currently exposes two registration flows.

`Register(NavigationChart chart, bool initializeChart = true)` adds a chart to the global registry by its `Name`.

Behavior:

- returns `false` if a chart with the same name is already registered
- initializes authored partitions by default after registration succeeds
- leaves the chart inert when `initializeChart` is `false`, so callers can stage registration before live activation

`Register(TraversalBuildResult buildResult, bool initializeChart = true)` applies the output of `TraversalAuthoringMap.Build()`.

Behavior:

- returns `false` if chart registration fails
- returns `false` if any generated transition fails to register
- rolls back the chart and any transitions registered in that call when registration fails partway through
- registers generated transitions with generated precedence semantics, so an equivalent manual transition may stay active without causing build-result registration to fail
- initializes the built chart by default after chart and transition registration succeed
- generated transitions are tied to that chart's lifetime and are unregistered automatically when the chart unloads or `PathManager.Reset()` clears global state

### 5.2 IsChartRegistered(...) and TryGetNavigationChart(...)

These are the basic lookup APIs for chart identity and retrieval.

They both operate under the internal reader lock and are safe against concurrent registry access.

### 5.3 AllCharts

`AllCharts` returns a snapshot of registered chart values.

Important detail:

- it copies chart values into a fresh array under the read lock
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

1. rents a pooled `SwiftHashSet<SolidChartPartition>` for all touched partitions
2. creates a temporary set for any already-active chart owners encountered on touched voxels
3. iterates `chart.GetAuthoredCells()`
4. resolves the voxel from `GlobalGridManager`
5. attaches or reuses a `SolidChartPartition` for authored solid traversal
6. attaches or reuses a `VolumeChartPartition` for authored volume traversal
7. records any existing owners for later cache invalidation
8. adds the current chart as an owner
9. rebinds neighbors for every touched path partition
10. marks `chart.IsInitialized = true`
11. invalidates cached guides for any previously owning chart keys collected during the process

### 6.2 Partition Reuse

When an authored voxel already has a compatible runtime partition, initialization does not create a duplicate.

Instead it:

- reuses the existing `SolidChartPartition` or `VolumeChartPartition`
- adds the chart to its `ChartOwners`
- rebinding neighbors later ensures the solid adjacency model stays current

This is what lets overlapping charts coexist on the same voxels for both solid and authored volume data.

### 6.3 Neighbor Binding

Initialization always rebinds neighbors for every touched partition.

This matters because:

- a new chart can change which adjacent voxels now have path partitions
- clearance and directional traversal checks depend on up-to-date neighbor pointers

`SolidChartPartition.BindNeighbors()` builds the cached neighbor array used later by surveyors and clearance checks.

### 6.4 Cache Invalidation on Initialize

If initialization discovers that a reused partition already belonged to some other chart, `PathManager` invalidates cached guides for those prior owners through `PathGuideFactory.InvalidateCacheFor(...)`.

This is a subtle but important correctness rule:

- changing chart ownership on live partitions can change path validity
- cached guide results must not survive that ownership change blindly
- unrelated cached guides remain reusable because invalidation is chart-targeted

### 6.5 Chart Update APIs

`TryUpdateChartCell(...)` and `ApplyChartUpdates(...)` let callers mutate authored chart cells after registration without unloading the whole chart.

The important behavior is:

- single-cell updates can target either chart-local indices or a world-space position
- batch updates are sparse deltas through `NavigationChartCellUpdate`
- inert charts registered with `initializeChart: false` simply update their stored authored data
- initialized charts re-resolve only the touched voxels through the same effective-cell ownership path used by initialize and unload
- solid neighbor rebinding only happens when a touched voxel's live solid presence changes
- cache invalidation stays chart-targeted and only happens when a touched voxel's live winning state changes

Current safety rule:

- if the mutated chart still owns generated transitions from `TraversalBuildResult`, `PathManager` unregisters those generated transitions on the first successful mutation instead of attempting live regeneration

## 7. Unload Lifecycle

### 7.1 UnloadChart(...)

`UnloadChart(...)` removes one chart's ownership from all of its authored voxels.

It returns immediately when:

- `chartKey` is null or empty
- the chart cannot be found
- the chart is not initialized

When it runs, it:

1. invalidates any cached guide results that reference the chart
2. walks all of the chart's authored cells
3. removes solid ownership from any attached `SolidChartPartition`
4. removes authored volume ownership from any attached `VolumeChartPartition`
5. removes a partition from the voxel entirely when it no longer has owners
6. refreshes and queues any still-active `SolidChartPartition` for neighbor rebinding
7. rebinds neighbors on still-active path partitions
8. removes the chart from the registry

Important nuance:

- `UnloadChart(...)` only removes chart-owned partition state
- it unregisters generated transitions that were registered through `TraversalBuildResult` for that chart
- it does not automatically unregister separately registered manual transitions

### 7.2 Shared Partition Behavior

If multiple charts own the same partition:

- unloading one chart removes only that chart's ownership
- the partition stays attached as long as at least one owner remains

This is verified in the existing pathing map tests.

### 7.3 Important Nuance

The current implementation of `UnloadChart(...)` only unloads initialized charts.

That means:

- a registered but never-initialized chart can still be removed cleanly
- unloading clears the chart's initialization flag so the same chart instance can be registered again later if needed

## 8. Clear and Teardown Behavior

### 8.1 Reset()

`Reset()` currently:

- clears the chart registry
- flushes guide caches if pooling is active
- clears chart initialization state on registered chart instances

It will walk live voxels and remove partitions itself.

That means the accurate teardown pattern is just call `Reset()`.

### 8.2 Tick(...)

`Tick()` is an internal bridge from `TrailblazerManager` to guide-cache cleanup.

It currently forwards to:

- `PathGuideFactory.CullExpiredGuides(currentFrame)`

So while `PathManager` does not own guide caches directly, it does participate in their frame-based maintenance.

## 9. Utility Methods

### 9.1 GetMaxSearchSize(...)

`GetMaxSearchSize(...)` computes a search bound for path requests based on the sizes of the start and end grids.

Behavior:

- returns `false` if either grid cannot be resolved
- returns one grid size if both voxels are in the same grid
- otherwise returns the sum of both grid sizes

This is not a geometric distance estimate. It is a grid-size-derived search cap hint.

### 9.2 NeedsPath(...)

`NeedsPath(...)` is the fast direct-travel viability check.

It:

1. line-traces between `startPos` and `endPos` through `GridTracer.TraceLine(...)`
2. walks the voxels touched by that trace
3. requires a path if any voxel lacks a `SolidChartPartition`
4. requires a path if any traversed partition is impassable for the given unit size

Important detail:

- this is a straight-line viability test
- it does not mean a route is impossible if it returns `true`
- it only means a path guide is needed instead of direct travel

### 9.3 `allowUnwalkableEndpoints`

The `allowUnwalkableEndpoints` argument affects the direct-travel check.

In the current implementation:

- the unit-size impassability check is skipped when `allowUnwalkableEndpoints` is true

This is a request-policy input, not a global chart rule.

## 10. Relationship to IVoxelPartition

`PathManager` is tightly coupled to `SolidChartPartition` and `VolumeChartPartition`.

Key partition responsibilities that matter here:

- storing chart owners
- binding cached neighbors
- computing clearance
- enforcing unit-size impassability through `IsImpassable(...)`
- resetting cleanly when returned to the pool

This means `PathManager` is not just a registry. It is the layer that keeps live partition topology coherent for the rest of the pathing system.

## 11. Threading and Determinism Notes

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

## 12. Common Integration Pattern

The usual chart lifecycle is:

```csharp
var chart = NavigationChart.From3D("Arena", data, minBounds, Fixed64.One);

PathManager.Register(chart);

// ... use the chart through requests and guides ...

PathManager.UnloadChart(chart);
```

For multi-chart teardown in tests or world resets, the normal pattern is:

```csharp
PathManager.Reset();
```

## 13. Common Gotchas

### Forgetting deferred registration still needs initialization

`Register(chart)` initializes by default, but `Register(chart, initializeChart: false)` still requires a later `InitializeChart(...)` call before the chart is pathable.

### Forgetting cache invalidation is tied to chart ownership

Initialization and unload can invalidate guide caches because chart ownership changes affect path validity.

### Assuming diagonal neighbors are freely traversable

They are not. The edge-leg check rejects diagonals that would cut through blocked or missing side voxels.

### Treating NeedsPath(...) as a full route solver

It is only a direct-line viability check.

### Ignoring shared ownership

Overlapping charts can legitimately share a partition. Unloading one chart does not necessarily remove that partition from the voxel.

## 14. Testing References

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
