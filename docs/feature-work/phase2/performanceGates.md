# Phase 2 Production Performance Gates

## Status

These are provisional, machine-specific gates for the Phase 2 context graph and
lifecycle implementation. They freeze representative production work against
the Phase 0 decisions; later phases must add topology-native edges, seams,
search, caches, and agent repath producers before the corresponding global
release gates can be closed.

Canonical measurements use BenchmarkDotNet 0.15.8, .NET 8.0.29, Windows 11
10.0.26200.9168, and an Intel Core i7-9700K (8 cores/8 threads). The gate job is
out of process with three launches, ten warmups, and 100 measured iterations per
case (300 observations). The generated project receives only
`/p:UseLocalLsfStack=true`; per-project local-stack references preserve
FixedMathSharp/SwiftCollections 7.x and GridForge 9.x identities.

## Frozen Workloads And Provisional Gates

### Pinned-generation pressure, active admission, and physical publication

`NavigationGraphContentionBenchmarks` preserves the original pinned-generation
workload and adds a distinct active-admission stress workload. The pinned query
holds enough background leases to make total query concurrency 1, 2, 4, or 8;
the pinned writer holds that many old-root leases. The active cases instead run
1, 2, 4, or 8 background readers that continuously admit a query lease, resolve
the exact `MapId`, and release it while the timed foreground query or writer
runs. The eight-reader active-query case intentionally reaches the configured
eight-query ceiling, so foreground admission must wait for a released slot.

Every writer sample ingests one exact obstacle final-state event and must
advance `GraphVersion`. Deterministic spin ceilings fail broken reader waves
instead of hanging the run. Aggregate counters record attempts, admissions,
rejections, exact lookups, lookup failures, and concurrent active-admission
leases.

Idle and inactive benchmark workers park on per-worker signals; only workers
participating in the current wave execute. This prevents inter-wave polling
from oversubscribing the eight-core reference host. The predeclared latency
gate remains attached to the original pinned workload and is not widened after
measurement:

| Measure | p50 | p95 | p99 | Worst |
| --- | ---: | ---: | ---: | ---: |
| Admitted query lease + exact MapId lookup | <= 10 us | <= 15 us | <= 25 us | <= 50 us |
| Exact physical mutation + maintenance/publication | <= 40 us | <= 100 us | <= 250 us | <= 500 us |

Additional gates:

- warmed query samples allocate zero managed bytes; the end-to-end writer stays
  at or below 1 KiB per publication;
- minimum writer graph-version advance is at least one;
- at most one retired generation remains while old-root readers are held, and
  it is reclaimed after the wave;
- active queries, active workspace bytes, and reserved result bytes never
  exceed the configured 8-query limits (1,280 workspace bytes and 512 result
  bytes in this workload);
- the one-map active root stays below 4 KiB and 32 persistent pages;
- Phase 2 repath/cache-invalidation production remains zero by contract.

Final capture status:

- pinned query/writer allocation, publication, retirement, capacity, and
  invalidation gates pass; pinned-writer p50 at concurrency 1 measured 41.15 us
  against 40 us while all other writer latency cells pass;
- pinned-query p99 at concurrency 2 measured 26.705 us against 25 us; every
  other pinned-query percentile and worst-case gate passes after idle workers
  and pinned lease holders were parked;
- active admission is archived as a separate stress result, not retroactively
  promoted to a release gate. Against the same reference envelope it exposes
  expected admission-tail pressure, especially when eight background readers
  saturate the eight-query ceiling. Active writers stay within every reference
  percentile except concurrency-8 p50 (41.05 us against 40 us).

The timed writer value is conservative end-to-end mutation, ingestion,
maintenance, and immutable publication latency. It is not presented as a pure
monitor-lock wait; lock/cache-gate isolation belongs with the cache producer in
a later phase. The writer allowance is the measured bounded cost of GridForge's
final-state notification plus Trailblazer's immutable copy-on-write replacement
chain. Pooling those replacement roots would complicate or violate ownership
while older generations remain leased; the zero-allocation requirement remains
on warmed read/query admission instead.

### Resumable structural carryover

`NavigationGraphCompositionBenchmarks.ToggleMiddleBridgeAndConverge` creates a
single explicit-transition chain of 16 or 128 maps, then suppresses/reverts its
middle bridge with component, explicit-edge, and dependency budgets fixed at
eight work items per maintenance frame. The affected component stays unpublished
until the resumable candidate converges.

| Maps | p50 | p95 | p99 | Worst | Frames | WIP bytes | WIP pages |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 16 | <= 0.75 ms | <= 1 ms | <= 1.5 ms | <= 2 ms | <= 18 | <= 64 KiB | <= 512 |
| 128 | <= 15 ms | <= 30 ms | <= 40 ms | <= 50 ms | <= 144 | <= 384 KiB | <= 3,072 |

Complexity gates are deterministic and independent of wall-clock variance:

- component-node work is at most `8 * MapCount`;
- explicit-edge work is at most `4 * MapCount`;
- convergence frames are at most `ceiling(9 * MapCount / 8)`: the resumable
  giant-component algorithm can visit each map through nine serial preparation,
  affected-domain, cleanup, rebuild, and publication phases while the
  component budget admits eight records per frame;
- dependency-entry work is at most five for the bridge toggle: remove and add
  the folded baked-transition dependency, update the grid-binding index,
  materialize the restored structural link, and update its incoming reverse
  index;
- active root plus carryover is at most 128 KiB/1,024 pages for 16 maps and
  768 KiB/4,096 pages for 128 maps;
- preparation/operation WIP, structural WIP, and their combined retained
  bytes/pages are recorded separately; the table's WIP ceiling applies to the
  combined maximum;
- copied node, reverse, component, and membership record counters are archived
  with every canonical run and must not grow outside the affected component.

Final capture status:

- both wall-clock tables, convergence frames, component work, explicit-edge
  work, dependency work, byte ceilings, and page ceilings pass;
- capping historical path-copy accounting at the live reachable root reduced
  the 128-map root-plus-carryover high-water to 2,329 pages and combined WIP to
  656 pages without changing a threshold;
- the default context now reserves 262,144 persistent pages. The separate
  maximum-envelope measurement is 8,818,680 bytes and 74,296 pages, so the
  production default covers the honest active-plus-WIP accounting.

This is the largest honest Phase 2 structural case. The planned 1M-cell
articulation split depends on topology-native node edges introduced in Phase 3;
claiming that workload here would measure a graph representation that does not
yet exist.

### Locality and overload convergence

Correctness tests are the production evidence for work that is deterministic
counter-driven rather than statistically timed:

- `DynamicOverlaySet_ShouldCaptureOnlyNewAddressAndCopyTouchedPages` pins one
  semantic delta to a new address and touched persistent pages.
- `InstallingAnotherMap_ShouldNotDiscardQueuedPhysicalChange` and
  `DependencyStamp_ShouldTrackOnlySelectedComponentsAndPages` pin physical and
  dependency locality.
- `Ingress_ShouldCoalesceExactFinalStateAndFailClosedOnOverflow`,
  `MaintenanceCarryover_ShouldFailClosedUntilExactBaselineCatchup`, and the
  oversized-baseline tests pin bounded overload and eventual exact catch-up.
- `OverlayWorkBudget_ShouldFoldPersistentSlotsAcrossFramesAtomically` and its
  resume regression pin multi-frame semantic folding without partial publish or
  reapplication.
- `CompositionWorkBudget_ShouldCarryOverWhenTotalGraphExceedsPerFrameBudget`
  pins structural carryover and unrelated-component availability.

## Canonical Evidence

The final corrected contention and structural tables, exact diagnostic maxima,
commands, and artifact names are archived in
`canonicalPerformanceResults.md`. That file is replaced only from a complete
Release capture after the benchmark asserts real publication and the focused
locality/convergence tests pass against the same source revision.

## Reproduction

```powershell
dotnet build tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj `
  -t:Rebuild -c Release -f net8.0 -p:UseLocalLsfStack=true `
  --no-restore -m:1 -nodeReuse:false

dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj `
  -c Release -f net8.0 -p:UseLocalLsfStack=true --no-build --no-restore -- `
  --filter "*NavigationGraphContentionBenchmarks*"

dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj `
  -c Release -f net8.0 -p:UseLocalLsfStack=true --no-build --no-restore -- `
  --filter "*NavigationGraphCompositionBenchmarks*"
```

Do not add a stack-wide `SemVer` or `AssemblySemVer` CLI property. Version
identity belongs to each local project reference; a stack-wide value can create
duplicate upstream assembly identities in the generated benchmark graph.
