# Trailblazer Benchmarks

This project uses BenchmarkDotNet to measure current graph, flow-field, ray,
and map-publication hot paths. Retired chart-backed search providers, caches,
hybrid fallback, and legacy steering scenarios are intentionally absent.

## Requirements

- The .NET SDK selected by the repository's `global.json`, plus the .NET 8 runtime
- `Release` configuration

## Running

List the available selections:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- list
```

Run all benchmarks:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- all
```

Aliases are derived from benchmark class names by removing `Benchmarks` and
joining the remaining words with hyphens. Useful selections include:

| Alias | Benchmark class |
| --- | --- |
| `navigation-surface-a-star` | `NavigationSurfaceAStarBenchmarks` |
| `navigation-guide-service` | `NavigationGuideServiceBenchmarks` |
| `navigation-cold-guide` | `NavigationColdGuideBenchmarks` |
| `navigation-cold-frame` | `NavigationColdFrameBenchmarks` |
| `navigation-a-star-contention` | `NavigationAStarContentionBenchmarks` |
| `navigation-flow-field` | `NavigationFlowFieldBenchmarks` and the promotion, agent, mutation, and articulation cases |
| `navigation-flow-field-contention` | `NavigationFlowFieldContentionBenchmarks` |
| `navigation-ray` | `NavigationRayBenchmarks` |
| `navigation-volume-routing` | `NavigationVolumeRoutingBenchmarks` |
| `navigation-map-bake` | `NavigationMapBakeBenchmarks` |
| `navigation-graph-lifecycle` | `NavigationGraphLifecycleBenchmarks` |
| `navigation-graph-composition` | `NavigationGraphCompositionBenchmarks` |
| `navigation-graph-contention` | `NavigationGraphContentionBenchmarks` |

For a fast development smoke check:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- navigation-guide-service --job short --inProcess
```

Do not treat short-run results as canonical measurements.

## Current provider benchmarks

`NavigationSurfaceAStarBenchmarks` measures production endpoint admission plus
an uncached graph A* search at approximately 100, 1K, 10K, and 100K expanded
nodes. BenchmarkDotNet reports latency and managed allocation; cleanup emits
the deterministic work-meter, one-frontier heap, workspace, and result-byte
counters for each case.

`NavigationGuideServiceBenchmarks.WarmFlowAcquireSampleDispose` measures public
graph Flow acquire, sample, and return after the payload and lease pools are
warm. Its 32x32 open plane and 1,024-cell corridor preflight at zero managed
bytes on the measured thread.

`NavigationAStarContentionBenchmarks` uses persistent manual worker threads at
1/2/4/8 concurrency. Same-key workers search concurrently, finish in reversed
stable-ordinal order, then publish through the canonical prefix and report
duplicate convergence plus reservation, result-payload, active/retained
workspace, worker-thread allocation, detached, and retired accounting. The
measured algorithm uses no `Task` scheduling; worker allocation excludes the
threads and events created in global setup.

`NavigationFlowFieldBenchmarks` covers cold reverse integration at 100, 1K,
10K, and 100K settled nodes, near-to-far prefix promotion, warm 100/500/5,000
agent batches, affected/unaffected invalidation, and an exact 1,000,000-node
articulation split. `NavigationFlowFieldContentionBenchmarks` covers same-key
and near/far publication with persistent manual workers and reversed
completion.

`NavigationRayBenchmarks` covers short, medium, and long successful rays; a
sparse blocked trace; one mixed seam/explicit world; worst-case raw A* guide
points; bounded A* simplification; same-lease Flow rejoin; and contention on the
context-owned immediate workspace. Global setup runs each real production
workload twice before timing and fails on incorrect statuses, setup topology,
counter/drain invariants, warmed direct-ray/Flow foreground allocation, or
contention-worker allocation. A* cases intentionally record their result-
payload allocation instead of requiring zero. Treat reported latency and
allocation as environment-specific BenchmarkDotNet evidence, not product
guarantees.

`NavigationVolumeRoutingBenchmarks` covers open and obstructed rectangular
Gas routes in 2D, open and obstructed 3D Flow, a large body, a hex vertical
diagonal, rule scanning, ladder and duck actions, a mixed medium/action route,
warm A*/Flow cache hits, and full-direction versus face-only controls. Cleanup
reports exact settled states, evaluated/primary/shortcut candidates, union and
covered-voxel interval work, transition candidates/pairs, successful dependency
merges, emitted dependency facts, A* guide steps, Flow payload nodes, immutable
payload bytes, and the warm
sample allocation preflight. BenchmarkDotNet supplies median, P95, P99, max,
and managed-allocation columns through the shared gate configuration.

The default Flow benchmark run uses the canonical three-launch, ten-warmup,
100-iteration Monitoring job. The exact 1M articulation case uses a separately
named one-launch, zero-warmup, one-iteration `SingleShot` job because it builds
and mutates the real million-node world. Do not compare those job boundaries as
like-for-like. An explicit CLI `--job dry` or `--job short` replaces the default
job for bounded preflight or smoke runs.

## Benchmark design

### Cold public acquisition and containing frames

`NavigationColdGuideBenchmarks` compares cold public Flow acquisition, matched
internal Flow construction, cold public A*, and warm public Flow acquisition.
All public cases include lease disposal but **not sampling**; the existing
`navigation-guide-service` cases remain the acquire/sample/dispose controls.
Planes contain exactly 100, 10,000 or 100,000 settled Flow nodes. A 100-node
ladder joins two separated landing strips with one explicit vertical action.
The ladder case measures route construction, not host execution of that action.

`NavigationColdFrameBenchmarks` measures a 32 Hz headless frame with 100 real,
input-driven Navigators on a plane and zero, one or four public Flow requests.
Each measured frame follows 32 untimed preparation frames. Cold bursts use
distinct exact profile keys that preserve the integration graph; warm bursts
reuse those same keys. There is no collision solver, existing guide-following,
networking or rendering in this fixture. Treat it as a lower-bound host workload.

World construction, publication, workspace/pool warmup, agent reset and cache
preparation are untimed. Cold setup verifies empty caches before **every**
invocation. Warm setup verifies the exact reusable payload; cleanup checks
payload sizes and drained leases/reservations. Console counters report work,
payload retention and allocation preflights alongside BenchmarkDotNet statistics.

While upstream changes remain unreleased, run the coordinated source stack:

```powershell
dotnet build tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -p:UseLocalLsfStack=true
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll navigation-cold-guide --filter '*Ladder*' --job dry --inProcess --artifacts artifacts/hardening-phase3/smoke-small
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll navigation-cold-guide --filter '*ColdPublicFlow*' '*ColdPublicAStar*' '*ColdInternalFlow*' --launchCount 3 --warmupCount 1 --iterationCount 3 --artifacts artifacts/hardening-phase3/slow-guides
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll navigation-cold-frame --filter '*Cold1*' '*Cold4*' --launchCount 3 --warmupCount 1 --iterationCount 3 --artifacts artifacts/hardening-phase3/slow-frames
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll navigation-cold-guide navigation-cold-frame navigation-guide-service --filter '*WarmPublicFlow*' '*Baseline*' '*Warm1*' '*Warm4*' '*WarmFlowAcquireSampleDispose*' --artifacts artifacts/hardening-phase3/canonical-controls
```

The final command retains the normal canonical job (three launches, 100 measured
iterations per launch). The slow cold commands use the predeclared nine-sample
protocol in the execution record: report minimum/median/maximum, not a reliable
P99 estimate. Generated child builds use `UsePrebuiltLocalLsfStack`
to consume the already built binaries. Do not run builds or competing benchmark
jobs during capture. Retain the full JSON/logs, not just a rounded mean. The
[Phase 3 evidence](../../docs/feature-work/navigationAcquisitionMeasurements.md)
records the acceptance budget, source identities, results and decision.
Use in-process smoke only for small cases: large-case setup plus acquisition can
exceed the in-process runner's timeout even when every query terminates normally.

### General rules

- World and map setup belong in `[GlobalSetup]`, never in benchmark bodies.
- Setup preflights fail fast when the intended route cannot be produced.
- Every acquired guide or graph lease is released by the benchmark.
- `[MemoryDiagnoser]` is used for provider and graph-service benchmarks.
- Canonical comparisons should archive BenchmarkDotNet JSON or Markdown output
  from `BenchmarkDotNet.Artifacts/results/`.

CI should at minimum compile the benchmark project in `Release`:

```bash
dotnet build tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj --configuration Release
```
