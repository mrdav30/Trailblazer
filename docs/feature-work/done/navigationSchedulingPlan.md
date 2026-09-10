# Deterministic Guide Acquisition Scheduling

## Status and purpose

Completed, independently reviewed locally and archived on 2026-09-09. Approved for
implementation on 2026-09-08 as the remaining scope of
[`TRB-Benchmark-001`](../benchmark-signal-hardening-backlog.md). The host explicitly
authorized context-owned frame-spread acquisition for both A* and Flow after
the focused geometry and dependency-validation optimizations were measured.

The final-build capture brings the 100K four-warm-request containing frame to an
observed P99 of 1.940 ms on the recorded headless host. Cold public acquisition
still has medians of 87.841 ms to 3.226 seconds. Scheduling addresses when this
work runs, not its total cost. Existing synchronous acquisition remains useful
outside a fixed frame and is not relabeled as frame-safe.

This plan also condenses the former `navigationAcquisitionInvestigation.md`.
The original Phase 3 measurement protocol and baseline are retained in the
[hardening plan](navigationHardeningPlan.md#phase-3---measurement-protocol-and-baseline).
Windows/Linux CI and final released-package validation remain unchecked
[coordinated release gates](../feature-work-overview.md#coordinated-release-gates),
not implied by this local feature closeout.

## Design and global constraints

- One context-owned bounded FIFO, with one active acquisition globally across
  A* and Flow. Waiting entries retain immutable query intent, not graph leases.
- Advance at most one acquisition phase per `context.Simulate()`, after graph
  maintenance and before ordered host hooks. Work queued by a hook starts no
  earlier than the following frame. No wall-clock cutoffs, background tasks,
  callbacks, automatic retries, or nondeterministic completion ordering.
- Reuse the existing single-item admission-gate continuation, query meters,
  payload publication and public guide binding. Do not duplicate search.
- Per-frame continuation limits are distinct from a query's total work budget.
  Positive immutable scheduler settings bound retained request count,
  descriptor bytes, lookup steps, endpoint candidates, nodes, edges and
  connection legs. Defaults are selected by sizing and repeated measurement.
- The initial budget contract is counted continuation slices, not a universal
  elapsed-time deadline. Atomic ray, workspace reset, dependency validation,
  array allocation and publication overhead must be included in frame evidence;
  improve measured offenders before claiming the selected workload fits.
- Add `context.Guides.Schedule(query, out request)` with explicit acceptance,
  pending, cancellation and terminal outcomes. A request owns any unclaimed
  result, transfers the appropriate existing lease exactly once and is
  disposable. Unique request-object identity avoids recycled-slot aliasing;
  requests are not pooled. Small per-request allocation is intentional.
- Queued, active and completed-but-unclaimed requests all occupy the finite
  retention ceiling. Taking or disposing a request releases its retained slot.
  A cancelled request must never affect a later request.
- Keep exact graph/dependency/raw-world checks and existing terminal failures.
  Stale work fails explicitly; it never silently restarts. Holding one gate can
  make synchronous acquisition of that algorithm return `CapacityExceeded`.
- Reset/dispose cancels all scheduled ownership before pathing teardown. Pending
  requests and results are runtime-only and never serialized. Hosts record and
  replay query intent in deterministic submission order.
- Engine-agnostic fixed-point behavior, develop in-place, no staging/commits or
  upstream writes. One non-private type per new file. No hollow tests, timing
  assertions in unit tests, compatibility forwarding APIs or new search engine.

## Implementation tasks

### Task 1: Scheduling lifecycle and public acquisition

- [x] Add immutable `NavigationSchedulingSettings`, optional context settings
  integration, a disposable `NavigationGuideRequest`, and the internal bounded
  FIFO implementation under `Pathing/Search/Guide`.
- [x] Extend public guide outcomes with `Pending` and `Cancelled`, preserving
  existing numeric values. Add explicit typed, exactly-once lease transfer.
- [x] Integrate one activation/admission/search/publication phase per fixed
  frame, cancellation, capacity, reset and disposal. Reuse existing gate work.
- [x] Regression-first tests for both algorithms, FIFO, small slices, capacity
  and descriptor bytes, wrong/second transfer, queued/active/completed disposal,
  reset, stale publication/raw-world changes, synchronous contention, host-hook
  ordering and identical replay completion frames/results.
- [x] Independent task review (specification and code quality).

### Task 2: Full-lifecycle frame evidence and measured atomic work

- [x] Add repeatable 10K/100K A*/Flow single and four-request scheduled scenarios
  alongside the existing 100-Navigator headless fixed-frame workload.
- [x] Record every activation, search, finalization and completion frame; include
  request submission, result transfer and cleanup in the measured lifecycle.
  Retain raw samples, semantic checks, total frames/latency, allocations and
  binary/source provenance. Do not hide worst completion frames in averages.
- [x] Use the smallest measured improvements to atomic work where required,
  with regression tests before behavioral changes. Record correctness issues
  separately in the evergreen issue tracker.
- [x] Compare observed P99 and maximum containing-frame costs against the
  provisional 2 ms navigation allowance and the full 31.25 ms frame. State the
  remaining measured limits without inventing a cross-machine guarantee.

### Task 3: Documentation, exact coverage and closure

- [x] Update runnable public integration examples, XML, lifecycle/serialization
  guidance and the benchmark guide. Explain FIFO latency, finite capacity,
  synchronous contention and host cancellation/ownership.
- [x] Run focused Debug checks, Release/ReleaseLean full solution builds/tests,
  exact reachable line/branch/method coverage in both configurations, DocFX
  warnings-as-errors and whitespace/link checks.
- [x] Independent final code and measurement reviews. Close Benchmark 001 only
  with an evidence-backed disposition of both warm and cold acquisition modes;
  preserve synchronous cold limitations rather than declaring them eliminated.

## Execution record

The CPU-cost investigation and focused changes are consolidated below, followed
by the completed scheduler implementation and measurements. Sizing is separate
from repeated acceptance capture.

### CPU cost improvements

Three separate changes improved the measured paths:

1. Release builds stopped computing source/target segment geometry and native
   template-to-retained-portal translation used only by Debug assertions. The
   assertions themselves were already omitted; their precomputed inputs were
   not. Required runtime rejection checks remain active.
2. Native rectangular edges reuse a proven portal template when the actual
   fixed-point center displacement exactly matches the template. Hex edges,
   seams and nonmatching geometry retain direct geometry evaluation. This is
   reuse of equivalent geometry, not removal of body-clearance checks.
3. One warm acquisition reuses exact-root dependency proof within that request,
   reducing full stamp scans from six to three. Publication/race checks remain;
   there is no cross-frame stale-proof cache.

Selected synchronous cold medians, in milliseconds:

| Workload | Original | Debug-only guards | Focused portal reuse | Original / focused |
| --- | ---: | ---: | ---: | ---: |
| Flow, 10K cells | 5,513.532 | 1,863.828 | 293.612 | 18.78x |
| A*, 10K cells | 2,138.129 | 716.940 | 87.841 | 24.34x |
| Flow, 100K cells | 55,415.369 | 18,924.456 | 3,225.761 | 17.18x |
| A*, 100K cells | 21,920.730 | 7,343.433 | 904.471 | 24.24x |

Debug guards remove about 66% of elapsed time; portal reuse removes another
83-88% of the remaining time in these rectangular workloads. The focused
min/median/max values are 288.409/293.612/306.684 ms (Flow 10K),
81.582/87.841/97.899 ms (A* 10K), 3175.062/3225.761/3284.959 ms (Flow 100K)
and 900.669/904.471/937.787 ms (A* 100K). Each row has nine actual operations
across three launches, not enough for a reliable cold P99. Allocation remains
779,656 / 35,320 / 7,698,392 / 164,456 bytes respectively.

These are sequential historical checkpoints, not randomized interleaved A/B
trials. The focused cold capture precedes scheduler integration; the final
scheduler-enabled build has its own scheduled and warm captures below.
Original complete measured-child binary identity cannot be reconstructed.
Flow integrates all 10K/100K cells; A* returns one route on the same map.
Map size is not a measured A* expansion count. Four cold requests deliberately
use distinct keys; four agents sharing one destination/profile can reuse a field.

The 100K standalone warm Flow median drops from 473.15 to 268.55 microseconds.
The original four-warm-request containing frame has median/P99 2.587/2.810 ms,
versus 1.730/1.870 ms after focused changes and 1.677/1.940 ms on the final build.
The final build's one excursion above 2 ms is retained below. These whole-frame
figures include 100 input-driven Navigators, not a physics/gameplay integration.

### Source and diagnostic boundary

The original sampled cold traces place about 92.43% (Flow) / 90.91% (A*) of
inclusive samples under `AdvanceSimple`; portal/body subtrees overlap and must
not be added. Warm 100K traces place about 95-97% under dependency validation:
six passes over 1,563 page stamps (9,378 visits). Sampling, inlining and
safepoints limit attribution. **Those profiles predate the optimizations and do
not establish today's dominant remaining cost.**

`TRB-Issue-116` corrected parent/child attribution: the benchmark parent and
untimed reflection probe used packaged FixedMathSharp 7.1, while measured
children used the sibling-source build. Normalized inspection found zero
method differences across 3,570 FixedMathSharp and 1,548 GridForge methods;
this is not whole-assembly identity or proof of every historical dependency.
Final scheduled/warm actual-child identity is independently checked below.

### Issue 115 - diagnostic work removal

`TRB-Issue-115` guards the assertion-only source/target segment calculations,
native-template-to-retained-portal translation and their assertions with
`#if DEBUG`. Required corridor intersection/rejection remains in Release.
Focused Debug, Release/Lean behavior and full coverage
checks passed at that checkpoint; no reachable production safety check was
removed to improve a timing or coverage number.

### Native portal and warm validation follow-up

Exact portal/profile equality, nonrectangular fallbacks, cache-hit behavior,
publication races and retained-byte accounting were regression-tested.
The investigation exposed `TRB-Issue-117`: extreme fixed-point bounds
[-1,073,741,824, +1,073,741,824] with cell width 1,073,741,824 produce a final
center spacing one raw unit short after intermediate saturation. Those prisms
overlap and have no valid portal. Release had ignored the portal failure;
both directions now correctly return `Impassable`. Runtime correctness wins
over retaining a previously accepted but invalid route.

### Issue 114 - separate allocation guard investigation

The 616-byte warmed Flow attach failure was mitigated by aligning the test's
256-cycle warmup with its measurement window. Three covered Release repetitions
and the Lean matrix passed at that checkpoint, but the allocator remains
unattributed. `TRB-Issue-114` stays a mitigated watch item in the
[issue tracker](../issue-tracker.md); this is not a production allocation fix.

### Practical interpretation and remaining measurement priorities

The 17-24x cold median improvement is substantial relative to this library's
baseline. Warm reuse meets the chosen observed-P99 allowance on this host.
It does **not** establish competitive performance against another library:
there is no matched competitor benchmark, and this headless workload omits
collision solving, gameplay, networking and rendering.

Cold latency remains unsuitable for responsive on-demand navigation: the
focused 10K A* route still takes about 88 ms and a 100K Flow about 3.23 seconds.
Scheduling limits how much runs in each frame; it does not remove that CPU
work or make seconds/minutes of FIFO waiting acceptable for every game.
Large shared fields are legitimate workloads, not automatically rare edge cases.

Useful next measurements, rather than approved implementation tasks, are:

- Profile the optimized cold path again; reduce the remaining measured work or
  per-edge cost before choosing another runtime optimization.
- Rerun small/nearby queries and measure representative authored maps, key reuse,
  goal changes, invalidation and guided-agent loads. Existing 100/1K and agent
  benchmark cases provide starting points, not current performance results.
- Include the actual Gravitas host when judging end-to-end capacity.
- Investigate the baseline 11,200 bytes/frame from the 100-Navigator contact
  reporting workload if GC steadiness is a priority; it exists without queries.
- Revisit remaining warm proof scans only if representative warm workloads
  consume their budget. Preserve exact dependency validation.

### Retained investigation evidence

Detailed raw logs, source fingerprints, profiles and independent reviews remain
in ignored local artifacts; these paths are not public downloads:

| Archive | SHA-256 |
| --- | --- |
| `artifacts/benchmark001/acquisition-investigation-evidence.zip` | `E3CE463E45F8DEB3DFD873E3301A574DAC11C50B89596CD6D321C88D5AE1E8F0` |
| `artifacts/issue115-checkpoint.zip` | `CC901E084506A33B82E5BA0495651067820B83282BC1855246D9CC41E337FDC8` |

Focused cold/warm results, actual-child binaries, source hashes and independent
measurement comparison are under `artifacts/benchmark001/focused-optimizations/`.
Use the [benchmark guide](../../../tests/Trailblazer.Benchmarks/README.md) for
current case selection, cold/warm protocols and coordinated-source execution.
The final scheduler checkpoint archive is identified at this plan's end.

### Scheduling lifecycle implementation

The public API, FIFO, single active gate and reset/disposal integration are
implemented. The initial focused Release capture passes 46 meaningful scheduler
tests and covers every reachable line, branch and method in the new executable
request/settings/scheduler types. Independent task review approved correctness
and ownership. Its two minor follow-ups compare each replay completion frame
independently and expand the public fixed-frame XML description; these are
included in the final verification scope.

The raw-world contract is intentionally conservative: any GridWorld sequence
change between activation and transfer makes the scheduled request stale,
including unrelated changes. Existing guide semantics after transfer are not
changed. Direct public `Pathing.Reset()` cancels scheduled ownership before
invalidating its held gate. Context disposal closes scheduling under its lock,
including a submission already waiting on that lock.

### Measurement preflight corrections

Initial sizing caught two benchmark-only problems before accepted measurement:
the generated prebuilt child cannot reference a runtime enum used as a public
benchmark parameter, and fixed-cadence input reversal accumulated drift until a
Navigator left the map. Parameters now use primitive strings; the deterministic
headless host reverses at position bounds. Every frame verifies fresh committed
cell state and each 32-frame block verifies actual movement. Cleanup disposes
pending requests before asserting drained payload ownership, even after a failed
preflight. Failed captures remain under `artifacts/benchmark001/scheduling`;
they are not timing evidence.

### Bounded Flow cleanup and slice sizing

The first complete sizing capture exposed reused Flow workspace clearing during
search construction. Old records are now cleared incrementally from the active
slot tail, spending the supplied node slice before seeding the next search. No
new fields, buffers or allocations were needed; cleanup is not charged to the
query's total expansion budget. A zero-node slice does no cleanup. Cancellation
preserves the remaining prefix for the next acquisition or explicit reset.
Fresh-workspace construction retains its previous behavior. Regression-first
tests cover both continuation and cancellation/reset, exact reference clearing,
unchanged cost/expansion counts and zero cleanup allocation. Independent review
approved the runtime and tests.

Sizing used separate one-launch captures, not substitutes for repeated evidence.
The final selected defaults are 8 retained requests, 65,536 logical descriptor
bytes, 8,192 lookup steps, 1 endpoint candidate, 256 nodes, 256 edges and 64
connection legs per applicable slice. Increasing lookup allowance removed the
extreme admission delay; limiting edges to 256 reduced ordinary search-frame
cost. These are measured starting points, not universal optimal settings.

All 12 actual one-request operations in `sizing-tuned-2.log` had observed frame
P99 below 2 ms. Five individual frames exceeded 2 ms, including an A* 100K
publication frame at 2.3398 ms. Only two of those five coincided with a
process-wide collection-counter change in the bracketing window; GC causation
is not established. The final Flow 100K sizing acquisition took 11,638 frames,
or 363.6875 simulated seconds at 32 Hz. The older smaller-slice configuration
took 51,850 frames. These latency observations are retained, not hidden by the
lower per-frame cost.

### Repeated capture protocol

After the full verification matrix, freeze runtime, tests, benchmark source and
slice settings. Build the coordinated solution once, then run without competing
builds/tests or log analysis:

```powershell
dotnet build Trailblazer.slnx -c Release -p:UseLocalLsfStack=true -m:1
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll navigation-scheduled-frame --filter '*RequestCount: 1*' '*RequestCount: 4*' '*SettledNodeCount: 100000, Algorithm: *AStar*, RequestCount: 0*' --launchCount 3 --warmupCount 1 --iterationCount 3 --keepFiles --artifacts artifacts/benchmark001/scheduling/canonical
```

Redirect the complete console output to `canonical.log` for nested frame data.
The explicit flags override the shared benchmark job's longer defaults. Nine
selected cases comprise 10K/100K A*/Flow with one/four distinct cold queries,
plus the 100K zero-request control. Each case has three independent processes,
one warmup and three actual whole operations per process; the allocation
diagnostic invocation remains separately labeled. There should be 27 successful
children, 81 actual operations, 27 warmups and 27 diagnostics.

Every frame retains ticks, allocations, scheduler phase, active request,
completion mask and collection-count-change mask. The benchmark includes 100
continuously moving input-driven Navigators. It verifies exact route cost,
steps or completed field payload, continued movement and drained ownership.
It does not represent cold Navigator-owned guided starts. Whole-operation wall
time includes intervening host simulation and is not isolated search cost.

Analyze with `tests/Trailblazer.Benchmarks/AnalyzeScheduledFrames.ps1`; independently
cross-check nested-record associations against BenchmarkDotNet's actual/result
measurements. Report all per-operation P99 values, phase maxima, threshold
excursions and deterministic completion frames. Frames inside one operation are
correlated; nine whole operations do not establish a reliable tail-latency SLA.

### Verification and provenance

The final runtime passes coordinated local-source builds for both library TFMs
in Release and ReleaseLean with zero warnings/errors. Covered Release passes
2,455 core and 65 adapter tests; ReleaseLean passes 2,393 core and 65 adapter
tests. Both reports contain exactly **30,841/30,841 lines, 12,087/12,087 branches
and 3,009/3,009 fully covered methods**. Focused Debug passes 223 tests. DocFX
warnings-as-errors passes and emits the new request/settings API pages. These
are Windows/local-source results, not CI or released-package claims.

The current Getting Started program and scheduling helper were extracted from
the documentation, compiled and executed against the preserved measured runtime;
the route has five steps and exact cost 2. An initial scratch ProjectReference
build used an uncoordinated graph and failed with an upstream output-file lock.
No upstream source was changed. The smoke now references the preserved child
assemblies explicitly; a successful serial coordinated solution build restored
all mutable outputs before canonical capture. Expected core assembly versions
are Trailblazer 2.0, FixedMathSharp 7.1, GridForge 9.1, SwiftCollections 7.0 and
its FixedMathSharp adapter 7.1.

Local evidence lives under `artifacts/benchmark001/scheduling`: `verify.ps1`,
`final-coverage`, `final-reports`, `final-*.log`, `docs-smoke`, retained sizing
logs/summaries/binaries, `capture-canonical.ps1` and the canonical identity
manifests/log/results. Full raw logs and actual-child assemblies are evidence
artifacts, not files to commit. Independent final runtime review approved the
cross-boundary changes without actionable findings. Independent measurement
review confirms all 135 scheduled record associations, 27 successful children,
81 actual operations and matching actual/result/statistical inputs. The final
warm capture retains nine successful children and all 900 actual observations.

### Repeated scheduled results - 2026-09-09

The capture uses Windows 11, an Intel i7-9700K, .NET 8.0.29, SDK 10.0.302 and
BenchmarkDotNet 0.15.8. Runtime/test/benchmark source hashes and parent runtime
DLL hashes match before and after capture. The actual scheduled child is
preserved in `canonical-binaries`; no historical assembly identity is inferred
from this new capture. All seven runtime DLLs are byte-identical between the
preserved scheduled and final-warm children. The benchmark parent still carries
the documented package-built FixedMathSharp binary while measured children use
the sibling-source build; matching version numbers or parent hashes alone do
not establish child identity. The independent review checks actual-child
manifests and confirms stable repository identities and all 23 selected source
hashes. Its full report is retained as `independent-final-measurement-review.md`.

All nine cases completed with nine actual whole operations each. The table
shows the range of per-operation observed frame P99 values, not a pooled
percentile that hides expensive lifecycle phases. Times are milliseconds:

| Nodes | Algorithm | Requests | Observed P99 range | Worst frame | Frames above 2 ms |
| ---: | --- | ---: | ---: | ---: | ---: |
| 10K | A* | 1 | 1.294-1.933 | 2.809 | 3 |
| 10K | A* | 4 | 1.276-1.485 | 1.735 | 0 |
| 10K | Flow | 1 | 1.087-1.226 | 2.535 | 3 |
| 10K | Flow | 4 | 1.048-1.144 | 1.622 | 0 |
| 100K | A* | 1 | 1.372-1.588 | 3.924 | 7 |
| 100K | A* | 4 | 1.335-1.432 | 3.552 | 12 |
| 100K | Flow | 1 | 1.074-1.185 | 3.453 | 3 |
| 100K | Flow | 4 | 1.106-1.225 | 2.896 | 6 |
| 100K | No-request control | 0 | 0.773-0.848 | 0.975 | 0 |

All 81 actual operations have observed frame P99 below the provisional 2 ms
allowance. The cold cases retain 653,175 actual frame observations, including
34 above 2 ms; the control adds 2,304 observations. None exceed 31.25 ms.
These are correlated frames from three independent processes per case, not
653,175 independent experiments or a cross-machine timing guarantee.

Activation and admission maxima are below 1 ms in this capture. The largest
publication/transfer frame is **2.6692 ms** for A* 100K, so atomic completion is
not universally below the allowance. Flow publication peaks at 1.2853 ms.
The largest overall frame, 3.9235 ms, occurs in A* 100K search. The complete
per-operation phase summaries, maxima and collection-count-change observations
remain in `canonical-summary.json`; no slow frame is removed or assumed to be
caused by GC. Nine of the 34 excursions overlap a collection-counter change;
25 do not. Separately retained warmup and allocation-diagnostic maxima are
5.1839 ms and 2.0935 ms. The actual-operation P99 finding is therefore not a
first-use/JIT guarantee; warmup is excluded only by the declared protocol.

Completion frames are identical across every recorded invocation, including
warmup and allocation diagnostics. Each request in a burst takes the same
number of frames, so the fourth completes at four times the first:

| Nodes | Algorithm | Single request frames | Fourth request frames | First / fourth ready at 32 Hz |
| ---: | --- | ---: | ---: | ---: |
| 10K | A* | 162 | 648 | 5.063 / 20.250 seconds |
| 10K | Flow | 1,141 | 4,564 | 35.656 / 142.625 seconds |
| 100K | A* | 1,574 | 6,296 | 49.188 / 196.750 seconds |
| 100K | Flow | 11,638 | 46,552 | 363.688 / 1,454.750 seconds |

This is a substantial latency tradeoff: the 100K Flow burst's last result takes
about **24.25 simulated minutes** with these conservative slices. Its median
accelerated headless whole-operation wall time is about 42.52 seconds, which
includes all intervening host frames and must not be confused with readiness
latency at 32 Hz. Schedule well ahead, prepare fields during loading and reuse
shared destinations; scheduling is not a way to produce a huge cold field
immediately. Hosts can tune counted slices, but must remeasure their actual
maps, bodies and containing frames.

Allocation accounting remains explicit. The moving host allocates 11,200 bytes
per frame, unchanged in the zero-request control. After accounting for those
exact frame counts, the acquisition totals match the preceding focused cold
allocation plus **352 bytes per scheduled request**. This is the bounded,
unique request handle, not a new per-frame allocation. Whole-acquisition
allocation totals include every intervening host frame; they are not isolated
search allocation or a timing subtraction.

### Final-build warm control

After scheduled measurement, the unchanged `navigation-cold-frame` fixture was
remeasured on the final scheduler-enabled build. It keeps the original
three-launch, ten-warmup, 100-actual-iteration protocol and the 100K baseline,
one-warm-request and four-warm-request cases:

```powershell
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll navigation-cold-frame --filter '*SimulateFixedFrame*100000*Baseline*' '*SimulateFixedFrame*100000*Warm1*' '*SimulateFixedFrame*100000*Warm4*' --keepFiles --artifacts artifacts/benchmark001/scheduling/final-warm
```

| Workload | Median ms | Observed P99 ms | Maximum ms |
| --- | ---: | ---: | ---: |
| No acquisition | 0.7831 | 0.895162 | 0.9218 |
| One warm request | 1.0341 | 1.209768 | 1.3410 |
| Four warm requests | 1.6767 | 1.939917 | 2.1293 |

All 900 actual measurements remain in the statistical inputs. The four-request
P99 remains below 2 ms, but one of its 300 frames exceeds 2 ms; no all-frame
guarantee is claimed. Allocation remains exactly 11,200 bytes per frame. The
three minimum-iteration warnings and all identified outliers remain recorded.
This reset/prepared-frame fixture is comparable with the prior focused warm
capture, not interchangeable with the continuous scheduled-host control.

## Final disposition

`TRB-Benchmark-001` is closed locally on the measured execution-mode decision:

- Warm reuse meets the provisional observed-P99 allowance on the final build.
- Synchronous 10K/100K cold acquisition remains rejected for the measured fixed
  frame; use it only where the host accepts the full blocking duration.
- Explicit scheduled acquisition meets the selected observed-P99 allowance,
  preserves deterministic work/ownership and avoids single-frame cold draining.
  Rare excursions and long FIFO readiness latency remain documented limits.
- Do not add speculative caching, background execution or a second search
  implementation to conceal those tradeoffs. Different host workloads require
  fresh measurement; this result does not automatically queue Navigator-owned
  acquisition, guarantee atomic phases under 2 ms or establish an engine SLA.

The final documentation checks validate 154 local Markdown targets and 2,006
local site targets across 134 HTML pages. Wiki helper tests pass 4/4, and the
published-copy rewrite and final DocFX warnings-as-errors checks pass. The
frame parser also passes valid/legacy-input checks and rejects truncated,
out-of-range and fractional collection masks.

The compact final evidence archive is
`artifacts/benchmark001-scheduling-checkpoint.zip` (SHA-256
`9BDC606D55E7825E93D76E7BC5960131A1656173C2A146A5659BB88D3E70F3AF`).
It preserves raw final captures, measured-child manifests/binaries, independent
reviews and the coverage/doc verification evidence. Earlier sizing/profiling
artifacts remain in their original local folders.
