# Phase 2 Production Performance Gates

## Status

These are machine-specific gates for the Phase 2 context graph and lifecycle.
They cover immutable graph preparation/publication and snapshot leases. Query
admission, search workspaces, result caches, topology-native edges, seams, and
agent repath producers are measured in the phases that first ship them.

Canonical measurements use BenchmarkDotNet 0.15.8, .NET 8.0.29, Windows 11
10.0.26200.9168, and an Intel Core i7-9700K (8 cores/8 threads). Each gate has
three launches, ten warmups, and 100 measured iterations per case. Generated
jobs receive `/p:UseLocalLsfStack=true`,
`/p:UsePrebuiltLocalLsfStack=true`, and `/m:1`; they consume the preceding
verified serial build rather than rebuilding the nested local dependency graph.

## Frozen Workloads And Gates

### Snapshot leases and physical publication

`NavigationGraphContentionBenchmarks` measures snapshot acquisition plus exact
`MapId` lookup directly. Pinned acquisition holds enough parked background
leases to make total reader concurrency 1, 2, 4, or 8. Pinned publication holds
that many old-root leases while one physical final-state change is published.
Active cases continuously acquire, resolve, and release leases while the timed
foreground acquisition or writer runs.

Every writer sample ingests one exact obstacle final-state event and must
advance `GraphVersion`. Idle workers and pinned lease holders park on signals;
only active cycling readers consume a core. Deterministic spin ceilings fail a
broken wave instead of hanging the run.

The predeclared release gate applies to the pinned workload:

| Measure | p50 | p95 | p99 | Worst |
| --- | ---: | ---: | ---: | ---: |
| Snapshot lease + exact MapId lookup | <= 10 us | <= 15 us | <= 25 us | <= 50 us |
| Physical mutation + maintenance/publication | <= 40 us | <= 100 us | <= 250 us | <= 500 us |

Additional gates:

- warmed lease samples allocate zero managed bytes;
- the end-to-end writer allocates at most 1 KiB per publication;
- every writer advances `GraphVersion` by at least one;
- at most one retired generation remains while the previous root is pinned and
  it is reclaimed after the wave;
- active leases never exceed the configured eight-lease ceiling;
- the one-map root remains below 4 KiB and 32 persistent pages;
- every exact lookup succeeds.

Final status: every pinned latency, allocation, publication, retirement,
capacity, and exact-lookup gate passes. The older query/cache benchmark misses
are not threshold changes: the premature Phase 4 layer was deleted and the
Phase 2 primitive was recaptured directly. Active cycling is retained as
separate saturation evidence, not promoted retroactively to a release gate.

The writer value is intentionally end-to-end GridForge notification,
Trailblazer reconciliation, and immutable publication time. Pooling the
replacement roots would complicate ownership while older roots are leased;
the small bounded writer allocation is accepted and the read path remains zero
allocation.

### Resumable structural carryover

`NavigationGraphCompositionBenchmarks.ToggleMiddleBridgeAndConverge` builds a
16- or 128-map explicit-transition chain, then suppresses/reverts its middle
bridge with component, explicit-edge, and dependency work fixed at eight items
per maintenance frame. The affected component stays unpublished until the
candidate converges.

| Maps | p50 | p95 | p99 | Worst | Frames | WIP bytes | WIP pages |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 16 | <= 0.75 ms | <= 1 ms | <= 1.5 ms | <= 2 ms | <= 18 | <= 64 KiB | <= 512 |
| 128 | <= 15 ms | <= 30 ms | <= 40 ms | <= 50 ms | <= 144 | <= 384 KiB | <= 3,072 |

Deterministic complexity gates:

- component-node work is at most `8 * MapCount`;
- explicit-edge work is at most `4 * MapCount`;
- convergence is at most `ceiling(9 * MapCount / 8)` frames;
- dependency-entry work is at most five for the bridge toggle;
- active root plus carryover is at most 128 KiB/1,024 pages for 16 maps and
  768 KiB/4,096 pages for 128 maps;
- copied node, reverse, component, and membership counters cannot grow outside
  the affected component.

Final status: all structural timing, frame, work, byte, page, and locality gates
pass. Historical unreachable path copies are excluded; live reachable COW paths
remain fully charged. The default context reserves 262,144 persistent pages,
while the maximum configured envelope measures 8,818,680 bytes/74,296 pages.

### Locality and overload convergence

Deterministic correctness tests pin the non-statistical gates:

- `DynamicOverlaySet_ShouldCaptureOnlyNewAddressAndCopyTouchedPages`;
- `InstallingAnotherMap_ShouldNotDiscardQueuedPhysicalChange`;
- `DependencyStamp_ShouldTrackOnlySelectedComponentsAndPages`;
- `Ingress_ShouldCoalesceExactFinalStateAndFailClosedOnOverflow`;
- `MaintenanceCarryover_ShouldFailClosedUntilExactBaselineCatchup`;
- `OverlayWorkBudget_ShouldFoldPersistentSlotsAcrossFramesAtomically`;
- `CompositionWorkBudget_ShouldCarryOverWhenTotalGraphExceedsPerFrameBudget`.

## Canonical Evidence

Exact results, diagnostic maxima, commands, and artifact names are archived in
`canonicalPerformanceResults.md`. Replace that record only after a complete
Release capture against the reviewed source revision.

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

Do not pass a stack-wide `SemVer` or `AssemblySemVer`. The serial prebuild owns
per-project identities; the generated jobs consume those verified binaries.
