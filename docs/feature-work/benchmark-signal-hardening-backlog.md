# Benchmark Signal Hardening Backlog

## Purpose

This document captures benchmark-derived hardening signals that fall outside the
active feature plan. It is intentionally undated and long-lived: individual
entries carry their own discovery dates, evidence, status, and next isolation
step.

Use this backlog for measured performance, allocation, scaling, and benchmark
evidence concerns. Bugs or correctness risks that are not primarily benchmark
signals belong in [`issue-tracker.md`](issue-tracker.md). Broad feature or
architecture work should be promoted into its own dated plan and referenced from
this backlog.

## Intake Rules

- Signal IDs use `TRB-Benchmark-NNN`. The next available ID is
  `TRB-Benchmark-005`.
- Assign an ID at intake and never reuse it, including after a signal closes or
  moves into a dated plan. Check this file's Git history before advancing or
  repairing the counter.
- Add a signal only when it comes from a benchmark, allocation guardrail,
  profiler trace, or repeated validation run.
- Record the command, date, affected row or test, measured value, why it
  matters, and the smallest useful next isolation step.
- Keep benchmark-only instrumentation in tests or benchmark support unless the
  runtime needs a durable diagnostic API.
- Prefer a focused fix when the signal has a narrow cause.
- Promote to a dated feature-work plan when the signal spans multiple
  subsystems, requires API design, or needs staged implementation.
- Close entries only after a runtime/test/docs change lands or after a written
  no-change decision explains why the signal is expected.

## Baseline Commands

Build the benchmark project against the coordinated local stack before capturing
evidence while upstream feature changes remain unreleased:

```powershell
dotnet build tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -p:UseLocalLsfStack=true
```

List the available navigation benchmark selections:

```powershell
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll list
```

Run a public guide-service smoke check (not performance-decision evidence):

```powershell
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll navigation-guide-service --job short --inProcess
```

After runtime changes, validate both coordinated local-stack configurations:

```powershell
dotnet test Trailblazer.slnx --configuration Release -p:UseLocalLsfStack=true
dotnet test Trailblazer.slnx --configuration ReleaseLean -p:UseLocalLsfStack=true
```

## Active Signals

None. The measurement-only watch below remains evidence-gated.

## Closed Signals

### TRB-Benchmark-003 - Remaining traversal cost needs focused isolation

- **Discovered:** 2026-09-10
- **Status:** Closed locally on 2026-09-10; two focused changes are implemented,
  measured and verified, with a bounded no-change decision for prefix skipping.
  This does not close the coordinated release gates or claim that every workload
  is frame-safe. `TRB-Issue-114` remains closed.
- **Evidence:** The matched capture in `TRB-Benchmark-002` below still measures
  median cold Flow acquisition of 287.615 ms at 10K cells and 3,068.373 ms at
  100K. Large cold A* also remains above the full 31.25 ms frame. The supplemental
  `RectObstructed2DAStar` case takes about 160 ms with only 27 settled medium
  states, but evaluates 607 candidates, 406 volume-union checks and 6,140 covered
  voxel intervals. The warm volume Flow acquisition/sample control takes about
  3.6 ms despite zero allocations. These supplemental timings are diagnostic
  observations, not established steady-state distributions.
- **Why it matters:** Scheduling distributes work; it does not remove the
  total cost or guarantee timely arrival of a useful result. Node count alone
  also conceals expensive body-clearance and sampling work. The selected
  input-driven frame results do not establish guided-agent or physics-host
  throughput.
- **Source/profile leads:** In the retained baseline Flow trace, native surface
  candidate preparation occupies about 37% of sampled request time inclusively;
  native edge evaluation and enumeration overlap that share. Incoming traversal
  currently rediscovers an already selected forward edge by enumerating its
  outgoing prefix. Repeated prism construction is another possible contributor,
  but some prism evidence is consumed by Release navigation rays and cannot
  simply be put behind `DEBUG`.
- **Initial isolation scope:** Establish a stable out-of-process timing protocol
  for `RectObstructed2DAStar` and `WarmFlowCacheHit`, then profile the full public
  acquisition/sample boundary to separate union geometry, ray work and cache
  validation. In parallel source analysis, quantify Flow's outgoing-prefix
  rediscovery. Attempt only the smallest measured reuse change after proving
  that it preserves exact cost, edge budgets, dependencies, stale detection and
  failure ordering. Prefix edges currently contribute observable work and
  failure behavior; skipping them is not a safe mechanical optimization.
- **Initial reproduction:** Use the cold command and supplemental protocol recorded
  below. The existing `navigation-volume-routing` benchmark selection contains
  both volume cases; use its out-of-process configuration from the
  [benchmark guide](../../tests/Trailblazer.Benchmarks/README.md) for the next
  timing decision. Ten diagnostic warmups did not establish steady state in
  every case, so do not use the pooled supplemental deltas as a speedup gate.
- **Exit gate:** Record either a matched improvement with unchanged semantic,
  work, allocation and full-coverage gates, or a bounded no-change disposition
  explaining the remaining workload limits. Do not infer that the library is
  universally fast enough from the first pass or silently weaken query budgets.

#### Isolated cost and focused changes

The follow-up begins from Trailblazer `develop`
`c7bb60973ac14c1ab52121decd76f69357f0d3dc` and GridForge `develop`
`b693e383c1a04e07fd8f4048a6c863b3f7af515d`. Both were clean before the changes.
Use `UseLocalLsfStack=true`; upstream feature changes remain unreleased.

Baseline sampled-thread traces are restricted to the measured execution root,
excluding setup and warmup. Obstructed A* spends about 67% inclusively in body
tracing and 50% in positive body/prism overlap; warm Flow acquire/sample spends
about 81% in body tracing and 66% in positive overlap. These nested shares
overlap and must not be summed or treated as predicted speedups.

Two small changes retain the existing algorithms:

1. **GridForge body tracing:** Reject disjoint whole-sweep/prism AABBs before
   expensive exact positive-overlap math, for both candidates and missing
   neighbors. Reuse existing checked sweep bounds and exact prism-vertex AABBs.
   Inclusive zero-tolerance overlap leaves tangency decisions to the existing
   narrow phase. Work debits and prism validation still precede rejection;
   closure, canonical order, physical snapshots and failure statuses are intact.
   The upstream change is tracked as `GF-Benchmark-002` in GridForge's benchmark
   backlog, not as a Trailblazer-owned geometry implementation.
2. **Trailblazer native evaluation:** Return and reuse the two prisms already
   constructed while resolving source/target passability. Retain complete
   Release ray evidence, exact portals/cost, source-before-target validation
   and default evidence on failure. No retained cache, capacity, public API,
   query budget or dependency rule changes.

The outgoing-prefix audit explains why an apparent shortcut was left alone.
A fully passable degree-`d` node visits `d` incoming candidates plus two
outgoing prefixes of length `d(d+1)/2`, or `d² + 2d` evaluated candidates.
The 10K/100K planes retain exactly 236,408 / 2,388,308 evaluations. Skipping
prefixes would skip work debits, dependency merges and possible earlier
overflow/capacity failures. That is not a safe mechanical optimization.
Prism reuse instead removes two redundant constructions per evaluated native
prefix member: 196,808 / 1,989,608 constructions in these planes. These counts
are source-derived, not independently instrumented timing measurements.

#### Matched measurements

Captures ran serially without competing builds, tests or profiling on Windows
11, Intel i7-9700K, .NET 8.0.29, SDK 10.0.302 and BenchmarkDotNet 0.15.8.
Both versions use the same out-of-process Monitoring protocols and retain all
observations, including outliers. Times below are milliseconds. These are
combined two-patch stack results, not separately measured gains per patch.

Volume: three launches, **100 warmups and 100 actual single-operation
iterations per launch**, or 300 observations per case/version. The longer
warmup replaces the earlier diagnostic ten-warmup comparison. Per-launch
first/last-20 means are retained; candidate obstructed A* ranges from
84.097–85.655 ms across those windows and warm Flow from 1.261–1.344 ms.
Small residual variation remains, but not the earlier large timing maturation.

| Volume boundary | Baseline min / median / P95 / observed P99 / max | Candidate min / median / P95 / observed P99 / max | Median reduction | Bytes, both |
| --- | ---: | ---: | ---: | ---: |
| Internal obstructed A* construction/disposal | 163.354 / 164.125 / 168.066 / 169.481 / 176.321 | 83.346 / 84.491 / 87.275 / 90.754 / 91.927 | 48.52% | 4,544 |
| Public warm Flow acquisition, one sample, disposal | 3.671 / 3.737 / 3.842 / 3.882 / 4.298 | 1.241 / 1.273 / 1.389 / 1.445 / 1.559 | 65.95% | 0 |

Sub-100 ms iterations trigger BenchmarkDotNet's minimum-iteration-time warning;
single requests intentionally remain unbatched. Observed P99 is interpolated
from all 300 raw observations, not a universal latency guarantee.

Cold public acquisition/disposal, **without sampling**: three launches, one
warmup and three actual single-operation iterations per launch, or nine
observations per case/version. This is the existing cold protocol, not a
steady-state tail study. No P95/P99 claims are made from nine observations.

| Cold public case | Baseline min / median / max | Candidate min / median / max | Median reduction | Bytes, both |
| --- | ---: | ---: | ---: | ---: |
| Flow, 100 cells | 10.351 / 10.610 / 13.864 | 9.356 / 9.613 / 12.499 | 9.40% | 18,576 |
| A*, 100 cells | 2.949 / 3.027 / 3.197 | 2.619 / 2.675 / 2.805 | 11.62% | 6,480 |
| Flow, 10K cells | 286.566 / 293.391 / 297.379 | 261.110 / 264.253 / 265.686 | 9.93% | 779,656 |
| A*, 10K cells | 82.010 / 83.084 / 84.823 | 73.590 / 74.655 / 79.724 | 10.14% | 35,320 |
| Flow, 100K cells | 3,171.907 / 3,232.861 / 3,286.268 | 2,910.909 / 2,936.949 / 2,999.373 | 9.15% | 7,698,392 |
| A*, 100K cells | 903.425 / 908.124 / 925.291 | 774.229 / 786.905 / 824.507 | 13.35% | 164,456 |

Commands, after the local-stack Release benchmark build:

```powershell
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll navigation-volume-routing --filter '*RectObstructed2DAStar*' '*WarmFlowCacheHit*' --launchCount 3 --warmupCount 100 --iterationCount 100 --keepFiles --artifacts artifacts/benchmark003/candidate-volume
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll navigation-cold-guide --filter '*ColdPublicFlow*Plane100*' '*ColdPublicAStar*Plane100*' --launchCount 3 --warmupCount 1 --iterationCount 3 --keepFiles --artifacts artifacts/benchmark003/candidate-cold
```

Baseline uses the same commands with `baseline-` artifact destinations before
source changes. Profiling uses the existing `dotnet-trace` sampled-thread-time
tool on an ignored harness calling the unchanged volume benchmark. Obstructed
A* uses 100 warmups / 80 measured calls; warm Flow uses 500 / 4,000. Only the
`RunMeasured` root is included in attribution, not the fixture's cold setup.

#### Semantic checks, provenance and remaining limits

All six volume and 18 cold cleanup records match exactly between versions,
including result signals, statuses, work, payload/dependency and drain evidence.
Allocations match in all eight cases. **Warm volume search counters describe
cold preflight**, not the timed sample. A separate untimed diagnostic using a
byte-identical benchmark/probe pair inspects the final immediate-ray meter:
both versions retain 11 lookups, 7 evaluated edges (3 primary / 4 shortcut),
5 union checks, 5 trace intervals, 126 covered intervals and 1 portal check;
all other recorded counters and the sample signal match too. This supplements
behavior/budget tests; it is one final immediate-ray observation, not complete
acquisition/sample accounting or a snapshot of every timed iteration. It is
not a substitute for those tests or a new public API.

Retained local evidence is under `artifacts/benchmark003`: full JSON/logs,
raw traces and stack summaries, frozen binaries and SHA256/version manifests,
the warm meter probe, strict `summarize.ps1 -RequireComplete`,
`verify-evidence.ps1`, and `verify.ps1` with test/coverage artifacts.
The summary requires the exact eight case identities, every launch and sample,
and unchanged allocations; missing suites cannot silently pass the final gate.

The baseline volume child was frozen before mutations and also supplies the
baseline profiling and meter probes. Baseline cold ran from the same unchanged
source/build sequence before mutations, but its separate child binaries were
not hashed before that job directory was reused: no separately verified cold
baseline binary-identity claim is made. Both candidate child sets are frozen;
their seven runtime/dependency hashes match. The benchmark harness is rebuilt
from unchanged source but has different hashes across builds/suites. In the
separate warm diagnostic, the probe, benchmark and unchanged dependencies are
byte-identical, with only the two changed runtime assemblies swapped.

| Runtime assembly | Baseline volume SHA256 | Candidate SHA256 |
| --- | --- | --- |
| Trailblazer | `54B01CB68B51080F742749A84AADD5E748160FDA861D5BB3CD8A550FED3C2CB3` | `120C0168581D082106E7E4A663108ED3DC07996E728F9D9B661A44E611114E42` |
| GridForge | `BC4BD60001024E4372031864805A5F88ECADC812DED79D27565665B6B443BE6A` | `86372D229795D9E834FB25B52055D58245991BA9C22AF4483AD192ACF78B9CAF` |

This is a focused cost reduction, not a claim that navigation is now universally
fast enough. Every large cold candidate observation still exceeds the 31.25 ms
frame, and the small obstructed volume request still takes about 84 ms. One
warm sample is much cheaper but does not establish throughput for 100 guided
agents. Existing frame controls are input-driven and do not include guided
movement or physics. Keep cold preparation/scheduling explicit; measure a real
containing guided-host frame before claiming a crowd-size or frame-budget win.
Do not add a prefix cache or weaken deterministic budgets speculatively.

#### Verification and disposition

- GridForge's six new translating-body cases pass against the pre-change
  implementation; the full 42-test body-trace class passes after the change in
  both configurations. Existing tangency, one-raw overlap, sparse/hex geometry,
  budget and zero-allocation tests remain unchanged.
- Trailblazer's six new native-evidence rows pass before prism reuse. The
  candidate's 306-test focused suite passes in Release and Lean, including
  native/incoming traversal, exact Flow budgets, independent search oracles and
  surface/volume rays. These characterize preserved semantics; the matched
  benchmark is the before/after evidence for the performance change.
- Full local-stack solution builds pass with zero warnings/errors and build
  both library targets. Final Release passes **2,467 core + 65 adapter** tests;
  Lean passes **2,405 core + 65 adapter**, with no failures or skips. Both reports
  contain exactly **30,842/30,842 lines, 12,089/12,089 branches and 3,011/3,011
  fully covered methods**. GridForge passes **825/825** tests in each package
  configuration with **8,848/8,848 lines, 3,777/3,777 branches and 1,114/1,114
  fully covered methods**. Existing coverage settings/exclusions are unchanged.
- The first covered Trailblazer Release run failed one exact-node allocation
  guard at 1,992 bytes. [TRB-Issue-118](issue-tracker.md#trb-issue-118---rebaserejoin-allocation-guards-use-different-warmup-and-measurement-bodies)
  records the confirmed duplicate warmup/measurement-body inconsistency, shared
  helper alignment and deliberate 22,528-byte mutation failure. The original
  allocation remains unattributed; no allocator fix or cured-flakiness claim is
  made. The first failed run is retained separately from the fresh passing
  `artifacts/benchmark003/validated` matrix, with no retry/tolerance gate. A
  second fresh covered Release core confirmation also passes 2,467/2,467.
- Final reports consume only fresh run directories. Duplicate TRX coverage
  attachments are byte-identical pairs, not independent coverage observations.
  Current Release runtime DLL hashes still match the measured candidate.
- At this pass's closeout, extra GridForge Debug validation passed 818/822,
  with four cursor/contact
  allocation failures. Restoring its complete pre-change production source
  reproduces the same failures and counts; restoring this patch then passes all
  42 body-trace tests in Debug. GridForge's `GF-Benchmark-003` records this
  separate pre-existing signal and its SwiftDictionary generic-key source lead.
  The Debug gate was not green at that point; this patch's local closeout did
  not waive it. The subsequent `GF-Benchmark-003` follow-up on 2026-09-10 confirms
  and fixes value-key boxing in SwiftDictionary's null guards, restoring all
  822 GridForge Debug tests. Its independently reproduced typed-indexer boxing
  is fixed too. SwiftCollections, GridForge and Trailblazer retain exact full
  Release/Lean coverage together against the local stack; Swift also has exact
  full Debug coverage. GridForge's tracker owns the complete closeout, and
  `artifacts/gf-benchmark003` retains its separate baseline, isolation and final
  evidence. This is an upstream allocation fix, not another traversal-speed claim.
- Independent source/test and evidence review found no blocking issue after
  documenting combined-patch attribution, warm-meter scope and binary provenance.
  These are local Windows results, not Linux CI or released-package validation.
- Close this narrow optimization slice. Keep the observed workload limits and
  `TRB-Benchmark-004` measurement watch; the next useful performance decision is
  representative guided-host throughput, not speculative prefix skipping.

### TRB-Benchmark-002 - Avoidable controller allocations and A* epoch reads

- **Discovered:** 2026-09-10
- **Status:** Closed locally on 2026-09-10; two focused runtime changes and
  behavior/allocation regressions are implemented, verified and independently
  reviewed. This closes the first optimization slice, not the remaining
  performance work in `TRB-Benchmark-003` or the coordinated release gates.
- **Baseline:** Clean Trailblazer `develop` at
  `12988b4c8af943659ed4e496eb589ec4db6a5435`; candidate is that commit plus the
  occupancy-resolution and A* epoch-check changes. No benchmark source, public
  API, scheduler, search budget, serialization schema or upstream source changed.
  Windows 11, Intel Core i7-9700K, .NET SDK 10.0.302 / runtime 8.0.29, Release,
  `UseLocalLsfStack=true`. Captures ran serially without competing test/build
  commands from this task.

#### Narrow causes and changes

1. `NavigatorOccupancyTracker.TryResolveVoxel` normalized an already bound grid
   configuration on every resolution, constructing a topology object. It now
   uses GridForge's existing `GridCellGeometry.TryGetPrism` against live grid
   geometry. On this host that removes 56 bytes per resolution, or 112 bytes
   per moving Navigator frame with two resolutions. No retained cache or new
   GridForge API is needed. Containment rejection still clears the outputs;
   dense/sparse rectangular and pointy/flat hex resolution retain exact identity.
2. `NavigationSurfaceAStarWork.IsWorldCurrent` eagerly read the lock-taking
   `GridWorld.ChangeSequence` property even when no live-world epoch was needed.
   It now short-circuits like Flow. A required world stamp **or** a completed
   simplification proof still forces the epoch comparison. The sampled A* trace
   highlighted this getter, but its roughly 79% sampled attribution is not a
   measured or predicted 79% speedup.

#### Matched cold acquisition

Run the following once for the clean baseline and once after rebuilding the
candidate, changing the artifact label to keep both captures:

```powershell
dotnet build tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -p:UseLocalLsfStack=true
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll navigation-cold-guide --filter '*ColdPublicFlow*Plane100*' '*ColdPublicAStar*Plane100*' --launchCount 3 --warmupCount 1 --iterationCount 3 --keepFiles --artifacts artifacts/performance-pass/baseline-cold
```

Each row has three fresh-process launches with three actual samples per launch
and one operation per sample. Times below are **minimum / median / maximum** in
milliseconds, not reliable P99 estimates from nine observations.

| Case | Baseline ms | Candidate ms | Bytes/op, unchanged |
| ---- | ----------- | ------------ | ------------------- |
| Flow, 100 cells | 10.343 / 10.493 / 13.636 | 10.384 / 10.575 / 13.670 | 18,576 |
| A*, 100 cells | 2.951 / 2.988 / 3.064 | 2.937 / 2.955 / 3.085 | 6,480 |
| Flow, 10K cells | 286.681 / 291.647 / 300.005 | 277.347 / 287.615 / 289.803 | 779,656 |
| A*, 10K cells | 85.419 / 86.841 / 90.763 | 80.688 / 82.777 / 84.321 | 35,320 |
| Flow, 100K cells | 3,146.105 / 3,181.650 / 3,217.363 | 3,016.988 / 3,068.373 / 3,096.713 | 7,698,392 |
| A*, 100K cells | 906.486 / 930.817 / 944.929 | 850.420 / 865.609 / 886.003 | 164,456 |

A* medians improved by an observed 4.68% / 7.01% at 10K / 100K. Unmodified Flow
also shifted by 1.38% / 3.56%, so attributing the entire A* delta to the edit would
overstate isolation. No Flow speedup is claimed. All 18 emitted cold counter
records match across versions, as do allocations in every row. Large synchronous
cold requests remain unsuitable for the measured fixed-frame budget.

#### Controller allocation and representative controls

A temporary harness invokes the existing benchmark setup, preparation, body,
validation and cleanup. Per version it captures three fresh processes with ten
warmup and 100 actual invocations for each of ten cases: 10K input-driven frames
with no requests, one warm request and four warm requests; 100/1,000-node A*
corridors; obstructed volume A*, ladder and mixed-action routes; and warm A*/Flow
volume reuse. This is 3,000 actual observations per version, **not** a replacement
for the benchmark suite's canonical timing protocol.

| 100-Navigator frame | Samples per version | Baseline bytes/frame | Candidate bytes/frame |
| ------------------ | ------------------- | -------------------- | --------------------- |
| No acquisitions | 300 | 11,200 in every sample | 0 in every sample |
| One warm acquisition | 300 | 11,200 in every sample | 0 in every sample |
| Four warm acquisitions | 300 | 11,200 in every sample | 0 in every sample |

All 900 measured candidate frames allocate zero bytes; all matching baseline
frames allocate 11,200 bytes. Every corresponding launch retains the same result
signal and emitted semantic/work/retention counters. Non-frame allocation values
are unchanged, and both warm volume controls remain zero-allocation.

The frame includes context simulation, all 100 Navigator simulations and motion
commits, and the selected acquisitions. Its 32 preparation frames per invocation
are untimed. It does not include a physics solver, rendering, networking or
existing-guide following; ladder/mixed cases build routes rather than execute
host actions. Several supplemental cases show within-run timing maturation
after ten warmups, so their pooled timing medians/tails are retained only as
diagnostics, not causal improvements, regressions or steady-state latency gates.

#### Verification and retained evidence

- New moving-frame regression reproduces 28,672 bytes before the fix across
  256 frames and requires zero afterward, while checking movement, current-frame
  committed-cell state, occupancy and absence of duplicate entry events. Four
  topology/storage resolution rows reproduce 14,336 bytes each before and zero
  after across 256 resolutions, with exact physical cell identity assertions.
- A new semantic regression keeps immutable surface A* valid after an unrelated
  raw-world mutation. Existing volume-mutation coverage protects required world
  stamps; the completed-proof mutation test now explicitly proves that its
  admission does not require a world stamp and that the proof has completed.
- The new allocation tests use the repository's 256-invocation warmup and the
  same helper for warmup/measurement. Earlier focused Lean runs during test
  development observed 4,088 / 6,680-byte transients; isolated checks passed.
  Final broad focused runs and the full matrix below pass strict zero-byte
  assertions without tolerances or retries. These observations do not identify
  the cause of the historical, closed `TRB-Issue-114`.
- Final solution builds pass with zero warnings/errors. Release passes 2,461
  core + 65 adapter tests; ReleaseLean passes 2,399 core + 65 adapter tests, no
  failures or skips. Both configurations cover exactly **30,838/30,838 lines,
  12,089/12,089 branches and 3,009/3,009 fully covered methods**, using the
  existing coverage settings without new exclusions.
- Independent source/test and evidence review found no remaining actionable
  findings after clarifying binary provenance and supplemental timing limits.
  Local Windows validation is not Linux CI or released-package validation.
- Local, ignored evidence lives under `artifacts/performance-pass/`: full cold
  BenchmarkDotNet JSON/logs in `baseline-cold` / `candidate-cold`, thread-sampling
  traces and stack summaries, retained assemblies in `baseline-bin` /
  `candidate-bin`, assembly/version/SHA256 manifests, the supplemental harness
  and its 60 logs, `summary.json`, and final test/TRX/Cobertura/HTML reports.
  `capture-scenarios.ps1`, `summarize.ps1` and `verify.ps1` retain the exact local
  supplemental capture, summarization and full-coverage commands.
- Baseline Trailblazer SHA256 is
  `2FC0B7782371F9C85D232CF474FEB58B973613E94B03AD36F0D1184288E5449F`;
  candidate is
  `9811A2020094D046893773DD8CB62F2AB55A4BAFF1B4E3778AF4A299C645C755`.
  All captured runtime dependency hashes match. The cold benchmark assembly was
  rebuilt from unchanged source/configuration and has a different hash; the
  supplemental harness and benchmark assembly are byte-identical across the
  retained before/after directories. Retained manifests, not the reused
  BenchmarkDotNet job output folder, identify the baseline binaries.

### TRB-Benchmark-001 - Guide acquisition exceeds the provisional navigation allowance

- **Discovered:** 2026-09-08
- **Status:** Closed locally on 2026-09-09; focused optimizations and the approved
  [deterministic scheduling plan](done/navigationSchedulingPlan.md) are implemented,
  verified and independently reviewed. CI/released-package validation remains
  part of the coordinated release gates, not a claim of this local closeout.
- **Evidence:** The Phase 3 out-of-process `SlowAcquisition` protocol measures
  median public Flow acquisition of 5.51 seconds at 10K nodes and 55.42 seconds
  at 100K nodes; public A* medians are 2.14 and 21.92 seconds. Four distinct cold
  requests produce median containing-frame costs of 21.73 and 221.11 seconds.
  Every large cold sample exceeds the full 31.25 ms frame. The canonical 100K
  four-warm-request frame also exceeds the 2 ms allowance in all 300 samples
  (minimum 2.535 ms; sample P99 2.810 ms). Distributions, counters and artifacts are in
  [the acquisition execution record](done/navigationHardeningPlan.md#phase-3---measurement-protocol-and-baseline).
- **Budget:** Provisional 32 Hz headless host, 2 ms navigation allowance, 100
  existing Navigators and bursts of one or four requests. Finite query work
  limits do not bound elapsed time to one frame.
- **Reproduction:** Run the cold guide/frame and canonical-control commands in the
  [benchmark guide](../../tests/Trailblazer.Benchmarks/README.md#cold-public-acquisition-and-containing-frames)
  against the recorded local source stack without competing builds or load.
- **Investigation:** The [request-root profile and source audit](done/navigationSchedulingPlan.md)
  attributes about 91-92% of sampled cold Flow/A* time to the simple surface-route
  path, including unused Release diagnostic geometry. Warm 100K acquisition
  spends about 95-97% in dependency validation; six full passes visit 9,378 page
  entries per successful acquisition in this workload. These overlapping sampled
  shares are attribution, not predicted speedups. `TRB-Issue-114` is closed as
  an inconclusive investigation in the [issue tracker](issue-tracker.md); its
  unattributed covered-test observation does not confound these separate
  acquisition traces, and no production allocator fix is claimed.
- **Focused fix result:** After resolving `TRB-Issue-115`, matched cold public
  Flow medians are 1.864 seconds at 10K and 18.924 seconds at 100K; A* medians
  are 0.717 and 7.343 seconds. The same nine-sample protocol shows 65.85-66.50%
  median reductions against the retained historical baseline, with unchanged
  allocations and reported reference counters. Every selected sample still
  exceeds the full frame allowance. Warm/frame performance was not remeasured;
  the [execution record](done/navigationSchedulingPlan.md#cpu-cost-improvements)
  retains the distributions, provenance limits and remaining decision.
- **Further focused result:** Guarded rectangular portal reuse and method-local
  dependency-proof reuse reduce cold Flow medians to 293.612 ms / 3,225.761 ms
  and A* medians to 87.841 ms / 904.471 ms at 10K / 100K. All 36 cold samples
  still exceed 31.25 ms. The directly measured 100K four-warm-request frame now
  has observed P99 1.870 ms and maximum 1.915 ms across 300 samples, with
  unchanged allocations and reference counters. See the
  [full follow-up evidence](done/navigationSchedulingPlan.md#cpu-cost-improvements).
- **Final result:** The scheduler-enabled build's four-warm-request P99 is
  1.940 ms across 300 observations, with one 2.129 ms excursion. All 81 scheduled
  actual operations have observed containing-frame P99 below 2 ms (highest
  1.933 ms). The cold cases retain 653,175 frames, including 34 above 2 ms and
  none above 31.25 ms; maximum is 3.924 ms. A* publication reaches 2.669 ms, so
  completion is not universally below the allowance. Both configurations retain
  exact 100% line/branch/method coverage.
- **Disposition:** Warm reuse meets the selected observed-P99 allowance.
  Synchronous cold acquisition remains rejected for the measured fixed frame;
  use loading/off-frame preparation or explicit scheduling. Scheduling preserves
  deterministic ownership and bounds continuation work but has substantial FIFO
  latency: 100K Flow takes about 6 simulated minutes for one request and 24.25
  minutes for the fourth at these conservative defaults. The
  [complete results and limits](done/navigationSchedulingPlan.md#final-disposition)
  retain every phase, outlier, completion frame and binary identity. No universal
  timing guarantee, automatic Navigator queuing or further speculative search
  optimization is implied by closure.

## Experimental Signals

| Signal | Status | Revisit When |
| ------ | ------ | ------------ |
| n/a    | n/a    | n/a          |

## Watch Items

### TRB-Benchmark-004 - Tiny cold Flow protocol has an iteration-position excursion

- **Discovered:** 2026-09-10 while measuring `TRB-Benchmark-003`.
- **Status:** Measurement watch; not a runtime regression, correctness issue,
  release blocker, or reopening of `TRB-Issue-114`.
- **Evidence:** In `ColdPublicFlow(Plane100)`, the second actual invocation is
  slower in all three launches of both versions: baseline 13.472–13.864 ms
  versus 10.351–10.616 ms for the other invocations; candidate 12.385–12.499 ms
  versus 9.356–9.797 ms. Allocations and reported semantic/work counters remain
  unchanged. The cause is not established.
- **Reproduction:** Use the exact cold command above and inspect ordered raw
  measurements, not just pooled means. Evidence is in the retained six launch
  sequences under `artifacts/benchmark003/{baseline,candidate}-cold`.
- **Next isolation:** Before making a small-request steady-state/tail claim,
  retain this cold protocol as a control and separately extend its warmups and
  actual iterations. If the position effect persists, profile that invocation
  boundary. Do not discard it as an outlier, alter runtime code without cause,
  or extrapolate a P99 from nine samples. It does not invalidate the matched
  protocol comparison or the separate 100-warmup volume results.

## Promotion Criteria

Promote a signal from this backlog into a dedicated dated plan when it has:

- reproducible evidence and a suspected runtime phase.
- enough subsystem breadth that a single focused patch would be misleading.
- API, architecture, or multi-workstream design decisions.
- correctness or ordering invariants that need staged implementation.
- benchmark and allocation evidence that should move with the new plan.

## Current Recommendation

Use the current [benchmark guide](../../tests/Trailblazer.Benchmarks/README.md)
for navigation workloads and canonical run settings. Keep this document as the
intake bucket for measured signals and promote broader work into a dated feature
plan when the scope outgrows a focused patch. Physics collision-shape throughput
belongs to Gravitas, not this navigation backlog.
