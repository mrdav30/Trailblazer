# Benchmark Performance Final Plan

## Purpose

This document carries the remaining performance work extracted from
`done/benchmarkPerformanceNextPlan.md` after the alpha-hardening benchmark phases completed.

The completed work made the benchmark harness trustworthy, removed warm-guide allocations, reduced
flow-field sampling metadata allocation, added mixed-cache and runtime-like scenario coverage, and
kept the local GridForge project references in place for follow-up testing.

This plan is intentionally narrower than the previous phase plans. These items are no longer
required to finish the current alpha-prep sweep, but they are the highest-signal candidates for the
next performance pass.

## Current Baseline

Last verified full-suite state from the completed plan:

- `dotnet build Trailblazer.slnx --configuration Release` passed with 0 warnings and 0 errors.
- `dotnet test Trailblazer.slnx --configuration Release` passed with 916 tests.
- `dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- pathing-scenario --filter '*FlowFieldFloodRange*' --job short --runtimes net8.0`
  completed all 5 flood-range scenario benchmarks.
- Local GridForge project references are still intentionally retained until the next package release
  is ready.

Important recent benchmark signals:

| Area | Benchmark | Latest signal |
| --- | --- | ---: |
| Flow-field cold generation | `ColdGuide_OpenPlane128` | 803-834 ms, 9.45-12.98 MB depending on phase/run |
| Flow-field raw survey | `RawSurvey_OpenPlane64` | about 114 ms, 2.36 MB after sampling metadata work |
| Flow-field flood scenario | `FlowFieldFloodRange_OpenPlane64` | 802.19 ms, 2.25 MB in the canonical Phase 1 run |
| Flow-field flood scenario | `FlowFieldFloodRange_OpenPlane128` | 86.20 ms, 9.02 MB in the canonical Phase 1 run |
| Guide cache warm hits | A*/FlowField/Volume cache hits | 0 B after guide-wrapper pooling and miss-path split |
| Flow-field warm guide | `WarmGuide_OpenPlane128` | 142.1 ns, 0 B in the canonical Phase 1 run |
| Navigation density | `CombinedSteering_Density512` | 28.44 us, 0 B after corrected steerable occupants |

## Phase 1 - Canonical Benchmark Reruns

**Severity:** Medium  
**Primary files:** `tests/Trailblazer.Benchmarks/*`,
`tests/Trailblazer.Tests/Benchmarks/BenchmarkHarnessPreflight.Tests.cs`

Before making another broad optimization pass, refresh the benchmark baseline with canonical runs
for the noisy or short-run-sensitive cases.

Deliverables:

- Add or document canonical commands for the known watchlist:
  - `WarmGuide_OpenPlane128`
  - `FirstFrameMixedSteering_100Agents`
  - `FirstFrameMixedSteering_500Agents`
  - `FlowFieldFloodRange_OpenPlane32`
  - `FlowFieldFloodRange_OpenPlane64`
  - `FlowFieldFloodRange_OpenPlane128`
  - `FlowFieldFloodRange_Blocker64Default`
  - `FlowFieldFloodRange_Blocker64Large`
- Keep using category filters instead of broad wildcard filters when benchmark class names would
  overmatch request-related methods.
- Decide whether the remaining `MinIterationTime` warnings need benchmark batching, a custom job,
  or documented acceptance because allocation/cardinality guards are the real authority.
- Record corrected baseline numbers for `NavSteeringBenchmarks` density cases now that
  `BenchmarkOccupant` implements `ISteer`.

Exit criteria:

- The final benchmark watchlist has stable command lines and fresh results.
- Remaining BenchmarkDotNet warnings are either removed or explicitly documented as environmental
  noise.
- No implementation optimization is started from a known noisy number.

### Phase 1 Notes - 2026-05-08

Status: complete.

Canonical commands:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- flow-field-path-request --filter '*WarmGuide_OpenPlane128*' --runtimes net8.0
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- navigation-scenario --filter '*FirstFrameMixedSteering*' --runtimes net8.0
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- nav-steering --filter '*CombinedSteering_Density*' --runtimes net8.0
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- pathing-scenario --filter '*FlowFieldFloodRange*' --runtimes net8.0
```

Canonical benchmark evidence:

| Benchmark | Phase 1 canonical result | Signal |
| --- | ---: | --- |
| `WarmGuide_OpenPlane128` | 142.1 ns, 0 B | The prior 7.7 us short-run result was setup/JIT noise. |
| `FirstFrameMixedSteering_100Agents` | 19.03 ms, 38.67 KB | Stable hitch detector baseline. |
| `FirstFrameMixedSteering_500Agents` | 12.82 ms, 18.45 KB | Stable but not monotonic versus 100 agents; use as separate scenario signal. |
| `CombinedSteering_Density32` | 3.647 us, 0 B | Corrected steerable-occupant baseline. |
| `CombinedSteering_Density128` | 10.783 us, 0 B | Corrected steerable-occupant baseline. |
| `CombinedSteering_Density512` | 28.444 us, 0 B | Corrected steerable-occupant baseline. |
| `FlowFieldFloodRange_OpenPlane32` | 121.15 ms, 577.68 KB | Canonical small open flood baseline. |
| `FlowFieldFloodRange_OpenPlane64` | 802.19 ms, 2.25 MB | Canonical anomalous slow open case. |
| `FlowFieldFloodRange_OpenPlane128` | 86.20 ms, 9.02 MB | Allocation scales normally, but time remains inverted. |
| `FlowFieldFloodRange_Blocker64Default` | 750.41 ms, 2.25 MB | Canonical blocker baseline; two high outliers were removed. |
| `FlowFieldFloodRange_Blocker64Large` | 752.00 ms, 2.25 MB | Large extra flood range does not change allocation in this fixture. |

Decisions:

- These canonical runs did not produce `MinIterationTime` warnings. The remaining recurring
  BenchmarkDotNet warning is WSL's high-priority permission warning; treat it as environmental.
- The first-frame mixed steering benchmarks should stay hitch detectors, not per-agent scaling
  curves, because the 500-agent fixture is faster than the 100-agent fixture in both short and
  canonical runs.
- The flood-range inversion is confirmed by canonical measurement. Phase 2 should profile the 64x64
  open and blocker cases directly before making runtime changes.
- Density numbers from earlier phases are superseded by the corrected `ISteer` occupant baselines
  above.

## Phase 2 - Profile Cold Flow-Field Generation

**Severity:** Critical  
**Primary files:** `src/Trailblazer/Pathing/Search/FlowField/FlowFieldSurveyor.cs`,
`src/Trailblazer/Pathing/Search/FlowField/FlowFieldSurveyResult.cs`,
`tests/Trailblazer.Benchmarks/Pathing/FlowFieldPathRequestBenchmarks.cs`,
`tests/Trailblazer.Benchmarks/Pathing/PathingScenarioBenchmarks.cs`

Cold flow-field generation is still the dominant remaining cost. The 128x128 cold path remains a
multi-frame hitch, and the scenario flood sweep has a suspicious 64x64-vs-128x128 timing inversion
while allocation scales normally.

Deliverables:

- Split raw survey timing into flood expansion, flow-vector generation, public field dictionary
  materialization, chart-key capture, and sampling-grid construction.
- Temporarily add or expose diagnostic counters for generated field count, closed partition count,
  route coverage, effective `MaxPathSearchRange`, and `ExtraFloodRange`; remove the diagnostics
  once the evidence is captured unless they are intentionally promoted to benchmark-only
  infrastructure.
- Use those counters to explain why `FlowFieldFloodRange_OpenPlane64` and blocker 64 are slower
  than `FlowFieldFloodRange_OpenPlane128` in short runs.
- Re-run the flood scenarios with canonical settings before treating the timing inversion as a
  runtime bug.

Exit criteria:

- The dominant cold-flow-field cost is isolated to one or two measured stages.
- The flood-sweep timing inversion is either explained as benchmark artifact or captured as a
  concrete runtime hotspot with a focused repro.
- Any implementation candidate has a before/after benchmark and correctness guard.

### Phase 2 Notes - 2026-05-08

Status: complete.

Durable deliverable:

- Phase 2 leaves no runtime, benchmark, or test instrumentation in the library.
- The retained work product is this plan update: canonical Phase 2 numbers, the isolated
  flood-expansion bottleneck, the 8192-vs-32768 metadata-capacity explanation, and the narrowed
  Phase 3 implementation target.
- The only code change worth carrying forward from the temporary investigation is the next-phase
  requirement to reproduce and fix `PathHeap` metadata lookup/probe behavior outside the full survey
  loop.

Temporary local instrumentation was used to split one-off flow-field timing into flood expansion,
sampling-grid construction, flow-vector generation, field storage, chart-key capture, result-array
materialization, and result creation. That profiling surface was removed after the measurements
below were captured; the library should not ship with Phase 2 diagnostic hooks.

Canonical flood rerun during Phase 2:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- pathing-scenario --filter '*FlowFieldFloodRange*' --runtimes net8.0
```

| Benchmark | Phase 2 canonical result | Allocation |
| --- | ---: | ---: |
| `FlowFieldFloodRange_OpenPlane32` | 97.90 ms | 577.68 KB |
| `FlowFieldFloodRange_OpenPlane64` | 801.70 ms | 2308.68 KB |
| `FlowFieldFloodRange_OpenPlane128` | 84.67 ms | 9232.68 KB |
| `FlowFieldFloodRange_Blocker64Default` | 706.15 ms | 2300.68 KB |
| `FlowFieldFloodRange_Blocker64Large` | 712.56 ms | 2300.68 KB |

The profiling split isolates the slow cases to flood expansion, not flow-vector generation or public
result materialization:

| Profile sequence | Fields | Heap meta capacity | Flood time | Total time |
| --- | ---: | ---: | ---: | ---: |
| Open64 first isolated run | 4096 | 8192 | 696.30 ms | 706.33 ms |
| Open64 second isolated run | 4096 | 8192 | 703.09 ms | 711.57 ms |
| Open128 first run | 16384 | 32768 | 1255.84 ms | 1294.57 ms |
| Open64 after Open128 grew metadata | 4096 | 32768 | 26.24 ms | 33.14 ms |
| Blocker64Default after Open128 grew metadata | 3840 | 32768 | 20.94 ms | 27.50 ms |
| Blocker64Large after Open128 grew metadata | 3840 | 32768 | 20.75 ms | 27.14 ms |

Explanation:

- BenchmarkDotNet runs each flood benchmark in an isolated process. The Open64 and blocker64 cases
  warm only to an 8192-entry `PathHeap` metadata dictionary, where these deterministic
  `SolidChartPartition` keys hit a pathological lookup/probe profile during flood expansion.
- Open128 pays a first-run capacity-growth cost, grows the same metadata dictionary to 32768
  entries, and BenchmarkDotNet measures the warmed 32768-entry state. That makes the larger case
  look faster despite generating four times as many fields.
- The inversion is therefore a benchmark-process-state artifact and a real runtime hotspot: flood
  expansion is overly sensitive to `PathHeap` metadata backing capacity and key distribution.
- `ExtraFloodRange` is not the blocker64 cause in this fixture. Both blocker variants close the same
  3840 fields, allocate the same amount, and run in the same ~27-29 ms range once the metadata
  dictionary is in the non-pathological capacity state.

Phase 3 should treat `PathHeap` metadata as both an allocation problem and a lookup/probe
distribution problem. Because Trailblazer, SwiftCollections, and GridForge are all owned in this
stack, the implementation should choose the right layer after a focused repro: either a reusable
Trailblazer heap metadata strategy with deterministic mixed keys, or a SwiftCollections-level
probing/hash-spreading fix if the dictionary behavior is generally pathological for structured
integer hashes.

## Phase 3 - Lower Cold Survey Allocation

**Severity:** High  
**Primary files:** `src/Trailblazer/Pathing/Search/Support/PathHeap.cs`,
`src/Trailblazer/Pathing/Search/AStar/*`,
`src/Trailblazer/Pathing/Search/FlowField/*`,
`src/Trailblazer/Pathing/Search/Volume/*`

`PathHeap` still allocates per tracked partition metadata during cold surveys. This affects
flow-field, A*, and volume surveyors, so it needs a focused shared-data-structure pass rather than a
flow-field-only patch.

Deliverables:

- Measure `PathHeap` metadata allocation independently from survey result materialization.
- Reproduce the Phase 2 8192-capacity metadata lookup hotspot outside the full survey loop so the
  fix can be validated without BenchmarkDotNet process-state noise.
- Design a lower-allocation metadata strategy that preserves deterministic ordering and existing
  path scoring behavior.
- Include lookup/probe distribution in the metadata strategy, not just allocation count.
- Add allocation guards around representative A*, flow-field, and volume cold surveys.
- Avoid changing survey behavior, tie-break order, or traversal validity while replacing metadata
  storage.

Exit criteria:

- Cold flow-field allocation drops below the current 2.36 MB 64x64 baseline without regressing A*
  or volume.
- Shared surveyors keep deterministic results on existing pathing tests.
- The new heap metadata design is reusable and does not bury generic infrastructure inside one
  surveyor.

### Phase 3 Notes - 2026-05-08

Status: complete.

Implemented deliverables:

- Added a focused `PathHeap` metadata allocation guard. The red run reproduced the old per-node
  metadata cost at 131,072 B for a 4096-node reused-capacity replay.
- Replaced `PathHeapMeta` class instances with struct metadata stored in a reusable
  `PathHeapMetadata<TNode>` table.
- Kept the metadata table in Trailblazer rather than SwiftCollections because the heap access
  pattern is specialized: add/update, lookup, closed-state marking, closed enumeration, and full
  fast-clear reuse with no single-entry removal.
- The new table uses deterministic hash mixing, linear probing, occupied-slot clearing, and a
  struct closed-node enumerable so repeated surveys do not allocate per tracked node or per closed
  enumeration.
- Added `PathHeapBenchmarks` for isolated metadata replay at the Phase 2 8192-entry and warmed
  32768-entry shapes.
- Added representative cold-survey allocation guards for A*, flow-field, and volume surveyors.

Root-cause details:

- The original Phase 2 hotspot was `SwiftDictionary` metadata lookup sensitivity for structured
  `SolidChartPartition` hashes at 8192 entries.
- A direct custom-comparer/hash-spread attempt improved isolated insertion probes but regressed the
  real repeated 128x128 flood replay because repeated missing-neighbor lookups interacted badly with
  the dictionary's quadratic probing.
- The final fix avoids that generic dictionary access pattern for heap metadata and keeps behavior
  scoped to the survey hot path.

Benchmark evidence:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- path-heap --filter '*MetadataReplay*' --job short --runtimes net8.0
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- pathing-scenario --filter '*FlowFieldFloodRange*' --job short --runtimes net8.0
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- pathing-scenario --filter '*FlowFieldFloodRange*' --runtimes net8.0
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- a-star-path-request --filter '*RawSurvey_OpenPlane32*' --job short --runtimes net8.0
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- volume-path-request --filter '*RawSurvey_DirectGasCorridor*' --job short --runtimes net8.0
```

| Benchmark | Phase 3 result | Allocation |
| --- | ---: | ---: |
| `MetadataReplay_Structured4096` at 8192 warmup | 9.889 ms | 0 B |
| `MetadataReplay_Structured4096` at 32768 warmup | 8.974 ms | 0 B |
| `FlowFieldFloodRange_OpenPlane32` | 1.742 ms | 545.51 KB |
| `FlowFieldFloodRange_OpenPlane64` | 10.066 ms | 2180.51 KB |
| `FlowFieldFloodRange_OpenPlane128` | 51.369 ms | 8720.51 KB |
| `FlowFieldFloodRange_Blocker64Default` | 8.676 ms | 2180.51 KB |
| `FlowFieldFloodRange_Blocker64Large` | 8.829 ms | 2180.51 KB |
| `RawSurvey_OpenPlane32` | 2.019 ms | 4.04 KB |
| `RawSurvey_DirectGasCorridor` | 63.07 us | 328 B |

Flood rows are from the canonical command; the isolated heap, A*, and volume rows are targeted
short-run checks.

Compared to the Phase 2 flood baseline, the 64x64 flow-field survey allocation dropped below the
2.36 MB target and the 8192-capacity timing inversion is gone. The canonical flood timings are now
size-shaped: 32 < 64 < 128.

Verification:

- `dotnet build Trailblazer.slnx --configuration Release` passed with 0 warnings and 0 errors.
- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~PathHeapTests` passed 10 tests.
- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter "FullyQualifiedName~AStarSurveyor_FindPath_ShouldKeepOpenPlane16ColdAllocationsUnderBudget|FullyQualifiedName~FlowFieldSurveyor_FindPath_ShouldKeepOpenPlane16ColdAllocationsUnderBudget|FullyQualifiedName~VolumeSurveyor_FindPath_ShouldKeepOpenPlane8ColdAllocationsUnderBudget"` passed 3 tests.
- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~FlowFieldSurveyor` passed 43 tests.
- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~AStar` passed 66 tests.
- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~VolumeSurveyor` passed 16 tests.
- `dotnet test Trailblazer.slnx --configuration Release` passed 917 tests.

Fast-follow:

- Consider a separate SwiftCollections investigation for quadratic-probing sensitivity with
  structured object hashes. Trailblazer no longer depends on that path for heap metadata, so this is
  not blocking the alpha performance pass.

## Phase 4 - Split Survey Scratch State and Lock Scope

**Severity:** Medium to High  
**Primary files:** `src/Trailblazer/Pathing/Search/*Surveyor.cs`,
`src/Trailblazer/Pathing/Search/Support/SurveyorLock.cs`

The global survey lock remains broad because surveyors own shared mutable scratch state. Narrowing
that lock requires per-call or pooled scratch state and careful deterministic cleanup.

Deliverables:

- Inventory each surveyor's mutable scratch fields and classify them as per-call, pooled, or truly
  shared.
- Introduce focused scratch-state containers where they reduce lock scope without increasing steady
  allocations.
- Keep path expansion order deterministic when scratch state is pooled or reused.
- Add tests around concurrent or interleaved survey calls if the public threading contract is
  widened.

Exit criteria:

- Lock scope is limited to the shared state that truly requires it.
- Cold and warm benchmark results do not regress from scratch-state pooling.
- No stale survey state can leak across failed, empty, or successful results.

### Phase 4 Notes - 2026-05-09

Status: complete.

Scratch inventory:

- `AStarSurveyor` keeps `_heap`, `_meta`, `_rawPath`, `_waypoints`, `_chartKeys`, and `_request`
  as reusable per-surveyor scratch.
- `FlowFieldSurveyor` keeps `_heap`, `_chartKeys`, `_samplingGrids`, `_samplingGridBuilders`, and
  `_request` as reusable per-surveyor scratch. Its normalized direction lookup is immutable shared
  data and does not need locking.
- `VolumeSurveyor` keeps `_heap`, `_meta`, `_rawPath`, `_waypoints`, `_chartKeys`, and `_request`
  as reusable per-surveyor scratch.
- None of the mutable survey scratch above is truly shared across surveyor types. The previous
  global lock serialized independent A*, flow-field, and volume surveys only because the lock lived
  outside the surveyor instances.

Implemented deliverables:

- Replaced the static global survey lock with a focused `SurveyorLock` instance on each shared
  surveyor.
- Kept the existing per-surveyor scratch reuse model, so no steady-state scratch allocation was
  added and deterministic expansion order remains unchanged.
- Preserved serialization for concurrent calls to the same shared surveyor instance, which still
  protects that surveyor's reusable scratch fields.
- Added coverage proving the old `GlobalLock` is gone, each shared surveyor has an independent
  scratch lock, and mixed concurrent A*, flow-field, and volume requests complete without stale
  survey state leaking between results.

Benchmark evidence:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- flow-field-path-request --filter '*RawSurvey_OpenPlane64*' --job short --runtimes net8.0
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- a-star-path-request --filter '*RawSurvey_OpenPlane32*' --job short --runtimes net8.0
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- volume-path-request --filter '*RawSurvey_DirectGasCorridor*' --job short --runtimes net8.0
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- flow-field-path-request --filter '*WarmGuide_OpenPlane128*' --job short --runtimes net8.0
```

| Benchmark | Phase 4 result | Allocation |
| --- | ---: | ---: |
| `RawSurvey_OpenPlane64` | 9.628 ms | 2.13 MB |
| `RawSurvey_OpenPlane32` | 2.000 ms | 4.04 KB |
| `RawSurvey_DirectGasCorridor` | 66.51 us | 328 B |
| `WarmGuide_OpenPlane128` | 147.3 ns | 0 B |

The short-run flow-field result remains in the post-Phase-3 range, and the warm guide hit remains
allocation-free. BenchmarkDotNet still reports the known high-priority permission warning under WSL,
plus `MinIterationTime` warnings for the tiny short-run raw survey checks; these do not indicate a
new Phase 4 regression.

Verification:

- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~SurveyorLockTests` passed 2 tests.
- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter "FullyQualifiedName~Surveyor"` passed 64 tests.
- `dotnet build Trailblazer.slnx --configuration Release` passed with 0 warnings and 0 errors.
- `dotnet test Trailblazer.slnx --configuration Release` passed 919 tests.

Fast-follow:

- No new fast-follow items were found in this phase. A more aggressive per-call or pooled scratch
  model could widen concurrency for multiple calls to the same surveyor type, but the current shared
  surveyor API still relies on mutable reusable scratch and should remain serialized per type until
  a measured workload justifies the added ownership complexity.

## Phase 5 - Cache Benchmark Infrastructure

**Severity:** Medium  
**Primary files:** `tests/Trailblazer.Benchmarks/Pathing/GuideCacheBenchmarks.cs`,
`src/Trailblazer/Pathing/Search/Support/Survey/ReusableSurveyResultCache.cs`

Mixed-cache pressure is now measured, but full-capacity mixed invalidation still requires expensive
real path generation during setup. A benchmark-only seed hook or internal cache benchmark would let
us isolate cache behavior without timing cold route generation.

Deliverables:

- Add a benchmark-only or internal-test-only way to seed `ReusableSurveyResultCache<T>` with known
  chart ownership and cache-key metadata.
- Measure full-capacity mixed invalidation with 128 entries per cache family.
- Keep cardinality preflights for entries scanned, matched, and removed.
- Keep runtime API clean; do not add public cache seed hooks just for BenchmarkDotNet.

Exit criteria:

- Full-capacity mixed invalidation can be benchmarked without cold path generation dominating setup.
- No-match invalidation remains allocation-free under full mixed pressure.
- Mutation benchmarks either avoid `MinIterationTime` warnings or clearly defer to direct
  allocation/cardinality guards.

### Phase 5 Notes - 2026-05-09

Status: complete.

Implemented deliverables:

- Added internal cache seeding support to `ReusableSurveyResultCache<T>` so benchmark and test
  fixtures can populate exact request-key/chart-owner shapes without running path generation.
- Added internal `PathGuideFactory` seed helpers for A*, flow-field, volume, and hybrid route-plan
  caches. These remain internal and do not change the public runtime API.
- Updated mixed guide-cache benchmarks from 32 entries per family to full cache capacity: 128 A*,
  128 flow-field, 128 volume, and 128 hybrid route-plan entries.
- Switched mixed invalidation cardinality to read the cache chart index before mutation, so
  `EntriesScanned`, `EntriesMatched`, and `EntriesRemoved` are pinned by the actual seeded cache
  shape.
- Removed the old mixed-cache real route generation setup from `GuideCacheBenchmarks`; the mixed
  pressure benchmarks now isolate cache behavior from cold survey setup cost.
- Fixed a small measured allocation in stale culling by reusing a per-cache stale-key buffer.

Cardinality shape:

| Scenario | Entries seeded | Indexed/matched entries | Expected removal |
| --- | ---: | ---: | ---: |
| Mixed no-match invalidation | 512 total | 0 | 0 |
| Mixed solid invalidation | 512 total | 256 | 256 |
| Mixed volume invalidation | 512 total | 128 | 128 |
| Mixed hybrid destination invalidation | 512 total | 128 | 128 |
| Mixed fresh cull | 512 total | n/a | 0 |
| Mixed stale cull with active quarter | 512 total, 96 active | n/a | 416 |

Benchmark evidence:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- guide-cache --filter '*InvalidateMixedCacheFor*' --job short --runtimes net8.0
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- guide-cache --filter '*CullMixedCache*' --job short --runtimes net8.0
```

| Benchmark | Phase 5 result | Allocation |
| --- | ---: | ---: |
| `InvalidateMixedCacheFor_NoMatchingChart` | 9.538 us | 0 B |
| `InvalidateMixedCacheFor_MatchingSolidChart` | 99.059 us | 0 B |
| `InvalidateMixedCacheFor_MatchingVolumeChart` | 60.839 us | 0 B |
| `InvalidateMixedCacheFor_MatchingHybridChart` | 79.580 us | 0 B |
| `CullMixedCache_NoStale` | 53.70 us | 0 B |
| `CullMixedCache_StaleWithActiveQuarter` | 157.91 us | 0 B |

BenchmarkDotNet still reports `MinIterationTime` warnings for these mutation benchmarks. The warning
is expected because the measured operations are intentionally tiny and cardinality/allocation guards
are the authority for this phase.

Verification:

- `dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter "FullyQualifiedName~ReusableSurveyResultCacheTests|FullyQualifiedName~BenchmarkHarnessPreflightTests.GuideCacheBenchmarks_ShouldSeedMixedCachePressureScenarios_AfterGlobalSetup"` passed 10 tests.
- `dotnet build Trailblazer.slnx --configuration Release` passed with 0 warnings and 0 errors.
- `dotnet test Trailblazer.slnx --configuration Release` passed 921 tests.

Fast-follow:

- No new fast-follow items were found. The cache seed helpers are internal-only; remove or revisit
  them only if the benchmark project stops using Trailblazer internals.

## Phase 6 - Reachability Snapshot Policy

**Severity:** Medium  
**Primary files:** `src/Trailblazer/Pathing/*Reachability*`,
`tests/Trailblazer.Benchmarks/Pathing/PathingScenarioBenchmarks.cs`

Reachability first-hit cost is visible for distinct `(unitSize, maxClimbHeight)` combinations.
Current snapshot caching is acceptable for the measured scenarios, but real hosts may create more
clearance/climb combinations than the current benchmark covers.

Deliverables:

- Add a workload-shaped benchmark for many distinct clearance and climb combinations.
- Compare first-hit cost, steady-hit cost, retained memory, and invalidation behavior.
- Decide whether snapshot eviction, pooling, or capped policy is needed.
- Preserve correctness for unreachable split-island cases and chart updates.

Exit criteria:

- Reachability snapshot behavior has a realistic budget for expected alpha host usage.
- Eviction or pooling is added only if measured workloads justify the complexity.
- Snapshot invalidation remains deterministic and chart-safe.

## Phase 7 - Transition Route Metadata

**Severity:** Low to Medium  
**Primary files:** `src/Trailblazer/Pathing/Search/Hybrid/*`,
`src/Trailblazer/Pathing/Transitions/*`,
`tests/Trailblazer.Benchmarks/Pathing/TransitionFallbackBenchmarks.cs`

Endpoint-owner metadata is enough for the present benchmarks. Exact per-segment chart metadata
should wait until route diagnostics or invalidation needs prove it useful.

Deliverables:

- Identify any real scenario where endpoint-owner metadata fails to support chart invalidation,
  diagnostics, or benchmark attribution.
- If needed, add per-segment chart ownership to route-plan records.
- Add tests for transition anchors that start or end exactly on chart boundaries.

Exit criteria:

- Transition route metadata stays as small as possible for alpha.
- Any metadata expansion is justified by a concrete invalidation or diagnostic gap.

## Phase 8 - A* Heuristic Investigation

**Severity:** Medium  
**Primary files:** `src/Trailblazer/Pathing/Search/AStar/*`,
`tests/Trailblazer.Benchmarks/Pathing/AStarPathRequestBenchmarks.cs`

After the harness was corrected, `ColdGuide_Heuristic_Octile` and
`ColdGuide_Heuristic_Euclidean` were materially slower than Manhattan on the 64x64 open-plane
fixture.

Deliverables:

- Canonically rerun the A* heuristic benchmarks.
- Capture closed-node count, route cost, and waypoint count for each heuristic.
- Inspect heuristic math, tie-break behavior, and open-list churn.
- Decide whether default heuristic guidance or implementation changes are warranted.

Exit criteria:

- The slower Octile/Euclidean results are explained with route-shape and closed-node evidence.
- Any heuristic change preserves path correctness and deterministic tie-breaking.

## Phase 9 - Package Reference Restore

**Severity:** Low  
**Primary files:** `src/Trailblazer/Trailblazer.csproj`,
`tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj`,
`tests/Trailblazer.Tests/Trailblazer.Tests.csproj`

Trailblazer is still locally linked to GridForge for rapid private-stack testing. Once the next
GridForge package is published, switch back to package references and rerun the allocation guards.

Deliverables:

- Replace local GridForge project references with the published package version.
- Run cache-hit allocation guards and full Release verification.
- Run the benchmark smoke suite that previously caught GridForge-related regressions.

Exit criteria:

- Package references match the published dependency stack.
- Cache-hit allocation guards still pass against packages.
- No local mount dependency remains in the alpha-ready project files.

## Completion Gate

This plan is complete when:

- Cold flow-field generation has either been reduced below the accepted alpha hitch budget or the
  remaining cost is documented as algorithmic and intentionally deferred.
- BenchmarkDotNet watchlist cases have canonical baselines.
- Cache, reachability, and transition metadata policies have measured justification.
- Local dependency links are restored to package references after the dependency releases land.
