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
  `TRB-Benchmark-006`.
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

### TRB-Benchmark-005 - Guided frames spend most time in long blocked segment tracing

- **Discovered:** 2026-09-10 during the representative guided-host performance pass.
- **Status:** Active for remaining full-frame budget misses. Exact rectangular
  candidate pruning/slab intersection in GridForge and raw-coordinate
  containment/distance/capsule simplifications in FixedMathSharp are now
  implemented, together with per-sample immutable dependency-proof reuse in
  Trailblazer. Recheck medians fall another 73-78% against the refreshed
  baseline; see the final follow-up. This is source work, not a profiling-only
  result. The earlier per-edge rejection experiment remains withdrawn.
- **Reproduction:** `navigation-guided-frame` follows real A*/Flow guides around
  a wall on a 100x100 grid. Each operation has 32 untimed preparation frames and
  64 measured frames at 32 Hz. Agents start together and retain the default
  16-frame line-of-sight recheck cadence. No physics solver is present.
- **Initial evidence:** Flow100 sizing retained three actual blocks. Frames
  0/16/32/48 take 1,140–1,192 ms; ordinary frames are roughly 4 ms. All nested
  frames allocate zero current-thread bytes, with zero collection-change masks.
  These are complete host-frame observations, not isolated acquisition timings.
- **Attribution:** A separate sampled-thread-time trace of the same Flow100
  fixture, restricted to the measured block, places about 90% inclusively in
  `GridTracer.TraceIntervalsInto`, 87% in `TryGetPlanarSegmentInterval`, and 60%
  in wide convex point containment. These are overlapping sampled shares, not
  additive measurements. The tracer enumerates the segment's bounding-box
  candidates and runs exact prism intersection on each before Trailblazer can
  reject the obstructed route.
- **Implemented slice:** GridForge `GF-Benchmark-004` uses the existing widened
  `Vector2d.OrientationSign` to reject convex footprints whose vertices all lie
  strictly on one side of the ray's supporting line. Zero/mixed signs and
  stationary segments retain the existing interval calculation. Candidate
  enumeration/counts, limits, output order, closed-set tangencies and exact
  fallback arithmetic are unchanged. Empty/default prisms still validate.
- **Evidence:** `artifacts/benchmark004/guided-sizing-2.log` (partial sizing,
  not a complete matrix), `guided-flow100.nettrace`, `guided-flow100-stacks.json`
  and the ignored `probe` harness. A*100 setup threw once in wide math before
  any measured result; a standalone run of the unchanged fixture later passed.
  Keep this separate correctness investigation out of timing comparisons.

#### Matched guided-frame result

The selected capture ran on 2026-09-10, serially on the same Windows/.NET
8.0.29 host as `TRB-Benchmark-004`, with normal runtime settings. Each version
has three cases, three launches per case, one warmup and three actual 64-frame
blocks per launch. A*/500 is deliberately excluded from this repeated capture:
its expensive sizing setup was stopped before completion. This is not a
complete four-case matrix or a cold-acquisition measurement.

```powershell
dotnet tests/Trailblazer.Benchmarks/bin/Release/net8.0/Trailblazer.Benchmarks.dll navigation-guided-frame --filter '*AgentCount: 100,*' '*FlowField*' --launchCount 3 --warmupCount 1 --iterationCount 3 --keepFiles --artifacts artifacts/benchmark004/guided-candidate
```

Use `guided-baseline` for the before capture. Build once per version with the
local-stack command above; do not run builds/tests/profilers during capture.
The baseline has Trailblazer `4a02250` plus the new benchmark-only fixture,
GridForge `7179cce` and SwiftCollections `4b5715b`. The candidate adds only the
GridForge runtime rejection. SHA256 comparison of all 47 frozen top-level
benchmark output files finds only `GridForge.dll` and `GridForge.pdb` changed.

**Provenance qualification from the follow-up:** those 47 files are frozen
parent outputs, not separately frozen before/after generated-child runtimes.
Both logs reuse the same generated-child directory. Its retained candidate
contains a different FixedMathSharp binary from the parent snapshots, so the
parent hashes alone do not establish exact historical child-binary attribution.
The timings remain recorded observations. The controlled comparison below
holds the actual child and all shared dependencies fixed; new captures also
archive the executed child immediately after each run.

Each version retains **27 actual blocks / 1,728 actual frames**, plus nine
warmup and nine diagnostic blocks. Independent analysis verifies every timed
block against full BenchmarkDotNet JSON, every exported array against the
summary, and exact 64-frame replay hashes across both versions, all launches
and all stages. All exported frames have zero measured current-thread bytes
and zero collection-change masks; the separate BDN diagnostic also reports
zero bytes per block. All nine child launches per version succeed.

The following are **complete nested host-frame times**, in milliseconds:

| Case | Frame median before / after | Observed P99 before / after | Maximum before / after | Frames >31.25 ms, before / after |
| --- | ---: | ---: | ---: | ---: |
| A*/100 | 2.382 / 2.638 | 1,217.647 / 137.981 | 1,250.667 / 146.656 | 36/576 / 36/576 |
| Flow/100 | 3.968 / 3.744 | 1,209.573 / 109.337 | 1,220.792 / 116.166 | 36/576 / 36/576 |
| Flow/500 | 20.736 / 20.721 | 4,047.782 / 478.972 | 4,117.407 / 501.471 | 36/576 / 36/576 |

Every >31.25 ms observation remains at frame 0/16/32/48. Median periodic-frame
cost falls from **1,205.348 / 1,187.399 / 3,995.667 ms** to
**136.355 / 108.475 / 462.501 ms**, respectively. Median whole 64-frame blocks
fall from **4,960.667 / 4,996.946 / 17,181.732 ms** to
**703.068 / 648.987 / 3,026.668 ms**: reductions of **85.8% / 87.0% / 82.4%**.
Whole blocks include validation/instrumentation outside the nested frame timer;
do not divide them by 64 and call the result a safe frame cost.

There is no universal improvement claim. Ordinary non-recheck A* frames rise
from a median **2.371 to 2.624 ms (+10.7%)**, with a higher median in all three
candidate launches. Ordinary Flow/100 falls **3.948 to 3.731 ms (-5.5%)**;
Flow/500 is essentially unchanged at **20.676 to 20.614 ms** with mixed launch
directions. Retain the A* change for attribution rather than hiding it behind
the large recheck win. All measured frames still exceed the selected 2 ms
navigation allowance, and all periodic frames exceed the full 31.25 ms frame.
These are correlated frames from nine actual blocks per case, not portable
P99 guarantees or proof of a stationary distribution.

#### Remaining work and verification

A separate candidate Flow/100 sampled-thread-time profile covers five measured
64-frame blocks, with preparation outside the selected stack root. Roughly
**55%** of measured-root sampled time is inclusively in direct-heading work,
**43%** in `GridTracer.TraceIntervalsInto`, **30%** in planar interval solving,
and **32%** in Flow guide sampling. The shares overlap and profiling perturbs
execution; they locate follow-up work, not an additional speedup measurement.

At this checkpoint, the next steps were to isolate remaining periodic ray work
from ordinary guide sampling and examine the ordinary A* change with a bounded
matched control. The follow-up below records those results. The current
rejection reduces false-positive narrow-phase cost, not the segment-AABB
candidate range's asymptotic size. Preserve candidate budgets, dependency and
failure order when evaluating further improvements. Do not disable/stagger
rechecks, shrink budgets, or weaken semantic checks to make this fixture pass.
Cold-acquisition scheduling does not by itself bound these synchronous rechecks.

Verification against the unreleased local stack:

- GridForge Release/Lean: **844/844 tests** each; exact coverage
  **8,861/8,861 lines**, **3,787/3,787 branches**, **1,115/1,115 fully covered methods**.
  The additional Debug solution run passes all **841 applicable tests**.
- Trailblazer Release: **2,467 core + 65 adapter** tests; Lean:
  **2,405 core + 65 adapter** tests. Both merged reports retain exact coverage:
  **30,842/30,842 lines**, **12,089/12,089 branches**, **3,011/3,011 fully covered methods**.
- All four solution builds cover both library targets with zero warnings/errors.
  These are local Windows checks, not Linux CI or released-package validation.
- Nineteen new GridForge cases assert literal geometry/budget/failure behavior.
  Baseline testing caught and corrected an asymmetric full-domain rounding
  expectation. The first candidate coverage run exposed an untested finite-
  segment miss on a crossing supporting line; that meaningful case restores
  full fallback coverage without deleting reachable code or relaxing a gate.
- Independent code/proof review found no remaining actionable findings. The
  guided-log parser's duplicate-record finding was reproduced, fixed and
  re-reviewed; its positive and malformed-record behavior checks pass.

Raw logs, full JSON, frozen before/after binaries and hashes, generated jobs,
parser summaries and profiles are under `artifacts/benchmark004`:
`guided-{baseline,candidate}`, `guided-{baseline,candidate}.log`,
`guided-{baseline,candidate}-summary.json`, `guided-{baseline,candidate}-bin`,
`guided-candidate-flow100.nettrace`, `guided-candidate-flow100-stacks.json`,
and `verification-final`. `TRB-Issue-119` retains the earlier A* setup exception
separately; the six later successful A* benchmark launches are not a fix.

#### Follow-up: ordinary-frame control and shared containment work

On 2026-09-11 (UTC), a bounded A*/100 control reused one frozen generated runner and
the same shared dependencies, swapping only GridForge DLL/PDB between the
pre-rejection and post-rejection versions. Execution order was after / before /
before / after. This uses the generated runner's no-acknowledgement console
host, **not** a parent-driven BenchmarkDotNet capture; keep it separate from
the historical timing table.

| Launch order | GridForge version | Ordinary-frame median | Periodic-frame median |
| --- | --- | ---: | ---: |
| 1 | After rejection | 2.697 ms | 143.338 ms |
| 2 | Before rejection | 2.625 ms | 1,273.634 ms |
| 3 | Before rejection | 3.177 ms | 1,277.752 ms |
| 4 | After rejection | 2.711 ms | 147.552 ms |

Each process retains one warmup, three actual and one diagnostic 64-frame
block. Independent analysis checks all 20 records / 1,280 exported frames,
including exact Int64 replay, zero current-thread allocations and zero
collection-change masks. Actual-only counts are 384 frames per version:
360 ordinary and 24 periodic. The two unchanged-before launches themselves
vary substantially. This control does not consistently reproduce the earlier
ordinary A* increase; it does not prove that no causal regression is possible
or identify a JIT mechanism. The periodic improvement remains large in every
launch. All four processes complete without the setup exception.

Source review finds that ordinary A* guide following does not normally enter
the changed segment helper. Occupancy updates do share its FixedMathSharp
point-containment implementation. Do not confuse that source distinction with
measured per-frame call counts or a proven cause of timing variability.

The follow-up removes unnecessary query-point transforms from
FixedMathSharp convex containment. All four callers supply a world point and
zero local offset. A single internal method widens that point once; polygon
rotation, edge order, wide predicates and boundary inclusion stay unchanged.
The unused internal offset argument and forwarding overload are removed, with
no public API change. Independent code/proof review finds no actionable issues.

Eight FixedMathSharp microbenchmark cases cover inside/outside queries,
four/six vertices and zero/30-degree rotation. Three fresh launches per case
show **26.3-37.0% lower standard BDN means**, with zero allocation/GC reports.
The default adaptive/outlier policy is retained and documented: all-raw actual
means also improve **26.2-36.9%**, and every corresponding launch-level raw
median decreases. These are isolated containment results, not frame savings.

#### Fresh guided-frame comparison

The same three-case, three-launch, one-warmup/three-actual protocol above was
repeated against the current local stack, before and after the FixedMathSharp
change. Before source heads are Trailblazer `f62f35a`, GridForge `b3e60f3`,
FixedMathSharp `839bbdc`, and SwiftCollections `4b5715b`. Only the two
FixedMathSharp containment implementation files change in production source.

Each version retains 27 actual blocks / 1,728 actual frames, plus nine warmup
and nine diagnostic blocks. Independent audit checks all 45 records / 2,880
exported frames per version, every array, all timed operations against full
BDN JSON, and exact Int64 replay across versions and stages. All nested
allocation and collection observations, and all BDN memory diagnostics, are
zero. All 18 benchmark children complete successfully.

Actual-only times in milliseconds:

| Case | Ordinary median before / after | Periodic median before / after | Whole 64-frame block median before / after |
| --- | ---: | ---: | ---: |
| A*/100 | 2.692 / 2.554 | 137.949 / 121.561 | 713.590 / 643.311 |
| Flow/100 | 3.690 / 3.402 | 108.988 / 97.084 | 657.504 / 593.096 |
| Flow/500 | 20.250 / 19.735 | 469.895 / 405.887 | 3,066.814 / 2,747.608 |

Whole-block medians decrease **9.8% / 9.8% / 10.4%**. Observed nested-frame
P99 changes **143.800 / 110.646 / 484.510 ms** to
**126.596 / 98.876 / 423.174 ms**, and maximum changes
**149.280 / 114.700 / 504.717 ms** to **135.342 / 104.784 / 442.627 ms**.
Every case still has **576/576 actual frames above 2 ms** and **36/576 above
31.25 ms**. All observations above 31.25 ms occur at periodic positions
0/16/32/48. Do not average away those peaks.

Ordinary-frame variability remains material. Relative to the same ordinal
baseline launch, candidate A*/100 launch 2 is 8.2% slower in ordinary median;
Flow/500 launch 2 is 12.7% slower. The latter's three actual blocks have ordinary
medians **23.766 / 23.611 / 23.692 ms**, versus roughly 18-19 ms in the other
candidate launches. It is not a single removable outlier. Periodic medians
improve in all ordinal comparisons, but these are correlated frames and
sequential process launches, not matched statistical pairs or stationary
throughput guarantees.

Both actual-child archives contain the same 63 paths. Six DLL/PDB files differ:
FixedMathSharp, GridForge and the benchmark assembly. This is a normal rebuilt
comparison, not a single-DLL swap. The generated guided runner is byte-identical.
Portable-PDB document checksums verify unchanged Trailblazer, GridForge,
benchmark and generated source; only the two intended FixedMathSharp documents
differ. The micro and guided captures use different build graphs, but all 200
authored FixedMathSharp source checksums match for each corresponding variant.

#### Residual cost and next boundary

A separate candidate Flow/100 sampled-thread-time profile covers five replay-
checked 64-frame blocks. Restricting stacks to `RunMeasured` excludes setup
and preparation; it still includes benchmark validation outside the nested
frame timer. Mutually exclusive stack groups account for approximately **61.4%
direct-heading work, 25.7% Flow sampling and 12.9% other measured-block work**.
These are sampled shares, not exact per-frame call counts or a speed comparison.

Within that root, `GridTracer.TraceIntervalsInto` accounts for about **49.0%**
inclusively and planar interval solving **36.7%**; Flow body-segment/anchor
validation accounts for about **13.4%**. These nested shares overlap. Long
blocked-ray tracing remains the first priority, with guide-sampling geometry
the next distinct cost. Repeatedly transformed polygon vertices and the
segment-AABB candidate scan remain candidates for measurement, not approved
shortcuts. Preserve full-domain geometry, candidate accounting, dependency
and failure order; do not stagger/disable rechecks or tighten budgets to pass.

The focused containment improvement is measured; `TRB-Benchmark-005` remains
active because guided-frame budget acceptance is still not met.

#### Containment slice verification

All eight solution builds against the unreleased local stack pass with zero
warnings/errors, covering both library targets. The full test runs and exact
coverage reports pass in both configurations:

| Repository | Configuration | Passing tests | Covered lines | Covered branches | Fully covered methods |
| --- | --- | ---: | ---: | ---: | ---: |
| FixedMathSharp | Release | 2,708 + 8 Chronicler | 47,858/47,858 | 8,828/8,828 | 3,452/3,452 |
| FixedMathSharp | ReleaseLean | 2,687 + 8 Chronicler | 47,951/47,951 | 8,828/8,828 | 3,448/3,448 |
| GridForge | Release | 844 | 8,861/8,861 | 3,787/3,787 | 1,115/1,115 |
| GridForge | ReleaseLean | 844 | 8,861/8,861 | 3,787/3,787 | 1,115/1,115 |
| Gravitas | Release | 3,950 | 56,093/56,093 | 15,906/15,906 | 5,341/5,341 |
| Gravitas | ReleaseLean | 3,895 | 56,091/56,091 | 15,906/15,906 | 5,340/5,340 |
| Trailblazer | Release | 2,467 + 65 adapter | 30,842/30,842 | 12,089/12,089 | 3,011/3,011 |
| Trailblazer | ReleaseLean | 2,405 + 65 adapter | 30,842/30,842 | 12,089/12,089 | 3,011/3,011 |

FixedMathSharp coverage includes its core and FluentAssertions assemblies;
the separate Chronicler tests run without contributing to that coverage gate.
Trailblazer reports merge core and Gravitas adapter coverage. These are local
Windows checks, not Linux CI or released-package validation.

Eight new literal containment cases pass on baseline and candidate and all
fail under a deliberate incorrect-scaling mutation. The restored implementation
passes all 38 focused relation tests. Independent source/proof, raw timing,
replay, binary-provenance and profile audits find no remaining actionable
issues. The guided-log parser's positive and malformed-record checks also pass.

Evidence is under `artifacts/benchmark005`: `ordinary-control-*.log`,
`ordinary-control-summary.json`, `control-{before,after}`, `control-hashes.json`,
the new `fms-containment-*` / `guided-*` captures, and `verification` build/test
logs and exact coverage reports. The controls' shared
generated runtime is intentionally not claimed to be bit-identical to the
historical parent snapshots.

#### Follow-up: per-edge rejection measured and withdrawn

On 2026-09-11, GridForge `GF-Benchmark-005` tested skipping exact intersection
against an individual footprint edge when both endpoints are strictly on the
same side of the ray's supporting line. Consecutive widened orientation signs
were reused, and mixed/zero signs retained the existing solver. The candidate
was correct in the reviewed cases but **was not retained**: its narrow gains
did not establish a useful, consistent improvement in complete guided frames.
The earlier whole-footprint rejection remains in place; there is no new runtime
change in this slice.

GridForge now has a `grid-planar-interval` benchmark for crossings, vertex
touches and one-raw strict misses on rectangles and both hex orientations.
Three-launch adaptive captures show hex touch means decreasing 3.7%/6.6%, but
crossing means changing +0.8%/-0.3%/+1.7% (flat hex/pointy hex/rectangle).
Unchanged flat-hex/rectangle strict-miss controls increase 3.7%/2.0%; they return
before the proposed guard. All-raw Actual means have the same directions.
These descriptive differences do not establish a universal regression,
statistical noise threshold or code-generation cause. GridForge's tracker
retains the complete micro comparison table and no-change decision; per-launch
distributions are in `artifacts/benchmark005/planar-edge-audit.json`.

The pointy-hex strict-miss **baseline is incomplete**: launch 2 reports a
`NullReferenceException` after 13 actual iterations, and launch 3 never runs.
`GF-Issue-006` retains the failing actual child/source/hash snapshot and two
successful direct frozen-runner replays. Those console-host replays are not
replacement parent-driven launches. The original capture has eight complete
cases, with 25 successful children and one failure; the candidate completes all
nine cases / 27 children. Exclude the partial baseline case from comparisons
and do not treat its unknown allocation diagnostic as zero. This is separate
from `TRB-Issue-119`; neither exception has an established cause or fix.

The guided comparison repeats the three-case protocol above on the same
Windows/.NET 8.0.29 host. Baseline source heads are Trailblazer `03b4975`,
GridForge `b3e60f3`, FixedMathSharp `e8a2ab5` and SwiftCollections `4b5715b`.
Only the GridForge planar-interval implementation changes in production source
for the experiment. Use `guided-ray-baseline` and `guided-ray-candidate` as
distinct artifact labels; all builds, micro runs and guided runs are serial.

Actual-only times, milliseconds; **candidate means the withdrawn experiment**:

| Case | Ordinary median before / candidate | Periodic median before / candidate | Whole 64-frame block median before / candidate |
| --- | ---: | ---: | ---: |
| A*/100 | 2.444 / 2.445 | 119.628 / 116.655 | 626.401 / 611.927 |
| Flow/100 | 3.256 / 3.288 | 95.537 / 94.493 | 578.179 / 575.558 |
| Flow/500 | 18.252 / 19.393 | 398.497 / 401.301 | 2,683.747 / 2,745.125 |

Whole-block medians change **-2.3% / -0.5% / +2.3%**. Observed full-frame
P99 changes **122.592 / 98.003 / 415.668 ms** to
**118.611 / 95.329 / 418.483 ms**. Maximum changes
**125.781 / 100.410 / 426.656 ms** to **119.799 / 101.762 / 429.244 ms**.
Each case still has **576/576 actual frames above 2 ms** and **36/576 above
31.25 ms**, all at periodic positions 0/16/32/48. Flow/500 ordinary medians
are higher in all three ordinal candidate launches (+2.0% to +7.4%). Whole-block
directions vary across launches for every case. These correlated observations
are not randomized matched pairs; no causal ordinary-frame regression is claimed.

Each version has 27 actual blocks / 1,728 actual frames, plus nine warmup and
nine diagnostic blocks. Independent audit verifies all 45 records / 2,880
exported frames per version, exact Int64 replay across versions/stages,
zero current-thread frame allocations and collection-change masks, and all
timed operations against full BDN JSON. All 18 guided children exit cleanly;
the six A* launches add watch evidence, not a resolution of `TRB-Issue-119`.

Actual guided child archives each contain 63 files. Only GridForge and the
benchmark DLL/PDB pairs differ; the generated guided runner is byte-identical.
Portable-PDB document checks show only
`GridCellGeometry.NavigationBodySegment.cs` changed among authored source
inputs. All 110 GridForge and 200 FixedMathSharp authored documents match the
corresponding micro capture for each variant. These are actual-child source and
binary checks, not historical parent-output attribution or proof of identical
native compilation.

Eight new literal GridForge cases retain reversed/closing-edge crossings,
vertex boundaries and exact raw rounding. All pass on unchanged and candidate
runtime; an intentionally wrong whole-prism-rejection mutation fails all eight.
A separate 33,324-query frozen-binary probe returns identical booleans/raw
intervals across both variants, including extreme-coordinate and one-raw cases.
The runtime candidate is removed; the behavior checks, benchmark fixture and
investigation evidence remain. Independent source/proof and evidence reviews
support that disposition.

Full local-stack Release and ReleaseLean validation passes after withdrawing
the guard:

| Repository | Passing tests, Release / Lean | Covered lines | Covered branches | Fully covered methods |
| --- | ---: | ---: | ---: | ---: |
| GridForge | 852 / 852 | 8,861/8,861 | 3,787/3,787 | 1,115/1,115 |
| Trailblazer, including adapter | 2,467 + 65 / 2,405 + 65 | 30,842/30,842 | 12,089/12,089 | 3,011/3,011 |

Both configurations build both library targets with zero warnings/errors.
These are local Windows checks, not Linux CI or released-package validation;
FixedMathSharp and Gravitas did not change and their suites were not rerun in
this slice. Trailblazer coverage merges its core and Gravitas adapter reports.
An additional final restored-source GridForge run passes the same Release/Lean
gates and all **849 Debug tests**, with zero build warnings/errors. The guided-
log parser's positive and malformed-record behavior checks also pass.

Evidence remains under `artifacts/benchmark005`: `planar-edge-*`,
`guided-ray-*`, `edge-source-{baseline,candidate}`, `planar-replay-*`,
`ray-guard-mutation.log`, `ray-focused-*.log`, `ray-verification`,
`ray-restored-gridforge` and `ray-parser-tests.log`.
The micro audit explicitly distinguishes a known incomplete baseline from
successful integrity checks; it does not silently accept failed measurements.

**Next hypothesis at that checkpoint:** Reduce repeated transformed-vertex work in
FixedMathSharp containment, independently of the rejected edge guard. The prior
measured-root profile remains relevant to the unchanged committed runtime; no
new speedup is promised. Preserve full-domain wide arithmetic, closing-edge
and early-exit behavior, and existing caller validation. Candidate enumeration,
sparse snapshots, geometry-failure order and synchronous LOS cadence remain
constraints, not performance knobs. `TRB-Benchmark-005` remains active.

#### Follow-up: reuse wide rotated endpoints inside containment

On 2026-09-13 UTC, keep the one-method FixedMathSharp change that carries each
rotated endpoint into the next polygon edge and widens point-minus-origin once.
Wide rotation is linear without scalar rounding, so subtracting the rotated
endpoints produces exactly the previous edge. Full-domain intermediates fit
`Signed192` and the determinant fits `Signed320`. Authored edge order, the closing
edge, earliest rejection and public validation remain unchanged. No cache, new
API, candidate pruning, budget change or deferred LOS recheck is introduced.

Fresh serial Release captures use the existing 100x100 wall-detour fixture,
32 Hz clock, synchronized 16-frame rechecks, three launches per case and three
actual 64-frame blocks per launch. The machine/runtime remain Windows 11,
i7-9700K, .NET 8.0.29 and BDN 0.15.8. Baseline source heads are Trailblazer
`5841b26`, GridForge `b19f373`, FixedMathSharp `e8a2ab5`, SwiftCollections
`4b5715b` and Gravitas `42af506`. The only runtime edit is containment in FMS.

| Case | Ordinary median, before -> after | Recheck median, before -> after | All-frame P99, before -> after | 64-frame block median, before -> after |
| --- | ---: | ---: | ---: | ---: |
| A*/100 | 2.492 -> 2.489 ms | 118.644 -> 107.127 ms | 119.746 -> 109.664 ms | 628.478 -> 580.658 ms (-7.6%) |
| Flow/100 | 3.337 -> 3.132 ms | 96.029 -> 86.388 ms | 98.386 -> 88.019 ms | 574.462 -> 526.588 ms (-8.3%) |
| Flow/500 | 18.277 -> 16.751 ms | 392.543 -> 353.334 ms | 410.771 -> 368.822 ms | 2,697.504 -> 2,432.333 ms (-9.8%) |

All nine corresponding launch-level block and recheck medians decrease.
Ordinary A* is effectively unchanged in aggregate and mixed by launch; do not
claim every kind of frame improved. All-frame maxima decrease from
125.908 / 100.961 / 431.662 ms to 112.192 / 91.839 / 383.899 ms. No sample is
trimmed. Each capture completes all nine children, 27 actual, nine warmup and
nine diagnostic blocks. Independent audit verifies all 45 records / 2,880
frame checksums across versions as exact Int64 values, with zero frame bytes
and collections. Timing summaries use the 1,728 actual frames per version.
The whole block includes benchmark instrumentation/validation; it is not a
per-frame budget. All 36 actual recheck frames per case still exceed 31.25 ms.

The executed-child archives each contain 63 files; only FixedMathSharp and
benchmark DLL/PDB pairs differ. Generated source/project/build files match.
Portable-PDB document checks confirm the sole authored-source difference is
`WideConvex2dRelations.cs`; all benchmark and downstream source inputs match.
This is a normal rebuilt comparison, not proof of identical native compilation.

**Incomplete microbenchmarks:** Both separately labeled unchanged FMS baselines
fail in `Outside(4 vertices, 30 degrees)`, launch 2: one native access violation,
one managed NRE in `GetWorldPoint`. Each executes only 23/24 planned children.
Neither capture, including its partial statistics, is used for performance
comparison. No candidate microcomparison or isolated-math speedup is claimed.
FMS tracks this as `FMS-Issue-018`; one frozen-child diagnostic replay with
first-chance native-fault capture passes without a dump. It changes hosting and
debugger timing and does not repair either baseline or establish a fix. The
separate `TRB-Issue-119` and `GF-Issue-006` remain open without a proven common cause.

**Remaining work:** A fresh five-block Flow/100 profile using the archived
candidate binaries attributes 58.5% of measured-root sampled time to direct
heading, 28.7% to guide sampling and 12.7% elsewhere. Within direct heading,
`GridTracer.TraceIntervalsInto` is 43.2% of the root inclusive and planar interval
calculation 27.0%; direct-heading containment is 9.8%. Guide-sampling body-anchor
validation is a separate 10.9%. These nested shares overlap and are approximate stack
samples, not exact call counts or independently additive savings. All five
profile blocks retain matching frame checksums and zero frame allocation.

Next, inspect repeated endpoint/edge work in the remaining planar-interval path
for a measured consolidation, preserving exact raw interval results and failure
order. Do not revive the rejected per-edge guard or add speculative public
geometry machinery. Keep Flow sampling as its own measured cost. This slice
improves the existing behavior; it does not close `TRB-Benchmark-005` or make
the stress fixture fit a fixed frame.

Evidence is retained under `artifacts/benchmark005`: `guided-vertex-*`,
`vertex-source-audit.json`, `vertex-profile-*`, `fms-vertex-baseline*`,
`fms-vertex-fault-probe` and the eight new FMS boundary cases. Deliberately
omitting the closing edge fails all six new closing-edge cases. Source/proof
and Ponytail reviews find no actionable issue in the containment change.

Full local-stack solution builds and tests pass in Release and ReleaseLean,
with zero build warnings/errors and exact reachable coverage:

| Repository | Tests, Release / Lean | Covered lines, Release / Lean | Covered branches | Fully covered methods, Release / Lean |
| --- | ---: | ---: | ---: | ---: |
| FixedMathSharp core + Chronicler | 2,716 + 8 / 2,695 + 8 | 47,856 / 47,949 | 8,828 | 3,452 / 3,448 |
| GridForge | 852 / 852 | 8,861 / 8,861 | 3,787 | 1,115 / 1,115 |
| Gravitas | 3,950 / 3,895 | 56,093 / 56,091 | 15,906 | 5,341 / 5,340 |
| Trailblazer core + adapter | 2,467 + 65 / 2,405 + 65 | 30,842 / 30,842 | 12,089 | 3,011 / 3,011 |

Every listed covered count equals its total. FMS coverage includes
FluentAssertions, not its separate Chronicler package; Trailblazer merges core
and adapter coverage. Both library targets build. These are local Windows
checks, not Linux CI or released-package validation.

The first FMS Lean run failed its unchanged product-comparison allocation guard
at 2,208 bytes. A focused test-only correction uses the repository's existing
warmed-allocation helper for one shared 64-iteration operation; exact zero bytes
and checksum assertions remain. An intentional allocation mutation fails at
5,632 bytes and is removed. `FMS-Issue-019` retains the original burst without
allocator attribution or a claim that flakiness is cured. The failed first
matrix remains in `vertex-verification`; all final green logs/reports are in
`vertex-verification-final`. No new test project, CI, runner framework or runtime
workaround is added.

#### Follow-up: profile the remaining planar-interval work

On 2026-09-13 UTC, a profiling-only pass refreshes the baseline after the
endpoint-reuse changes were committed. No runtime, test, benchmark, dependency,
budget or LOS-cadence change is made. Source heads are Trailblazer `864225a`,
GridForge `b19f373`, FixedMathSharp `eec40bf`, SwiftCollections `4b5715b` and
Gravitas `42af506`. The same Windows/i7-9700K/.NET 8.0.29 local-source Release
protocol runs serially, without competing builds, tests or profiles.

| Case | Ordinary median | Recheck median | All-frame P99 | All-frame maximum | 64-frame block median |
| --- | ---: | ---: | ---: | ---: | ---: |
| A*/100 | 2.420 ms | 105.639 ms | 107.088 ms | 115.947 ms | 571.505 ms |
| Flow/100 | 3.060 ms | 88.105 ms | 90.970 ms | 94.324 ms | 539.468 ms |
| Flow/500 | 16.857 ms | 354.757 ms | 366.528 ms | 396.471 ms | 2,441.737 ms |

This is a refreshed baseline, not another optimization or a before/after claim.
All nine children complete: 27 actual, nine warmup and nine diagnostic blocks.
The audit retains all 45 records / 2,880 frame checksums, with zero measured
frame allocation and collections; timings use the 1,728 actual frames. Every
case still has 36/36 actual recheck frames above 31.25 ms. No outlier is removed.
Whole-block timings include benchmark instrumentation/validation, not just the
fixed frames.

Separate profiles reuse all 63 files from that executed child, adding only the
existing probe entry point and its source copy. Portable-PDB checks match the
authored benchmark and five local runtime assemblies' source documents to the
current checkout. Profiles capture 20 A*/100, 20 Flow/100 and ten Flow/500
blocks, excluding setup/preparation from the `RunMeasured` attribution root.
All 50 blocks / 3,200 frame checksums match the baseline exactly as Int64 values,
with zero frame allocation and collections. These longer profiles confirm the
earlier Flow/100 signal across the other selected workloads:

| Sampled share of the measured root | A*/100 | Flow/100 | Flow/500 |
| --- | ---: | ---: | ---: |
| Direct-heading work, inclusive | 66.4% | 58.8% | 50.5% |
| Its `TraceIntervalsInto`, inclusive | 39.8% | 45.5% | 37.0% |
| Its planar interval calculation, inclusive | 25.1% | 27.4% | 23.5% |
| Containment, across all callers | 10.5% | 10.2% | 11.0% |
| Direct-heading segment intersection, inclusive | 7.3% | 7.1% | 5.9% |
| Flow guide-sampling body-anchor validation, inclusive | n/a | 12.9% | 12.9% |

Nested rows overlap; they are approximate sampled attribution, not additive
savings or exact invocation counts. Profiles cover about 13.1 / 12.2 / 28.9
seconds of measured-root sampled time. This root includes benchmark validation
and replay hashing outside the inner host-frame timer. Profiles are not timing
comparisons with the unprofiled baseline. Absence of a distinct A* sampling
stack is not a claim that A* sampling is free.

**Next experiment:** Specialize exact zero-rotation containment privately inside
FixedMathSharp's existing implementation. GridForge's endpoint and prism checks
use the no-rotation overload, which currently still constructs a rotation frame
and performs general wide vertex rotation. At zero rotation, the determinant
can use widened raw differences directly: the current determinant is the same
expression multiplied by the positive scale squared. Preserve vertex-count
validation, both windings, repeated/collinear and closing edges, and earliest
mixed-sign rejection. Never narrow `point - origin - offset` or reconstruct
world vertices through saturating scalar arithmetic. Keep nonzero rotation
unchanged and use the existing rotated benchmark cases as controls. This is a
source-supported hypothesis, not implemented or measured as a speedup here.

Streamed interval extrema can separately replace the small parameter array and
deduplication pass: duplicates do not affect min/max. It is lower priority
because it leaves the measured wide math intact. Reusing supporting-line signs
would add bookkeeping, and those signs cannot replace endpoint containment
predicates. Neither warrants bundling into the first experiment. The rejected
per-edge guard remains rejected; no new geometry API, cache, framework or CI
project is proposed.

Containment alone cannot explain enough of the measured cost to make the stress
fixture fit a fixed frame. Remeasure complete guided frames after the focused
experiment, then revisit remaining interval/tracer work and the separate Flow
body-validation cost. `TRB-Benchmark-005` stays active. No historical exception
is reproduced or resolved by these successful captures.

Evidence: `artifacts/benchmark005/guided-interval-baseline*`,
`interval-profile-*`, `interval-source-audit.json`, `collect-interval-profiles.ps1` and
`audit-interval-profiles.ps1`. The Release benchmark build passes with zero
warnings/errors. Full unit/coverage and Lean matrices are not rerun in this
profiling-only pass; the preceding committed-source validation remains the
historical record, not a new validation claim.
Independent raw-evidence/source review verifies the results and attribution;
the overview now makes the measured-root denominator explicit. The Ponytail
review finds no unnecessary scope in the proposed next experiment.

#### Follow-up: remove the measured geometry work

On 2026-09-13 UTC, the coordinated working trees implement the next reductions
against GridForge `b19f373`, FixedMathSharp `eec40bf` and Trailblazer `3bf1b87`.
The benchmark source is unchanged. Public APIs, query budgets, candidate
accounting, dependencies and the default synchronous 16-frame LOS cadence stay
unchanged.

- **GridForge (`GF-Benchmark-006`):** Preserve admission against the complete
  address range, but narrow rectangular candidate geometry to conservative
  per-column Z ranges. Use actual cell centers, closed boundaries and guarded
  rounding; retain Y candidates and fall back for horizontal/vertical rays,
  unrepresentable arithmetic and unsupported topology. Rectangular containment
  uses canonical closed bounds. Eligible rectangular interval queries use exact
  slab comparisons before rounding only the selected entry/exit parameters.
- **FixedMathSharp:** Zero-angle convex containment uses widened raw differences
  instead of general rotation. Raw segment endpoint-distance comparisons reuse
  the existing exact strict capsule predicate, removing the unused rational
  projection bridge. For a nondegenerate endpoint-authored 2D capsule, a
  closed-contained start and strictly contained end return `[0,1]` without a
  boundary solve. Defer unused dot products until needed; end-boundary,
  zero-axis and rotated/general fallback paths remain.
- **Trailblazer:** Flow sampling reuses the existing cache's exact immutable-root
  dependency proof within one call instead of walking the same stamp three or
  four times. Changed roots still require full comparison. Every live payload
  generation/invalidation lookup and world-sequence check stays at its existing
  boundary. The held guide lock fixes payload identity; no retained proof,
  new cache or public API is needed.
- **Compatibility correction during review:** An early column candidate skipped
  horizontal collinear contacts whose extrapolated parameter rounds onto a
  finite endpoint. Literal forward/reverse regressions now preserve `[1,1]` /
  `[0,0]` through the original horizontal path. The intermediate `column`
  capture predates this correction and is not the final source. No known
  regression is left open or hidden by a changed expectation.

The serial Windows/i7-9700K/.NET 8.0.29 Release protocol is unchanged. Each
capture has three fresh launches per selected case, one warmup and three actual
64-frame blocks per launch, plus diagnostic blocks. The final `flow-proof`
capture and refreshed `interval-baseline` compare as follows:

| Case | Ordinary median before / after | Recheck median before / after | All-frame P99 before / after | 64-frame block median before / after |
| --- | ---: | ---: | ---: | ---: |
| A*/100 | 2.420 / 2.070 ms | 105.639 / 28.325 ms | 107.088 / 28.912 ms | 571.505 / 238.710 ms |
| Flow/100 | 3.060 / 1.303 ms | 88.105 / 19.141 ms | 90.970 / 19.810 ms | 539.468 / 153.375 ms |
| Flow/500 | 16.857 / 7.442 ms | 354.757 / 90.386 ms | 366.528 / 94.696 ms | 2,441.737 / 801.356 ms |

Recheck reductions are **73.2% / 78.3% / 74.5%**; whole-block reductions are
**58.2% / 71.6% / 67.2%**. Blocks include validation/hashing outside the host
frame timer; dividing their durations by 64 does not establish frame latency.
Actual timing uses 1,728 frames per capture, with no outlier removal. These are
descriptive serial local measurements, not randomized paired trials or a
cross-platform guarantee. A*/500 remains outside this selected capture.

Intermediate captures isolate meaningful steps. In particular, `distance` to
`slab` changes exactly one authored GridForge runtime document: recheck medians
improve 27.5% / 38.8% / 35.6%, with improvement in all nine launch medians.
`slab` to `endpoints` combines the final raw-distance and capsule changes in
FixedMathSharp; do not attribute their gains separately. Ordinary A* is
effectively unchanged in that last step (+0.216%); ordinary timing does not
improve uniformly across every intermediate launch.

The subsequent `endpoints` to `flow-proof` step changes only the Flow guide
lease in authored runtime source. Ordinary Flow/100 and Flow/500 medians move
from **1.590 / 9.671 ms** to **1.303 / 7.442 ms**; their block medians move from
**177.332 / 967.227 ms** to **153.375 / 801.356 ms**. The unchanged A* control
also moves (ordinary **2.255 to 2.070 ms**, block **254.771 to 238.710 ms**).
Do not attribute that control movement, or every captured Flow difference,
uniquely to the proof-reuse patch.

The final independent audit verifies baseline, `endpoints` and `flow-proof`:
27 successful children, 135 properly staged records and **8,640 exact
signed-Int64 frame checksums**, with zero measured frame allocation/GC. Raw
summaries and full BDN measurements agree. All 189 retained child files and
18 PE/PDB identities verify; all 697 final authored source hashes match the
checkout. Baseline to final changes nine runtime documents (FMS five,
GridForge three, Trailblazer one). Benchmark and Swift sources are unchanged;
FMS/GridForge DLLs and PDBs are byte-identical between the last two captures.
Earlier geometry-stage audits remain in `guided-final-endpoints-review-audit.json`;
the final extension is `guided-flow-proof-review-audit.json`.

**Remaining acceptance:** Final rechecks above 31.25 ms are **0/36 A*/100,
0/36 Flow/100 and 36/36 Flow/500**; maxima are **29.805 / 20.400 / 97.517 ms**.
Ordinary frames all fit 31.25 ms; all 540 Flow/100 ordinary frames also fit
the smaller 2 ms navigation allowance. That allowance still has misses in
the other cases and all rechecks. Keep `TRB-Benchmark-005` active; neither a
budget increase nor
spreading/skipping LOS checks is used to manufacture acceptance. A separate
`endpoints` Flow/100 profile, before the Flow proof-reuse patch, puts planar
interval calculation at about 1.5% of sampled `RunMeasured` time versus 27.4%
before, and tracing at 8.9% versus
45.5%. The original geometry bottleneck is substantially reduced, not evidence
that all guided-frame costs are solved. Shares overlap and include benchmark
validation; they are not additive pure-frame savings.

Tests cover full-domain differences, one-raw boundaries, degenerate/reversed
segments, rounded tangencies, exact interval misses, sparse candidates and
unchanged capacity/failure precedence. Deliberate broken closing-edge,
narrowed-difference, rounded-slab and capsule-boundary mutations fail the new
behavior tests, then pass with the correct implementation restored. Independent
correctness and Ponytail reviews find no remaining actionable issues. No new
test project, CI framework, public geometry API or cache is added.
Three additional Flow cases cover publication after initial validation and
exact cache invalidation during a rejoin. Removing the acquired-graph check or
the final cache lookup makes sampling incorrectly succeed and the new tests
fail, as their mutation runs demonstrate. The first characterization run's
wrong Right heading assertion was corrected from the fixture's authored `(3,0,0)` to
`(0,0,0)` route; that retained failure is a test-authoring error, not a runtime
regression. Restored focused Release/Lean selections each pass 200 tests.

**Full local validation:** All four solutions build both library targets with
zero warnings/errors in Release and ReleaseLean against `UseLocalLsfStack=true`.
Every configured line, branch and fully covered method is covered exactly,
without new exclusions or relaxed gates. Counts below are both covered and
total; `+` identifies the separate integration-test project.

| Repository / configuration | Passed tests | Lines | Branches | Fully covered methods |
| --- | ---: | ---: | ---: | ---: |
| FixedMathSharp Release | 2,744 + 8 | 47,868 | 8,846 | 3,453 |
| FixedMathSharp ReleaseLean | 2,723 + 8 | 47,961 | 8,846 | 3,449 |
| GridForge Release | 887 | 8,957 | 3,857 | 1,118 |
| GridForge ReleaseLean | 887 | 8,957 | 3,857 | 1,118 |
| Gravitas Release | 3,950 | 56,093 | 15,906 | 5,341 |
| Gravitas ReleaseLean | 3,895 | 56,091 | 15,906 | 5,340 |
| Trailblazer Release | 2,470 + 65 | 30,847 | 12,091 | 3,011 |
| Trailblazer ReleaseLean | 2,408 + 65 | 30,847 | 12,091 | 3,011 |

The four-repository matrix passes after the geometry changes. Trailblazer's
complete matrix passes again after its proof-reuse change; upstream source is
unchanged between those matrices. These are local Windows source-stack checks,
not Linux CI, released-package validation or release authorization.

Evidence stays under `artifacts/benchmark005`: `guided-{zero-angle,column,
rectangle,distance,slab,endpoints,flow-proof}*`, independent audits,
`endpoints-profile-*`, focused/mutation logs, `reduction-verification` and
`flow-proof-verification`.
Use the existing guided-frame command above with a distinct artifact directory
and `UseLocalLsfStack=true`; timings and full verification run serially.
Historical benchmark exceptions and incomplete microcaptures remain separate
evidence, without a claimed common cause or fix.

## Closed Signals

### TRB-Benchmark-004 - Tiny cold Flow protocol has an iteration-position excursion

- **Discovered:** 2026-09-10 while measuring `TRB-Benchmark-003`.
- **Status:** Closed locally on 2026-09-10 with a measurement-protocol correction
  and a documented no-runtime-change decision. This was not established as a
  runtime regression, correctness issue, release blocker, or reason to reopen
  `TRB-Issue-114`.
- **Original evidence:** In the historical `ColdPublicFlow(Plane100)` capture,
  actual invocation 2 was slower in all three launches of both versions:
  baseline 13.472–13.864 ms versus 10.351–10.616 ms for the other invocations;
  candidate 12.385–12.499 ms versus 9.356–9.797 ms. Retain those original
  six sequences in `artifacts/benchmark003/{baseline,candidate}-cold`.
- **Scope:** A payload-cache miss against warmed graph/workspaces/pools is not
  necessarily warmed execution of cold integration. Global setup's repeated
  cache-hit acquisitions do not establish that distinction.

#### Ordered isolation and disposition

All five captures ran serially against unchanged local-stack Release binaries:
Trailblazer `4a022509e99d89a36cd5a5980f0a1c3d7beb6214`, GridForge
`7179ccecb7d32107fec31461177a34d2221fd666`, SwiftCollections
`4b5715b2232930480c207fa6e00944713f5000d6`. This is a new protocol-isolation
build, not the historical candidate. The Windows host used .NET 8.0.29,
BenchmarkDotNet 0.15.8 and the High performance power plan. No inherited
`DOTNET_`/`COMPlus_` overrides were present; only the explicitly labelled
diagnostic changed tiering. Child jobs used concurrent workstation GC.

Every launch used Monitoring, one operation per invocation and no unrolling.
The exact filter `'*ColdPublicFlow*Plane100?)'` selected only 100 nodes, unlike
the historical `Plane100*` filter that also selects 10K/100K. Commands are in
the [benchmark guide](../../tests/Trailblazer.Benchmarks/README.md#cold-caches-versus-runtime-warmup).
For the diagnostic, add `--envVars DOTNET_TieredCompilation:0` to the 1-warmup,
100-actual command. Repeat the original short command afterwards.

| Capture in execution order | Launches | Warmups / actuals per launch | Actual observations | Actual min / median / max (ms) |
| --- | ---: | ---: | ---: | ---: |
| Original normal control | 3 | 1 / 3 | 9 | 9.742 / 10.172 / 13.270 |
| Extend actual count only | 3 | 1 / 100 | 300 | 2.280 / 2.630 / 20.600 |
| Extend warmup count | 3 | 100 / 100 | 300 | 2.239 / 2.329 / 5.996 |
| Tiering-disabled diagnostic | 3 | 1 / 100 | 300 | 2.375 / 2.431 / 3.150 |
| Repeat normal short control | 3 | 1 / 3 | 9 | 9.774 / 10.218 / 13.892 |

All **312 warmup and 918 actual observations** were retained and independently
cross-checked against full JSON, with no mismatches. `WorkloadResult` duplicates
were not counted again. The mixed early/late 1/100 median is not a steady-state
statistic; no P99 claim is made from nine observations.

The extended normal runs repeat excursions at total post-setup timed invocation
ordinals **3 and 8**, not specifically an actual-stage boundary:

- With one warmup: actual 2 is 12.891–14.110 ms; actual 7 is 20.516–20.600 ms.
- With 100 warmups: warmup 3 is 12.882–12.988 ms; warmup 8 is 20.129–20.317 ms.
- With tiering disabled: the corresponding actual 2/7 values are 2.388–2.499 ms.
- Returning to the normal short control reproduces actual 2 at 12.955–13.892 ms.

Normal 1/100 actual first-20 means of 8.693 / 10.469 / 10.771 ms fall to
last-20 means of 2.336 / 2.401 / 2.445 ms. Normal 100/100 first-20 means are
2.350 / 2.352 / 2.618 ms, versus last-20 means of 2.291 / 2.347 / 2.351 ms.
The latter still includes about 10% launch-3 drift and actual excursions of
5.857 and 5.996 ms. Keep those observations; longer warmup is not proof of a
perfectly stationary distribution or a universal latency guarantee.

All 15 launch cleanup records match: matched internal preflight settles 100
nodes with 2,048 evaluated edges and 2,079 lookups; Flow payload retention is
8,192 bytes; warm allocation preflight and drained active/reserved/detached
ownership are zero. Separate allocation diagnostics all report 18,576 bytes/op.
These are not per-actual public-request work or GC counters: BenchmarkDotNet
collects extra allocation statistics in a separate invocation after actual
timing, as shown in its [tagged engine source](https://raw.githubusercontent.com/dotnet/BenchmarkDotNet/v0.15.8/src/BenchmarkDotNet/Engines/Engine.cs).

**Decision:** close the narrow watch as warmup/tiering-sensitive measurement
behavior. The exact runtime/JIT mechanism was not traced; no production fix or
change to the runtime configuration is justified. .NET documents
[tiered compilation](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation)
as background optimization, but that general mechanism does not identify which
work caused an individual excursion. The diagnostic is not a recommendation to
disable tiering. Retain short controls as early-invocation evidence and inspect
ordered longer-warmed observations before making warmed tiny-request claims.
Do not reclassify the historical larger-node measurements as steady state,
apply a correction factor, or invalidate their matched-protocol comparisons.

Raw logs/full JSON, generated jobs/runtime configurations, frozen assemblies
and SHA256 provenance are under `artifacts/benchmark004`: `original-control`,
`sequence-1w-100a`, `warmed-100w-100a`, `no-tiering-1w-100a`, `control-repeat`,
`frozen-bin` and `provenance.json`. Reopen only for a new reproducible regression
under a matched protocol or if a specific host needs causal/tail investigation.

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
- Close this narrow optimization slice. Keep the observed workload limits.
  The subsequent `TRB-Benchmark-004` entry closes the measurement watch with
  ordered protocol evidence; `TRB-Benchmark-005` measures representative
  guided-host frames. Neither follow-up authorizes speculative prefix skipping.

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

None.

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
