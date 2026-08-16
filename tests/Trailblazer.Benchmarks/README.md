# Trailblazer Benchmarks

This project uses BenchmarkDotNet to measure current graph, flow-field, volume,
heap, and map-authoring hot paths. The retired chart-backed A* provider, cache,
hybrid fallback, and legacy steering scenarios are intentionally absent.

## Requirements

- .NET 8 SDK
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
| `navigation-a-star-contention` | `NavigationAStarContentionBenchmarks` |
| `navigation-flow-field` | `NavigationFlowFieldBenchmarks` and the promotion, agent, mutation, and articulation cases |
| `navigation-flow-field-contention` | `NavigationFlowFieldContentionBenchmarks` |
| `volume-path-request` | `VolumePathRequestBenchmarks` |
| `path-heap` | `PathHeapBenchmarks` |
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

The default Flow benchmark run uses the canonical three-launch, ten-warmup,
100-iteration Monitoring job. The exact 1M articulation case uses a separately
named one-launch, zero-warmup, one-iteration `SingleShot` job because it builds
and mutates the real million-node world. Do not compare those job boundaries as
like-for-like. An explicit CLI `--job dry` or `--job short` replaces the default
job for bounded preflight or smoke runs.

`VolumePathRequestBenchmarks` remains with the retained volume/handoff branch.

## Benchmark design

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
