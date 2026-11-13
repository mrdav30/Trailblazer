# Trailblazer Benchmarks

This project uses BenchmarkDotNet to measure current graph, flow-field, ray, and
map-publication hot paths. Retired chart-backed search providers, caches, hybrid
fallback, and legacy steering scenarios are intentionally absent.

## Requirements

- The .NET SDK selected by the repository's `global.json`, plus the .NET 10
  runtime
- `Release` configuration

## Running

List the available selections:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net10.0 -- list
```

Run all benchmarks:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net10.0 -- all
```

Aliases are derived from benchmark class names by removing `Benchmarks` and
joining the remaining words with hyphens. Useful selections include:

| Alias                              | Benchmark class                                                                            |
| ---------------------------------- | ------------------------------------------------------------------------------------------ |
| `navigation-surface-a-star`        | `NavigationSurfaceAStarBenchmarks`                                                         |
| `navigation-guide-service`         | `NavigationGuideServiceBenchmarks`                                                         |
| `navigation-cold-guide`            | `NavigationColdGuideBenchmarks`                                                            |
| `navigation-cold-frame`            | `NavigationColdFrameBenchmarks`                                                            |
| `navigation-guided-frame`          | `NavigationGuidedFrameBenchmarks`                                                          |
| `navigation-planar-frame`          | `NavigationPlanarFrameBenchmarks`                                                          |
| `navigation-recovery`              | `NavigationRecoveryBenchmarks`                                                             |
| `navigation-scheduled-frame`       | `NavigationScheduledFrameBenchmarks`                                                       |
| `navigation-a-star-contention`     | `NavigationAStarContentionBenchmarks`                                                      |
| `navigation-flow-field`            | `NavigationFlowFieldBenchmarks` and the promotion, agent, mutation, and articulation cases |
| `navigation-flow-field-contention` | `NavigationFlowFieldContentionBenchmarks`                                                  |
| `navigation-ray`                   | `NavigationRayBenchmarks`                                                                  |
| `navigation-volume-routing`        | `NavigationVolumeRoutingBenchmarks`                                                        |
| `navigation-map-bake`              | `NavigationMapBakeBenchmarks`                                                              |
| `navigation-graph-lifecycle`       | `NavigationGraphLifecycleBenchmarks`                                                       |
| `navigation-graph-composition`     | `NavigationGraphCompositionBenchmarks`                                                     |
| `navigation-graph-contention`      | `NavigationGraphContentionBenchmarks`                                                      |

For a fast development smoke check:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net10.0 -- navigation-guide-service --job short --inProcess
```

Do not treat short-run results as canonical measurements.

### Native planar frames

`navigation-planar-frame` uses a shared Trailblazer and Gravitas world at
16 Hz with continuous CCD. Both shapes have radius 1/4, and the side-view
capsule has a 1/4-unit axis. The top-down open, top-down blocked, and side-view
support rows each measure two complete fixed steps per operation. The blocked
circle starts tangent to the wall and requests inward motion on both steps.
The side-view ladder row measures an ascent and a descent, including two
immediate A* requests, six complete fixed steps, and two action completions.
Setup checks accepted poses, support, clipping, and ladder exits before timing.

For coordinated source measurement, first build the benchmark project with
`UseLocalLsfStack=true`. Then set `UsePrebuiltLocalLsfStack=true` for the
benchmark host and its generated children. From the repository root:

```text
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll navigation-planar-frame --launchCount 2 --warmupCount 3 --iterationCount 5
```

Compare timings and allocation per pair within each row. Divide the time by
the row's frame count for a containing-frame mean.

## Accepting a capture

### Recovery candidate acquisition controls

The recovery harness explicitly selects `Immediate` (control) or `Scheduled`.
For example, `recovery-smoke Flow100 Scheduled Default` exercises all five
episodes twice; `recovery-capture 1 Scheduled Default Flow` selects Flow100 and
Flow500 for a deliberately scoped diagnostic launch. Omit `Flow` for the full
15-case acquisition/recovery matrix. Cold/warm cases configure the selected mode
initially. Lifecycle cases always prepare 32 moving frames in Immediate mode,
then change policy without releasing already-adopted guidance; restore records
the selected durable policy before population.

`Default` means **default work limits with workload-sized request/lease
retention**, not all public defaults. An explicit tuning argument contains six
positive limits in `lookup,candidate,node,edge,connection,operation` order, for
example `32768,64,2048,4096,64,128`. Such runs retain the exact settings and a
distinct work-limit label; they do not change runtime defaults. Candidate
records retain containing frames, operation turns, waiting/readiness outcomes
and separate ordinary cache-hit/follower-checkout/build/promotion counters. Warm
reuse requires zero builds and exactly one successful reuse per agent, counting
distinct ordinary hits plus follower checkouts. Its raw record is exported
before any acceptance failure is raised.

The recovery analyzer defaults to all populations and three complete launches.
Use `-ExpectedPopulations Flow` (or `Flow100`, `Flow500`, `AStar100`) and
`-ExpectedDirectLaunches 1` only for explicitly labeled diagnostic subsets. Use
`-Smoke` for smoke logs. Replay groups distinguish acquisition mode and work
limits; failed/censored raw records remain diagnostic evidence, never
acceptance.

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
and treat missing allocation diagnostics as unknown rather than zero. Retain the
actual executed child binaries, generated source and runtime configuration when
an exception occurs. A successful replay is additional diagnostic evidence, not
a replacement for the missing launches of an earlier capture.

## Current provider benchmarks

`NavigationSurfaceAStarBenchmarks` measures production endpoint admission plus
an uncached graph A* search at approximately 100, 1K, 10K, and 100K expanded
nodes. BenchmarkDotNet reports latency and managed allocation; cleanup emits the
deterministic work-meter, one-frontier heap, workspace, and result-byte counters
for each case.

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

`NavigationFlowFieldBenchmarks` covers cold reverse integration at 100, 1K, 10K,
and 100K settled nodes, near-to-far prefix promotion, warm 100/500/5,000 agent
batches, affected/unaffected invalidation, and an exact 1,000,000-node
articulation split. `NavigationFlowFieldContentionBenchmarks` covers same-key
and near/far publication with persistent manual workers and reversed completion.

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

`NavigationVolumeRoutingBenchmarks` covers open and obstructed rectangular Gas
routes in 2D, open and obstructed 3D Flow, a large body, a hex vertical
diagonal, rule scanning, ladder and duck actions, a mixed medium/action route,
warm A*/Flow cache hits, and full-direction versus face-only controls. Cleanup
reports exact settled states, evaluated/primary/shortcut candidates, union and
covered-voxel interval work, transition candidates/pairs, successful dependency
merges, emitted dependency facts, A* guide steps, Flow payload nodes, immutable
payload bytes, and the warm sample allocation preflight. BenchmarkDotNet
supplies median, P95, P99, max, and managed-allocation columns through the
shared gate configuration.

Cold volume cases use internal admission/search/payload construction and
disposal, not the full public guide-service acquisition boundary. Warm cases
measure public acquisition, one exact guide sample, and disposal. For warm
cases, the printed search/work/dependency counters describe the untimed cold
preflight; they are not counters for the timed sample. Do not infer per-agent
frame throughput from either boundary.

For a matched obstruction/sampling investigation, use the same longer warmup on
both versions and retain every observation:

```powershell
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll navigation-volume-routing --filter '*RectObstructed2DAStar*' '*WarmFlowCacheHit*' --launchCount 3 --warmupCount 100 --iterationCount 100 --keepFiles --artifacts artifacts/volume-investigation
```

This remains one operation per iteration. Inspect each launch's timing sequence
for warmup drift, and report observed tails rather than a universal P99
guarantee. Sub-100 ms iterations produce BenchmarkDotNet's
minimum-iteration-time warning; these per-request observations intentionally do
not batch requests together.

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
ladder joins two separated landing strips with one explicit vertical action. The
ladder case measures route construction, not host execution of that action.

`NavigationColdFrameBenchmarks` measures a 32 Hz headless frame with 100 real,
input-driven Navigators on a plane and zero, one or four public Flow requests.
Each measured frame follows 32 untimed preparation frames. Cold bursts use
distinct exact profile keys that preserve the integration graph; warm bursts
reuse those same keys. There is no collision solver, existing guide-following,
networking or rendering in this fixture. Treat it as a lower-bound host
workload.

World construction, publication, workspace/pool warmup, agent reset and cache
preparation are untimed. Cold setup verifies empty caches before **every**
invocation. Warm setup verifies the exact reusable payload; cleanup checks
payload sizes and drained leases/reservations. Console counters report work,
payload retention and allocation preflights alongside BenchmarkDotNet
statistics.

While upstream changes remain unreleased, run the coordinated source stack:

```powershell
dotnet build tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net10.0 -p:UseLocalLsfStack=true
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll navigation-cold-guide --filter '*Ladder*' --job dry --inProcess --artifacts artifacts/hardening-phase3/smoke-small
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll navigation-cold-guide --filter '*ColdPublicFlow*' '*ColdPublicAStar*' '*ColdInternalFlow*' --launchCount 3 --warmupCount 1 --iterationCount 3 --artifacts artifacts/hardening-phase3/slow-guides
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll navigation-cold-frame --filter '*Cold1*' '*Cold4*' --launchCount 3 --warmupCount 1 --iterationCount 3 --artifacts artifacts/hardening-phase3/slow-frames
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll navigation-cold-guide navigation-cold-frame navigation-guide-service --filter '*WarmPublicFlow*' '*Baseline*' '*Warm1*' '*Warm4*' '*WarmFlowAcquireSampleDispose*' --artifacts artifacts/hardening-phase3/canonical-controls
```

The final command retains the normal canonical job (three launches, 100 measured
iterations per launch). The slow cold commands use the predeclared nine-sample
protocol in the execution record: report minimum/median/maximum, not a reliable
P99 estimate. Generated child builds use `UsePrebuiltLocalLsfStack` to consume
the already built binaries. Do not run builds or competing benchmark jobs during
capture. Retain the full JSON/logs, not just a rounded mean. The
[Phase 3 evidence](../../docs/feature-work/done/navigationHardeningPlan.md#phase-3---measurement-protocol-and-baseline)
records the acceptance budget, source identities, results and decision. Use
in-process smoke only for small cases: large-case setup plus acquisition can
exceed the in-process runner's timeout even when every query terminates
normally.

#### Cold caches versus runtime warmup

Here, **cold** means a payload-cache miss against an already published graph,
with reusable workspaces and pools. It does not mean a fresh process, nor does
warming cache hits establish that cold integration has finished warming up. Keep
early-invocation measurements separate from longer-warmed measurements.

For an ordered small-request investigation, preserve the short control, then
change actual count and warmup count separately on the same binaries:

```powershell
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll navigation-cold-guide --filter '*ColdPublicFlow*Plane100?)' --launchCount 3 --warmupCount 1 --iterationCount 3 --keepFiles --artifacts artifacts/cold-protocol/control
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll navigation-cold-guide --filter '*ColdPublicFlow*Plane100?)' --launchCount 3 --warmupCount 1 --iterationCount 100 --keepFiles --artifacts artifacts/cold-protocol/long-actual
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll navigation-cold-guide --filter '*ColdPublicFlow*Plane100?)' --launchCount 3 --warmupCount 100 --iterationCount 100 --keepFiles --artifacts artifacts/cold-protocol/long-warmup
```

The final `?)` anchors this filter to the quoted `Plane100` parameter; a
trailing `Plane100*` also selects 10K and 100K. Verify discovery reports exactly
one case. Retain every warmup and actual observation, grouped by launch and
invocation index. Compare early/late windows before describing a distribution as
steady state; a larger warmup count alone is not proof. `WorkloadResult` repeats
the actual timings and must not be counted again. BenchmarkDotNet's
allocation/GC diagnostic uses a separate invocation, not a GC record for each
timed request. Do not infer a runtime fix from warmup sensitivity or recommend
disabling tiered compilation to improve a benchmark result. Historical short
controls remain valid matched-protocol comparisons, not warmed steady-state
throughput claims.

### Complete guided-host frames

`NavigationGuidedFrameBenchmarks` runs 100/500 ungrouped Navigators following
real A* or Flow guides. A wall with a distant gap blocks direct travel on a
100x100 rectangular grid. Setup verifies that direct rays are blocked by
geometry, not exhausted budgets. Held-guide capacity is sized independently from
the single reusable search workspace. Map publication and initial guide
acquisition are outside the measurement; large A* setup can take minutes.

Each BenchmarkDotNet operation is a **64-frame block**, preceded by 32 untimed
preparation frames at 32 Hz. Every nested frame includes context simulation, all
Navigator simulations and motion commits. This exercises guide following, local
avoidance, motor/turning work, occupancy and periodic line-of-sight rechecks. It
is a headless horizontal-support host with no collision solver, engine,
networking, rendering or movement groups. The measured initial route portion
stays on the starting side of the wall; it is not an arrival or full detour
acceptance test.

The original benchmark remains
`NavigationGuidedFrameBenchmarks.Simulate64GuidedFrames`; its method body, agent
simulation, parameters, and legacy payload are uninstrumented. Three separate
instrumented identities use interval 16:

| Method                                              | Payload `mode`    | Phase assignment             |
| --------------------------------------------------- | ----------------- | ---------------------------- |
| `Simulate64GuidedFramesInstrumentedUnphased`        | `Unphased`        | null                         |
| `Simulate64GuidedFramesInstrumentedBalancedPhases`  | `BalancedPhases`  | stable agent index modulo 16 |
| `Simulate64GuidedFramesInstrumentedUnbalancedPhase` | `UnbalancedPhase` | zero for every agent         |

Instrumented cases observe each agent's real direct ray immediately around its
ordered `Navigator.Simulate()` call, inside the same containing-frame timer.
Per-agent schedule validation and aggregate reconciliation run afterward. The
instrumented `NAVIGATION_GUIDED_SAMPLES` payload adds `mode`, `intervalFrames`,
`phaseAssignments`, `clockOrigin`, `mandatoryRayCount`, `preparationRayCounts`,
`periodicRayCounts`, `firstAdmissionTicks`, `firstAdmissionAllocatedBytes`, and
`firstAdmissionCollectionMask`.

Per-frame validation is outside the inner timer/allocation bracket. It requires
live guidance, safe body bounds, fresh committed cells and movement in each
32-frame block. Per-frame position/velocity/cell hashes must replay exactly
within each process; the log analyzer also compares them across launches. Reset
verifies drained active/reserved/detached/scheduled ownership.

`NAVIGATION_GUIDED_SAMPLES` records retain raw timestamp ticks, current-thread
allocations, process-wide collection-count-change masks and replay hashes. JSON
export is untimed. Collection counters bracket slightly more than the stopwatch
and indicate overlap, not causal GC attribution. Whole-operation BenchmarkDotNet
timings include instrumentation and validation, so use the nested records for
frame costs. No warmup, diagnostic or outlier frames are silently folded into or
removed from actual-only statistics.

Start with a separately labelled sizing run before choosing a repeated protocol.
Select one exact method identity and pair it with the case in every filter
pattern because BenchmarkDotNet unions separate patterns:

```powershell
$method = 'Simulate64GuidedFramesInstrumentedBalancedPhases'
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll navigation-guided-frame --filter "*.$method(AgentCount: 100,*" "*.$method(AgentCount: 500,*FlowField*" --launchCount 1 --warmupCount 1 --iterationCount 3 --keepFiles --artifacts artifacts/guided-sizing
```

For a bounded repeated capture, the same paired filters run A*/100, Flow/100 and
Flow/500 in three fresh processes each. Set `$method` to exactly one of the four
identities above and use a distinct artifacts path and console log for each
mode. The selection deliberately excludes the much more expensive A*/500 setup;
do not describe it as the complete four-case matrix.

```powershell
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll navigation-guided-frame --filter "*.$method(AgentCount: 100,*" "*.$method(AgentCount: 500,*FlowField*" --launchCount 3 --warmupCount 1 --iterationCount 3 --keepFiles --artifacts artifacts/guided-capture
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
median/P95/P99/maximum, counts above 2 ms and 31.25 ms, total/maximum
allocation, and collection-overlap counts; each sample uses its own stopwatch
frequency. Independently check expected cases, launches, and block counts
against BenchmarkDotNet JSON, including failed setups: a partial capture is not
a passing matrix. Nested frames are correlated; hundreds of ordinary frames do
not cancel out a repeatedly expensive recheck frame.

For a bounded end-to-end semantic lifecycle smoke, use:

```powershell
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll guided-phase-smoke
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
alone do not establish which assemblies the child executed, especially with the
prebuilt local-stack references. Compare the frozen child manifests before
attributing a result to a single changed library.

Establish a compiled benchmark-host hash with the actual comparison job
settings. A quick preflight with different launch/warmup/iteration counts can
produce a different generated job directory and embedded debug metadata, even
with identical method bodies. Preserve that preflight separately; subsequent
identical-job captures must still match their formal host and runtime-library
hashes exactly.

### Navigator acquisition and recovery episodes

`NavigationRecoveryBenchmarks` keeps the guided benchmark's 100x100 wall-gap,
query/start order, profile, budgets, one search workspace, and balanced
interval-16 LOS phases. It selects only `AStar100`, `Flow100`, and `Flow500`,
crossed with `ColdInitial`, `WarmCompatible`, `SameMapRebake`,
`ProfileReplacement`, and `Restore`. Existing guided methods and records are
unchanged. The frozen 15-case baseline is synchronous. Current candidate runs
also use the now-implemented `ConfigureAcquisitionMode` hook for opt-in
Scheduled acquisition; the frozen baseline binaries and records remain
unchanged.

Each invocation creates a fresh context outside timing, publishes the same map,
and proves both payload caches empty. Thus even the actual invocation following
warmup is genuinely cache cold. This also recreates context-owned workspaces; it
is not the older cold-guide benchmark's retained-workspace protocol. The warm
case explicitly acquires the original ordered queries, releases them, then
checks exact A* keys or compatible Flow origin coverage without rebuilding.
Recovery cases prepare 32 moving guided frames before the trigger. The restore
case populates new shells in the same context, retaining its compatible cache;
JSON serialize/reset/construct/prepare/populate/assert work is separately
reported. Profile replacement and map admission occur outside the frame timer;
the first measured frame includes the next context publication and all affected
controllers.

Every measured frame includes `context.Simulate()`, every
`Navigator.Simulate()`, and every motion commit in original order. Continue
until every eligible original agent has actually produced a nonzero guided
heading and movement, or a validated arrival, with a 10,000-frame censor limit.
First success and first failure are disjoint latched outcomes of each original
request; later live guide/LOS changes cannot revoke success or count that
request as censored. A direct-first heading qualifies only after an untimed
current-foot direct-ray proof; that validation ray is not repeated for an
already completed original request. This fixture authors no actions and rejects
a spurious pending action. A lease alone is not readiness. Fresh graph/cell
versions, occupancy tickets and physical-cell agreement, body/wall bounds,
movement, and complete position/velocity/readiness replay are validated outside
the nested timer. Failure/censor records are exported before failing acceptance.
No cancellations are issued by these five immediate-mode workloads.

The ordinary 15-case arms preconfigure scheduler retention for the population:
100/500 requests and `max(65536, agents * 512)` descriptor bytes (65536/256000),
compared with the runtime default of eight requests. Existing default counted
slices are unchanged: 8192 lookups, 1 endpoint candidate, 256 nodes, 256 edges,
64 connection legs. The record contains concrete scheduler settings, query
limits and query budgets. No changed candidate slice configuration can be
silently treated as this baseline.

The `NAVIGATION_RECOVERY_SAMPLES` JSON records contain one whole episode: raw
frame ticks, allocations, process-wide GC overlap masks, replay hashes,
readiness frame per original agent (one-based; -1 if never ready),
failures/cancellations/ censored remainder, and process CPU delta. Process CPU
is process-wide, includes untimed per-frame validation, excludes
preparation/export, and may be zero for short episodes due to OS accounting
granularity. It is not summed frame CPU. The whole-operation BDN timing also
includes validation; nested ticks are the full-host frame measurement. Export
runs outside both timing scopes.

Internal cumulative counters distinguish actual query-worker build starts
(including failed builds), existing-payload checkout hits, and accepted Flow
prefix promotions. Per-episode snapshot differences exclude preparation and
diagnostic cache inspection. Ordinary query-meter totals are aggregated once on
completion/cancellation, including failed admitted work. They exclude active
unfinalized work, gate-rejected requests, immediate LOS/guide sampling, and
unmetered clearing/copy work; they are not a complete frame-work allowance.
`completedQueries` includes cancelled queries finalized by disposal. In the
frozen synchronous baseline there was no in-flight cohort, and
`coalescedRequests` was null. Current candidate records alias that field to
`sharedFollowerCheckouts`, including followers of an already cached leader; it
is neither the number of cold searches eliminated nor proof of cold-build
sharing by itself. Read it alongside builds, ordinary hits, coverage misses, and
promotions.

Build and run semantic/replay smoke (two episodes per case; not timing
evidence):

```powershell
dotnet build tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net10.0 -p:UseLocalLsfStack=true
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll recovery-smoke Flow100
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll recovery-smoke Flow500
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll recovery-smoke AStar100
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll recovery-readiness-smoke
```

The existing BDN protocol can run all 15 cases, each in three fresh child
launches, one warmup and three actual complete episodes:

```powershell
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll navigation-recovery --filter '*RecoverUntilUsable*' --launchCount 3 --warmupCount 1 --iterationCount 3 --keepFiles --artifacts artifacts/navigator-recovery/bdn
```

**Frozen reruns must not rebuild against live sources.** The normal BDN launcher
generates a build whose local-stack references resolve live sibling `bin` paths.
Preserve the exact executed generated child and its `Execute` command if using
BDN for a frozen rerun. Alternatively, copy the complete Release benchmark
output directory to an immutable evidence directory, retain its SHA256 manifest
and source identities, then run this direct protocol from that copy (no
MSBuild):

```powershell
# Replace with the already frozen complete output directory; do not copy over it later.
$frozenHost = 'artifacts/navigator-recovery/baseline-frozen/Trailblazer.Benchmarks.dll'
1..3 | ForEach-Object {
    dotnet $frozenHost recovery-capture $_
    if ($LASTEXITCODE -ne 0) { throw "Recovery launch $_ failed." }
} 2>&1 | Tee-Object artifacts/navigator-recovery/baseline-capture.log
```

Each direct process runs all 15 cases, with exactly one explicitly tagged warmup
and three actual episodes per case, using the identical scenario/frame loop.
Three processes provide three fresh launches for every case, though unlike BDN
the cases share each launch's process history in the fixed population/episode
order above. Do not mix these two process-isolation protocols in a comparison.
Use separate logs for repeat captures and preserve failures. The terminal marker
asserts all 15 cases / 15 warmup / 45 actual records completed in that launch.

```powershell
& tests/Trailblazer.Benchmarks/AnalyzeRecoveryFrames.ps1 -LogPath artifacts/navigator-recovery/baseline-capture.log -OutputPath artifacts/navigator-recovery/baseline-summary.json
# Direct smoke logs require -Smoke and are never labeled Actual.
& tests/Trailblazer.Benchmarks/AnalyzeRecoveryFrames.ps1 -LogPath artifacts/navigator-recovery/task1/smoke-flow100.log -OutputPath artifacts/navigator-recovery/task1/smoke-summary.json -Smoke
& tests/Trailblazer.Benchmarks/TestRecoveryFrameAnalysis.ps1
```

The analyzer retains raw records and separates Warmup, Actual, Pilot, Jitting,
Diagnostic and Smoke. BDN overhead operations do not run the episode and are
ignored; `WorkloadResult` repeats actuals and is not another observation.
Monitoring uses one invocation per iteration; a nonmatching/multi-operation
record fails pairing. Formal direct logs require launches 1,2,3, all 180
records, and each launch to contain all 15 cases at 1+3 records. An intentional
prefix investigation may use `-ExpectedDirectLaunches 1` or `2`; the summary
labels it `BoundedDiagnostic`, not a formal capture. `recovery-readiness-smoke`
exercises real leased-to-direct, direct-first, post-success invalid-path and
sticky original-failure lifecycles while other original requests are waiting.
Cross-launch replay is checked for each exact mode/algorithm/count/episode.
Check the reported launch list has exactly 1,2,3 and BDN JSON independently when
used. The report exposes per-episode observed median/P95/P99/max, total frame
time, counts above 2/31.25 ms, allocation, GC overlap, CPU and completion delay;
correlated nested frames and tiny episode counts do not justify universal tail
guarantees.

The separate `recovery-diagnostic <launch>` command runs six fixed Flow/100 cold
controls in order: mixed A/B/A/B destinations in Immediate and Scheduled,
uncovered first origin in both modes, then Scheduled retention 8 and 100. It
keeps the original map, profile, budgets, 32 Hz, B limits
`65536,128,4096,8192,64,256`, and the 10,000-frame cap. Each control has one
warmup and three Actual episodes. Diagnostic records carry their own identity,
per-original-agent start/destination mapping and readiness; pressure records
also carry per-frame retained requests, per-frame sticky `CapacityExceeded`
status observations and the number of distinct agents observed with that status.
Status observations are not rejected-attempt counts. The command exports each
failed/censored episode before rejecting it. These candidate-only controls have
no historical baseline and must not be pooled with the 15-case matrix. Two local
launches are semantic/replay checks only; root owns the later uncontended
three-launch, two-capture timing protocol on frozen binaries.

```powershell
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll recovery-diagnostic 1 2>&1 | Tee-Object -FilePath artifacts/navigator-recovery/diagnostic.log
if ($LASTEXITCODE -ne 0) { throw 'Recovery diagnostic failed; inspect the retained log.' }
& tests/Trailblazer.Benchmarks/AnalyzeRecoveryFrames.ps1 -LogPath artifacts/navigator-recovery/diagnostic.log -OutputPath artifacts/navigator-recovery/diagnostic-summary.json -DiagnosticControls -ExpectedDirectLaunches 1
```

For ordinary direct logs, pass `-ExpectedMode` and `-ExpectedWorkLimits` for
non-default arms; the analyzer verifies the exact selected case set and concrete
budget, scheduler and query-limit identity before accepting records. It remains
compatible with the frozen baseline's older settings shape.

### Direct-visibility full-host controls

`NavigationDirectVisibilityFrameBenchmarks` adds five fixed FlowField controls:
`Clear100`, `Short100`, `Late100`, `Ineligible100`, and `Sparse500`. Its two
methods are `Simulate64FramesUnphased` and `Simulate64FramesBalancedPhases`. The
original guided-frame methods, parameters, and payloads are unchanged. These
controls use radius 1/8, `TrekRate.Slow`, 32 preparation plus 64 measured frames
at 32 Hz, and interval 16 (null phase or agent index modulo 16).

Clear/short require actual direct success and no guide leases throughout the
moving lifecycle. Late/ineligible/sparse require one real Flow lease per agent
and no direct path. The ineligible control adds a distant unbound physical grid;
the sparse control physically seeds a narrow oblique band, staging rectangle,
and detour connectors, with a missing physical wall. Setup validates captured
physical pages, one Solid component, materialization, and workspace headroom.
Every invocation resets the same formation; each 32-frame block must move, every
frame checks fresh physical/semantic committed cells, and full replay hashes
must match. Only existing immediate-meter observations run inside the timer; no
second diagnostic ray is issued in a timed frame.

Run semantic validation before accepting or freezing any timing protocol:

```powershell
dotnet build tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net10.0 -p:UseLocalLsfStack=true
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll direct-visibility-smoke
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll direct-visibility-smoke --list
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll navigation-direct-visibility-frame --filter '*Simulate64FramesUnphased*' --list flat
dotnet tests/Trailblazer.Benchmarks/bin/Release/net10.0/Trailblazer.Benchmarks.dll navigation-direct-visibility-frame --filter '*Simulate64FramesBalancedPhases*' --list flat
```

The smoke runs all five cases in both cadences, with full setup preflight and
two reset/replay/cleanup cycles. Its raw timing is diagnostic only, not formal
acceptance evidence. Discovery must select exactly five parameter cases per
cadence. BDN's flat list prints methods only; `direct-visibility-smoke --list`
prints the actual expanded BDN parameter cases without executing workloads. The
automatic catalog alias is `navigation-direct-visibility-frame`. Use the
existing Monitoring job and a separately approved capture protocol; never
silently raise budgets or replace a failed clear scene with blockage. The
control-only edge budget is `max(320000, 32 * width * length)` (1,572,864 for
large scenes); all other budgets and the primary guided fixture are unchanged.
Use this same sizing on every baseline and candidate capture. The 4,798-cell
Sparse500 default-cell map also requires an explicit 8,192 per-map dynamic-slot
capacity (global capacity remains 16,384); the default 4,096 cannot materialize
that physical seed. Other cases retain the existing settings defaults. New
`NAVIGATION_GUIDED_SAMPLES` records include `controlCase`; the existing analyzer
validates its exact algorithm/count mapping and keeps control keys disjoint from
historical records. Analyze formal BDN logs with `AnalyzeGuidedFrames.ps1`, not
the standalone smoke log.

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
pause caused the frame duration. The `NAVIGATION_SCHEDULE_SAMPLES` JSON records
retain every observation, including activation, publication, result transfer,
cancellation-free disposal, GC and outliers. Export also records exact scheduler
settings and stopwatch frequency. Use the surrounding BenchmarkDotNet log to
associate records with warmup, actual and diagnostic invocations before
computing actual-only statistics.

The host runs 100 input-driven Navigators, reversing input at deterministic
position bounds to stay on the map during long acquisitions. Movement is
verified over each 32-frame block. Each measured frame includes context
maintenance/scheduling, all Navigator simulations/commits, request polling,
typed lease transfer and disposal. Host input assignment and per-frame behavior
validation are outside the nested frame timer, but remain part of the
whole-operation measurement. Validation checks fresh committed-cell frames and
continued movement, exact request costs, A* step counts, complete Flow payloads,
and drained scheduler/payload ownership.

Map preparation, cache eviction, agent reset and 32 initial moving frames are
untimed. The fixed sample buffers retain approximately 32 MB per benchmark
instance. Untimed JSON export makes additional copies; retain this protocol when
comparing builds rather than selectively warming away its GC effects.

Begin with a separately labeled out-of-process dry/sizing capture, then freeze
the selected cases and slice settings before repeated capture. The intended cold
protocol is three launches, one warmup and three actual whole acquisitions per
launch; pass `--launchCount 3 --warmupCount 1 --iterationCount 3` explicitly to
override the shared benchmark job's longer defaults. Report completion frame
counts (and simulated waiting time at 32 Hz), per-acquisition and per-phase
maxima, observed frame P99, and counts above 2 ms/31.25 ms. Nested frames are
correlated samples: thousands of cheap search frames must not hide an expensive
activation or completion in a pooled percentile. The
[scheduling execution record](../../docs/feature-work/done/navigationSchedulingPlan.md)
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
