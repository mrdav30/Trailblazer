# Cold Guide Acquisition Measurements

## Acceptance criteria recorded before measurement

This is the Phase 3 execution evidence for
[Navigation Hardening](navigationHardeningPlan.md#phase-3---cold-public-guide-acquisition-measurement).
It measures the current synchronous boundary; it does not introduce a scheduler.

The provisional host is a deterministic 32 Hz headless simulation (31.25 ms per
frame), with **2 ms allotted to navigation at P99**. Existing-agent frames contain
100 input-driven Navigators and cold request bursts of one or four requests.
These are explicit measurement assumptions, not a user-supplied game's SLA.
Wall-clock measurements never control simulation state or request ordering.

The measurement host is an Intel Core i7-9700K (8 cores / 8 logical processors),
32 GiB installed memory (34,290,831,360 bytes reported usable), Windows 11 Home
build 26200, .NET SDK 10.0.302 and .NET 8.0.29 x64. Measurements use `Release`,
BenchmarkDotNet 0.15.8, concurrent workstation GC, the High performance power
plan and one invocation per iteration. The normal canonical
job uses three process launches, ten warmups and 100 measured iterations per
launch. Smoke jobs do not contribute to the acquisition decision.

### Slow-workload protocol amendment

The initial in-process smoke observed approximately 5.47 seconds for a single
10K-node cold request and 21.53 seconds for a four-request frame. The broad smoke
was stopped deliberately after those rows, rather than spending hours repeating
preflight. These values are **sizing observations only**, not accepted results.

Before collecting decision evidence, independent measurement review accepted a
separate **SlowAcquisition** protocol for cold rows: three independent process
launches, one warmup and three measured invocations per launch, with the same
Monitoring strategy, cold-cache checks and untimed preparation. All nine raw
samples and launch IDs must be retained. Report minimum, median and maximum;
nine samples do not establish a reliable population P99. Do not compare their
quantile precision with the normal 300-sample controls.

This deviation tests the narrower rejection criterion for the large workloads:
if every observed cold operation already exceeds even the entire 31.25 ms frame, it is unsuitable for
the provisional 2 ms navigation allowance regardless of an unknown upper tail.
Warm and no-acquisition frame controls retain the normal canonical job.

Representative geometry includes wide rectangular Solid planes at exactly 10K
and 100K settled nodes, a small plane control, and an authored ladder transition
control. The containing frame includes context publication maintenance and
Navigator motor, turning, occupancy and motion commit. It excludes Gravitas
collision solving, rendering, networking and game logic: this is a lower-bound
headless host workload, not a promise of engine-host performance.

The four-request cold burst deliberately represents four independent cache
misses, not four agents asking for the same reusable field. Its queries differ
only in exact arrival-tolerance profile bits so geometry and work stay matched;
every resulting field is checked for the full settled-node count. A same-key
burst can reuse the first result and must not be assigned the four-cold cost.

## Source identity

Measurements target the committed local source stack plus the benchmark additions
in this working tree. Production runtime source is unchanged by this phase; its
other edits are documentation and the test-only warmup mitigation below. No
released package is claimed to contain the unreleased changes.

| Repository | Commit | Local assembly/package identity |
| --- | --- | --- |
| Trailblazer | `040c886972f011d15eb9b3189314224817e331d5` | 2.0.0 |
| FixedMathSharp | `41eba3cc09e533e505faa66bcd3c7c68606f7258` | 7.1.0 |
| SwiftCollections | `df71a0fad083f42a8dde528f8a0634ab8d5a5a54` | 7.0.0 core / 7.1.0 FixedMathSharp |
| GridForge | `b693e383c1a04e07fd8f4048a6c863b3f7af515d` | 9.1.0 |
| Gravitas (test matrix only) | `42af506fec0d1aabe5ff9750e0a2d148fe82a5b0` | 1.1.0 |

Chronicler.Core 0.4.0 and MemoryPack 1.21.4 remain package dependencies.
Generated BenchmarkDotNet projects consume the prebuilt local `Release` stack;
do not build another configuration over its binaries during measurement.

The actual benchmark binaries report assembly versions 2.0.0.0 (Trailblazer),
9.1.0.0 (GridForge), 7.0.0.0 (SwiftCollections) and 7.1.0.0 (FixedMathSharp and
SwiftCollections.FixedMathSharp). Project entries in the parent `.deps.json` can carry a placeholder
package version; the measured child consumes the verified prebuilt assemblies.

The four new benchmark source files were frozen for repeated capture with these
SHA-256 identities (paths relative to `tests/Trailblazer.Benchmarks/`):

| File | SHA-256 |
| --- | --- |
| `Pathing/NavigationColdGuideBenchmarks.cs` | `C28FAFC64B3F6333DF5CE20A290487E892B909B99DA4A1D56CCE7413FDCE58DE` |
| `Pathing/NavigationColdFrameBenchmarks.cs` | `69FEEB753611483CCE929F7B149492C7BF1B4DA160B9012C5A52E8CD6635F32E` |
| `Support/ColdGuideBenchmarkScenario.cs` | `518EBF36C76BD6FD052018CC387A9E5551FEEB712F4905E1FF04F62A0A250F8F` |
| `Support/BenchmarkNavigator.cs` | `4312A89220CAEC05C9D05ADCB0FA9575FDDABC8B094E53BB3308D4537920A6D7` |

## Workload and capacity contract

Planes are 10×10, 100×100 and 400×250 unit-cell rectangular Solid maps. The
ladder is two 50-cell landing strips, two units apart vertically, joined by one
explicit Climb action with cost one. The destination is chosen so reverse Flow
settles the whole authored workload. Every public result is checked against the
matched internal cost; public Flow payload inspection also checks exact node
and action counts without rebuilding a missing payload.

Profiles use radius 0.25, height one, zero root-to-foot offset, zero step/drop
allowance and Solid-only travel; only the ladder allows Climb. Authoring grants
radius/height clearance four and zero area/enter costs. These are unobstructed
geometry controls, not a model of an arbitrary game map.

For `N` authored nodes, requests allow `512N` lookup probes, `N` expansions,
`32N` evaluated edges and `4N` in each connection/transition/staged-leg/trace/
covered-interval category. Endpoint allowance is two for planes and eight for
the stacked ladder; simplification rays are disabled. Context settings provide
one active query slot, node capacity `N`, guide-point capacity `2N−1`, two
component slots and eight reusable entries per algorithm. Maximum payload-byte
limits come from the runtime's exact retained-byte sizing helpers, not an
unbounded cache setting. All measured requests must terminate with `Success`.

Reported payload bytes are retained immutable payload storage, not whole-process
memory. The matched internal workspace's allocation is reported separately; this
benchmark-only workspace remains present for the reference control. Constructor
phase allocations include world/publication setup and pool creation. They are
not acquisition allocation or a retained-heap estimate. Frame allocation also
includes the headless host's contact reporting (`SetGroundContact` creates a
`GroundCondition` for each Navigator), so it must not all be attributed to queries.

## Execution status

All three captures are complete: 28 rows, 84 successful child process launches
and 3,744 actual samples. The 12 cold-guide and four cold-frame rows each retain
nine samples; the 12 canonical warm/control rows each retain 300. Every measured
iteration executes one operation. Actual samples, exported result samples and
statistical input values agree exactly; none are discarded.

### Cold guide acquisition

Times are **milliseconds**; allocation is managed bytes per measured operation.
Every row contains all nine samples from `SlowAcquisition`. No row failed or
discarded an actual sample. Public cases include acquire and dispose, not sampling.

| Geometry | Method | Minimum ms | Median ms | Maximum ms | Allocated bytes |
| --- | --- | ---: | ---: | ---: | ---: |
| Ladder100 | ColdPublicFlow | 26.687 | 86.272 | 91.629 | 18,800 |
| Ladder100 | ColdPublicAStar | 45.702 | 83.564 | 86.440 | 15,776 |
| Ladder100 | ColdInternalFlow | 44.019 | 86.215 | 92.545 | 18,768 |
| Plane100 | ColdPublicFlow | 69.101 | 71.578 | 82.281 | 18,576 |
| Plane100 | ColdPublicAStar | 26.910 | 28.063 | 28.973 | 6,480 |
| Plane100 | ColdInternalFlow | 69.880 | 75.774 | 78.855 | 18,544 |
| Plane10000 | ColdPublicFlow | 5,418.923 | 5,513.532 | 5,535.810 | 779,656 |
| Plane10000 | ColdPublicAStar | 2,128.327 | 2,138.129 | 2,149.250 | 35,320 |
| Plane10000 | ColdInternalFlow | 5,418.373 | 5,456.844 | 5,498.386 | 779,624 |
| Plane100000 | ColdPublicFlow | 54,962.518 | 55,415.369 | 55,696.227 | 7,698,392 |
| Plane100000 | ColdPublicAStar | 21,890.990 | 21,920.730 | 22,096.479 | 164,456 |
| Plane100000 | ColdInternalFlow | 55,290.124 | 55,473.867 | 55,767.391 | 7,698,360 |

The small cases carry BenchmarkDotNet's minimum-iteration-time warning; the
ladder also has substantial within-launch variation. They establish the geometry
control and observed range, not a precise small-query tail-latency claim. The
exporter's automatic P95/P99 columns are not used as reliable tail evidence for
these nine-sample rows. Outlier hints identify samples; they do not remove them.

Matched internal Flow has the same geometry, final cost and settled-node count,
but excludes public cache publication and lease creation and uses a different
advance slice. Its latency is a construction control, **not** a precise public-
wrapper-overhead subtraction. Similar multi-second times warrant profiling cold
construction before selecting an API or attributing a bottleneck.

### Frames containing cold acquisitions

Every frame advances the context and all 100 Navigators, commits accepted headless
motion and completes the requested independent cache misses. Agent reset and
32 preparatory frames are outside measurement. These single-frame iterations do
not model a continuously running host's heap pressure or changing-world history.
The same nine-sample protocol and **millisecond** units apply.

| Settled nodes per field | Cold requests | Minimum ms | Median ms | Maximum ms | Allocated bytes | Retained Flow payload bytes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 10,000 | 1 | 5,410.267 | 5,463.207 | 5,560.740 | 790,856 | 769,272 |
| 10,000 | 4 | 21,650.534 | 21,725.395 | 21,812.597 | 3,129,824 | 3,077,088 |
| 100,000 | 1 | 54,529.646 | 55,445.706 | 55,583.791 | 7,709,592 | 7,688,008 |
| 100,000 | 4 | 219,800.980 | 221,105.422 | 222,399.686 | 30,804,768 | 30,752,032 |

Every four-request frame contains four full fields, not one field reused
four times. All 12 child processes exit successfully with drained leases and
reservations. Standalone and frame captures are independent runs: subtracting
their cold medians does not isolate the small existing-agent cost. Use the
no-acquisition and warm-frame controls for that comparison.

### Baseline and warm containing frames

These rows retain the canonical three launches and 100 actual single-frame
invocations per launch. Times are **milliseconds**; P95 and P99 describe the
300 observed samples, not a universal host SLA. No actual samples are removed.
All frames include the same 100 input-driven Navigators and untimed preparation
as the cold frames. Warm fields are seeded before measurement and each exact
cache key is validated without rebuilding it.

| Nodes per field | Workload | Median ms | P95 ms | P99 ms | Maximum ms | Allocated bytes |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 10,000 | No acquisition | 0.758 | 0.855 | 0.950 | 0.970 | 11,200 |
| 10,000 | One warm request | 0.812 | 0.884 | 0.957 | 0.998 | 11,200 |
| 10,000 | Four warm requests | 0.875 | 0.934 | 0.968 | 1.126 | 11,200 |
| 100,000 | No acquisition | 0.790 | 0.833 | 0.895 | 0.931 | 11,200 |
| 100,000 | One warm request | 1.262 | 1.355 | 1.439 | 1.488 | 11,200 |
| 100,000 | Four warm requests | 2.587 | 2.716 | 2.810 | 3.122 | 11,200 |

All warm frame rows allocate the same 11,200 bytes as their no-acquisition
control, from headless contact reporting. This does not make the whole frame
allocation-free. Retained Flow payload bytes match the corresponding one/four
cold fields; baseline retains none. All lease and reservation checks drain.

The 100K four-warm-request frame exceeds the provisional 2 ms allowance in every
sample, despite avoiding cold construction. The other five controls remain
below that allowance at observed P99. Cache reuse is valuable, but cache-hit
validation/acquisition also needs profiling before promising burst capacity.

### Standalone warm controls

Times below are **microseconds**, with 300 retained samples per row. The first
four rows acquire and dispose without sampling. The last two are the unchanged
public acquire/sample/dispose controls on different 1,024-cell geometries;
their medians cannot be subtracted from the new rows to isolate sampling cost.

| Geometry | Method | Median us | P95 us | P99 us | Maximum us | Allocated bytes |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Ladder100 | WarmPublicFlow | 52.650 | 74.005 | 81.488 | 227.600 | 0 |
| Plane100 | WarmPublicFlow | 60.300 | 79.530 | 87.907 | 93.900 | 0 |
| Plane10000 | WarmPublicFlow | 74.600 | 106.700 | 112.811 | 121.400 | 0 |
| Plane100000 | WarmPublicFlow | 473.150 | 501.015 | 531.332 | 555.000 | 0 |
| Corridor1024 | WarmFlowAcquireSampleDispose | 97.550 | 132.645 | 144.110 | 167.900 | 0 |
| OpenPlane32 | WarmFlowAcquireSampleDispose | 93.200 | 130.635 | 162.382 | 504.500 | 0 |

The four new `WarmPublicFlow` rows perform an untimed request and exact-payload
validation before each invocation; the first setup seeds the field. Unlike
warm-frame setup, subsequent setup requests also exercise the reusable public
boundary. Their full payloads and drained leases are checked outside timing.

The two existing service controls instead use a global warmup/preflight, checking
allocation and lease state there, then validating request/sample success and
disposing the lease in every measured operation. Their cleanup log prints static
zero lease/reservation fields and the payload size captured during setup
(79,200 bytes each); those printed fields are not fresh teardown measurements.
No stronger final-drain claim is inferred from those older log lines.

Single-invocation warm rows carry the minimum-iteration-time warning. Keeping
all samples exposes observed variation, including the maxima above, but does
not remove timer/noise limitations or establish population tail guarantees.

### Deterministic work and retained payloads

These are the matched internal Flow counters reported by the new benchmark cases,
not measurements of A*'s search counters. All requests end in `Success`;
all cleanup checks find zero active/reserved leases and zero detached bytes.

| Geometry | Settled nodes | Evaluated edges | Lookup probes | Endpoint candidates | Transition candidates / pairs |
| --- | ---: | ---: | ---: | ---: | ---: |
| Ladder100 | 100 | 782 | 2,151 | 3 | 3 / 2 |
| Plane100 | 100 | 2,048 | 2,079 | 2 | 0 / 0 |
| Plane10000 | 10,000 | 236,408 | 476,569 | 2 | 0 / 0 |
| Plane100000 | 100,000 | 2,388,308 | 6,096,442 | 2 | 0 / 0 |

| Geometry | Flow payload bytes | A* payload bytes | A* guide steps | Benchmark-only internal workspace allocation bytes |
| --- | ---: | ---: | ---: | ---: |
| Ladder100 | 8,408 | 12,008 | 199 | 113,304 |
| Plane100 | 8,192 | 2,720 | 37 | 113,304 |
| Plane10000 | 769,272 | 31,560 | 397 | 13,672,200 |
| Plane100000 | 7,688,008 | 160,696 | 1,297 | 110,552,200 |

Ladder Flow contains exactly one semantic action. All guide-only warm allocation
preflights report zero. A retained cache payload is permitted after a request;
the next cold iteration verifies both caches are empty before timing begins.

## Reproduction and retained artifacts

Run the three capture commands in the benchmark guide's
[cold acquisition section](../../tests/Trailblazer.Benchmarks/README.md#cold-public-acquisition-and-containing-frames)
sequentially after its local-source `Release` build. The slow guide selection
contains 12 rows, slow frames four rows, and canonical controls 12 rows including
the existing public acquire/sample/dispose controls. A successful process exit
alone is insufficient: every row must have a nonempty result distribution and
the expected nine or 300 actual samples.

The retained generated evidence is under `artifacts/hardening-phase3/`:

- `slow-guides/`: cold public Flow/A*, matched internal Flow and small geometry
  controls, using the predeclared three-launch slow protocol.
- `slow-frames/`: 100-agent frames with one or four distinct cold acquisitions.
- `canonical-controls/`: warm acquisitions, baseline/warm frames and existing
  acquire/sample/dispose controls under the unchanged canonical protocol.

Each capture retains the BenchmarkDotNet log and `results/*-report-full-compressed.json`,
including raw measurements, launch indices, allocation and host metadata.
Generated reports remain ignored; this authored record carries their interpretation.
Sizing/smoke directories are not part of the decision dataset.

The three accepted capture directories are also retained together in
`artifacts/hardening-phase3/phase3-acquisition-evidence.zip` (SHA-256
`D1ACA63C0FD9D9E357AC422E418EA4C79B534512BAE9AF4150E684ED200F0B06`).
This is a local ignored archive, not a published CI artifact; preserve it before
cleaning generated files. Its five full-compressed JSON reports have these
identities (each filename has the prefix `Trailblazer.Benchmarks.Pathing.` and
suffix `-report-full-compressed.json`):

| Capture / report class | SHA-256 |
| --- | --- |
| `slow-guides / NavigationColdGuideBenchmarks` | `4B63AD930791121D865FC637C41F57D2A7D14097EC0DFEB0EABF6A51ACADB786` |
| `slow-frames / NavigationColdFrameBenchmarks` | `FFCC5908E7BE3BD6A110868E05AA822F5ED15C47B7069FE18EAD30878E12D92D` |
| `canonical-controls / NavigationColdFrameBenchmarks` | `6C944E45F1E85C9F76DB4024391F5EFD4893CC48774264B5CD7FBD6C8783046D` |
| `canonical-controls / NavigationColdGuideBenchmarks` | `D3F90E28F7EC034512341FC203095129494D4BCE83BDF4D8C98CD14D65ABE486` |
| `canonical-controls / NavigationGuideServiceBenchmarks` | `DBA46081CA8F32399B7A0D3CFDFFB68CDF290068638144E3CC3788B86DD750DD` |

## Verification record

Independent review checked benchmark/source boundaries, both protocols, all 28
distributions against raw measurements and CSV quantiles, allocations, retained
payload evidence and the profiling-first decision. Final review corrected the
older service controls' static cleanup reporting distinction above; it is not
used as fresh drain evidence. The four new source hashes still match capture.

Both local-source solution configurations build with zero warnings/errors.
Fresh covered `Release` execution passes 2,395 core and 65 adapter tests;
`ReleaseLean` passes 2,333 core and 65 adapter tests. Both independently generated
reports pass exact equality for 30,628 lines, 11,977 branches, 2,982 methods and
2,982 fully covered methods. Reports are retained under
`artifacts/hardening-phase3/coverage/{Release,ReleaseLean}/final/report/`.

Final coverage was recollected after all benchmark captures, using only each
configuration's `final/core/` and `final/adapter/` results, not earlier attempts.
The initial 616-byte allocation failure
and the test-only steady-state warmup mitigation are recorded as `TRB-Issue-114`.
No runtime allocator fix is claimed. Release DocFX completes with zero warnings
and errors; wiki rewrite tests pass 4/4, the published-copy rewrite succeeds,
and local links resolve across all 132 generated HTML pages. These are local Windows results, not
Windows/Linux CI evidence for the uncommitted diff.

For each configuration, final verification ran these commands in order, replacing
`Release` with `ReleaseLean` for the second matrix:

```powershell
dotnet build Trailblazer.slnx -c Release -p:UseLocalLsfStack=true -v:minimal
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj -c Release --no-build -p:UseLocalLsfStack=true --settings tests/Trailblazer.Tests/coverlet.runsettings --results-directory artifacts/hardening-phase3/coverage/Release/final/core
dotnet test tests/Trailblazer.Gravitas.Tests/Trailblazer.Gravitas.Tests.csproj -c Release --no-build -p:UseLocalLsfStack=true --settings tests/Trailblazer.Gravitas.Tests/coverlet.runsettings --results-directory artifacts/hardening-phase3/coverage/Release/final/adapter
reportgenerator '-reports:artifacts/hardening-phase3/coverage/Release/final/**/coverage.cobertura.xml' '-targetdir:artifacts/hardening-phase3/coverage/Release/final/report' '-reporttypes:Html;MarkdownSummaryGithub;JsonSummary'
```

ReportGenerator 5.5.10 aggregates only the two intended assemblies. Each summary
is checked for exact covered/total equality in lines, branches, methods and
fully covered methods, not just a rounded percentage. Release is rebuilt before
the final DocFX command because the local stack shares build intermediates.

Small ladder smoke checks verify all four methods produce nonempty results,
exactly 100 settled nodes, one action, three endpoint candidates, 782 evaluated
edges, 2,151 lookup probes, zero warm allocation and drained leases. The initial
two-candidate fixture budget correctly failed; its final finite allowance is
eight. The 100K in-process sizing attempt produced a 55.14-second workload sample
but exceeded the runner's overall timeout and is excluded from accepted results.

## Acquisition decision

**The current synchronous boundary is not adequate for the provisional large-
field, in-frame workload. Profile acquisition cost before designing a scheduler.**

Every observed 10K/100K cold public Flow and A* acquisition exceeds even the
entire 31.25 ms frame. Public Flow medians are 5.51 seconds and 55.42 seconds;
the corresponding four-cold-request frame medians are 21.73 and 221.11 seconds.
This rejection does not depend on estimating a tail from nine samples.

Warm reuse changes the scale substantially, but does not establish unlimited
request throughput. The 100K four-warm-request containing frame has an observed
P99 of 2.810 ms and a minimum of 2.535 ms, exceeding the 2 ms navigation allowance.
Its one-request control has P99 1.439 ms. These are headless lower-bound frames;
adding real collision solving and host work cannot be assumed to fit the same
allowance. Neither result is a general performance claim for other hardware,
geometry, profiles or request patterns.

Record this as `TRB-Benchmark-001`, an open performance signal rather than a
confirmed algorithmic defect. The next focused task is to profile admission,
search/evaluation, allocation and publication in these exact cold cases, plus
large warm acquisition/validation. Preserve the current semantic checks and
repeat matched measurements after any approved optimization. Internal/public
construction controls suggest investigation below the public wrapper is useful,
but do not identify the dominant method or justify bypassing validation.

If large uncached requests still need to run during gameplay after that work,
the measurements justify evaluating a context-owned continuation/scheduler.
That is a separate approval boundary, subject to the constraints below. For the
current API, hosts must budget and place synchronous acquisitions deliberately,
for example during deterministic loading/setup, and account for later cache
invalidation. A finite work budget is not a millisecond deadline.

No scheduler, background completion, public API change or production-runtime
optimization is included in Phase 3. This is a measured follow-up decision,
not a claim that today's large in-frame workload is acceptable.

## Constraints for any approved scheduling follow-up

This section records reviewed constraints, not an implementation commitment or
a claim that a scheduler already meets the 2 ms target.

- Own pending requests, cancellation and teardown in `TrailblazerWorldContext`.
  Preserve its fixed-frame clock, graph maintenance and ordered-hook lifecycle.
- Define stable admission order across A* **and** Flow. Each algorithm already
  sorts a batch and publishes its ready prefix, but both compete for shared
  admission capacity; caller or background completion order must not pick winners.
- Keep total query budgets separate from aggregate per-frame slices. Search
  work meters persist across advances. Existing batch admission hands a slice
  to each query, so one admission call's limits are not an aggregate frame cap.
- Define explicit public pending, cancellation and terminal-result ownership.
  Current public statuses are terminal-only, and disposal cancels an internal
  batch rather than an independently owned public request.
- Bind completion to the originating query and still-current caller intent.
  Navigator destination/profile replacement or reset, and owner/context disposal,
  must prevent an older completion from attaching guidance to a changed actor.
  Current steering replacement releases its old guidance before replacing the query;
  graph freshness alone would not preserve that guarantee for a pending result.
- Bound queues, active workspaces, held graph leases, reservations, ready-but-
  unpublished payloads and completion latency. An active batch already occupies
  its algorithm's admission gate; retaining it can reject later immediate calls.
- Preserve exact dependency/world validation at publication, including cached
  results, Flow prefixes and negative proofs. Never publish stale guidance or
  turn repeated invalidation into an unbounded retry loop.
- Define deterministic result-availability and application frames across restore,
  replay and rollback. Current records restore durable intent, not caches,
  leases or partially completed search. A future cache hit or discarded search
  progress must not silently change authoritative completion frames. Verify
  continuous-versus-restored replay with cold/warm caches and in-flight work;
  this does not require serializing runtime cache objects or identify a defect
  in today's synchronous behavior.
- Profile admission, search, result allocation and publication separately before
  promising frame-spread behavior. Flow allocates payload arrays in one step;
  cache publication can validate dependencies and compare payload prefixes under
  one lock. Resumable search alone does not establish a small whole-frame bound.

Scheduling reduces time monopolized in a single frame, not total query work.
If construction remains multi-second, small deterministic slices can produce
unacceptably long waits and retain resources across many world revisions. The
next scope should therefore isolate the cold-work cost before selecting a
scheduler API. No background tasks, runtime changes or new continuation API are
introduced by this measurement phase.
