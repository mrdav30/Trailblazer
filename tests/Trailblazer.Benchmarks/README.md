# Trailblazer Benchmarks

This project benchmarks the path-request and navigation hot paths using [BenchmarkDotNet](https://benchmarkdotnet.org/).

The suite is layered so that regression in a high-level steering or cache benchmark can be diagnosed
by running the lower-level surveyor or guide-resolution benchmark in isolation.

## Requirements

- .NET 8 SDK
- `Release` configuration (mandatory — avoid `Debug` for performance measurements)

## Running

### List available benchmark selections

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- list
```

### Run all benchmarks

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- all
```

### Run a selection by alias

Aliases are derived from the benchmark class name. `Benchmarks` is stripped and the remaining words
are joined with `-`. Pass one or more aliases as leading arguments before any BenchmarkDotNet flags.

| Alias | Benchmark class |
| --- | --- |
| `a-star-path-request` | `AStarPathRequestBenchmarks` |
| `flow-field-path-request` | `FlowFieldPathRequestBenchmarks` |
| `guide-cache` | `GuideCacheBenchmarks` |
| `navigation-scenario` | `NavigationScenarioBenchmarks` |
| `nav-steering` | `NavSteeringBenchmarks` |
| `pathing-scenario` | `PathingScenarioBenchmarks` |
| `transition-fallback` | `TransitionFallbackBenchmarks` |
| `volume-path-request` | `VolumePathRequestBenchmarks` |

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- a-star-path-request
```

Multiple aliases run together:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- a-star-path-request guide-cache
```

### Filter to specific methods

Add `--filter` after the alias. The filter pattern is forwarded to BenchmarkDotNet's method-name
filter and supports `*` wildcards.

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- a-star-path-request --filter '*Corridor*'
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- nav-steering --filter '*SteadyState*'
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- all --list flat
```

### Fast development check (InProcessShortRunConfig)

`InProcessShortRunConfig` is registered for quick local smoke runs. Use it during development to
verify benchmark code compiles and produces plausible numbers without a full benchmark run.

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- a-star-path-request --config InProcessShortRunConfig
```

Do not treat results from short-run mode as canonical measurements.

## Benchmark Suite Structure

### A\* request and guide hot paths

**Class**: `AStarPathRequestBenchmarks` | **Alias**: `a-star-path-request`  
**Categories**: `Pathing`, `AStar`

Covers: raw `AStarSurveyor` survey cost, cold and warm `PathGuideFactory.RequestGuide` across
open-plane, corridor, sparse-blocker, choke-point, and heuristic-comparison scenarios.

Key benchmarks:

- `RawSurvey_OpenPlane32` — baseline raw survey without guide allocation
- `WarmGuide_OpenPlane32` — the baseline warm cache hit (marked `Baseline = true`)
- `ColdGuide_Corridor1024` — long-route reconstruction cost, reveals waypoint list insertion scaling
- `FailedRoute_ChokeUnitSize2` — unsuccessful survey with unit size 2, single-voxel gap

### Flow-field request and reuse hot paths

**Class**: `FlowFieldPathRequestBenchmarks` | **Alias**: `flow-field-path-request`  
**Categories**: `Pathing`, `FlowField`

Covers: raw `FlowFieldSurveyor` field generation, cold and warm guide resolution, many-start
destination reuse, coverage miss, `SampleFlowVector` at exact and fractional positions, and
`ExtraFloodRange` scaling on blocker maps.

Key benchmarks:

- `WarmGuide_OpenPlane64` — baseline destination-centric warm reuse (marked `Baseline = true`)
- `ManyStartWarmReuse_32Starts` — proves field is generated once and reused for 32 different starts
- `SampleFlowVector_FractionalPosition` — fractional-position sampling cost
- `RawSurvey_Blocker_LargeFloodRange` — shows ExtraFloodRange impact on flood generation

### Guide cache lifecycle

**Class**: `GuideCacheBenchmarks` | **Alias**: `guide-cache`  
**Categories**: `Pathing`, `Cache`

Covers: A\* and flow-field cache hits, misses below and above the 128-entry LRU capacity,
`InvalidateCacheFor`, `CullExpiredGuides` with no stale and many stale entries, and
`FlushCache` force versus soft modes.

Key benchmarks:

- `AStarCacheHit` — baseline cache hit (marked `Baseline = true`)
- `AStarCacheMiss_OverCapacity_Eviction` — exercises LRU eviction above the 128-entry threshold
- `CullExpiredGuides_ManyStale` — cost of scanning 128 stale entries at frame 10000

### NavSteering frame-facing hot paths

**Class**: `NavSteeringBenchmarks` | **Alias**: `nav-steering`  
**Categories**: `Navigation`, `Steering`

Covers: first-frame guide resolution, direct-LOS steady state at default and every-frame
cooldowns, guided A\* and flow-field steady state, and `ComputeCombinedSteering` at 32, 128,
and 512 registered occupants.

Key benchmarks:

- `SteadyState_DirectLOS_DefaultCooldown` — baseline (marked `Baseline = true`)
- `SteadyState_DirectLOS_EveryFrameRecheck` — every-frame LOS recheck cost
- `FirstFrame_GuidedAStar` — cold first-frame cost including guide resolution
- `CombinedSteering_Density512` — occupant-density scan at maximum tested density

### Navigation scenario workloads

**Class**: `NavigationScenarioBenchmarks` | **Alias**: `navigation-scenario`  
**Categories**: `Navigation`, `Scenario`

Covers: first-frame and steady fixed-step updates for 100-agent and 500-agent mixed workloads
containing direct LOS, A\*, flow-field, and combined-steering agents.

Key benchmarks:

- `FirstFrameMixedSteering_100Agents` — batched cold first-frame guide and steering setup
- `FirstFrameMixedSteering_500Agents` — larger first-frame hitch shape
- `FixedStepMixedSteering_100Agents` — steady per-agent mixed frame cost
- `FixedStepMixedSteering_500Agents` — larger steady per-agent mixed frame cost

### Pathing scenario workloads

**Class**: `PathingScenarioBenchmarks` | **Alias**: `pathing-scenario`  
**Categories**: `Pathing`, `Scenario`

Covers: dynamic chart invalidation plus A\* repath waves, shared flow-field guide checkout for
100 and 500 starts, reachability snapshot first-hit checks, transition request construction and
cache-key reads, transition request churn, and raw flow-field flood-range sweeps.

Key benchmarks:

- `DynamicObstacleUpdate_RepathWave64` — one chart update followed by 64 A\* guide requests
- `FlowFieldSharing_500Starts` — many starts sharing one cached destination field
- `ReachabilityFirstHit_ClearanceCombos` — distinct `(unitSize, maxClimbHeight)` snapshot keys
- `TransitionRequestChurn_64Requests` — host-style request creation every fixed step
- `FlowFieldFloodRange_OpenPlane128` — large raw flow-field flood and allocation shape

### Transition-aware and volume routing

#### Transition fallback

**Class**: `TransitionFallbackBenchmarks` | **Alias**: `transition-fallback`  
**Categories**: `Pathing`, `Transition`

Covers: cold and warm A\* guide resolution for a disconnected jump-link scenario and a
solid→swim-entry→liquid→swim-exit→solid path; cold and warm flow-field staged guide
resolution for a jump-link transition.

Key benchmarks:

- `WarmGuide_AStar_JumpLink` — baseline warm guide hit through a transition (marked `Baseline = true`)
- `ColdGuide_AStar_SwimPath` — multi-segment transition assembly cold cost
- `ColdGuide_FlowField_JumpLink` — staged flow-field guide cold initialization

#### Volume path requests

**Class**: `VolumePathRequestBenchmarks` | **Alias**: `volume-path-request`  
**Categories**: `Pathing`, `Volume`

Covers: raw `VolumeSurveyor` survey for a direct 5-cell gas corridor, cold and warm
`PathGuideFactory.RequestVolume` for both a straight corridor and an L-shaped path that
produces intermediate waypoints.

Key benchmarks:

- `WarmGuide_DirectGasCorridor` — baseline warm volume guide hit (marked `Baseline = true`)
- `ColdGuide_LShapeGasPath` — L-shaped corridor cold cost with waypoint reconstruction

## Design Principles

- Chart registration, initialization, and world setup are in `[GlobalSetup]`, never in benchmark bodies.
- Cold benchmarks flush the guide cache in `[IterationSetup]` with `BenchmarkPathFixture.FlushGuideCache()`.
- Warm benchmarks prime the guide in setup and rely on the cache being populated before measurement.
- Every guide returned from `PathGuideFactory.RequestGuide` is returned with `PathGuideFactory.ReturnGuide(...)`.
- Preflight validation in setup throws `InvalidOperationException` when a scenario no longer produces
  the expected route shape, ensuring benchmarks fail fast rather than silently measuring the wrong thing.
- Each benchmark class uses its own `BenchmarkPathFixture` instances to prevent cross-contamination
  between benchmark groups.
- `[MemoryDiagnoser]` is applied to all benchmark classes.

## Baseline Artifacts

Before starting optimization work, capture a baseline:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- all --exporters json
```

BenchmarkDotNet writes results to `BenchmarkDotNet.Artifacts/results/` by default. Archive the
JSON or markdown reports before making hot-path changes so regressions can be compared against
a known state.

## CI Guidance

CI should at minimum compile the benchmark project in `Release`:

```bash
dotnet build tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj --configuration Release
```

Running full benchmarks in CI is optional until local variance is understood. When you are ready to
add performance gates, use BenchmarkDotNet's `--compare` or a stored baseline artifact rather than
raw timing thresholds, which are sensitive to runner hardware.
