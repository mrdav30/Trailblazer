# Phase 2 Canonical Performance Results

## Capture Status

Final Phase 2 simplification capture completed on 2026-08-13. BenchmarkDotNet
executed three launches, ten warmups, and 100 measured iterations per case.
Every writer sample advanced `GraphVersion`, every exact lookup succeeded, and
no deterministic spin ceiling fired. Frozen thresholds were not widened.

## Assembly Identity Audit

The serial local-stack build completed with zero warnings and zero errors. Each
stack assembly appeared once in the output:

| Assembly | Loaded identity | Copies |
| --- | --- | ---: |
| FixedMathSharp | 7.0.0.0 | 1 |
| SwiftCollections | 7.0.0.0 | 1 |
| GridForge | 9.0.0.0 | 1 |
| Trailblazer | 0.0.0.0 | 1 |
| Trailblazer.Benchmarks | 1.0.0.0 | 1 |

Generated BenchmarkDotNet jobs consumed this verified prebuild instead of
recursively rebuilding the local dependency graph.

## Pinned Snapshot Results

Times are microseconds. Lease acquisition/lookup allocates zero managed bytes;
physical publication allocates 904 bytes, below the 1 KiB gate.

| Total readers | Lease p50 | Lease p95 | Lease p99 | Lease max | Writer p50 | Writer p95 | Writer p99 | Writer max |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 3.200 | 4.705 | 6.935 | 29.100 | 32.300 | 85.280 | 126.972 | 172.700 |
| 2 | 1.800 | 7.100 | 17.916 | 25.700 | 30.900 | 69.930 | 136.301 | 195.400 |
| 4 | 1.750 | 5.010 | 19.712 | 27.600 | 32.000 | 73.350 | 112.191 | 148.100 |
| 8 | 1.700 | 5.305 | 9.790 | 23.200 | 31.300 | 62.405 | 121.013 | 133.800 |

All pinned latency, allocation, retirement, capacity, publication, and lookup
gates pass. The former 26.705 us query p99 and 41.15 us writer p50 misses were
resolved by deleting the premature query/cache synchronization layer and
measuring the Phase 2 snapshot primitive directly; no threshold changed and no
special-case fast path was added.

## Active Lease-Cycling Stress

For lease acquisition, the listed concurrency includes the timed foreground
lease. For writer publication it is the number of hot background readers. This
is saturation evidence, not a predeclared Phase 2 release gate.

| Readers | Lease p50 | Lease p95 | Lease p99 | Lease max | Writer p50 | Writer p95 | Writer p99 | Writer max |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 3.000 | 4.505 | 5.601 | 20.500 | 32.600 | 78.525 | 123.339 | 183.100 |
| 2 | 1.600 | 26.190 | 51.203 | 53.500 | 35.650 | 79.525 | 119.010 | 175.100 |
| 4 | 2.000 | 53.415 | 83.714 | 217.100 | 40.500 | 101.200 | 152.390 | 505.000 |
| 8 | 47.900 | 726.150 | 1,179.037 | 2,353.200 | 57.000 | 739.115 | 1,340.546 | 2,246.200 |

The eight-core host shows expected monitor-holder/scheduler tails when all cores
continuously cycle leases. No lease acquisition was rejected and no exact lookup
failed, so this does not indicate a capacity or correctness failure. Production
callers should not spin at full offered load; later query scheduling belongs to
the search phase that owns the real workload.

Across the contention capture, diagnostic maxima were:

| Diagnostic | Maximum/result |
| --- | ---: |
| Active snapshots | 2 |
| Active snapshot leases | 8 |
| Retired generations | 1 |
| Retired snapshot bytes | 2,202 |
| Active snapshot bytes | 2,202 |
| Persistent graph pages | 21 |
| Maximum active cycling leases | 8 |
| Maximum attempts in one launch | 176,061 |
| Maximum rejections in one launch | 0 |
| Exact lookup failures | 0 |
| Minimum writer version advance | 1 |

## Structural Carryover Results

The timed workload is `ToggleMiddleBridgeAndConverge`; times are milliseconds.

| Maps | p50 | p95 | p99 | Max | Frames | Component nodes | Explicit edges | Dependency entries |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 16 | 0.437 | 0.578 | 1.002 | 1.788 | 16 | 116 | 63 | 5 |
| 128 | 9.432 | 12.045 | 25.742 | 26.245 | 142 | 900 | 511 | 5 |

All latency, frame, component-node, explicit-edge, and dependency-entry gates
pass.

| Maps | Active bytes/pages | Composition WIP bytes/pages | Operation WIP bytes/pages | Combined WIP bytes/pages |
| ---: | ---: | ---: | ---: | ---: |
| 16 | 39,006 / 307 | 7,764 / 77 | 948 / 13 | 8,712 / 90 |
| 128 | 301,470 / 2,329 | 60,628 / 637 | 1,332 / 19 | 61,960 / 656 |

All byte and page ceilings pass. Historical unreachable COW paths are excluded;
live paths remain charged. The production default is 262,144 persistent pages,
and the maximum configured envelope remains 8,818,680 bytes/74,296 pages.

Completion-frame copy counters were:

| Maps | Node records | Reverse records | Component records | Membership records |
| ---: | ---: | ---: | ---: | ---: |
| 16 | 2 | 1 | 3 | 32 |
| 128 | 2 | 1 | 3 | 256 |

## Verification And Commands

The focused locality/carryover/overload filter passed before canonical capture.
The exact benchmark commands were:

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

## Artifacts

```text
BenchmarkDotNet.Artifacts/results/Trailblazer.Benchmarks.Pathing.NavigationGraphContentionBenchmarks-report-default.md
BenchmarkDotNet.Artifacts/results/Trailblazer.Benchmarks.Pathing.NavigationGraphContentionBenchmarks-report-full-compressed.json
BenchmarkDotNet.Artifacts/results/Trailblazer.Benchmarks.Pathing.NavigationGraphCompositionBenchmarks-report-default.md
BenchmarkDotNet.Artifacts/results/Trailblazer.Benchmarks.Pathing.NavigationGraphCompositionBenchmarks-report-full-compressed.json
BenchmarkDotNet.Artifacts/Trailblazer.Benchmarks.Pathing.NavigationGraphContentionBenchmarks-20260813-185857.log
BenchmarkDotNet.Artifacts/Trailblazer.Benchmarks.Pathing.NavigationGraphCompositionBenchmarks-20260813-190239.log
```

Generated artifacts remain local/ignored. This document is the concise
checked-in evidence record.
