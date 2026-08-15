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
| `flow-field-path-request` | `FlowFieldPathRequestBenchmarks` |
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

`NavigationGuideServiceBenchmarks.WarmGuideAcquireSampleAdvanceDispose`
measures public acquire, sample, advance, and dispose after the graph payload
and lease pools are warm. Its 32x32 open plane and 1,024-cell corridor match the
retired provider's comparison shapes and preflight at zero managed bytes.

`NavigationAStarContentionBenchmarks` uses persistent manual worker threads at
1/2/4/8 concurrency. Same-key workers search concurrently, finish in reversed
stable-ordinal order, then publish through the canonical prefix and report
duplicate convergence plus reservation, result-payload, active/retained
workspace, worker-thread allocation, detached, and retired accounting. The
measured algorithm uses no `Task` scheduling; worker allocation excludes the
threads and events created in global setup.

`FlowFieldPathRequestBenchmarks` and `VolumePathRequestBenchmarks` remain while
their legacy providers still support the volume/handoff branch. Later phases
remove those benchmarks with the corresponding provider authority.

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
