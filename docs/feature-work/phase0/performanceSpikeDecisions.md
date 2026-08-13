# Phase 0 Streaming And Endpoint Performance Spikes

## Status And Scope

These benchmarks are synthetic decision aids for the graph redesign. They do
not benchmark production snapshot, dependency, or endpoint code because those
types do not exist yet. They make the competing work and retained-memory shapes
explicit so Phase 1 does not accidentally freeze a global scan or a duplicate
index.

The checked-in workloads are:

- `StreamedGridChurnBenchmarks`: 128 structural components, 500/5,000 active
  guides, and one/64 distinct streamed changes. It compares immediate global
  invalidation under `ReaderWriterLockSlim`, per-event paged snapshot
  publication, and one batched paged publication for a detached event prefix.
- `EndpointResolutionStrategyBenchmarks`: 4K, 64K, and 1M address volumes at
  dense and 1% authored density with one/16 covered addresses. It compares a
  graph scan, exact covered-address lookup through a direct ordinal table, and a
  compact directory of occupied 64-address buckets over the existing sorted
  node array.

The endpoint workload starts after GridForge has produced exact covered voxel
addresses. It does not measure footprint geometry, map selection, clearance, or
nearest-candidate ranking. The churn workload is uncontended and therefore does
not measure reader p95/p99 or writer wait.

## Noncanonical ShortRun Evidence

The following smoke results are not performance baselines or release gates.
They were captured on an Intel i7-9700K under Windows 11, .NET 8.0.29,
concurrent Workstation GC, BenchmarkDotNet 0.15.8, and the in-process `ShortRun`
job. Canonical evidence needs the normal out-of-process job, an idle machine,
stored artifacts, and repeated runs.

### Streamed churn

| Guides | Events | Immediate global RW invalidation | Per-event structural snapshots | Batched structural snapshot |
| ---: | ---: | ---: | ---: | ---: |
| 500 | 1 | 39.62 ns / 0 B | 102.74 ns / 232 B | 111.10 ns / 232 B |
| 500 | 64 | 2.567 us / 0 B | 6.861 us / 14,848 B | 740.67 ns / 688 B |
| 5,000 | 1 | 247.69 ns / 0 B | 122.55 ns / 232 B | 133.80 ns / 232 B |
| 5,000 | 64 | 15.651 us / 0 B | 8.343 us / 14,848 B | 2.285 us / 688 B |

The 64-event workload changes 64 of 128 components. Its deterministic fan-out
is 32,000/320,000 invalidation touches for global invalidation at 500/5,000
guides, versus 250/2,500 dependency-matched touches. A single component change
touches only three or four of 500 guides and 39 or 40 of 5,000 guides instead of
the whole population.

The important result is the shape, not the local nanoseconds: structural
dependencies bound repath fan-out, while detaching and publishing one immutable
prefix avoids the allocation and lock repetition of per-event snapshots. The
single-event/500-guide case also shows why the design should not claim that
snapshots always make an isolated writer operation faster.

### Endpoint lookup

The clean targeted smoke used a 1,048,576-address volume, 16 exact covered
addresses, and either 100% or 1% authored density.

| Density | Graph scan | Covered direct lookup | Compact occupied-bucket lookup | Query allocation |
| ---: | ---: | ---: | ---: | ---: |
| 100% | 783.301 us | 7.693 ns | 139.392 ns | 0 B |
| 1% | 6.858 us | 11.645 ns | 34.026 ns | 0 B |

The direct ordinal table retains 4 MiB for either 1M-address scenario. The
compact bucket directory adds about 128 KiB for the dense case and 82 KiB for
the 1% case; the sorted node array is existing graph storage and is excluded
from both figures. The graph scan adds no index bytes but scales with authored
node count on every endpoint query.

This does not justify one lookup representation for every map. It does show
that a full graph scan is the wrong steady-state fallback, and that the exact
covered-address result should feed the map's already-selected lookup rather
than a second endpoint-only spatial index.

## Frozen Decisions And Gates

- Consume exact GridForge covered voxel addresses. Endpoint resolution must be
  proportional to covered addresses and lookup probes, never all authored nodes.
- Reuse the bake's density/byte-selected node lookup. A direct ordinal table is
  eligible only under the frozen per-map density and retained-byte threshold;
  sparse maps use a compact authored-cell-scaled lookup. Do not add a separate
  endpoint-only spatial index without a later measured workload that the map
  lookup cannot satisfy.
- Publish at most one immutable candidate snapshot for one detached maintenance
  prefix. Copy only the root path and unique touched pages; per-event snapshot
  publication is rejected by the measured allocation shape.
- Track guide/cache dependencies by structural component. A publication may
  stale exactly the union of guides whose dependency sets intersect changed
  components; unrelated guides and cache entries must have zero repath/discard
  count.
- Query readers do not take the global publication `ReaderWriterLockSlim`.
  Publication and retirement require an explicit lock order, bounded retained
  generations, and lease-safe reclamation.
- Warm endpoint lookup allocates zero. Publication reports copied pages/bytes,
  changed components, dependency-matched guides, unrelated guides touched,
  retired generations/bytes, and event count in addition to time.
- The production contention benchmark must report query p50/p95/p99, writer
  wait p50/p95/p99/worst, and repath-wave count at 1/2/4/8 query threads. This
  synthetic uncontended spike cannot honestly set numeric writer-wait or memory
  capacity defaults; those values remain a required canonical Phase 0/Phase 2
  gate before concurrent publication is accepted.

## Reproduction

```bash
dotnet build tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- streamed-grid-churn --filter '*' --job short --inProcess
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- endpoint-resolution-strategy --filter '*1M_SixteenOverlap*' --job short --inProcess
```

Remove `--job short --inProcess`, run on the canonical baseline machine, and
archive the BenchmarkDotNet JSON/Markdown artifacts before using timings for a
regression comparison.
