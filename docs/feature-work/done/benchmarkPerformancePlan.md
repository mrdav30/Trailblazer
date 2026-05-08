# Benchmark Performance Plan

## Purpose

This document records the phased remediation work identified from the first full benchmark run
across all six benchmark classes (`a-star-path-request`, `flow-field-path-request`, `guide-cache`,
`nav-steering`, `transition-fallback`, `volume-path-request`).

The benchmarks were run with `--job short` (3 warmup / 3 actual iterations, 1 launch) on an Intel
Core i7-9700K, .NET 8.0.26, X64 RyuJIT. Artifacts are committed under
`tests/Trailblazer.Benchmarks/BenchmarkDotNet.Artifacts/results/`.

Phases are ordered by severity and implementation dependency, not estimated effort.

---

## Severity Reference

| Symbol | Meaning |
|--------|---------|
| 🔴 | Critical — a single occurrence can cause a game-frame hitch |
| 🟠 | High — degrades steady-state throughput under normal usage |
| 🟡 | Medium — worth fixing before 1.0; acceptable risk to defer past alpha |
| 🔵 | Low — informational or designer-facing; fix opportunistically |

---

## Phase 1 — Fix Cache Infrastructure Allocations

**Severity:** 🟠 High  
**Affected benchmarks:** `AStarCacheMiss_OverCapacity_Eviction` (128 µs, 83.6 KB),
`InvalidateCacheFor_NoMatchingChart` (7.2 µs with no match)  
**Primary files:** `src/Trailblazer/Pathing/Search/Support/Survey/ReusableSurveyResultCache.cs`,
`src/Trailblazer/Pathing/Search/PathGuideFactory.cs`

### Problem 1a — LINQ in the LRU eviction path

`ReusableSurveyResultCache<T>.TryGetOrCreate` uses:

```csharp
T evictCandidate = _cache.OrderBy(g => g.Value.LastUsedFrame)
    .FirstOrDefault(g => !g.Value.IsInUse).Value;
```

This allocates an `IOrderedEnumerable<KeyValuePair<int,T>>` every time the cache reaches capacity.
On a 128-entry cache, the `OrderBy` creates a sort buffer proportional to `MaxCacheSize`. This is
the root cause of the 83.6 KB allocation on the eviction path — approximately **129x** more than
a below-capacity miss.

**Fix:** Replace with a single-pass O(n) min-scan:

```csharp
T? evictCandidate = null;
foreach (KeyValuePair<int, T> kvp in _cache)
{
    if (kvp.Value.IsInUse) continue;
    if (evictCandidate == null || kvp.Value.LastUsedFrame < evictCandidate.LastUsedFrame)
        evictCandidate = kvp.Value;
}
```

No heap allocation. Same O(n) complexity, no LINQ dependency.

### Problem 1b — O(n) full-cache scan on every chart invalidation

`PathGuideFactory.InvalidateCacheFor(chartKey)` calls `InvalidateWhere` on all three caches. Each
`InvalidateWhere` scans every entry in the cache and calls `UsesChart` per entry. For 128 entries
across 3 caches, this is 384 comparisons even when no entry uses the given chart.

The `NoMatchingChart` benchmark measured **7.2 µs** with nothing to evict. On a busy host that
invalidates charts every time a dynamic obstacle changes, this cost repeats for every chart-owning
event.

**Fix:** Add a reverse lookup index to `ReusableSurveyResultCache<T>`:

```csharp
// chart key → cache keys that reference it
private readonly SwiftDictionary<string, SwiftList<int>> _chartIndex = new();
```

Populate the index in `TryGetOrCreate` when inserting a new result, and remove entries in
`Return` and `InvalidateAll`. The `InvalidateWhere(UsesChart)` call in `PathGuideFactory` can
be replaced with a direct O(k) lookup where k is the number of entries that actually reference
the chart.

### Deliverables

- Remove LINQ import from `ReusableSurveyResultCache.cs` (replaced by manual scan).
- Add `_chartIndex` to `ReusableSurveyResultCache<T>` and keep it consistent through all mutation
  paths: `TryGetOrCreate`, `Return`, `EvictStaleEntries`, `InvalidateAll`, `InvalidateWhere`.
- Add focused tests that assert: (a) eviction chooses the entry with the lowest `LastUsedFrame`
  and allocates near zero bytes; (b) `InvalidateWhere` on a miss across a full cache performs
  fewer dictionary operations than the old O(n) scan.
- Run `guide-cache --job short` after the change to confirm the eviction cost drops below
  `AStarCacheMiss_BelowCapacity` plus a small constant.

### Phase 1 implementation notes — 2026-05-05

Implemented:

- `ReusableSurveyResultCache<T>` now uses a single-pass LRU scan instead of LINQ `OrderBy`.
- Cache entries are indexed by chart key through `_chartIndex`, and `PathGuideFactory.InvalidateCacheFor`
  now uses direct chart-key invalidation instead of predicate scans.
- The chart index is maintained through insertion, return-with-dispose, stale eviction,
  predicate invalidation, chart invalidation, and full invalidation.
- Added focused cache tests for allocation-bounded over-capacity eviction, LRU selection after
  refreshing an older entry, no-match chart invalidation, and multi-chart index cleanup.

Benchmark corrections made while validating:

- `AStarCacheMiss_OverCapacity_Eviction` previously measured the cost of filling 128 cold cache
  entries plus the eviction request. It now seeds the cache in `IterationSetup` and measures only
  the 129th request.
- `InvalidateCacheFor_*` previously seeded one A* entry. It now seeds the A* cache to capacity so
  the no-match path validates the reverse-index behavior against a full cache.

Local verification notes:

- The local machine only has .NET 10 installed, so tests and benchmarks were run with
  `DOTNET_ROLL_FORWARD=Major`. Re-run on a .NET 8 runtime before using these numbers as release
  baselines.
- Corrected `AStarCacheMiss_OverCapacity_Eviction` short run: ~16.2 µs, 648 B allocated. This
  is below the same-run `AStarCacheMiss_BelowCapacity` result (~25.6 µs, 648 B allocated).
- Full A* cache no-match invalidation short run: ~6.7 µs, zero allocated.
- `FlowFieldCacheMiss_BelowCapacity` still reproduces the phase 2 anomaly locally at ~47 ms and
  ~902,944 B allocated under .NET 10 roll-forward.

---

## Phase 2 — Investigate the Flow-Field Cold Miss Anomaly

**Severity:** 🔴 Critical  
**Affected benchmark:** `FlowFieldCacheMiss_BelowCapacity` (34.7 ms, 902,848 B on a 32×32 grid)  
**Contrast:** `ColdGuide_OpenPlane64` in `FlowFieldPathRequestBenchmarks` measured ~100 µs on a
64×64 grid — a 4× larger search space yet ~347× faster.  
**Primary files:** `src/Trailblazer/Pathing/Search/FlowField/FlowFieldSurveyor.cs`,
`src/Trailblazer/Pathing/Search/FlowField/FlowFieldSurveyResult.cs`

### What is known

`FlowFieldSurveyor.FindPath` has two phases:

1. **Flood phase** (`FloodPath`): iterates the min-heap until the start voxel is reached or the
   search range is exhausted. The heap is shared state on `FlowFieldSurveyor.Shared` and protected
   by `SurveyorLock.GlobalLock`.
2. **Flow generation phase** (`GenerateFlowFields`): iterates all closed heap entries and
   constructs a fresh `SwiftDictionary<WorldVoxelIndex, FlowField>` sized to `_heap.TrackedCount`.
   This dictionary is the primary allocation source.

The `FlowFieldSurveyResult.Create` path also calls `_chartKeys.ToArray()` which allocates a
`string[]`. Neither of these should produce 902 KB on a 32×32 grid.

### What is unknown and must be measured

The 34.7 ms anomaly is **347× slower than a 4× larger grid** in the other benchmark class. This
disproportion strongly suggests the two benchmarks are not measuring equivalent conditions. The
likely candidates are:

1. **First-call initialization cost.** `FlowFieldSurveyor.Shared` uses
   `Lazy<FlowFieldSurveyor>(LazyThreadSafetyMode.ExecutionAndPublication)`. If the lazy
   initializer runs during the first cold-miss iteration in the benchmark process, and if the
   global grid or chart state requires initialization at that point, the measured time could
   include one-time setup cost that was already paid in the `FlowFieldPathRequestBenchmarks`
   global setup.
2. **Different `MaxPathSearchRange` or voxel density.** The cache benchmark and the path-request
   benchmark may construct requests with different effective search spaces even if the nominal
   grid size is smaller in the cache benchmark.
3. **`SurveyorLock.GlobalLock` contention with `ReusableSurveyResultCache._lock`.** The FF
   surveyor is called from inside `TryGetOrCreate` while the cache holds an
   `UpgradeableReadLock`. If any other lock-acquiring path is running in the benchmark process
   (e.g., from global setup residue), a priority-inversion or wait condition could inflate the
   first call.
4. **Genuine surveyor cost difference.** The path-request benchmark uses a global setup that
   registers and warms the chart before the first benchmark invocation. If the cache benchmark's
   `BenchmarkPathFixture` does not perform the same pre-warmup, the FF surveyor may be computing
   voxel neighbor bindings or chart initialization inline.

### Investigation steps

1. Add `[IterationSetup]`-guarded timing instrumentation (via `Stopwatch`) inside
   `FlowFieldSurveyor.FindPath` to split flood-phase and generation-phase costs independently.
   Compare across both benchmark classes.
2. Verify that `BenchmarkChartFactory.GridConfigForSquare(32)` and the `ColdGuide_OpenPlane64`
   fixture produce charts with equivalent pre-initialized neighbor state at the point the first
   `FindPath` call is issued.
3. If the anomaly is confirmed to be real (i.e., the FF surveyor genuinely takes 34 ms on a
   32×32 grid under real conditions), profile `GenerateFlowFields` for heap-iteration cost and
   dictionary sizing. The `new SwiftDictionary<WorldVoxelIndex, FlowField>(_heap.TrackedCount)`
   pre-allocation should be inexpensive, but if `_heap.TrackedCount` is unexpectedly large the
   generation pass is O(n × neighbor-scan).

### Remediation target (post-investigation)

Once the root cause is confirmed, the fix must address whichever of these applies:

- If the anomaly is a benchmark artifact (warm-up gap), ensure both benchmark classes use
  equivalent chart pre-initialization in `GlobalSetup`.
- If the FF surveyor genuinely allocates 902 KB per cold run: pool the output
  `SwiftDictionary<WorldVoxelIndex, FlowField>` by retaining it on the `FlowFieldSurveyResult`
  across `Reset()`/reuse cycles rather than constructing a fresh one every call.
- Remove the `_chartKeys.ToArray()` allocation by storing chart keys in a pre-allocated
  fixed-size buffer or using a `SwiftList<string>` that can be reused per-call.
- Consider whether `SurveyorLock.GlobalLock` scope can be narrowed to exclude the
  `GenerateFlowFields` pass, which only reads closed heap state.

### Deliverables

- Instrumented benchmark or test that isolates flood-phase vs generation-phase cost for the
  cache benchmark's 32×32 grid.
- A confirmed root cause with evidence before any code change lands.
- Post-fix benchmark showing `FlowFieldCacheMiss_BelowCapacity` within 2× of
  `ColdGuide_OpenPlane64` normalized to the same grid area.

### Phase 2 implementation notes — 2026-05-05

Confirmed on .NET 8.0.26:

- `FlowFieldCacheMiss_BelowCapacity` reproduced at ~54.0 ms / 881 KB before Phase 2 changes.
- The prior `ColdGuide_OpenPlane64` contrast was a benchmark artifact. The benchmark class created
  several `BenchmarkPathFixture` instances in one `GlobalSetup`; each setup disposed the previous
  `GridWorld`, leaving earlier requests pointed at stale voxels. That stale request path measured
  ~19.8 us / 336 B and did not represent a successful cold flow-field guide.

Implemented:

- `GuideCacheBenchmarks` now uses one live fixture for A* and flow-field cache scenarios.
- `FlowFieldPathRequestBenchmarks` now uses one live fixture with spatially separated charts and
  validates all configured flow-field requests after setup so stale requests fail fast.
- `SolidChartPartition.IsImpassable` now skips radial clearance for units that fit inside one
  voxel and returns the cached walkability state instead.
- `SolidChartPartition.GetHashCode` now hashes the stored `WorldIndex` fields directly instead of
  resolving the live `Voxel` during heap dictionary operations.
- `FlowFieldSurveyor` compares partitions to the request start/end by `WorldIndex` instead of
  resolving `current.Voxel` inside the flood and generation loops.

Post-change short-run evidence:

- Corrected `ColdGuide_OpenPlane64`: ~113.8 ms / 3.44 MB.
- `FlowFieldCacheMiss_BelowCapacity`: ~43.9 ms / 881.8 KB.
- The corrected 64×64 cold guide is now consistent with the 32×32 cache miss when normalized by
  grid area, so the original 347× discrepancy is resolved.

Fast-follow:

- Audit the remaining benchmark classes that create multiple `BenchmarkPathFixture` instances in
  one `GlobalSetup` (`AStarPathRequestBenchmarks`, `NavSteeringBenchmarks`,
  `TransitionFallbackBenchmarks`, and `VolumePathRequestBenchmarks`). They may contain the same
  stale-request measurement bug.
- Cold flow-field generation still allocates one fresh field dictionary per result. Pooling or
  reusing `FlowFieldSurveyResult.Fields` on reset/eviction remains the next likely allocation win.
- `PathHeapMeta` was tested as a struct to reduce per-node metadata allocation, but the required
  dictionary write-backs made the 32×32 benchmark slightly slower while saving only ~32 KB. Keep it
  as a reference type unless a broader heap redesign removes those write-backs.

---

## Phase 3 — Eliminate Allocation in `SampleFlowVector`

**Severity:** 🟡 Medium  
**Affected benchmark:** `SampleFlowVector_ExactVoxel` and `SampleFlowVector_FractionalPosition`
(both ~1.4 µs, **1,600 B** per call)  
**Primary file:** `src/Trailblazer/Pathing/Search/FlowField/FlowFieldSurveyor.cs`

### Problem

`SampleFlowVector` calls `GetFlowDirection` four times for bilinear interpolation — once per
corner of the voxel the agent occupies. Each `GetFlowDirection` call invokes
`TrailblazerWorldManager.TryGetVoxel(position, ...)`. If `Voxel` is a reference type and the
world manager allocates a new `Voxel` object per lookup (rather than returning a ref to a stored
instance), each of the four corner lookups allocates. At ~400 B per lookup × 4 lookups = 1,600 B,
this matches the observed per-call allocation.

This matters because `SampleFlowVector` is called from `FlowFieldGuide.TryGetMovementDirection`,
which is invoked every navigation frame for every agent using a flow-field guide. Even a single
agent navigating at 30 Hz generates 48,000 B of Gen0 pressure per second from this call alone.

### Investigation

Confirm whether `TrailblazerWorldManager.TryGetVoxel` returns an existing `Voxel` reference from
a pre-allocated grid, or constructs a new one per call. If the world manager returns a cached
reference (i.e., the allocation is elsewhere), profile `GetFlowDirection` for boxing or struct
copy paths.

### Fix options (in preference order)

1. **Pass voxel indices directly.** If the four corner world positions can be converted to
   `WorldVoxelIndex` values using only arithmetic (no grid lookup), replace the four
   `GetFlowDirection(Vector3d, ...)` calls with direct index lookups into the `fields` dictionary.
   This eliminates all world-manager calls from the hot path.
2. **Cache-local voxel resolve.** If corner positions must go through the world manager, resolve
   the current voxel once (already done in the caller for the `ContainsPosition` check) and
   compute neighbor indices by offset rather than by world-space position.
3. **Degrade to nearest-cell when on-voxel.** For `SampleFlowVector_ExactVoxel`, the agent is
   exactly on a voxel center and bilinear interpolation produces the same result as a direct
   `fields[currentIndex]` lookup. An early-exit before the corner resolution saves 4 lookups in
   the common case.

### Deliverables

- Confirmed allocation source with a minimal repro test or profiler output.
- `SampleFlowVector` allocates zero bytes per call in both the exact and fractional cases.
- Unit tests covering exact, fractional, and out-of-field positions.

### Phase 3 implementation notes — 2026-05-05

Confirmed on .NET 8.0.26:

- Baseline `SampleFlowVector_ExactVoxel`: ~1.58 us / 1.56 KB.
- Baseline `SampleFlowVector_FractionalPosition`: ~1.62 us / 1.56 KB.
- A first direct-index attempt removed the world-manager calls but still allocated about 280 B per
  `WorldVoxelIndex` dictionary lookup, so the final hot path avoids `WorldVoxelIndex` hashing
  during sampling as well.

Implemented:

- `FlowFieldSurveyResult` now carries compact per-grid sampling metadata generated with the flow
  result.
- `FlowFieldGuide.TryGetMovementDirection` samples through the survey-result overload, which
  converts world positions to local voxel keys with fixed-point arithmetic and then reads cached
  directions directly.
- Exact voxel samples now early-out after one lookup instead of doing all four bilinear corner
  reads.
- Added allocation tests for exact, fractional, and out-of-field survey-result sampling.
- Updated the `SampleFlowVector_*` benchmarks to measure the result-aware runtime path.

Post-change short-run evidence:

- `SampleFlowVector_ExactVoxel`: ~176 ns / 0 B.
- `SampleFlowVector_FractionalPosition`: ~520 ns / 0 B.
- `FlowFieldCacheMiss_BelowCapacity`: ~45.3 ms / 1.13 MB. This is a small cold-allocation increase
  from storing the sampling maps, but it keeps per-frame sampling allocation-free.

Fast-follow:

- The legacy `SampleFlowVector(Vector3d, fields)` compatibility overload still falls back to
  world-manager voxel lookup because it does not receive the survey-result sampling metadata.
  Keep internal hot paths on the result overload, and consider deprecating or replacing the
  fields-only overload before public API freeze.
- Revisit the sampling-map storage when addressing cold flow-field allocation. A pooled map or a
  dense planar direction buffer could reduce the extra cold-result memory while preserving the
  zero-allocation sampling path.

---

## Phase 4 — Fix Warm Transition-Aware Guide Paths

**Severity:** 🟠 High  
**Affected benchmarks:** `WarmGuide_FlowField_JumpLink` (16.3 µs, 12.7 KB, **106×** baseline),
`WarmGuide_AStar_SwimPath` (5.9 µs, 3.4 KB, **38.5×** baseline)  
**Primary files:** `src/Trailblazer/Pathing/Search/PathGuideFactory.cs`,
`src/Trailblazer/Pathing/Search/FlowField/FlowFieldGuide.cs`

Both of these benchmarks measure a warm guide request (cache has been seeded once) for a
transition-aware path. The expectation is that a warm guide should be close to the cache-hit
baseline. Both are instead 1–2 orders of magnitude more expensive.

### Problem 4a — Flow-field warm transition (106×)

`RequestFlowField` in `PathGuideFactory` does the following on every call:

```csharp
bool pathFound = _cachedFlowResults.TryGetOrCreate(request, ..., out result);

if (pathFound
    && result.Fields != null
    && request.StartNode != null
    && result.Fields.ContainsKey(request.StartNode.WorldIndex))
{
    // fast warm path
}

if (pathFound)
    _cachedFlowResults.Return(result, dispose: false);

if (!request.AllowTraversalTransitions)
    return null;

return TryBuildTransitionFallbackFlowGuide(request, ...) ? fallbackGuide : null;
```

When the cached flow field does not contain the start node's world index (the start is in a
different chart from the destination), the guide falls out of the fast warm path and calls
`TryBuildTransitionFallbackFlowGuide` on every request. This builds a new `HybridRoutePlan` from
scratch via `HybridPathRequest.CreateFromFlowField`, allocates the staged sub-guide objects, and
returns a new `FlowFieldGuide` with a staged plan. The cached survey result is returned but the
staged guide is constructed fresh each time.

The 12.7 KB per call and 106× cost reflect this per-call plan reconstruction.

**Fix:** The staged `HybridRoutePlan` that transitions a flow-field request across a chart boundary
is deterministic given the same start/end/unit-size inputs. Cache the output in
`_cachedFlowResults` under a derived key, or cache the staged guide itself. The simplest
approach: treat the staged guide as a variant survey result. If `_cachedFlowResults` already
contains an entry for the request's `RequestCacheKey` in the staged/transition form, return it
directly without rebuilding the plan.

### Problem 4b — A* swim-path warm guide (38.5×)

`WarmGuide_AStar_SwimPath` is 38.5× more expensive than `WarmGuide_AStar_JumpLink` with the same
warm cache. Both call `RequestAStar`, which calls `_cachedAStarResults.TryGetOrCreate`. If the
swim-path warm request is not matching the cached key (e.g., because `RequestCacheKey` is
computed differently for swim-path requests than for jump-link requests, or because the swim
path routes through a different chart chain with a different hash), every warm call is
effectively a cold miss.

**Investigate:** Confirm whether the swim-path `RequestCacheKey` matches the key used when the
cache was seeded during `IterationSetup`. If the key does not match due to traversal-type being
included in the hash but not in the cache benchmark's seeding step, that is a cache correctness
gap as well as a performance gap.

### Deliverables

- For 4a: Warm `WarmGuide_FlowField_JumpLink` brings the cost within 2× of `FlowFieldCacheHit`
  baseline. Zero `HybridRoutePlan` allocations on a warm call.
- For 4b: Confirm cache key match for swim-path requests and align seeding in the benchmark.
  Warm `WarmGuide_AStar_SwimPath` cost within 2× of `WarmGuide_AStar_JumpLink`.
- New `TransitionFallbackBenchmarks` warm-guide regression test that asserts sub-microsecond
  cost on a second identical request.

### Phase 4 implementation notes — 2026-05-06

Phase 4 had two separate causes:

- `WarmGuide_FlowField_JumpLink` was rebuilding the transition `HybridRoutePlan` after the direct
  flow-field result failed the start-voxel containment check.
- `WarmGuide_AStar_SwimPath` was a benchmark harness artifact. The benchmark created multiple
  fixtures in one setup pass, so later fixture setup reset the world after earlier requests were
  created and primed.

Changes:

- Added `HybridRoutePlanSurveyResult` and a dedicated reusable cache for deterministic
  transition route plans.
- Added `ReusableSurveyResultCache<T>.TryCheckout(...)` so hot paths can reuse an existing
  cached result without allocating a creation callback or running a miss resolver.
- Updated `RequestFlowField` to return a cached transition fallback plan before repeating the
  known direct flow-field miss.
- Reworked `TransitionFallbackBenchmarks` to use one world fixture, offset each scenario in the
  same grid, and prime all requests only after all charts and transitions are registered.
- Added/tightened a warm flow-field transition fallback allocation regression test.

Short-run evidence:

- Baseline before Phase 4 changes:
  - `WarmGuide_AStar_JumpLink`: ~195 ns / 408 B.
  - `WarmGuide_AStar_SwimPath`: ~5.87 us / 3.40 KB.
  - `WarmGuide_FlowField_JumpLink`: ~11.86 us / 15.1 KB.
- Comparison baseline:
  - `FlowFieldCacheHit`: ~258 ns / 664 B.
- Post-change:
  - `WarmGuide_AStar_JumpLink`: ~190 ns / 408 B.
  - `WarmGuide_AStar_SwimPath`: ~194 ns / 408 B.
  - `WarmGuide_FlowField_JumpLink`: ~178 ns / 320 B.

Fast-follow:

- The transition route-plan cache indexes chart invalidation through segment endpoint owners.
  This matches the current route-step shape, but if future hybrid routing records exact
  per-segment chart usage, switch the cache key metadata to that richer source.
- Warm staged flow-field requests still allocate a small `FlowFieldGuide` wrapper. It is now below
  the alpha budget, so guide-object pooling should wait for evidence from broader benchmarks.

---

## Phase 5 — Add Fast-Fail for Unreachable A* Routes

**Severity:** 🟡 Medium  
**Affected benchmark:** `FailedRoute_ChokeUnitSize2` (~168 µs — ~11× a successful cold guide)  
**Primary file:** `src/Trailblazer/Pathing/Search/AStar/AStarSurveyor.cs`

### Problem

`AStarSurveyor.FindPath` runs to heap exhaustion before declaring failure when the destination is
unreachable. This is the correct behavior for correctness, but it means that any caller that
requests a path without verifying reachability first will pay the full search cost on every
frame until a path becomes possible. At 168 µs per failed request, a group of agents blocked by
a dynamic obstacle that regenerates their path each frame will generate sustained budget pressure.

### Approach

Add a pre-check that uses partition connectivity (chart region tags, flood-fill epoch, or
connectivity component ID) to determine whether the destination partition is reachable from the
source partition without running A*. `PathManager` already maintains chart and partition state
that may expose region membership.

Rules:

- The pre-check must be deterministic and must not depend on wall-clock time or platform-specific
  ordering.
- If connectivity data is not yet available (chart not yet initialized) or the check is
  inconclusive, fall through to the full A* traversal as today.
- The fast-fail must not suppress legitimate paths — it must only fire when it can prove
  the destination is definitively unreachable.
- Do not add the pre-check inline inside `AStarSurveyor`. Place it in `PathGuideFactory`'s
  `ResolveAStarResult` or in a new helper so the surveyor stays a pure search implementation.

### Deliverables

- `FailedRoute_ChokeUnitSize2` benchmark cost drops to near-zero allocation and sub-microsecond
  when the unreachability can be detected from partition state.
- New test confirming that a transitionally reachable route (chart changes state) correctly
  re-evaluates rather than fast-failing from stale connectivity data.

### Implementation Notes

- Added a conservative solid-partition reachability snapshot in `SolidPartitionReachability`.
  The check is keyed by unit size and max climb height, and it returns inconclusive when a
  request allows traversal transitions, unwalkable endpoints, missing chart state, or any other
  condition that could still require full A* evaluation.
- Stamped reachability component IDs directly onto `SolidChartPartition` instances during
  snapshot construction. The hot repeated-failure path can then compare component IDs without a
  dictionary lookup or allocation.
- Invalidated reachability snapshots from chart initialization, unload, reset, rebind, live-state
  cleanup, and partition walkability changes so a route that becomes reachable is re-evaluated.
- Applied the fast-fail in `PathGuideFactory` before A* survey creation, leaving `AStarSurveyor`
  as the pure search implementation and preserving traversal-transition fallback behavior.
- Aligned the benchmark harness by prebuilding the failed unit-size-2 request and removing this
  failed-route case from per-iteration guide-cache flushing. Failed routes are not guide-cached,
  and the previous iteration setup forced `InvocationCount=1`, which obscured the steady-state
  fast-fail cost.

Short-run evidence:

- Baseline before Phase 5 changes:
  - `FailedRoute_ChokeUnitSize2`: ~130.4 us / 1.07 KB.
- Post-change:
  - `FailedRoute_ChokeUnitSize2`: ~74.7 ns / 0 B.

Fast-follow:

- The first request for a new `(unitSize, maxClimbHeight)` snapshot still pays the component
  flood-fill cost. Current failed-route steady state is effectively free, but if runtime workloads
  create many distinct clearance/climb combinations, consider eager chart-time snapshots or a
  bounded snapshot policy.
- `AStarPathRequestBenchmarks` still carries some broader multi-fixture setup shape from earlier
  benchmark coverage. If full-class runs show stale-world artifacts, consolidate its fixture setup
  the way the Phase 4 transition benchmark was consolidated.

---

## Phase 6 — Eliminate Fixed Allocation in Combined Steering Per Tick

**Severity:** 🟡 Medium  
**Affected benchmarks:** `CombinedSteering_Density32/128/512` (baseline 22–29 µs,
**3,992 B** constant regardless of occupant count)  
**Primary files:** `src/Trailblazer/Navigation/Steering/NavSteering.cs`,
`GridForge/src/GridForge/Grids/Managers/GridScanManager.cs`,
`GridForge/src/GridForge/Utility/GridTracer.cs`,
`SwiftCollections/src/SwiftCollections/Utility/SwiftHashTools.cs`

### Problem

The combined steering path (separation + alignment + cohesion + path heading) allocates a flat
3,992 B per tick per agent regardless of whether 32 or 512 occupants are nearby. The constant
size rules out per-occupant allocation; a fixed buffer or result object is created per call.

At ~4 KB/tick/agent, a scene with 100 actively steering agents generates ~400 KB of Gen0
pressure per update frame — before any path guide work.

### Investigation

Resolved root causes:

- Trailblazer called GridForge's iterator/LINQ scan API for every combined-steering tick.
- GridForge's hot scan path needed caller-owned result and scratch storage to avoid iterator,
  pool, and temporary hash-set churn.
- SwiftCollections boxed value-type keys/items through generic null guards in
  `SwiftDictionary<TKey,TValue>` and `SwiftHashSet<T>` insert paths.
- `SwiftHashTools.CombineHashCodes(int, int, int)` was falling through the `params object[]`
  overload, allocating an object array and boxed coordinates for every spatial-grid hash.
- The density benchmark harness created three density fixtures in sequence, leaving only the
  last world active. The Phase 6 density scenarios now share one active world with separated
  occupant clouds so 32/128/512 measure their own populations.

### Fix

Implemented:

- Added GridForge `ScanRadiusInto` overloads and `GridScanScratch` so hot callers can reuse
  `SwiftList`/`SwiftHashSet` storage.
- Updated `NavSteering` to reuse a per-instance `SwiftList<ISteer>` and `GridScanScratch` for
  combined-steering neighbor scans.
- Added SwiftCollections generic null guards for type-parameter collection paths, plus integer
  `SwiftHashTools.CombineHashCodes` overloads for allocation-free coordinate hashing.
- Temporarily switched Trailblazer, GridForge, and Chronicler project files to local
  GridForge/SwiftCollections/Chronicler project references while validating the upstream package
  fixes.

Short-run evidence:

- Baseline before Phase 6 changes:
  - `CombinedSteering_Density32`: ~22.39 us / 3.90 KB.
  - `CombinedSteering_Density128`: ~25.21 us / 3.90 KB.
  - `CombinedSteering_Density512`: ~28.96 us / 3.90 KB.
- Post-change, with local GridForge and SwiftCollections projects:
  - `CombinedSteering_Density32`: ~1.613 us / 0 B.
  - `CombinedSteering_Density128`: ~3.629 us / 0 B.
  - `CombinedSteering_Density512`: ~9.182 us / 0 B.

### Deliverables

- `CombinedSteering_Density*` allocated bytes drop to zero or near-zero.
- Density scaling benchmark shows cost grows only from the occupant scan itself, not from a
  fixed allocation.
- Existing nav-steering tests remain green in Release.

Fast-follow:

- Resolved before Phase 7: SwiftCollections 4.0.3, GridForge 6.0.2, and Chronicler 0.2.0 were
  published with the Phase 6 upstream fixes, and Trailblazer returned to package references.
- `NavSteeringBenchmarks` still has independent setup fixtures for direct LOS and guided path
  scenarios. Phase 6 fixed the density setup because it affected the active benchmark; consolidate
  the rest of the class before relying on full-class navigation benchmark runs.
- Phase 7 temporarily reintroduced a local GridForge project reference for the new
  `WorldVoxelIndex.GetHashCode` fix. Switch Trailblazer back to the published GridForge package
  after that fix is released.

---

## Phase 7 — Investigate `FlowFieldCacheHit` Overhead vs A*

**Severity:** 🔵 Low  
**Affected benchmark:** `FlowFieldCacheHit` (211 ns, 664 B vs A* hit 138 ns, 360 B — 53% slower,
84% more memory)  
**Primary files:** `src/Trailblazer/Pathing/Search/FlowField/FlowFieldGuide.cs`,
`src/Trailblazer/Pathing/Search/PathGuideFactory.cs`

### Problem

Both `AStarCacheHit` and `FlowFieldCacheHit` follow the same `TryGetOrCreate` fast path (cache
present, `HasPath` true, `Checkout()`, return cached result). However, `RequestFlowField` has an
additional bounds check:

```csharp
if (pathFound
    && result.Fields != null
    && request.StartNode != null
    && result.Fields.ContainsKey(request.StartNode.WorldIndex))
```

If this check succeeds, the result is returned immediately. The extra `ContainsKey` call and the
conditional null checks are cheap, but the 304-byte allocation gap (664 B vs 360 B) suggests that
`FlowFieldGuide.Initialize` or the `FlowFieldGuide` constructor itself allocates more than
`AStarGuide.Initialize`. Specifically, if `FlowFieldGuide` retains a reference to the
`SwiftDictionary<WorldVoxelIndex, FlowField>` (which it does via `FlowMap.Fields`), the guide
object itself may carry a larger object graph than the A* equivalent.

### Investigation

Compare the object size of `FlowFieldGuide` vs `AStarGuide` at the point returned from
`RequestGuide`. Determine whether the 304-byte gap is from the guide objects themselves or from
allocation inside `Initialize`.

### Deliverables

- Document the allocation gap root cause as a comment in `PathGuideFactory`.
- If fixable without changing the guide interface: reduce `FlowFieldGuide` allocation to within
  50 B of `AStarGuide` on a warm hit.

### Phase 7 implementation notes — 2026-05-07

Confirmed:

- With the newly published Phase 6 packages before this change, the old gap narrowed but still
  reproduced: `AStarCacheHit` measured ~326.1 ns / 408 B and `FlowFieldCacheHit` measured
  ~214.7 ns / 544 B.
- The remaining 136 B allocation gap was not from `FlowFieldGuide.Initialize` or the guide object
  graph. It came from the required `result.Fields.ContainsKey(request.StartNode.WorldIndex)`
  validation on the FlowField warm-hit path.
- `WorldVoxelIndex.GetHashCode` in GridForge passed a nested `VoxelIndex` struct into
  `SwiftHashTools.CombineHashCodes(...)`, selecting the `params object[]` overload and allocating
  an object array plus boxed values for every dictionary probe.

Implemented:

- Updated GridForge `WorldVoxelIndex.GetHashCode` to feed the nested voxel hash into the
  four-integer `SwiftHashTools.CombineHashCodes` overload.
- Added a GridForge allocation regression test for `WorldVoxelIndex.GetHashCode`.
- Added a Trailblazer regression test that keeps `FlowFieldCacheHit` allocation within 50 bytes
  per request of `AStarCacheHit`.
- Documented the warm-hit dictionary-probe invariant in `PathGuideFactory`.
- Temporarily switched Trailblazer source, test, and benchmark projects to the local GridForge
  project reference for verification until the next GridForge package is published.

Short-run evidence:

- GridForge targeted test failed before the fix at ~40,960 B across 256 hash calls and passed
  after the fix.
- Trailblazer focused allocation guard passed in Release.
- Post-change `guide-cache --filter *CacheHit* --job short` with local GridForge:
  - `AStarCacheHit`: ~338.9 ns / 408 B.
  - `FlowFieldCacheHit`: ~173.5 ns / 384 B.

Fast-follow:

- Publish a new GridForge package containing the `WorldVoxelIndex.GetHashCode` fix, then switch
  Trailblazer's local GridForge project references back to package references.
- `AStarCacheHit` still allocates ~408 B. The likely remaining sources are request cache-key
  recomputation and the captured creation callback passed into `ReusableSurveyResultCache`; keep
  this as a separate warm-hit cache cleanup rather than widening Phase 7.

---

## Summary

| Phase | Severity | Benchmarks | Primary Change |
|-------|----------|-----------|----------------|
| 1 | 🟠 High | `AStarCacheMiss_OverCapacity_Eviction`, `InvalidateCacheFor_NoMatchingChart` | Replace LINQ eviction; add chart reverse index |
| 2 | 🔴 Critical | `FlowFieldCacheMiss_BelowCapacity` | Investigate 34.7 ms anomaly; pool FF output dict |
| 3 | 🟡 Medium | `SampleFlowVector_*` | Eliminate per-call world-manager allocation |
| 4 | 🟠 High | `WarmGuide_FlowField_JumpLink`, `WarmGuide_AStar_SwimPath` | Cache staged transition results; fix swim-path cache key |
| 5 | 🟡 Medium | `FailedRoute_ChokeUnitSize2` | Add reachability pre-check before A* expansion |
| 6 | 🟡 Medium | `CombinedSteering_Density*` | Reuse scan buffers; add upstream allocation-free GridForge/SwiftCollections paths |
| 7 | 🔵 Low | `FlowFieldCacheHit` | Fix GridForge `WorldVoxelIndex` hash allocation |

Phases 1 and 4 are the highest-ROI starting points: Phase 1 requires no design changes and
directly eliminates the LINQ dependency in a shared hot path; Phase 4 addresses the most
user-visible regression (warm transition guides being 2 orders of magnitude slower than expected).
Phase 2 must be instrumented before any code changes land, because the root cause of the 34.7 ms
anomaly is not yet confirmed.

## Fast-Follow Findings

- The phase 1 invalidation benchmark now seeds the A* cache to capacity, but it still does not
  seed all three result caches to 128 entries each. Extending that benchmark to full A*/FlowField/
  Volume pressure should wait until the phase 2 flow-field cold-miss anomaly is understood,
  otherwise setup cost will dominate the invalidation measurement.
- A* warm cache hits still allocate ~408 B per request after Phase 7. Investigate request cache-key
  hashing and captured survey-result factory callbacks as a future cache-hit cleanup.
