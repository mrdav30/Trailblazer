# Benchmark Performance Next Plan

## Purpose

This document captures the next phase of Trailblazer performance work after completing phases 1
through 7 in `benchmarkPerformancePlan.md`.

The previous phase plan closed the planned alpha-hardening items. This plan starts from the fresh
full benchmark run on 2026-05-07 and focuses on the remaining high-value work: making the benchmark
harness trustworthy, reducing cold flow-field generation cost, and removing residual warm-guide
allocations.

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

These results are consistent with the completed phase work and should remain guarded:

| Area | Benchmark | Fresh result | Notes |
| --- | --- | ---: | --- |
| Navigation | `CombinedSteering_Density32` | 1.72 us, 0 B | Phase 6 held. |
| Navigation | `CombinedSteering_Density128` | 3.77 us, 0 B | Scales with occupant count, not fixed allocation. |
| Navigation | `CombinedSteering_Density512` | 9.24 us, 0 B | Still clean under density. |
| Flow field sampling | `SampleFlowVector_ExactVoxel` | 183.7 ns, 0 B | Phase 3 held. |
| Flow field sampling | `SampleFlowVector_FractionalPosition` | 530.8 ns, 0 B | Phase 3 held. |
| Guide cache | `InvalidateCacheFor_NoMatchingChart` | 3.43 us, 0 B | Reverse-index work held for A* pressure. |
| Transition fallback | `WarmGuide_FlowField_JumpLink` | 174.8 ns, 320 B | Phase 4/7 warm path is fast; allocation remains. |

### Remaining Heavy Costs

These are the clearest optimization targets once the harness issues below are corrected:

| Area | Benchmark | Fresh result | Signal |
| --- | --- | ---: | --- |
| Flow field cold generation | `ColdGuide_OpenPlane64` | 119.6 ms, 3.25 MB | Dominant cold-path cost. |
| Flow field cold generation | `ColdGuide_OpenPlane128` | 834.3 ms, 12.98 MB | Super-frame hitch territory. |
| Flow field raw survey | `RawSurvey_OpenPlane64` | 110.1 ms, 3.24 MB | Cost is mostly survey/result generation, not guide wrapper. |
| Flow field cache miss | `FlowFieldCacheMiss_BelowCapacity` | 46.7 ms, 812.8 KB | Consistent with 32x32 area scaling, still too high. |
| A* cache eviction path | `AStarCacheMiss_OverCapacity_Eviction` | 11.3 ms, 16.5 KB | Needs harness isolation before treating as implementation regression. |
| Warm guide reuse | `ManyStartWarmReuse_32Starts` | 6.23 us, 12.6 KB | About 393 B per requester; likely guide/request wrapper allocation. |

### Benchmark Quality Warnings

Several numbers are not safe optimization targets yet:

- `AStarPathRequestBenchmarks` still creates multiple `BenchmarkPathFixture` instances in one
  `GlobalSetup`. The fresh A* cold numbers are suspiciously flat across 32, 64, 256, and 1024
  scenarios, and `RawSurvey_OpenPlane32` reports 33.3 ns. Treat the full A* class as provisional.
- `VolumePathRequestBenchmarks` creates separate fixtures for direct and L-shape scenarios. The
  tiny raw/cold timings may be valid for the very small volume paths, but the setup should be
  consolidated or split before using the numbers as baselines.
- `NavSteeringBenchmarks` still has separate fixtures for direct LOS, guided A*, guided flow-field,
  and density. Density is already consolidated and trustworthy; direct/guided setup should be
  corrected before relying on full-class first-frame comparisons.
- Several cold benchmarks trigger BenchmarkDotNet `MinIterationTime` warnings. These should use
  more operations per measured iteration or a custom job before we compare deltas.
- `AStarCacheMiss_OverCapacity_Eviction` currently measures a potentially different A* route from
  the below-capacity miss. It should isolate eviction cost from path length/search cost.

## Phase 1 - Restore Benchmark Trust

**Severity:** High  
**Primary files:** `tests/Trailblazer.Benchmarks/Pathing/AStarPathRequestBenchmarks.cs`,
`tests/Trailblazer.Benchmarks/Pathing/VolumePathRequestBenchmarks.cs`,
`tests/Trailblazer.Benchmarks/Navigation/NavSteeringBenchmarks.cs`,
`tests/Trailblazer.Benchmarks/Pathing/GuideCacheBenchmarks.cs`

Fix this before making new A* or volume optimizations. Otherwise we risk chasing artifacts.

Deliverables:

- Consolidate each remaining multi-fixture benchmark class into one active world with spatially
  separated scenarios, or split scenarios into separate benchmark classes with one fixture each.
- Add post-setup validation after every scenario has been registered, not only immediately after
  each fixture setup.
- Add preflight invariants that fail fast when a benchmark resolves a stale or trivial route:
  expected route found, minimum waypoint count, expected chart key, and no unexpected cache leak.
- Add optional survey counters for raw/cold benchmarks: closed-node count, route length, field
  count, and cache hit/miss classification.
- Fix `AStarCacheMiss_OverCapacity_Eviction` so the measured operation isolates eviction. Either
  use equivalent-length requests for below/over-capacity cases or add a dedicated eviction-only
  benchmark with a stubbed or prebuilt result path.
- Adjust cold/first-frame benchmarks with `OperationsPerInvoke`, `InvocationCount`, or a custom
  job so BenchmarkDotNet stops warning about sub-100 ms measured iterations.

Exit criteria:

- A* cold guides scale with scenario size and path shape.
- `RawSurvey_OpenPlane32` is no longer a nanosecond-scale result.
- Full `all --job short` reports no stale-route artifacts.
- Harness fixes do not alter runtime library behavior.

### Phase 1 Notes - 2026-05-07

Implemented:

- Added benchmark preflight tests that instantiate the benchmark classes, run `GlobalSetup`, and
  verify the configured route methods still resolve after all scenarios are registered. The initial
  red run caught the stale-world artifacts in `AStarPathRequestBenchmarks` and
  `VolumePathRequestBenchmarks`.
- Consolidated `AStarPathRequestBenchmarks`, `VolumePathRequestBenchmarks`, and
  `NavSteeringBenchmarks` into one active fixture per class, with spatially separated scenarios.
- Added post-setup validation inside the benchmark classes so invalid requests fail during setup
  instead of producing trivial benchmark numbers.
- Added offset-aware benchmark chart helpers and adjacent request-pair generation for cache
  pressure cases.
- Changed `AStarCacheMiss_OverCapacity_Eviction` to use adjacent equivalent-cost requests so the
  measured delta is eviction pressure instead of a longer A* route.

Verification:

- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~BenchmarkHarnessPreflight`
  passed with 3 tests.
- `dotnet test Trailblazer.slnx --configuration Release` passed with 903 tests.
- `dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- all --filter '*' --job short --runtimes net8.0`
  completed all 53 benchmarks in 10 minutes 51 seconds with no setup or stale-route failures.
- The full A* run now reports `RawSurvey_OpenPlane32` at 2.431 ms and cold guides scaling by shape
  and size: open 32 at 2.820 ms, corridor 64 at 997.847 us, corridor 256 at 4.540 ms, corridor
  1024 at 32.658 ms, blocker 64 at 31.845 ms.
- The full guide-cache run reports `AStarCacheMiss_BelowCapacity` at 84.753 us and
  `AStarCacheMiss_OverCapacity_Eviction` at 155.692 us, removing the prior 11.3 ms path-length
  artifact.
- The full volume run reports `RawSurvey_DirectGasCorridor` at 74.120 us,
  `ColdGuide_DirectGasCorridor` at 80.752 us, and `ColdGuide_LShapeGasPath` at 98.583 us.
- The full navigation run kept density steering allocation-free at 1.731 us, 3.648 us, and
  9.639 us for 32, 128, and 512 occupants, and validated guided first-frame scenarios in the
  unified world.

Fast-follow observations:

- True cold and first-frame benchmarks still trigger BenchmarkDotNet `MinIterationTime` warnings for
  the smaller scenarios. Keep this in Phase 5 scenario design by batching first-frame operations
  without hiding cold-cache behavior.
- With the A* harness corrected, `ColdGuide_Heuristic_Octile` and
  `ColdGuide_Heuristic_Euclidean` are materially slower than Manhattan on the 64x64 open-plane
  fixture. Carry this as an A* investigation candidate once the flow-field cold path is addressed.

## Phase 2 - Reduce Cold Flow-Field Generation Cost

**Severity:** Critical  
**Primary files:** `src/Trailblazer/Pathing/Search/FlowField/FlowFieldSurveyor.cs`,
`src/Trailblazer/Pathing/Search/FlowField/FlowFieldSurveyResult.cs`

Fresh evidence:

- Phase 1 verified run: `ColdGuide_OpenPlane64` at 116.349 ms and 3.25 MB.
- Phase 1 verified run: `ColdGuide_OpenPlane128` at 796.391 ms and 12.98 MB.
- Phase 1 verified run: `RawSurvey_OpenPlane64` at 113.219 ms and 3.24 MB.
- Phase 1 verified run: `FlowFieldCacheMiss_BelowCapacity` at 44.092 ms and 812.8 KB.

Investigation order:

1. Split raw survey timing into flood cost, flow-vector generation cost, field dictionary
   allocation cost, chart-key storage cost, and sampling-map construction cost.
2. Inspect `FlowFieldSurveyResult.Reset()` and result reuse to see whether `Fields`, chart keys,
   and sampling metadata can be retained and cleared instead of reallocated.
3. Compare `SwiftDictionary<WorldVoxelIndex, FlowField>` against a dense planar direction buffer
   for open-plane flow fields. Preserve deterministic lookup behavior and chart invalidation.
4. Recheck lock scope around flood and generation. Avoid holding global survey locks longer than
   the shared heap truly requires.

Exit criteria:

- Preserve path correctness and deterministic field generation.
- Reduce cold 64x64 flow-field allocation materially below 3 MB.
- Reduce cold 128x128 generation enough that it no longer creates a multi-frame hitch on the
  benchmark machine, or document why the algorithmic cost is inherent.
- Add focused tests around reused result state and invalidation so stale fields cannot leak.

### Phase 2 Notes - 2026-05-07

Implemented:

- Replaced the duplicate sparse sampling-direction dictionary with bounded dense sampling buffers
  for dense flow fields, with sparse dictionary fallback when the closed field bounds are too wide
  for the number of fields.
- Added a prepass over the closed flood set to build per-grid sampling bounds before result
  materialization. This keeps open-plane sampling metadata compact without changing public
  `Fields` lookup behavior.
- Cached the normalized direction vector for each deterministic `SpatialDirection` once and reused
  it during flow-vector generation instead of normalizing every field direction.
- Fixed a flow-field generation edge check that skipped the `SouthWest` diagonal because it used
  `i > 6` instead of GridForge's diagonal predicate.
- Fixed the diagonal leg axis mapping shared by flow-field, A*, volume, and reachability checks.
  GridForge offsets use X for East/West and Z for North/South; the previous helper logic had those
  positive-axis legs swapped in several pathing components.

Verification:

- Added `FlowFieldSurveyor_FindPath_ShouldKeepOpenPlane16ColdAllocationsUnderBudget`, which failed
  at 203,808 B before the sampling metadata change and now stays below the 180,000 B guard.
- Added `FlowFieldSurveyor_HasValidDiagonalLegs_ShouldUseGridAxesForHorizontalDiagonals`, which
  failed before the shared diagonal-leg mapping fix and now passes for the southeast and northwest
  horizontal diagonal cases.
- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~FlowFieldSurveyor`
  passed with 43 tests.
- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~AStar`
  passed with 65 tests.
- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~Volume`
  passed with 106 tests.
- `dotnet test Trailblazer.slnx --configuration Release` passed with 906 tests.

Benchmarks:

| Benchmark | Phase 1 verified | Phase 2 result | Signal |
| --- | ---: | ---: | --- |
| `ColdGuide_OpenPlane64` | 116.349 ms, 3.25 MB | 114.993 ms, 2.36 MB | Allocation down materially; time flat. |
| `ColdGuide_OpenPlane128` | 796.391 ms, 12.98 MB | 803.909 ms, 9.45 MB | Allocation down materially; time slightly worse within short-run noise. |
| `RawSurvey_OpenPlane64` | 113.219 ms, 3.24 MB | 114.605 ms, 2.36 MB | Confirms improvement is memory-focused, not flood-time-focused. |
| `FlowFieldCacheMiss_BelowCapacity` | 44.092 ms, 812.8 KB | 44.381 ms, 578.86 KB | Cache miss allocation down about 29%; time flat. |
| `SampleFlowVector_ExactVoxel` | 183.7 ns, 0 B | 169.2 ns, 0 B | Sampling stayed allocation-free. |
| `SampleFlowVector_FractionalPosition` | 530.8 ns, 0 B | 514.1 ns, 0 B | Sampling stayed allocation-free. |

Remaining findings:

- The 128x128 cold path is still a multi-frame hitch. The dense sampling change removed duplicate
  metadata storage, but the dominant time remains flood expansion plus public result dictionary
  materialization.
- `FlowFieldSurveyResult.Reset()` does not currently unlock meaningful reuse by itself because the
  cache does not recycle reset flow-field result instances on raw cold surveys. Retaining fields
  there would add stale-state risk without an accompanying result-pool design.
- `PathHeap` still allocates per tracked partition metadata during cold surveys. A lower-allocation
  heap metadata design is the next likely cold-flow-field allocation target, but it would touch A*
  and volume surveyors too and should be handled as a separate focused phase.
- Reducing the global survey lock scope still requires splitting shared mutable surveyor scratch
  state into per-call or pooled state. The current phase kept lock behavior unchanged.
- BenchmarkDotNet still reports high-priority permission warnings in WSL and `MinIterationTime`
  warnings for short cache-miss scenarios. Continue handling those under Phase 5 scenario design.

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

- Add allocation microbenchmarks for request construction and request-key construction separately
  from guide resolution.
- Add a non-capturing cache miss factory path or a `TryCheckout`-then-create flow if it removes
  warm-hit allocations without making miss logic harder to reason about.
- Consider caching immutable request keys inside request objects, if it does not widen public API
  semantics or create stale-key risk.
- Defer guide wrapper pooling unless warm guide allocation remains after cheaper fixes.

Exit criteria:

- A*/FlowField/Volume warm guide hits allocate zero or near-zero bytes.
- `ManyStartWarmReuse_32Starts` no longer scales linearly in allocation per requester.
- Existing allocation guards remain deterministic and stable under Release.

## Phase 4 - Measure Mixed Cache Pressure

**Severity:** Medium  
**Primary files:** `tests/Trailblazer.Benchmarks/Pathing/GuideCacheBenchmarks.cs`,
`src/Trailblazer/Pathing/Search/PathGuideFactory.cs`

The Phase 1 invalidation benchmark now seeds A* to capacity, but it still does not exercise a full
mixed cache containing A*, flow-field, volume, and hybrid transition entries.

Deliverables:

- Add mixed-cache invalidation benchmarks with all cache families at or near capacity.
- Add no-match and matching-chart variants under mixed pressure.
- Add cull benchmarks with realistic stale/active ratios for each guide type.
- Track invalidation cardinality: entries scanned, entries matched, entries removed.

Exit criteria:

- Dynamic chart invalidation remains bounded by affected entries, not total cache size.
- No-match invalidation stays allocation-free under mixed cache pressure.

## Phase 5 - Add Scenario Benchmarks

**Severity:** Medium  
**Primary files:** `tests/Trailblazer.Benchmarks/Navigation/*`,
`tests/Trailblazer.Benchmarks/Pathing/*`

The current benchmark suite is strong at isolated hot paths. The next suite should add runtime-like
workloads so regressions show up before integration tests or game hosts feel them.

New benchmarks to add:

- Multi-agent fixed-step steering: 100 and 500 agents with a mix of direct LOS, A*, flow-field,
  and combined steering.
- Dynamic obstacle update: chart invalidation followed by a repath wave.
- Flow-field sharing: many agents with same destination, varying start positions, and explicit
  allocation reporting per requester.
- First-frame navigation setup with enough operations per iteration to remove BenchmarkDotNet
  `MinIterationTime` warnings.
- Reachability snapshot first-hit cost for distinct `(unitSize, maxClimbHeight)` combinations.
- Cache-key/request creation microbenchmarks for A*, flow-field, volume, and transition requests.
- Flow-field flood-range sweep: 32x32, 64x64, 128x128, blocker field, and large flood range.

Exit criteria:

- Scenario benchmarks have clear preflight assertions and route-shape counters.
- The suite distinguishes cold hitches, steady-state per-frame work, and cache lifecycle pressure.

## Phase 6 - API Cleanup Before Alpha

**Severity:** Low to Medium  
**Primary files:** `src/Trailblazer/Pathing/Search/FlowField/FlowFieldSurveyor.cs`,
`docs/wiki/OVERVIEW.md`, `README.md`

Keep these as fast-follow cleanup unless they block the earlier phases:

- Decide whether to deprecate or replace the legacy `SampleFlowVector(Vector3d, fields)` overload.
  The result-aware overload is the allocation-free runtime path.
- Revisit staged transition route-plan metadata if future routing records exact per-segment chart
  usage. Current endpoint-owner metadata is sufficient for the present benchmarks.
- Keep Trailblazer locally linked to GridForge until the next package release is ready, then switch
  project references back to package references and rerun cache-hit allocation guards.
- Reconsider reachability snapshot policy if real workloads create many distinct clearance/climb
  combinations.

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
