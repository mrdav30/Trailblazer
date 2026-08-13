# Phase 2 Canonical Performance Results

## Capture Status

Final Phase 2 capture completed on 2026-08-13 against the final reviewed source.
BenchmarkDotNet executed three launches, ten warmups, and 100 measured
iterations per case. Both benchmark runs completed successfully; all writer
samples advanced `GraphVersion`, all active-reader exact lookups succeeded, and
no spin ceiling fired.

The frozen thresholds were not changed after capture. The contention values
below are refreshed after parking idle benchmark workers and pinned lease
holders. Structural page ceilings use the final live-root accounting.

## Assembly Identity Audit

The final serial rebuild completed with zero warnings and zero errors. The
loaded identities and output counts were:

| Assembly | Loaded identity | Output copies |
| --- | --- | ---: |
| FixedMathSharp | 7.0.0.0 | 1 |
| SwiftCollections | 7.0.0.0 | 1 |
| GridForge | 9.0.0.0 | 1 |
| Trailblazer | 0.0.0.0 | 1 |
| Trailblazer.Benchmarks | 1.0.0.0 | 1 |

## Pinned-Generation Results

Times are microseconds. Query samples allocate zero managed bytes; writer
samples allocate 904 bytes, below the 1 KiB gate.

| Total concurrency | Query p50 | Query p95 | Query p99 | Query max | Writer p50 | Writer p95 | Writer p99 | Writer max |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 8.100 | 9.900 | 10.901 | 14.500 | 41.150 | 69.620 | 111.817 | 120.400 |
| 2 | 2.300 | 11.215 | 26.705 | 38.600 | 29.300 | 69.500 | 112.937 | 183.000 |
| 4 | 2.200 | 6.805 | 17.711 | 25.200 | 29.200 | 76.620 | 125.513 | 164.900 |
| 8 | 2.100 | 6.200 | 10.966 | 33.000 | 29.950 | 57.820 | 94.841 | 129.000 |

Pinned writer latency misses only concurrency-1 p50 (41.15 us against 40 us).
Pinned queries miss only the 25 us p99 gate at concurrency 2 (26.705 us); all
other pinned-query cells pass. The concurrency-2 miss is isolated to one launch:
per-launch p99 was 16.467, 33.947, and 12.608 us, with all five samples above
25 us in launch two. Parking idle workers and pinned holders removed the prior
concurrency-8 worst-case miss, confirming that it came from benchmark
oversubscription rather than the query path.

## Active-Admission Stress Results

These cases run the listed number of background readers continuously through
admission, exact `MapId` lookup, and release. The timed query adds one foreground
admission; at eight readers it competes at the configured eight-query ceiling.
Times are microseconds.

| Background readers | Query p50 | Query p95 | Query p99 | Query max | Writer p50 | Writer p95 | Writer p99 | Writer max |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 2.500 | 48.705 | 52.804 | 59.000 | 32.350 | 93.350 | 148.670 | 371.500 |
| 2 | 2.950 | 57.600 | 79.353 | 111.100 | 36.000 | 87.190 | 119.360 | 226.500 |
| 4 | 36.650 | 128.980 | 590.163 | 1,368.900 | 39.400 | 85.135 | 148.698 | 182.900 |
| 8 | 760.600 | 29,464.390 | 30,870.789 | 31,242.500 | 41.050 | 83.395 | 115.078 | 173.000 |

No active-query latency gate was declared before this workload was added. For
comparison, it exceeds the pinned reference envelope in the tails at every
reader count and sharply at the saturated eight-reader case. Active writer
results fit the same reference envelope except p50 at eight readers (41.05 us
against 40 us). Active queries allocate zero bytes and active writers allocate
904 bytes.

The saturated case attempted 663,570 to 727,608 admissions per launch and
rejected only 220 to 313 (at most 0.043%). Its millisecond tail is therefore not
a retry-volume effect. Eight hot-cycle background readers plus the foreground
benchmark oversubscribe the eight-core host and can preempt the monitor holder;
this is deliberate capacity-saturation evidence, not a predeclared latency-gate
failure.

Across all contention launches, the exact diagnostic maxima were:

| Diagnostic | Maximum/result |
| --- | ---: |
| Active snapshots | 2 |
| Active snapshot leases | 8 |
| Retired generations | 1 |
| Retired snapshot bytes | 2,202 |
| Active snapshot bytes | 2,202 |
| Persistent graph pages | 21 |
| Active queries | 8 |
| Active workspace bytes | 1,280 |
| Retained workspace bytes | 1,280 |
| Active result bytes | 512 |
| Repath/cache-invalidation waves | 0 |
| Minimum writer version advance | 1 |
| Maximum active-admission leases | 8 |
| Maximum attempts in one launch | 727,608 |
| Maximum admissions in one launch | 727,295 |
| Maximum rejections in one launch | 313 |
| Exact lookup failures | 0 |

## Structural Carryover Results

The timed workload is `ToggleMiddleBridgeAndConverge`; times are milliseconds.

| Maps | p50 | p95 | p99 | Max | Frames | Component nodes | Explicit edges | Dependency entries |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 16 | 0.447 | 0.640 | 0.768 | 1.797 | 16 | 116 | 63 | 5 |
| 128 | 9.479 | 10.580 | 29.638 | 30.861 | 142 | 900 | 511 | 5 |

All latency, frame, component-node, explicit-edge, and dependency-entry gates
pass.

| Maps | Active bytes | Active pages | Composition WIP bytes/pages | Operation WIP bytes/pages | Combined WIP bytes/pages |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 16 | 39,006 | 307 | 7,764 / 77 | 948 / 13 | 8,712 / 90 |
| 128 | 301,470 | 2,329 | 60,628 / 637 | 1,332 / 19 | 61,960 / 656 |

All byte and page ceilings pass. Capping historical unreachable path copies at
the live reachable root reduced the 128-map root-plus-carryover high-water from
4,746 to 2,329 pages and WIP from 3,073 to 656 pages without changing a gate.
The production default is 262,144 persistent pages; the separate maximum
configuration-envelope measurement remains 8,818,680 bytes and 74,296 pages.

Persistent-copy counters included the completion frame:

| Maps | Node records | Reverse records | Component records | Membership records |
| ---: | ---: | ---: | ---: | ---: |
| 16 | 2 | 1 | 3 | 32 |
| 128 | 2 | 1 | 3 | 256 |

## Verification And Commands

The focused locality, carryover, and overload filter passed 11/11 before the
canonical runs. The exact build and benchmark commands were:

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

The final generated artifacts are:

```text
BenchmarkDotNet.Artifacts/results/Trailblazer.Benchmarks.Pathing.NavigationGraphContentionBenchmarks-report-default.md
BenchmarkDotNet.Artifacts/results/Trailblazer.Benchmarks.Pathing.NavigationGraphContentionBenchmarks-report-full-compressed.json
BenchmarkDotNet.Artifacts/results/Trailblazer.Benchmarks.Pathing.NavigationGraphCompositionBenchmarks-report-default.md
BenchmarkDotNet.Artifacts/results/Trailblazer.Benchmarks.Pathing.NavigationGraphCompositionBenchmarks-report-full-compressed.json
BenchmarkDotNet.Artifacts/Trailblazer.Benchmarks.Pathing.NavigationGraphContentionBenchmarks-20260813-161336.log
BenchmarkDotNet.Artifacts/Trailblazer.Benchmarks.Pathing.NavigationGraphCompositionBenchmarks-20260813-161031.log
```

BenchmarkDotNet's generated artifacts remain local/ignored. This document is the
concise checked-in evidence record.
