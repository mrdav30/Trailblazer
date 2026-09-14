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
| `navigation-guided-frame` | `NavigationGuidedFrameBenchmarks` |
| `navigation-scheduled-frame` | `NavigationScheduledFrameBenchmarks` |
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

## Accepting a capture

Verify the full log against the expected cases, child launches and measurement
stages before using a result. The launcher returns `1` for critical validation
errors, failed reports/builds, and failed or unknown exits in returned child
executions. A failure in either the canonical or single-shot group remains a
failure after the other group runs. Successful help/list commands return `0`.

A zero exit is still not a complete capture audit: BenchmarkDotNet does not
include extra diagnoser executions in its returned reports, and empty summaries
can represent informational or invalid/no-match requests. Partial statistics
from an earlier launch can survive a later child failure.

Exclude incomplete cases, require successful timed and diagnostic child exits,
and treat missing allocation diagnostics as unknown rather than zero. Retain
the actual executed child binaries, generated source and runtime configuration
when an exception occurs. A successful replay is additional diagnostic evidence,
not a replacement for the missing launches of an earlier capture.

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

Cold volume cases use internal admission/search/payload construction and disposal,
not the full public guide-service acquisition boundary. Warm cases measure public
acquisition, one exact guide sample, and disposal. For warm cases, the printed
search/work/dependency counters describe the untimed cold preflight; they are
not counters for the timed sample. Do not infer per-agent frame throughput from
either boundary.

For a matched obstruction/sampling investigation, use the same longer warmup
on both versions and retain every observation:

```powershell
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll navigation-volume-routing --filter '*RectObstructed2DAStar*' '*WarmFlowCacheHit*' --launchCount 3 --warmupCount 100 --iterationCount 100 --keepFiles --artifacts artifacts/volume-investigation
```

This remains one operation per iteration. Inspect each launch's timing sequence
for warmup drift, and report observed tails rather than a universal P99 guarantee.
Sub-100 ms iterations produce BenchmarkDotNet's minimum-iteration-time warning;
these per-request observations intentionally do not batch requests together.

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
[Phase 3 evidence](../../docs/feature-work/done/navigationHardeningPlan.md#phase-3---measurement-protocol-and-baseline)
records the acceptance budget, source identities, results and decision.
Use in-process smoke only for small cases: large-case setup plus acquisition can
exceed the in-process runner's timeout even when every query terminates normally.

#### Cold caches versus runtime warmup

Here, **cold** means a payload-cache miss against an already published graph,
with reusable workspaces and pools. It does not mean a fresh process, nor does
warming cache hits establish that cold integration has finished warming up.
Keep early-invocation measurements separate from longer-warmed measurements.

For an ordered small-request investigation, preserve the short control, then
change actual count and warmup count separately on the same binaries:

```powershell
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll navigation-cold-guide --filter '*ColdPublicFlow*Plane100?)' --launchCount 3 --warmupCount 1 --iterationCount 3 --keepFiles --artifacts artifacts/cold-protocol/control
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll navigation-cold-guide --filter '*ColdPublicFlow*Plane100?)' --launchCount 3 --warmupCount 1 --iterationCount 100 --keepFiles --artifacts artifacts/cold-protocol/long-actual
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll navigation-cold-guide --filter '*ColdPublicFlow*Plane100?)' --launchCount 3 --warmupCount 100 --iterationCount 100 --keepFiles --artifacts artifacts/cold-protocol/long-warmup
```

The final `?)` anchors this filter to the quoted `Plane100` parameter; a trailing
`Plane100*` also selects 10K and 100K. Verify discovery reports exactly one case.
Retain every warmup and actual observation, grouped by launch and invocation
index. Compare early/late windows before describing a distribution as steady
state; a larger warmup count alone is not proof. `WorkloadResult` repeats the
actual timings and must not be counted again. BenchmarkDotNet's allocation/GC
diagnostic uses a separate invocation, not a GC record for each timed request.
Do not infer a runtime fix from warmup sensitivity or recommend disabling tiered
compilation to improve a benchmark result. Historical short controls remain
valid matched-protocol comparisons, not warmed steady-state throughput claims.

### Complete guided-host frames

`NavigationGuidedFrameBenchmarks` runs 100/500 ungrouped Navigators following
real A* or Flow guides. A wall with a distant gap blocks direct travel on a
100x100 rectangular grid. Setup verifies that direct rays are blocked by
geometry, not exhausted budgets. Held-guide capacity is sized independently
from the single reusable search workspace. Map publication and initial guide
acquisition are outside the measurement; large A* setup can take minutes.

Each BenchmarkDotNet operation is a **64-frame block**, preceded by 32 untimed
preparation frames at 32 Hz. Every nested frame includes context simulation,
all Navigator simulations and motion commits. This exercises guide following,
local avoidance, motor/turning work, occupancy and periodic line-of-sight
rechecks. It is a headless horizontal-support host with no collision solver,
engine, networking, rendering or movement groups. The measured initial route
portion stays on the starting side of the wall; it is not an arrival or full
detour acceptance test.

The original benchmark remains
`NavigationGuidedFrameBenchmarks.Simulate64GuidedFrames`; its method body,
agent simulation, parameters, and legacy payload are uninstrumented. Three
separate instrumented identities use interval 16:

| Method | Payload `mode` | Phase assignment |
| --- | --- | --- |
| `Simulate64GuidedFramesInstrumentedUnphased` | `Unphased` | null |
| `Simulate64GuidedFramesInstrumentedBalancedPhases` | `BalancedPhases` | stable agent index modulo 16 |
| `Simulate64GuidedFramesInstrumentedUnbalancedPhase` | `UnbalancedPhase` | zero for every agent |

Instrumented cases observe each agent's real direct ray immediately around its
ordered `Navigator.Simulate()` call, inside the same containing-frame timer.
Per-agent schedule validation and aggregate reconciliation run afterward. The
instrumented `NAVIGATION_GUIDED_SAMPLES` payload adds `mode`,
`intervalFrames`, `phaseAssignments`, `clockOrigin`, `mandatoryRayCount`,
`preparationRayCounts`, `periodicRayCounts`, `firstAdmissionTicks`,
`firstAdmissionAllocatedBytes`, and `firstAdmissionCollectionMask`.

Per-frame validation is outside the inner timer/allocation bracket. It requires
live guidance, safe body bounds, fresh committed cells and movement in each
32-frame block. Per-frame position/velocity/cell hashes must replay exactly
within each process; the log analyzer also compares them across launches.
Reset verifies drained active/reserved/detached/scheduled ownership.

`NAVIGATION_GUIDED_SAMPLES` records retain raw timestamp ticks, current-thread
allocations, process-wide collection-count-change masks and replay hashes.
JSON export is untimed. Collection counters bracket slightly more than the
stopwatch and indicate overlap, not causal GC attribution. Whole-operation
BenchmarkDotNet timings include instrumentation and validation, so use the
nested records for frame costs. No warmup, diagnostic or outlier frames are
silently folded into or removed from actual-only statistics.

Start with a separately labelled sizing run before choosing a repeated protocol.
Select one exact method identity and pair it with the case in every filter
pattern because BenchmarkDotNet unions separate patterns:

```powershell
$method = 'Simulate64GuidedFramesInstrumentedBalancedPhases'
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll navigation-guided-frame --filter "*.$method(AgentCount: 100,*" "*.$method(AgentCount: 500,*FlowField*" --launchCount 1 --warmupCount 1 --iterationCount 3 --keepFiles --artifacts artifacts/guided-sizing
```

For a bounded repeated capture, the same paired filters run A*/100, Flow/100 and
Flow/500 in three fresh processes each. Set `$method` to exactly one of the four
identities above and use a distinct artifacts path and console log for each
mode. The selection deliberately excludes the much more expensive A*/500 setup;
do not describe it as the complete four-case matrix.

```powershell
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll navigation-guided-frame --filter "*.$method(AgentCount: 100,*" "*.$method(AgentCount: 500,*FlowField*" --launchCount 3 --warmupCount 1 --iterationCount 3 --keepFiles --artifacts artifacts/guided-capture
```

Capture console output to a log, then analyze it:

```powershell
& tests/Trailblazer.Benchmarks/AnalyzeGuidedFrames.ps1 -LogPath artifacts/guided-capture.log -OutputPath artifacts/guided-summary.json
& tests/Trailblazer.Benchmarks/TestGuidedFrameAnalysis.ps1
```

The parser checks record shape, method-to-mode identity, the hand-derived
per-agent schedule, stage pairing, successful child exits, and cross-launch
replay. It reports `StageValidation`, `StageCounts`, full-block statistics,
per-launch records, and pooled actual-only frame statistics. Instrumented modes
also report `Periodic` and `Ordinary` slices. Frame statistics contain observed
median/P95/P99/maximum, counts above 2 ms and 31.25 ms, total/maximum allocation,
and collection-overlap counts; each sample uses its own stopwatch frequency.
Independently check expected cases, launches, and block counts against
BenchmarkDotNet JSON, including failed setups: a partial capture is not a
passing matrix. Nested frames are correlated; hundreds of ordinary frames do
not cancel out a repeatedly expensive recheck frame.

For a bounded end-to-end semantic lifecycle smoke, use:

```powershell
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll guided-phase-smoke
```

Its `json-end-to-end-restore-preparation` sample includes serialization, source
Navigator reset, restored-shell construction, prepare/populate, and assertions.
It is not a population-only timing label or an acceptance benchmark.

Preserve the **executed child runtime**, not just the parent benchmark output.
With `--keepFiles`, each `// Execute: dotnet ... in ...` log line identifies the
generated runner and its working directory. Copy that directory, including its
dependencies, runtime configuration, symbols and generated runner, to a unique
evidence folder immediately after each capture; record SHA256 hashes and the
source revisions/diff. Keep the generated project/source and full log as well.
Later captures can reuse the same generated directory. Parent-output hashes
alone do not establish which assemblies the child executed, especially with
the prebuilt local-stack references. Compare the frozen child manifests before
attributing a result to a single changed library.

### Scheduled cold acquisition across fixed frames

`NavigationScheduledFrameBenchmarks` measures one complete scheduled acquisition
burst at 10K/100K nodes with A* or Flow and one/four distinct cold queries. A
zero-request row is a 256-frame instrumentation/continuous-host control. The
BenchmarkDotNet operation includes the entire acquisition plus all intervening
headless host frames; its elapsed time is not isolated search cost.

Every containing frame separately records timestamp ticks, current-thread
allocation, scheduler phase, active request index, completion mask and GC
collection-count-change mask. These process-wide counters bracket a slightly
wider interval than the stopwatch; a change shows correlation, not that a GC
pause caused the frame duration. The
`NAVIGATION_SCHEDULE_SAMPLES` JSON records retain every observation, including
activation, publication, result transfer, cancellation-free disposal, GC and
outliers. Export also records exact scheduler settings and stopwatch frequency.
Use the surrounding BenchmarkDotNet log to associate records with warmup,
actual and diagnostic invocations before computing actual-only statistics.

The host runs 100 input-driven Navigators, reversing input at deterministic
position bounds to stay on the map during long acquisitions. Movement is
verified over each 32-frame block. Each measured frame includes
context maintenance/scheduling, all Navigator simulations/commits, request
polling, typed lease transfer and disposal. Host input assignment and per-frame
behavior validation are outside the nested frame timer, but remain part of the
whole-operation measurement. Validation checks fresh committed-cell frames and
continued movement, exact request costs, A* step counts, complete Flow payloads,
and drained scheduler/payload ownership.

Map preparation, cache eviction, agent reset and 32 initial moving frames are
untimed. The fixed sample buffers retain approximately 32 MB per benchmark
instance. Untimed JSON export makes additional copies; retain this protocol
when comparing builds rather than selectively warming away its GC effects.

Begin with a separately labeled out-of-process dry/sizing capture, then freeze
the selected cases and slice settings before repeated capture. The intended
cold protocol is three launches, one warmup and three actual whole acquisitions
per launch; pass `--launchCount 3 --warmupCount 1 --iterationCount 3` explicitly
to override the shared benchmark job's longer defaults. Report completion frame counts (and simulated waiting time at
32 Hz), per-acquisition and per-phase maxima, observed frame P99, and counts
above 2 ms/31.25 ms. Nested frames are correlated samples: thousands of cheap
search frames must not hide an expensive activation or completion in a pooled
percentile. The [scheduling execution record](../../docs/feature-work/done/navigationSchedulingPlan.md)
owns the selected commands, results and interpretation.

Summarize a retained log without mixing warmup or allocation-diagnostic frames
into actual observations:

```powershell
& tests/Trailblazer.Benchmarks/AnalyzeScheduledFrames.ps1 -LogPath artifacts/scheduled-capture.log -OutputPath artifacts/scheduled-frame-summary.json
```

The parser pairs each nested record with the following `WorkloadWarmup` or
`WorkloadActual` line. Records after `AfterActualRun` remain a separate
diagnostic category; `WorkloadResult` is not a new invocation. It rejects
unmatched records and incomplete completion masks. Independently verify the
expected cases, launch/sample counts and whole-operation values against the
BenchmarkDotNet JSON before accepting the capture.

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
