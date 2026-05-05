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

---

## Phase 6 — Eliminate Fixed Allocation in Combined Steering Per Tick

**Severity:** 🟡 Medium  
**Affected benchmarks:** `CombinedSteering_Density32/128/512` (19–25 µs, **3,992 B** constant
regardless of occupant count)  
**Primary files:** `src/Trailblazer/Navigation/Steering/NavSteering.cs`,
`src/Trailblazer/Navigation/MovementGroups/`

### Problem

The combined steering path (separation + alignment + cohesion + path heading) allocates a flat
3,992 B per tick per agent regardless of whether 32 or 512 occupants are nearby. The constant
size rules out per-occupant allocation; a fixed buffer or result object is created per call.

At ~4 KB/tick/agent, a scene with 100 actively steering agents generates ~400 KB of Gen0
pressure per update frame — before any path guide work.

### Investigation

Profile or instrument `GetHeading` and the group steering path to identify the exact allocation
site. Likely candidates:

- A `new Navigator[]` or `new ISteer[]` result set allocated to hold the nearby occupant scan
  results from `GridScanManager.ScanRadius`.
- A `new MovementGroupSession()` or similar per-call state object.
- Array construction inside the group-behavior weight combination step.

### Fix

Once the allocation site is confirmed:

- If the allocation is a result buffer for the occupant scan, replace it with a pre-allocated
  per-instance `SwiftList<ISteer>` cleared at the start of each tick.
- If it is a per-call struct or object, evaluate whether it can be stack-allocated or stored as
  a field on `NavSteering` and reused.

A 100-agent scene producing zero Gen0 allocation from `GetHeading` under steady-state navigation
is the target.

### Deliverables

- `CombinedSteering_Density*` allocated bytes drop to zero or near-zero.
- Density scaling benchmark shows cost grows only from the occupant scan itself, not from a
  fixed allocation.
- Existing nav-steering tests remain green in Release.

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

---

## Summary

| Phase | Severity | Benchmarks | Primary Change |
|-------|----------|-----------|----------------|
| 1 | 🟠 High | `AStarCacheMiss_OverCapacity_Eviction`, `InvalidateCacheFor_NoMatchingChart` | Replace LINQ eviction; add chart reverse index |
| 2 | 🔴 Critical | `FlowFieldCacheMiss_BelowCapacity` | Investigate 34.7 ms anomaly; pool FF output dict |
| 3 | 🟡 Medium | `SampleFlowVector_*` | Eliminate per-call world-manager allocation |
| 4 | 🟠 High | `WarmGuide_FlowField_JumpLink`, `WarmGuide_AStar_SwimPath` | Cache staged transition results; fix swim-path cache key |
| 5 | 🟡 Medium | `FailedRoute_ChokeUnitSize2` | Add reachability pre-check before A* expansion |
| 6 | 🟡 Medium | `CombinedSteering_Density*` | Identify and pool fixed per-tick allocation |
| 7 | 🔵 Low | `FlowFieldCacheHit` | Reduce FF warm-hit object graph size |

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
