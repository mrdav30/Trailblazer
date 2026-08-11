# Benchmark Performance Next Plan

**Status:** Complete. Remaining work was extracted to
[`benchmarkPerformanceFinalPlan.md`](../benchmarkPerformanceFinalPlan.md) when
this plan was archived.

## Purpose

This document captures the next phase of Trailblazer performance work after
completing phases 1 through 7 in `done\benchmarkPerformancePlan.md`.

The previous phase plan closed the planned alpha-hardening items. This plan
starts from the fresh full benchmark run on 2026-05-07 and focuses on the
remaining high-value work: making the benchmark harness trustworthy, reducing
cold flow-field generation cost, and removing residual warm-guide allocations.

## Fresh Run Context

Command:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- all --filter '*' --job short --runtimes net8.0
```

Environment:

- BenchmarkDotNet 0.15.8
- Ubuntu 24.04.1 LTS under WSL
- Intel Core i7-9700K, 8 logical cores
- .NET SDK 10.0.203
- Runtime .NET 8.0.26, X64 RyuJIT
- Local GridForge project reference intentionally retained for this run
- Total runtime: about 22 minutes, 53 benchmarks executed

Generated reports:

- `BenchmarkDotNet.Artifacts/results/Trailblazer.Benchmarks.Navigation.NavSteeringBenchmarks-report-github.md`
- `BenchmarkDotNet.Artifacts/results/Trailblazer.Benchmarks.Pathing.AStarPathRequestBenchmarks-report-github.md`
- `BenchmarkDotNet.Artifacts/results/Trailblazer.Benchmarks.Pathing.FlowFieldPathRequestBenchmarks-report-github.md`
- `BenchmarkDotNet.Artifacts/results/Trailblazer.Benchmarks.Pathing.GuideCacheBenchmarks-report-github.md`
- `BenchmarkDotNet.Artifacts/results/Trailblazer.Benchmarks.Pathing.TransitionFallbackBenchmarks-report-github.md`
- `BenchmarkDotNet.Artifacts/results/Trailblazer.Benchmarks.Pathing.VolumePathRequestBenchmarks-report-github.md`

## Fresh Signal

### Trustworthy Hot-Path Improvements

These results are consistent with the completed phase work and should remain
guarded:

| Area                | Benchmark                             |    Fresh result | Notes                                             |
| ------------------- | ------------------------------------- | --------------: | ------------------------------------------------- |
| Navigation          | `CombinedSteering_Density32`          |    1.72 us, 0 B | Phase 6 held.                                     |
| Navigation          | `CombinedSteering_Density128`         |    3.77 us, 0 B | Scales with occupant count, not fixed allocation. |
| Navigation          | `CombinedSteering_Density512`         |    9.24 us, 0 B | Still clean under density.                        |
| Flow field sampling | `SampleFlowVector_ExactVoxel`         |   183.7 ns, 0 B | Phase 3 held.                                     |
| Flow field sampling | `SampleFlowVector_FractionalPosition` |   530.8 ns, 0 B | Phase 3 held.                                     |
| Guide cache         | `InvalidateCacheFor_NoMatchingChart`  |    3.43 us, 0 B | Reverse-index work held for A* pressure.          |
| Transition fallback | `WarmGuide_FlowField_JumpLink`        | 174.8 ns, 320 B | Phase 4/7 warm path is fast; allocation remains.  |

### Remaining Heavy Costs

These are the clearest optimization targets once the harness issues below are
corrected:

| Area                       | Benchmark                              |       Fresh result | Signal                                                                |
| -------------------------- | -------------------------------------- | -----------------: | --------------------------------------------------------------------- |
| Flow field cold generation | `ColdGuide_OpenPlane64`                |  119.6 ms, 3.25 MB | Dominant cold-path cost.                                              |
| Flow field cold generation | `ColdGuide_OpenPlane128`               | 834.3 ms, 12.98 MB | Super-frame hitch territory.                                          |
| Flow field raw survey      | `RawSurvey_OpenPlane64`                |  110.1 ms, 3.24 MB | Cost is mostly survey/result generation, not guide wrapper.           |
| Flow field cache miss      | `FlowFieldCacheMiss_BelowCapacity`     |  46.7 ms, 812.8 KB | Consistent with 32x32 area scaling, still too high.                   |
| A* cache eviction path     | `AStarCacheMiss_OverCapacity_Eviction` |   11.3 ms, 16.5 KB | Needs harness isolation before treating as implementation regression. |
| Warm guide reuse           | `ManyStartWarmReuse_32Starts`          |   6.23 us, 12.6 KB | About 393 B per requester; likely guide/request wrapper allocation.   |

### Benchmark Quality Warnings

Several numbers are not safe optimization targets yet:

- `AStarPathRequestBenchmarks` still creates multiple `BenchmarkPathFixture`
  instances in one `GlobalSetup`. The fresh A* cold numbers are suspiciously
  flat across 32, 64, 256, and 1024 scenarios, and `RawSurvey_OpenPlane32`
  reports 33.3 ns. Treat the full A* class as provisional.
- `VolumePathRequestBenchmarks` creates separate fixtures for direct and L-shape
  scenarios. The tiny raw/cold timings may be valid for the very small volume
  paths, but the setup should be consolidated or split before using the numbers
  as baselines.
- `NavSteeringBenchmarks` still has separate fixtures for direct LOS, guided A*,
  guided flow-field, and density. Density is already consolidated and
  trustworthy; direct/guided setup should be corrected before relying on
  full-class first-frame comparisons.
- Several cold benchmarks trigger BenchmarkDotNet `MinIterationTime` warnings.
  These should use more operations per measured iteration or a custom job before
  we compare deltas.
- `AStarCacheMiss_OverCapacity_Eviction` currently measures a potentially
  different A* route from the below-capacity miss. It should isolate eviction
  cost from path length/search cost.

## Phase 1 - Restore Benchmark Trust

**Severity:** High  
**Primary files:**
`tests/Trailblazer.Benchmarks/Pathing/AStarPathRequestBenchmarks.cs`,
`tests/Trailblazer.Benchmarks/Pathing/VolumePathRequestBenchmarks.cs`,
`tests/Trailblazer.Benchmarks/Navigation/NavSteeringBenchmarks.cs`,
`tests/Trailblazer.Benchmarks/Pathing/GuideCacheBenchmarks.cs`

Fix this before making new A* or volume optimizations. Otherwise we risk chasing
artifacts.

Deliverables:

- Consolidate each remaining multi-fixture benchmark class into one active world
  with spatially separated scenarios, or split scenarios into separate benchmark
  classes with one fixture each.
- Add post-setup validation after every scenario has been registered, not only
  immediately after each fixture setup.
- Add preflight invariants that fail fast when a benchmark resolves a stale or
  trivial route: expected route found, minimum waypoint count, expected chart
  key, and no unexpected cache leak.
- Add optional survey counters for raw/cold benchmarks: closed-node count, route
  length, field count, and cache hit/miss classification.
- Fix `AStarCacheMiss_OverCapacity_Eviction` so the measured operation isolates
  eviction. Either use equivalent-length requests for below/over-capacity cases
  or add a dedicated eviction-only benchmark with a stubbed or prebuilt result
  path.
- Adjust cold/first-frame benchmarks with `OperationsPerInvoke`,
  `InvocationCount`, or a custom job so BenchmarkDotNet stops warning about
  sub-100 ms measured iterations.

Exit criteria:

- A* cold guides scale with scenario size and path shape.
- `RawSurvey_OpenPlane32` is no longer a nanosecond-scale result.
- Full `all --job short` reports no stale-route artifacts.
- Harness fixes do not alter runtime library behavior.

### Phase 1 Notes - 2026-05-07

Implemented:

- Added benchmark preflight tests that instantiate the benchmark classes, run
  `GlobalSetup`, and verify the configured route methods still resolve after all
  scenarios are registered. The initial red run caught the stale-world artifacts
  in `AStarPathRequestBenchmarks` and `VolumePathRequestBenchmarks`.
- Consolidated `AStarPathRequestBenchmarks`, `VolumePathRequestBenchmarks`, and
  `NavSteeringBenchmarks` into one active fixture per class, with spatially
  separated scenarios.
- Added post-setup validation inside the benchmark classes so invalid requests
  fail during setup instead of producing trivial benchmark numbers.
- Added offset-aware benchmark chart helpers and adjacent request-pair
  generation for cache pressure cases.
- Changed `AStarCacheMiss_OverCapacity_Eviction` to use adjacent equivalent-cost
  requests so the measured delta is eviction pressure instead of a longer A*
  route.

Verification:

- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~BenchmarkHarnessPreflight`
  passed with 3 tests.
- `dotnet test Trailblazer.slnx --configuration Release` passed with 903 tests.
- `dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- all --filter '*' --job short --runtimes net8.0`
  completed all 53 benchmarks in 10 minutes 51 seconds with no setup or
  stale-route failures.
- The full A* run now reports `RawSurvey_OpenPlane32` at 2.431 ms and cold
  guides scaling by shape and size: open 32 at 2.820 ms, corridor 64 at 997.847
  us, corridor 256 at 4.540 ms, corridor 1024 at 32.658 ms, blocker 64 at 31.845
  ms.
- The full guide-cache run reports `AStarCacheMiss_BelowCapacity` at 84.753 us
  and `AStarCacheMiss_OverCapacity_Eviction` at 155.692 us, removing the prior
  11.3 ms path-length artifact.
- The full volume run reports `RawSurvey_DirectGasCorridor` at 74.120 us,
  `ColdGuide_DirectGasCorridor` at 80.752 us, and `ColdGuide_LShapeGasPath` at
  98.583 us.
- The full navigation run kept density steering allocation-free at 1.731 us,
  3.648 us, and 9.639 us for 32, 128, and 512 occupants, and validated guided
  first-frame scenarios in the unified world.

Fast-follow observations:

- True cold and first-frame benchmarks still trigger BenchmarkDotNet
  `MinIterationTime` warnings for the smaller scenarios. Keep this in Phase 5
  scenario design by batching first-frame operations without hiding cold-cache
  behavior.
- With the A* harness corrected, `ColdGuide_Heuristic_Octile` and
  `ColdGuide_Heuristic_Euclidean` are materially slower than Manhattan on the
  64x64 open-plane fixture. Carry this as an A* investigation candidate once the
  flow-field cold path is addressed.

## Phase 2 - Reduce Cold Flow-Field Generation Cost

**Severity:** Critical  
**Primary files:**
`src/Trailblazer/Pathing/Search/FlowField/FlowFieldSurveyor.cs`,
`src/Trailblazer/Pathing/Search/FlowField/FlowFieldSurveyResult.cs`

Fresh evidence:

- Phase 1 verified run: `ColdGuide_OpenPlane64` at 116.349 ms and 3.25 MB.
- Phase 1 verified run: `ColdGuide_OpenPlane128` at 796.391 ms and 12.98 MB.
- Phase 1 verified run: `RawSurvey_OpenPlane64` at 113.219 ms and 3.24 MB.
- Phase 1 verified run: `FlowFieldCacheMiss_BelowCapacity` at 44.092 ms and
  812.8 KB.

Investigation order:

1. Split raw survey timing into flood cost, flow-vector generation cost, field
   dictionary allocation cost, chart-key storage cost, and sampling-map
   construction cost.
2. Inspect `FlowFieldSurveyResult.Reset()` and result reuse to see whether
   `Fields`, chart keys, and sampling metadata can be retained and cleared
   instead of reallocated.
3. Compare `SwiftDictionary<WorldVoxelIndex, FlowField>` against a dense planar
   direction buffer for open-plane flow fields. Preserve deterministic lookup
   behavior and chart invalidation.
4. Recheck lock scope around flood and generation. Avoid holding global survey
   locks longer than the shared heap truly requires.

Exit criteria:

- Preserve path correctness and deterministic field generation.
- Reduce cold 64x64 flow-field allocation materially below 3 MB.
- Reduce cold 128x128 generation enough that it no longer creates a multi-frame
  hitch on the benchmark machine, or document why the algorithmic cost is
  inherent.
- Add focused tests around reused result state and invalidation so stale fields
  cannot leak.

### Phase 2 Notes - 2026-05-07

Implemented:

- Replaced the duplicate sparse sampling-direction dictionary with bounded dense
  sampling buffers for dense flow fields, with sparse dictionary fallback when
  the closed field bounds are too wide for the number of fields.
- Added a prepass over the closed flood set to build per-grid sampling bounds
  before result materialization. This keeps open-plane sampling metadata compact
  without changing public `Fields` lookup behavior.
- Cached the normalized direction vector for each deterministic
  `SpatialDirection` once and reused it during flow-vector generation instead of
  normalizing every field direction.
- Fixed a flow-field generation edge check that skipped the `SouthWest` diagonal
  because it used `i > 6` instead of GridForge's diagonal predicate.
- Fixed the diagonal leg axis mapping shared by flow-field, A*, volume, and
  reachability checks. GridForge offsets use X for East/West and Z for
  North/South; the previous helper logic had those positive-axis legs swapped in
  several pathing components.

Verification:

- Added
  `FlowFieldSurveyor_FindPath_ShouldKeepOpenPlane16ColdAllocationsUnderBudget`,
  which failed at 203,808 B before the sampling metadata change and now stays
  below the 180,000 B guard.
- Added
  `FlowFieldSurveyor_HasValidDiagonalLegs_ShouldUseGridAxesForHorizontalDiagonals`,
  which failed before the shared diagonal-leg mapping fix and now passes for the
  southeast and northwest horizontal diagonal cases.
- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~FlowFieldSurveyor`
  passed with 43 tests.
- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~AStar`
  passed with 65 tests.
- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~Volume`
  passed with 106 tests.
- `dotnet test Trailblazer.slnx --configuration Release` passed with 906 tests.

Benchmarks:

| Benchmark                             |     Phase 1 verified |       Phase 2 result | Signal                                                                  |
| ------------------------------------- | -------------------: | -------------------: | ----------------------------------------------------------------------- |
| `ColdGuide_OpenPlane64`               |  116.349 ms, 3.25 MB |  114.993 ms, 2.36 MB | Allocation down materially; time flat.                                  |
| `ColdGuide_OpenPlane128`              | 796.391 ms, 12.98 MB |  803.909 ms, 9.45 MB | Allocation down materially; time slightly worse within short-run noise. |
| `RawSurvey_OpenPlane64`               |  113.219 ms, 3.24 MB |  114.605 ms, 2.36 MB | Confirms improvement is memory-focused, not flood-time-focused.         |
| `FlowFieldCacheMiss_BelowCapacity`    |  44.092 ms, 812.8 KB | 44.381 ms, 578.86 KB | Cache miss allocation down about 29%; time flat.                        |
| `SampleFlowVector_ExactVoxel`         |        183.7 ns, 0 B |        169.2 ns, 0 B | Sampling stayed allocation-free.                                        |
| `SampleFlowVector_FractionalPosition` |        530.8 ns, 0 B |        514.1 ns, 0 B | Sampling stayed allocation-free.                                        |

Remaining findings:

- The 128x128 cold path is still a multi-frame hitch. The dense sampling change
  removed duplicate metadata storage, but the dominant time remains flood
  expansion plus public result dictionary materialization.
- `FlowFieldSurveyResult.Reset()` does not currently unlock meaningful reuse by
  itself because the cache does not recycle reset flow-field result instances on
  raw cold surveys. Retaining fields there would add stale-state risk without an
  accompanying result-pool design.
- `PathHeap` still allocates per tracked partition metadata during cold surveys.
  A lower-allocation heap metadata design is the next likely cold-flow-field
  allocation target, but it would touch A* and volume surveyors too and should
  be handled as a separate focused phase.
- Reducing the global survey lock scope still requires splitting shared mutable
  surveyor scratch state into per-call or pooled state. The current phase kept
  lock behavior unchanged.
- BenchmarkDotNet still reports high-priority permission warnings in WSL and
  `MinIterationTime` warnings for short cache-miss scenarios. Continue handling
  those under Phase 5 scenario design.

## Phase 3 - Remove Warm Guide Allocation

**Severity:** Medium  
**Primary files:** `src/Trailblazer/Pathing/Search/PathGuideFactory.cs`,
`src/Trailblazer/Pathing/Search/Support/Survey/ReusableSurveyResultCache.cs`,
`src/Trailblazer/Pathing/Search/*Guide.cs`

Fresh evidence:

- `AStarCacheHit`: 333.6 ns, 408 B.
- `FlowFieldCacheHit`: 199.9 ns, 384 B.
- `WarmGuide_OpenPlane32`: 200.7 ns, 360 B.
- `WarmGuide_OpenPlane64`: 178.0 ns, 384 B.
- `WarmGuide_DirectGasCorridor`: 457.2 ns, 368 B.
- `WarmGuide_AStar_JumpLink`: 183.8 ns, 408 B.
- `ManyStartWarmReuse_32Starts`: 6.23 us, 12.6 KB.

Likely sources:

- Request cache-key construction on every call.
- Captured creation callbacks passed into cache miss paths.
- Per-request guide wrapper allocation.
- Dictionary probes whose key hashing is now improved but still repeated.

Deliverables:

- Add allocation microbenchmarks for request construction and request-key
  construction separately from guide resolution.
- Add a non-capturing cache miss factory path or a `TryCheckout`-then-create
  flow if it removes warm-hit allocations without making miss logic harder to
  reason about.
- Consider caching immutable request keys inside request objects, if it does not
  widen public API semantics or create stale-key risk.
- Defer guide wrapper pooling unless warm guide allocation remains after cheaper
  fixes.

Exit criteria:

- A*/FlowField/Volume warm guide hits allocate zero or near-zero bytes.
- `ManyStartWarmReuse_32Starts` no longer scales linearly in allocation per
  requester.
- Existing allocation guards remain deterministic and stable under Release.

### Phase 3 Notes - 2026-05-07

Implemented:

- Added allocation guards for A*, flow-field, and volume warm guide hits,
  request cache-key reads, reusable survey-result cache checkout, and
  guide-wrapper pool rent/release.
- Replaced tuple-based request hash composition with `PathRequestHashBuilder` so
  A*, flow-field, volume, and hybrid request keys avoid tuple/interface
  allocation and randomized string hashing.
- Added reusable guide-wrapper pools for A*, flow-field, and volume guide
  instances. Returning a guide now returns the shared survey result first,
  resets only wrapper state, then keeps the wrapper available for the next warm
  hit.
- Split A*, flow-field, and volume miss paths into cold helper methods. JIT
  disassembly showed the captured `TryGetOrCreate` miss delegate allocated a
  display class at warm-hit method entry even when `TryCheckout` returned
  immediately.
- Reused a fixed guide buffer in `ManyStartWarmReuse_32Starts` so the benchmark
  measures warm reuse instead of allocating a per-invocation returned-guide
  array.
- Added request construction and request cache-key benchmarks for A*,
  flow-field, and volume requests.

Verification:

- `WarmGuideHits_ShouldAllocateNearZero_WhenReturnedGuidesCanBeReused` initially
  failed at 178,176 B over 256 A* warm hits, then at 6,144 B after guide pooling
  exposed the warm-hit display-class allocation. It now passes under the 1,024 B
  guard for A*, flow-field, and volume combined.
- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~PathGuideFactoryCoverageTests`
  passed with 13 tests.

Short-run benchmark evidence:

| Benchmark                               |  Phase 3 result | Signal                                                                                              |
| --------------------------------------- | --------------: | --------------------------------------------------------------------------------------------------- |
| `AStarCacheHit`                         |   275.1 ns, 0 B | Cache checkout plus pooled wrapper is allocation-free.                                              |
| `FlowFieldCacheHit`                     |   130.8 ns, 0 B | Cache checkout plus pooled wrapper is allocation-free.                                              |
| `WarmGuide_OpenPlane32`                 |   282.5 ns, 0 B | A* warm guide allocation removed.                                                                   |
| `WarmGuide_OpenPlane64`                 |   135.9 ns, 0 B | Flow-field warm guide allocation removed.                                                           |
| `WarmGuide_OpenPlane128`                |   7.767 us, 0 B | Allocation removed; time needs canonical rerun because short-run setup showed first-workload noise. |
| `WarmGuide_DirectGasCorridor`           |   117.6 ns, 0 B | Volume warm guide allocation removed.                                                               |
| `WarmGuide_LShapeGasPath`               |   124.7 ns, 0 B | Volume alternate shape stayed allocation-free.                                                      |
| `WarmGuide_AStar_JumpLink`              |   119.7 ns, 0 B | Transition fallback A* warm path allocation removed.                                                |
| `WarmGuide_FlowField_JumpLink`          |   134.4 ns, 0 B | Transition fallback flow-field warm path allocation removed.                                        |
| `ManyStartWarmReuse_32Starts`           |   4.142 us, 0 B | Allocation no longer scales per requester.                                                          |
| `RequestCacheKey_OpenPlane32`           |    9.93 ns, 0 B | A* key read/hash path is allocation-free.                                                           |
| `RequestCacheKey_OpenPlane64`           |    8.78 ns, 0 B | Flow-field key read/hash path is allocation-free.                                                   |
| `RequestCacheKey_DirectGasCorridor`     |    8.84 ns, 0 B | Volume key read/hash path is allocation-free.                                                       |
| `RequestConstruction_OpenPlane32`       | 555.1 ns, 112 B | Construction still allocates the request object, as expected.                                       |
| `RequestConstruction_OpenPlane64`       | 499.2 ns, 112 B | Construction still allocates the request object, as expected.                                       |
| `RequestConstruction_DirectGasCorridor` | 627.8 ns, 104 B | Construction still allocates the request object, as expected.                                       |

Fast-follow observations:

- Benchmark filter arguments that include `*Request*` overmatch benchmark class
  names containing `Request`; prefer category filters such as
  `--anyCategories Warm Request` for targeted reruns.
- `WarmGuide_OpenPlane128` should be watched in a canonical run because the
  short-run result had unusual first-workload JIT/setup behavior despite
  reporting 0 B.
- Transition request construction/cache-key microbenchmarks are still worth
  adding under Phase 5; Phase 3 covered A*, flow-field, and volume request
  objects.

## Phase 4 - Measure Mixed Cache Pressure

**Severity:** Medium  
**Primary files:**
`tests/Trailblazer.Benchmarks/Pathing/GuideCacheBenchmarks.cs`,
`src/Trailblazer/Pathing/Search/PathGuideFactory.cs`

The Phase 1 invalidation benchmark now seeds A* to capacity, but it still does
not exercise a full mixed cache containing A*, flow-field, volume, and hybrid
transition entries.

Deliverables:

- Add mixed-cache invalidation benchmarks with all cache families at or near
  capacity.
- Add no-match and matching-chart variants under mixed pressure.
- Add cull benchmarks with realistic stale/active ratios for each guide type.
- Track invalidation cardinality: entries scanned, entries matched, entries
  removed.

Exit criteria:

- Dynamic chart invalidation remains bounded by affected entries, not total
  cache size.
- No-match invalidation stays allocation-free under mixed cache pressure.

### Phase 4 Notes - 2026-05-08

Implemented:

- Added mixed guide-cache pressure benchmarks that populate A*, flow-field,
  volume, and hybrid transition route-plan caches in the same fixture.
- Added no-match and matching-chart mixed invalidation variants for the shared
  solid chart, shared gas-volume chart, and shared hybrid destination chart.
- Added mixed cull variants for all-fresh entries and a stale set with one
  active A*/flow/volume guide per four entries. Hybrid route plans are returned
  immediately by the staged flow guide path, so they participate as stale
  returned cache entries rather than active checked-out guide entries.
- Added cardinality result helpers that report indexed entries scanned, entries
  matched, and entries removed. The benchmark methods return scalar removal
  counts so BenchmarkDotNet does not allocate just to consume a custom return
  struct.
- Added benchmark preflight coverage for the mixed pressure setup and
  cardinality expectations.
- Fixed a small runtime allocation surfaced by the cull benchmark:
  `ReusableSurveyResultCache<T>` now allocates its stale-removal list only after
  it finds the first stale reusable entry.

Verification:

- `GuideCacheBenchmarks_ShouldSeedMixedCachePressureScenarios_AfterGlobalSetup`
  first failed on missing mixed benchmark APIs, then exposed two setup issues:
  duplicate flow-field request keys in the generated pressure set and hybrid
  transition route plans without chart-owner metadata when the synthetic route
  started or ended exactly on a transition anchor. Both are now guarded.
- `EvictStaleEntries_ShouldNotAllocate_WhenNoEntriesAreStale` failed at 184 B
  before the lazy stale-removal list change and now passes below the 64 B guard.
- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~BenchmarkHarnessPreflightTests`
  passed with 4 tests.
- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~ReusableSurveyResultCacheTests`
  passed with 7 tests.

Short-run benchmark evidence:

| Benchmark                                     |           Phase 4 result | Signal                                                                                                                 |
| --------------------------------------------- | -----------------------: | ---------------------------------------------------------------------------------------------------------------------- |
| `CullMixedCache_NoStale`                      |            16.48 us, 0 B | No-stale cull is now allocation-free under mixed pressure.                                                             |
| `CullMixedCache_StaleWithActiveQuarter`       |        40.62 us, 2.25 KB | Removes stale returned entries while preserving active A*/flow/volume guides.                                          |
| `InvalidateMixedCacheFor_NoMatchingChart`     | 18.10 us, 288 B reported | Direct allocation guard is allocation-free; BDN includes one-invocation setup noise for this mutation-style benchmark. |
| `InvalidateMixedCacheFor_MatchingSolidChart`  |            79.72 us, 0 B | Removes the 64 indexed A*/flow-field entries for the shared solid chart.                                               |
| `InvalidateMixedCacheFor_MatchingVolumeChart` |          12.50 us, 288 B | Removes the 32 indexed volume entries for the shared gas-volume chart.                                                 |
| `InvalidateMixedCacheFor_MatchingHybridChart` |          80.85 us, 576 B | Removes the 32 indexed hybrid route-plan entries for the shared destination chart.                                     |

Cardinality covered by preflight:

| Variant               | Indexed entries scanned | Entries removed |
| --------------------- | ----------------------: | --------------: |
| No matching chart     |                       0 |               0 |
| Matching solid chart  |                      64 |              64 |
| Matching volume chart |                      32 |              32 |
| Matching hybrid chart |                      32 |              32 |

Fast-follow observations:

- A full 128 entries per cache family made benchmark iteration setup dominate
  wall time because it required repeated cold flow-field and hybrid route-plan
  generation. Phase 4 therefore uses an aggregate 128-entry mixed pressure set,
  32 entries per family, and keeps the existing A* capacity benchmarks for true
  128-entry single-family pressure.
- A synthetic cache seed hook or a dedicated internal
  `ReusableSurveyResultCache<T>` benchmark would let us measure full-capacity
  mixed invalidation without timing real path generation in setup.
- BenchmarkDotNet still reports `MinIterationTime` warnings for these
  one-invocation mutation benchmarks. Treat the direct allocation guards and
  cardinality preflights as the authority for correctness/allocation claims, and
  use the short-run numbers as comparative smoke-test signal.

## Phase 5 - Add Scenario Benchmarks

**Severity:** Medium  
**Primary files:** `tests/Trailblazer.Benchmarks/Navigation/*`,
`tests/Trailblazer.Benchmarks/Pathing/*`

The current benchmark suite is strong at isolated hot paths. The next suite
should add runtime-like workloads so regressions show up before integration
tests or game hosts feel them.

New benchmarks to add:

- Multi-agent fixed-step steering: 100 and 500 agents with a mix of direct LOS,
  A*, flow-field, and combined steering.
- Dynamic obstacle update: chart invalidation followed by a repath wave.
- Flow-field sharing: many agents with same destination, varying start
  positions, and explicit allocation reporting per requester.
- First-frame navigation setup with enough operations per iteration to remove
  BenchmarkDotNet `MinIterationTime` warnings.
- Reachability snapshot first-hit cost for distinct `(unitSize, maxClimbHeight)`
  combinations.
- Transition request creation/cache-key microbenchmarks, plus scenario-level
  request churn where hosts create requests every fixed step.
- Flow-field flood-range sweep: 32x32, 64x64, 128x128, blocker field, and large
  flood range.

Exit criteria:

- Scenario benchmarks have clear preflight assertions and route-shape counters.
- The suite distinguishes cold hitches, steady-state per-frame work, and cache
  lifecycle pressure.

### Phase 5 Notes - 2026-05-08

Implemented:

- Added `NavigationScenarioBenchmarks` with 100-agent and 500-agent mixed
  first-frame and steady fixed-step workloads. Each mixed set includes direct
  LOS, A\*, flow-field, and combined-steering agents, and exposes preflight
  counters for route shape, guide-backed agents, and non-zero headings.
- Added `PathingScenarioBenchmarks` covering dynamic chart update plus a
  64-request A\* repath wave, 100-start and 500-start shared flow-field guide
  checkout, reachability snapshot first-hit checks for four
  `(unitSize, maxClimbHeight)` combinations, transition request construction and
  cache-key reads, transition request churn, and flow-field flood sweeps over
  open and blocker charts.
- Added benchmark harness preflight tests for the scenario classes. The first
  red run confirmed the tests were guarding missing scenario APIs; the passing
  run now verifies chart updates, guide resolution counts, failed reachability
  routes, transition request churn, and sampled flood fields.
- Fixed a benchmark-only realism issue: `BenchmarkOccupant` now implements
  `ISteer`, so existing and new combined-steering scans measure actual steerable
  occupants instead of scanning past non-`ISteer` fixtures.
- Updated the benchmark README with `navigation-scenario` and `pathing-scenario`
  aliases and suite descriptions.

Verification:

- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~BenchmarkHarnessPreflightTests`
  passed with 6 tests.
- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~PathingScenarioBenchmarks_ShouldKeepScenarioRoutesValid`
  passed after the final sharing-batch adjustment.
- `dotnet build Trailblazer.slnx --configuration Release` passed with 0 warnings
  and 0 errors.
- `dotnet test Trailblazer.slnx --configuration Release` passed with 914 tests.
- `dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- navigation-scenario --filter '*' --job short --runtimes net8.0`
  completed all 4 navigation scenario benchmarks.
- `dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- pathing-scenario --filter '*' --job short --runtimes net8.0`
  completed all 14 pathing scenario benchmarks with no `MinIterationTime`
  warnings after batching the flow-field sharing methods.

Short-run benchmark evidence:

| Benchmark                                         |       Phase 5 result | Signal                                                                                |
| ------------------------------------------------- | -------------------: | ------------------------------------------------------------------------------------- |
| `FirstFrameMixedSteering_100Agents`               |  18.746 ms, 39.60 KB | Cold mixed first-frame hitch shape is now visible.                                    |
| `FirstFrameMixedSteering_500Agents`               |  12.459 ms, 18.89 KB | Larger mixed first-frame scenario is covered; short-run result needs canonical rerun. |
| `FixedStepMixedSteering_100Agents`                |       3.794 us, 20 B | Steady mixed frame is near allocation-free.                                           |
| `FixedStepMixedSteering_500Agents`                |       3.759 us, 20 B | Steady mixed frame scales without obvious per-agent allocation.                       |
| `DynamicObstacleUpdate_RepathWave64`              | 32.587 ms, 174.05 KB | Chart invalidation plus repath wave is now measured as one scenario.                  |
| `FlowFieldSharing_100Starts`                      |        144.8 ns, 0 B | Shared destination guide checkout remains effectively allocation-free.                |
| `FlowFieldSharing_500Starts`                      |       173.3 ns, 23 B | Larger shared checkout stays near-zero allocation after batching.                     |
| `ReachabilityFirstHit_ClearanceCombos`            |   30.473 ms, 9.18 MB | First-hit snapshot cost is now visible for clearance/climb churn.                     |
| `TransitionRequestConstruction_AStarJumpLink`     |      537.5 ns, 112 B | Transition-aware A\* request construction baseline.                                   |
| `TransitionRequestConstruction_FlowFieldJumpLink` |      535.4 ns, 112 B | Transition-aware flow-field request construction baseline.                            |
| `TransitionRequestChurn_64Requests`               |      529.8 ns, 112 B | Host-style request churn baseline.                                                    |
| `TransitionRequestCacheKey_AStarJumpLink`         |        10.15 ns, 0 B | Transition-aware A\* key reads stay allocation-free.                                  |
| `TransitionRequestCacheKey_FlowFieldJumpLink`     |         9.28 ns, 0 B | Transition-aware flow-field key reads stay allocation-free.                           |
| `FlowFieldFloodRange_OpenPlane32`                 | 94.864 ms, 591.54 KB | Flood sweep now includes the 32x32 case.                                              |
| `FlowFieldFloodRange_OpenPlane64`                 |  772.230 ms, 2.36 MB | 64x64 open-plane sweep is unexpectedly time-heavy in this combined scenario.          |
| `FlowFieldFloodRange_OpenPlane128`                |   82.098 ms, 9.45 MB | Allocation scales with field size, but time does not in this short run.               |
| `FlowFieldFloodRange_Blocker64Default`            |  587.352 ms, 2.36 MB | Blocker field sweep is covered.                                                       |
| `FlowFieldFloodRange_Blocker64Large`              |  585.001 ms, 2.36 MB | Enlarged flood range is covered; similar cost in this setup.                          |

Fast-follow observations:

- The flood sweep allocation scales as expected by field size, but short-run
  time does not: `OpenPlane64` and blocker 64 are much slower than
  `OpenPlane128`. Before using the time deltas as optimization targets, inspect
  the generated field counts, effective `MaxPathSearchRange`, and route coverage
  for each sweep.
- First-frame mixed navigation is useful as a hitch detector, but the first
  short run is noisy and should be rerun canonically before comparing changes.
- `BenchmarkOccupant` implementing `ISteer` may move existing
  `NavSteeringBenchmarks` density numbers because those scans now include real
  steerable occupants. Treat future density numbers as the corrected baseline.

## Phase 6 - API Cleanup Before Alpha

**Severity:** Low to Medium  
**Primary files:**
`src/Trailblazer/Pathing/Search/FlowField/FlowFieldSurveyor.cs`,
`docs/wiki/Overview.md`, `README.md`

Keep these as fast-follow cleanup unless they block the earlier phases:

- Decide whether to deprecate or replace the legacy
  `SampleFlowVector(Vector3d, fields)` overload. The result-aware overload is
  the allocation-free runtime path.
- Revisit staged transition route-plan metadata if future routing records exact
  per-segment chart usage. Current endpoint-owner metadata is sufficient for the
  present benchmarks.
- Keep Trailblazer locally linked to GridForge until the next package release is
  ready, then switch project references back to package references and rerun
  cache-hit allocation guards.
- Reconsider reachability snapshot policy if real workloads create many distinct
  clearance/climb combinations.

### Phase 6 Notes - 2026-05-08

Implemented:

- Removed the legacy `FlowFieldSurveyor.SampleFlowVector(Vector3d, fields)`
  overload before alpha. The result-aware
  `SampleFlowVector(Vector3d, FlowFieldSurveyResult)` overload is now the public
  sampling path and uses result-owned `FlowFieldSamplingGrid` metadata.
- Updated flow-field tests to use result-aware sampling. No deprecated
  compatibility API or compatibility documentation was retained because the
  library is still pre-alpha.
- Added flow-field flood diagnostics to `PathingScenarioSummary`:
  `MaxPathSearchRange` and `ExtraFloodRange`. The pathing scenario preflight now
  checks open 32/64/128 field-count scaling, successful survey coverage, blocker
  large-flood configuration, and the effective request range used by each flood
  sweep.

Verification:

- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~FlowFieldSurveyorTests`
  passed with 45 tests.
- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~BenchmarkHarnessPreflightTests`
  passed with 6 tests.
- `dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- pathing-scenario --filter '*FlowFieldFloodRange*' --job short --runtimes net8.0`
  completed all 5 flood-range scenario benchmarks. WSL still reported the
  expected BenchmarkDotNet high-priority permission warning.
- `dotnet build Trailblazer.slnx --configuration Release` passed with 0 warnings
  and 0 errors.
- `dotnet test Trailblazer.slnx --configuration Release` passed with 916 tests.

Short-run flood evidence:

| Benchmark                              |       Phase 6 result | Signal                                                                 |
| -------------------------------------- | -------------------: | ---------------------------------------------------------------------- |
| `FlowFieldFloodRange_OpenPlane32`      | 111.53 ms, 577.68 KB | Small open sweep remains stable.                                       |
| `FlowFieldFloodRange_OpenPlane64`      |   729.78 ms, 2.25 MB | 64x64 sweep remains the anomalously slow open case.                    |
| `FlowFieldFloodRange_OpenPlane128`     |    80.07 ms, 9.02 MB | Allocation scales by field size, but short-run time remains inverted.  |
| `FlowFieldFloodRange_Blocker64Default` |   680.68 ms, 2.25 MB | Blocker sweep is close to the slow 64x64 open case.                    |
| `FlowFieldFloodRange_Blocker64Large`   |   594.16 ms, 2.25 MB | Larger extra flood range does not increase allocation in this fixture. |

Remaining work:

- All open fast-follow and carry-forward items were moved into
  [`benchmarkPerformanceFinalPlan.md`](../benchmarkPerformanceFinalPlan.md).

## Suggested Verification Commands

Targeted runs:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- a-star-path-request --filter '*' --job short --runtimes net8.0
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- flow-field-path-request --filter '*' --job short --runtimes net8.0
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- guide-cache --filter '*' --job short --runtimes net8.0
```

Full verification:

```bash
dotnet build Trailblazer.slnx --configuration Release
dotnet test Trailblazer.slnx --configuration Release
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- all --filter '*' --job short --runtimes net8.0
```
