# Certified Navigator Direct-Visibility Rejection Implementation Plan

> **For agentic workers:** Use subagent-driven development or executing-plans
> task-by-task, with independent correctness and Ponytail review at each phase
> exit. Keep decisions and condensed evidence here, not in another plan.

**Created:** 2026-09-15

**Subsequent benchmark disposition (2026-09-20):** The user closed
[TRB-Benchmark-005](../benchmark-signal-hardening-backlog.md#trb-benchmark-005---guided-frames-spend-most-time-in-long-blocked-segment-tracing)
as an accepted stopping point, with the unmet 2 ms target explicitly deferred.
This supersedes the historical directions below to resume or keep that campaign
open; measurements, exceptions, unresolved failures and release gates are unchanged.

**Status:** Complete locally and archived on 2026-09-16. All five phases are
implemented, measured and independently reviewed. Retain for user branch review,
with two explicitly recorded non-regression target misses, not a blanket
performance pass. The broader 2 ms/recovery goal remains open under
`TRB-Benchmark-005`.
Work remains on `codex/certified-direct-visibility` in the existing Trailblazer,
GridForge and Gravitas checkouts. No merge, push or release is authorized.

**Source baseline:** Planning began at Trailblazer `5f44495` / GridForge
`02aa2de`; the Trailblazer feature branch started from `develop` at `785b801`.
The final frozen baseline is Trailblazer `e6391ee` / GridForge `be85b56`.
The execution record identifies the remaining sibling revisions and binaries.

**Goal:** Reduce work on long, obstructed Navigator direct-travel checks by
proving an obstruction before processing an irrelevant suffix, without false
visibility decisions or weakened active-guide freshness.

**Architecture:** Prefer one internal, negative-only certificate in the existing
immediate direct-heading boundary. GridForge owns complete geometric candidate
discovery and original-segment geometry; Trailblazer owns navigation semantics,
proof validity and controller behavior. The complete ray remains authoritative
for positive visibility and cases outside the proved eligibility envelope.

**Tech Stack:** C# 11; `netstandard2.1` and `net8.0`; FixedMathSharp, GridForge,
existing Trailblazer workspaces/meters, xUnit v3 and BenchmarkDotNet.

**Spec:** The [decision contract](#decision-contract),
[proof and fallback gates](#proof-and-fallback-gates) and
[acceptance matrix](#acceptance-matrix) below are the design specification.
The concrete contract below is a reviewed design, not yet a shipped API.
The user's latest direction supersedes the former calibration-based closeout:
baseline first, implementation second, comparable measurement third. Process
thresholds guide investigation; they do not replace engineering judgment or
turn ordinary timing variability into evidence against an unbuilt feature.

## Why investigate this

[`TRB-Benchmark-005`](../benchmark-signal-hardening-backlog.md#trb-benchmark-005---guided-frames-spend-most-time-in-long-blocked-segment-tracing)
remains open. The prior measured-frame Flow/500 profile attributes about **45%**
of sampled controller time to direct-heading checks. This is historical local
attribution, not a new timing run or a prediction of recoverable time. The
body-interior experiment remains withdrawn; its mixed results do not justify
reviving it. The 2 ms navigation allowance and mandatory recovery bursts are
not solved by this proposal.

The pre-implementation pipeline explains the opportunity:

1. `TrailblazerGuideService.TryGetDirectHeading` leases the graph and serialized
   immediate workspace, then starts one query meter.
2. `NavigationRayWork.Trace` eagerly calls `GridTracer.TraceIntervalsInto` for
   the **complete centerline**, not a swept-body stream.
3. `MapIntervals` maps all results and records dependencies before `EvaluateChain`
   can reject travel. Later portal/prism checks certify radius/height clearance;
   volume traversal has additional body certification.
4. Success must be cost-neutral and pass final dependency/heading validation.

A nearby decisive obstruction therefore did not spare far-segment
candidate discovery, prism/interval evaluation, sorting or mapping. That is a
real structural opportunity. The implementation and containing-frame measurements
below establish the benefit within the narrow supported envelope; source
inspection alone was not performance evidence.

### Approaches considered

| Approach | Decision |
| --- | --- |
| Cast shorter rays, or stop at the first blocked interval | Reject. Artificial endpoints change body/portal validity and fixed-point interval peers; another representation or explicit corridor may remain passable. |
| Bounded negative certificate using original-segment geometry | Investigate first. A strict separating obstruction may suffice; otherwise a complete bounded prefix must rule out every continuation. Unsupported cases use the full authority. |
| General ordered, resumable tracing plus a persistent reachable frontier | Defer. It adds cross-grid ordering, tie closure, state and lifetime machinery. Require a separate scope review if the narrow proof cannot work without it. |

## Global constraints

Follow [AGENTS.md](../../../AGENTS.md) for coding, tests, exact coverage, documentation,
issue tracking and worktree ownership, and the [overview](../feature-work-overview.md)
for coordinated release gates. The user authorizes this feature branch and
checkpoint commits, but not merging, pushing or releasing. Feature-specific
constraints:

- Correctness and deterministic replay before speed; fixed-point simulation
  math only. Both library targets share the implementation.
- Keep `PathQuery` as the public request and both A* and Flow on the same
  Navigator path. No public visibility strategy hierarchy, per-algorithm toggle,
  compatibility wrapper, automatic scheduler, result cache or cross-agent reuse.
- Do not change generic endpoint rays, A* simplification, Flow rejoin/action
  approach, `TraceIntervalsInto`, or `TraceNavigationBodyInto` contracts. Share
  existing primitives where useful; do not copy their algorithms into Trailblazer.
- Preserve exact query positions, profile, medium, unrestricted direct-ray
  constraint and `EndpointAllowance.None`. Do not introduce endpoint snapping
  or map-hint-based reseeding as an optimization.
- Keep LOS phases/countdowns, required-check opportunities, synchronous Navigator
  acquisition and pending-action ownership unchanged. The public scheduled
  acquisition service is separate. No multi-frame ray continuation in this slice.
- Reuse bounded context-owned scratch; no allocation per warmed check, retained
  live grid/voxel objects, wall-clock decisions, unbounded scans or hidden growth.
  Counted limits are not elapsed-time guarantees.
- No speculative helper layers or a new benchmark/CI/regression project.
  No serialization change is intended; design risks are not confirmed bugs.

## Decision contract

These are proposed **internal outcomes**, not new public API names. Phase 0
freezes their smallest concrete representation; don't alias a partial trace to
the generic ray's `Blocked` result before the controller boundary distinguishes it.

| Outcome | Required meaning |
| --- | --- |
| Clear | Complete existing successful, cost-neutral proof and exact heading. A prefix never establishes this. |
| Certified obstruction | A complete, freshly validated proof rules out direct travel. It is not a cost result, guide dependency payload or reusable certificate. |
| Inconclusive / ineligible | No obstruction decision. Continue or use the complete authority under the accounting rules below. Never interpret partial output as blocked. |
| Observed failure | Preserve explicit stale, query-budget, scratch-capacity or arithmetic classification. Observed stale evidence cannot become ordinary obstruction. |

### Deliberate semantic difference requiring Phase 0 sign-off

After a valid obstruction certificate is complete, the direct-only operation may
stop without inspecting the suffix. **Unobserved suffix** overflow, exhaustion,
stale map state and dependencies do not supersede that certificate. Failures
observed while constructing or validating the certificate do supersede it.

This is not transparent failure-order preservation. In particular, the old
complete ray might find a stale suffix and discard guidance; a fresh negative
certificate may retain a separately validated guide instead. If identical old
suffix-status/retry timing is required, stop this feature: scanning the suffix
to preserve that precedence defeats the principal saving.

| Navigator situation | Clear | Certified obstruction | Observed stale |
| --- | --- | --- | --- |
| Initial direct attempt | Enter direct mode | Continue ordinary same-call A*/Flow acquisition | Prepare retry; suppress same-call acquisition |
| Periodic check with a valid guide | Release guide, enter direct mode | Keep the independently fresh guide | Release guide, zero heading and prepare retry |
| Periodic check already in direct mode | Remain direct | Leave direct mode and prepare retry | Leave direct mode and prepare retry |

Budget/capacity/overflow keep their existing non-success handling; they do not
assert a geometric obstruction. Inconclusive is not returned to steering as a
decision. Test each row's headings, leases, events and acquisition frame rather
than merely asserting a Boolean. Preserve the existing next-heading retry
boundary; do not quietly turn it into immediate or scheduled acquisition.

## Proof and fallback gates

### Geometry and alternatives

- Keep original start/end and their fixed-point parameterization for interval
  and portal evaluation. A bounded discovery envelope is not a new ray endpoint.
- GridForge must certify that **all** representations relevant to the proposed
  obstruction were considered, including topology reach and rounded endpoint
  peers. Current ordering is entry ascending, exit descending, durable grid
  configuration, then voxel index. Equal-entry peers and transitive overlap
  groups must not be cut arbitrarily.
- A strict separating cut is preferred if it can rule out every eligible
  representation/corridor without a general search frontier. Otherwise the
  prefix must close over all reachable alternatives, including later seam
  sources that unlock earlier records and explicit corridor crossings.
- An unpassable cell, a missing interval or an expensive semantic area alone
  is not a certificate. Use exact profile/medium/body/portal semantics, not
  Trailblazer-local neighbor math or a center-point collision substitute.
- The first envelope may be narrow, but eligibility must be established from
  current authoritative data, not benchmark dimensions, known wall locations,
  registration order or presumed absence of overlaps/connections. Unproved
  hex, volume, overlap, seam or explicit cases remain on the full authority.
- `GridCoveredAddressCursor` is useful bounded-storage precedent, not an ordered
  trace: its complete eligible-generation input and address order do not certify
  an unseen-candidate frontier. Collecting/sorting the entire segment before
  yielding does not count as avoiding discovery work.

### Freshness and ownership

Use the existing immediate lock and graph lease. Fence world identity/change
sequence before discovery and at final validation. Validate relevant grid
generation/configuration/sequence, exact policy, page and component evidence,
including negative pages with no requested-medium component.

Excluding alternative maps requires **absence/completeness evidence** as well
as evidence for the obstacle. Initially require unchanged graph publication
identity through certificate validation, unless existing exact stamps already
prove the candidate universe unchanged. An added overlapping map can invalidate
the proof without changing the obstacle page. Do not revive page-proof skipping.

Observed relevant dependency/world mutation returns stale, not a negative proof.
A changed graph identity alone instead makes the certificate **inconclusive**:
an unrelated publication may leave the full ray's dependencies valid. Use
budget-safe full-authority fallback; do not discard valid guidance merely because
the shortcut lost its completeness proof. Phase 0 must prove this fallback too.

Clear scratch and references on terminal results, reset and disposal. Fallback
may retain only validated work required by the accounting rule below; never
expose partial output as a decision. Keep active-guide and held-action freshness
independent; being between LOS slots never permits stale guidance.

### One direct-attempt budget, not a free speculative probe

All chargeable prefix and fallback work belongs to **one** direct-attempt meter.
No reset, refund, second allowance, unmetered eligibility traversal or increased
public budget to make the experiment fit. A subsequent ordinary guide acquisition
retains its existing independent budget; do not couple those operations.

The baseline requirement is that inconclusive/clear fallback not fail a query
that the complete ray can satisfy solely because prefix work was repeated.
Phase 0 must demonstrate either eligibility rejection before chargeable traversal,
or reuse/continuation of chargeable work without replaying it. Record category
counts, not just totals, with exact and one-below tests. Failure is a Phase 0
no-go. A separate speculative allowance or changed fallback budget envelope is
**not** implicitly approved by this plan.

Query exhaustion, bounded scratch exhaustion, unsupported proof and arithmetic
failure remain distinguishable. Never turn failure to finish a certificate into
`Certified obstruction`. Preserve zero-disabled categories and failed-debit
atomicity. A new GridForge report must name prefix completeness explicitly;
do not reuse a full-trace completion marker for truncated output.

## Implementation boundaries

Phase 0 records exact changed files and signatures after its proof gate, not
speculative result types or a new tracing API in advance.

| Owner | Existing boundary and responsibility |
| --- | --- |
| Trailblazer direct decision | `TrailblazerGuideService.TryGetDirectHeading`, its immediate workspace and meter; reuse `NavigationRayWork` semantic/body primitives without changing generic consumers. |
| Navigator state | `NavSteering.Simulation` initial and periodic calls; explicit result mapping without rewriting phases or acquisition. |
| GridForge geometry | `GridTracer`, `GridTraceIntervalScratch` and `GridWorld` discovery; complete original-segment geometry, never navigation semantics. Preserve existing trace contracts. |
| Verification | Extend existing ray, guide-service, steering lifecycle and GridForge trace tests with context-first fixtures. Use existing ray/guided-frame benchmarks and raw-frame analyzer; diagnostic spikes stay ignored. |

## Phase 0 - Prove the opportunity and freeze the contract

- [x] Capture current source identities and the unchanged complete-ray outcomes,
  meter categories, dependency evidence and controller state table. Read the
  [benchmark README](../../../tests/Trailblazer.Benchmarks/README.md),
  [publication](../../wiki/MapPublication.md) and [serialization](../../wiki/Serialization.md)
  boundaries before touching state or lifecycle.
- [x] Build one bounded, ignored diagnostic against the complete ray. Record
  actual candidate visits/prism evaluations, interval output/sorting and map
  work before/after the earliest **certifiable** obstruction. Separate admitted
  range counts from work actually performed; do not claim a saving from the
  first blocked voxel's index. Include the existing guided wall and a separate
  long oblique/sparse blocked scene plus open/late-blocked controls.
- [x] Prove original-parameter discovery completeness and the narrow eligibility
  envelope with literal adversarial examples from the acceptance matrix.
  Prefer a strict separator; try a closed bounded prefix only if necessary.
- [x] Demonstrate the fallback budget rule on clear/inconclusive and exact-budget
  inputs, including unrelated publication during proof. Reject a prototype that
  needs reset/refund or repeated charged work.
- [x] Settle the deliberate suffix-failure precedence, controller result mapping,
  certificate scope, exact interfaces, capacities and reset behavior here. Have
  independent correctness and Ponytail reviewers challenge both proof and scope;
  obtain user sign-off on the semantic difference before production changes.
- [x] Calibrate and freeze the measurement protocol below before candidate timing.
  Record a go/no-go decision here: useful avoided work in both blocked scenes,
  bounded proof/ownership, passing fallback accounting, and an actionable small
  lower-stack interface. Diagnostic code is not production completion.

**Decision:** Technical feasibility passed. The original 5% calibration rule
was missed by two cases, but that does not reject the feature. Reconcile and
freeze the existing before-implementation evidence under the amended measurement
approach below; retain every original capture and its variability. Do not start
runtime work with an unsettled benchmark setup.

**Exit:** Reviewed concrete proof, accounting and interfaces with reproducible
work-count evidence. Source feasibility alone does not complete Phase 0. If every
useful proof needs full discovery or general frontier machinery, stop with a
no-change decision and return the evidence for a separate scope review.

## Phase 1 - Implement the minimum geometric capability, if required

Complete in GridForge commit `4a6b1bd`. Independent correctness/Ponytail review
and scoped re-review pass. One cleanup-test assertion gap was corrected and
backed by a failing cleanup mutation test before completion.

- [x] Write failing GridForge completeness, original-parameter, ordering, stale
  and limit tests for the frozen interface; run them before implementation.
- [x] Add the smallest capability using current topology/prism/interval math and
  caller-owned scratch. Every emitted prefix must carry its actual completeness
  and world/run stamp. No navigation semantics belong upstream.
- [x] Verify all generic trace regressions still pass, including far disjoint
  unrepresentable geometry and rounded collinear endpoint peers. Cover reset,
  terminal reuse, exact retained bytes and warmed allocation.
- [x] Run focused then full GridForge Release/Lean checks and independent review.
  Record why no new API was necessary instead if existing primitives suffice.

**Exit:** Exact, bounded, independently tested geometry contract, with the
unchanged eager APIs intact. This is not yet a Navigator performance improvement.

## Phase 2 - Integrate the certified direct-only decision

Implemented and independently reviewed. Paired direct-service/controller tests
failed against complete suffix tracing and pass with the new integration.
Fallback, freshness, cleanup and exact local coverage pass. Candidate containing-
frame performance and the final cross-stack/release gates remain separate.

- [x] Write failing paired complete-ray/certificate behavior tests and controller
  state assertions. Use a separate expectation for intentional unseen-suffix
  differences; never change generic-ray tests to conceal a contract change.
- [x] Implement the internal proof and full-authority continuation using Phase 0's
  single-meter design. Keep the same lock/lease and reuse existing semantic/body
  primitives. Integrate both initial and periodic Navigator checks together.
- [x] Complete the acceptance matrix, including fallback budget parity,
  publication races, held actions, restore, and both LOS cadence modes.
- [x] Verify exact coverage and warmed allocations; run independent correctness,
  lifetime/accounting and Ponytail reviews. Remove prototype scaffolding or
  dead branches; leave no parallel public navigation story.

**Exit:** No false obstruction/visibility; the only controller behavior differences
are the explicitly approved suffix-precedence cases; generic consumers unchanged.

## Phase 3 - Measure containing frames and make the retain/withdraw decision

Complete. All four candidate repeats and both unchanged post-candidate controls
are retained. Independent raw-data audit confirms the comparisons. The retention
ruling below explicitly accepts two narrow target misses under the user's amended
experimental-judgment instruction; neither is relabeled a pass.

- [x] Run the frozen protocol below serially with executed-child archives and
  raw observations. Report work avoided and certificate/complete-authority counts
  for initial fixture queries only, not a game-wide eligibility rate. Containing
  frames include inconclusive attempts and full continuation.
- [x] Check isolated early/late/open/short and ineligible controls, then complete
  guided frames in original and balanced cadence. Verify exact replay within
  each contract and baseline/candidate replay where no semantic difference is
  exercised. Do not waive movement/guide validation to meet a timing threshold.
- [x] Measure initial checks and simultaneous invalidation/recovery separately.
  Keep acquisition cost visible even if visibility becomes cheaper. No pooled
  ordinary-frame percentile may conceal a recovery burst.
- [x] Record the retain/withdraw judgment against every target and mandatory
  correctness gate. The amended execution rule supersedes the original automatic
  withdrawal rule; retain the measured benefit with the two explicit exceptions.
  Inconclusive differences are not passes or grounds for an endless timing loop.

**Exit:** Independently audited, repeatable containing-frame benefit or a bounded
no-change decision. Neither outcome automatically closes `TRB-Benchmark-005`.

## Phase 4 - Documentation and cross-stack closeout

The coordinated local production matrix, final closeout review and documentation
checks are complete. Released-package and Windows/Linux CI validation remain
separate coordinated release gates.

- [x] Run full GridForge, Gravitas and Trailblazer Release/Lean builds/tests,
  both library targets, exact coverage, replay and allocation gates against
  the coordinated source stack. Include changed lower libraries if necessary.
- [x] Document observable direct-decision behavior in evergreen guidance/XML,
  complete documentation gates and independent final review.
- [x] Consolidate the implemented outcome here before moving the plan to `done`.
  Keep `TRB-Benchmark-005` open unless the full 2 ms/recovery criteria are met
  or the user approves a changed goal.

**Exit:** One documented runtime story and complete local evidence. Coordinated
Windows/Linux released-package gates and releases remain under the overview.

## Acceptance matrix

| Area | Required observable tests |
| --- | --- |
| Original geometry | Reverse direction, same-point/short ray, off-center endpoints, artificial prefix at an open portal, exact corner/edge/tangency and one-raw-unit penetration; full-domain denominators and unrepresentable geometry. |
| Complete alternatives | Blocked plus passable overlapping map; equal-entry and transitive ties; unmapped bridge; later seam unlocking an earlier record; explicit multi-leg corridor. Never certify from one bad alternative. |
| Eligibility | Dense/sparse, rectangular and both hex orientations, zero/positive radius and height limits, Solid/Gas/Liquid, step/drop and capabilities. Proved cases may certify; unproved cases must retain full authority, not falsely block. |
| Cost and endpoints | Neutral clear ray, expensive target/area/connection and same-medium target gating; preserve canonical cost-neutral success selection and exact heading, no snapping or actions crossed as ordinary motion. |
| Failure priority | Early certificate plus unobserved suffix stale/overflow/query cap/scratch cap; separately inject the same failures into proof work and final validation. Pin the approved difference and reject stale certificates. |
| Dependency absence | Negative page without requested-medium component; map removal/dormancy; changed/equivalent policy; add a passable overlapping map while obstacle page is unchanged; world mutation/grid-slot reuse/reset during proof. An unrelated publication with obstacle and guide dependencies unchanged must use budget-safe fallback, not manufacture stale guidance. |
| Accounting | Zero, exact, one-below and one-above limits per category; failed-debit atomicity; unsupported/inconclusive fallback; no second direct budget; independent ordinary acquisition budget. |
| Controller lifecycle | Initial/guided/direct checks, both algorithms, `CanPathfind=false`, guide disposal/retention, zero heading/retry frame/events, off-phase mandatory checks, repeated calls, missed slots, stop/restart/clock lifetime and JSON/MemoryPack restore. |
| Ownership and unchanged consumers | Serialized concurrent immediate calls, terminal/fallback reset and disposal, no warmed allocation; generic endpoint/simplification/Flow rejoin/action rays retain exact statuses, costs, budgets and dependencies. |

Useful existing counterexamples are `OrderedRay_ShouldRevisitAnEarlierCanonicalRecordUnlockedByALaterSeamSource`
and `OrderedRay_ShouldPreserveTheTransitiveTieGroupAcrossAnUnmappedBridge` in
`NavigationRayTests.cs`, plus `DisjointUnrepresentableCandidate_ShouldStillFailAfterAnEarlierIntersection`
and `HorizontalTrace_ShouldRetainRoundedCollinearEndpointIntervals` in GridForge's
`GridPlanarTraceIntervalTests.cs`. They are constraints to preserve, not tests to delete.

## Measurement protocol and acceptance

### Amended execution rule after user review

Preserve the existing pre-change captures and frozen source/runtime snapshot.
The original guided-frame benchmark and runtime source are unchanged by this
branch's setup work; new full-host controls supplement rather than replace it.
Any benchmark-code, runtime-option or workload change must happen before
implementation and receive a new baseline. No such change is assumed necessary
merely because measurements vary.

Use all four completed `calibration-r1-*` captures as the current eight-case,
3/10/9 before-implementation baseline, including both missed calibration cases.
Do not relabel the old 5% check as passed. Repeat exactly the same cases,
cadences, job settings, budgets and analyzer for the candidate. After candidate
captures, rerun the frozen unchanged baseline as a drift control. Do not compare
different workload identities or quietly change JIT/tiering options.

The first candidate capture also establishes its generated benchmark-host hash
using these same formal 3/10/9 settings. Use the capture helper's existing
preflight switch only to bootstrap that unknown host hash; all source, runtime
library, pinned-tool, case-count, replay and allocation checks still apply.
Preserve its bootstrap manifest, pin the observed host hash for subsequent
captures, and retain this first capture as candidate repeat one. Do not infer
the formal host hash from a differently configured quick run or discard this
capture based on its timings.

For the main Flow/500 improvement, compare both candidate repeats against the
fastest baseline capture median within that same cadence, including the
post-candidate control. For every case/cadence's non-regression check, compare
each candidate repeat's block median and frame P95 against the lowest matching
baseline block median and frame P95 respectively, across the two pre-change
captures and the post-candidate control. Do not pool cases, cadences or stages.
These are deliberately conservative references; display every baseline and
candidate value, not just the reference. Retain the 10% improvement target and
10% non-regression checks below, using these references instead of the original
pair labels. Differences comparable to baseline variability
are inconclusive, not speedups or automatic proof of regression. Investigate
material uncertainty with a focused check; do not endlessly rerun until green.
The implementation/retention decision belongs to the evidence and user review,
not to a baseline-only calibration veto. Correctness, budgets, deterministic
replay and allocation requirements remain mandatory.

### Original calibration protocol and retained targets

The following calibration was executed before implementation and is preserved
as history. Its one-correction stop rule no longer closes the feature; the
amended rule above governs the resumed experiment.

The preceding experiment had meaningful same-binary drift. Before candidate
timings, run two identical-baseline captures per cadence on the same host with
**3 launches, 10 warmup blocks and 9 actual blocks per case/launch**. Use the
existing 64-frame operations and 32-frame preparation. Require per-case block
median agreement within 5% and observed frame P95 within 10%, and inspect raw
per-launch sequences. More warmup alone is not evidence of stationarity.

If calibration fails, allow one documented correction of environmental load or
protocol, then repeat calibration from scratch before any candidate comparison.
If it still fails, stop timing-based acceptance and report the evidence limitation;
do not disable runtime optimization, erase early measurements or keep rerunning
until a favorable pair appears. Retain the historical short protocol as history,
not as a warmed throughput baseline.

After calibration, freeze commands, inputs and thresholds. Capture B1/C1/C2/B2
within **each** cadence with identical settings, in serial without competing
builds/tests/profiles. Required existing cases are A*/100, Flow/100 and Flow/500;
do not describe that selection as including A*/500. Add separately named clear,
short, late-blocked and ineligible full-host controls during Phase 0, preserving
the original benchmark identities and semantics. Freeze their scene definitions
before candidate timing; include at least one eligible non-fixture-specific scene.

Retain only with:

- At least **10% lower Flow/500 complete block median in both repeats of both
  cadences**, to justify a structural feature rather than another micro-optimization.
- No selected case/control worsening more than **10%** in complete block median
  or observed frame P95 in either pair. All fallback work is included.
- No newly failing 31.25 ms full-frame gate on a previously passing selected
  workload. Report every maximum and count over 2 ms / 31.25 ms, with ordinary,
  periodic and mandatory recovery work distinguishable.
- Exact behavioral/replay, zero warmed allocation, bounded retained bytes and
  full coverage gates. Preserve every actual observation and successful timed
  and diagnostic exit; missing diagnostics are unknown, not zero.

Freeze executed children, dependencies, generated runners, source revisions/diffs
and raw logs/JSON. Verify hashes and independently recompute the comparisons.
Use existing analysis scripts; a new telemetry or CI project is not warranted.
These are local headless acceptance criteria, not universal frame-time promises
or competitor comparisons. Do not increase the 2 ms goal or reduce LOS frequency.

Use the [benchmark README](../../../tests/Trailblazer.Benchmarks/README.md) for
commands, analysis and executed-child archiving. Phase 0 must record the exact
frozen commands and artifact locations here, including inherited local-stack
properties for generated sibling benchmarks; do not infer them from outer builds.

## Frozen full-host controls

The reviewed `NavigationDirectVisibilityFrameBenchmarks` adds exactly five
FlowField controls per cadence; the original A*/100, Flow/100 and Flow/500
remain unchanged. This tests the shared direct-ray path without an unnecessary
algorithm-by-scene Cartesian product. Methods are `Simulate64FramesUnphased`
and `Simulate64FramesBalancedPhases`, with interval 16 and null phase or stable
agent index modulo 16. All use unit rectangular single-layer geometry, foot
Y=-1/2, radius 1/8, height 1, zero root offset/step/drop, arrival radius 1/4,
permissive zero-cost Solid cells, `TrekRate.Slow`, and 32 preparation plus
64 measured frames at 32 Hz.

| Case | Agents | Grid | Start X/Z for agent i | Destination X/Z | Obstruction |
| --- | ---: | --- | --- | --- | --- |
| Clear100 | 100 | Dense 256x192 | `(2+2*i, 2+i)` | `(254,128)` | None; actual direct success required |
| Short100 | 100 | Dense 32x32 | `(2+2*(i%10), 2+2*(i/10))` | Start + `(4,2)` | None; actual direct success required |
| Late100 | 100 | Dense 256x192 | Same as Clear100 | `(254,128)` | Semantic wall x=250 below z=189 |
| Ineligible100 | 100 | Same as Late100 | Same as Clear100 | `(254,128)` | Same wall plus distant unbound 1-cell grid at `(10000,10000)` |
| Sparse500 | 500 | Sparse 256x192 | `(4+i%25, 4+2*(i/25))` | `(253,185)` | Physical wall x=48 below z=189 |

Sparse physical storage is the clipped union of staging x=2..30/z=2..44,
the band `abs((z-2)*251-(x-2)*183) <= 4*251`, west x=2..4/east x=251..253
connectors, and top z=189..191 connector, minus the wall. It has 4,798 physical
cells and uses default-cell navigation authoring, not a dense semantic copy.
Its per-map dynamic-slot cap is 8,192; the existing global 16,384 remains.
Other controls retain settings defaults. Workspace capacities use the full
grid dimensions; query limits are lookup 10,240,000, endpoint 16, expanded nodes
width*length, evaluated edges `max(320000,32*width*length)`, trace 160,000,
covered intervals 320,000, simplification rays 1,024, and zero transition work.

Setup must finish publication/materialization before admission. All ten
case/cadence combinations pass setup plus two complete reset/replay cycles in
both Release and Lean, with zero measured allocation/GC masks and exact
N mandatory / 4N periodic rays per measured block. Clear/short keep moving in
direct mode with zero guide leases; negatives keep exactly N Flow leases.
Every frame validates fresh physical/semantic committed cells and query intent;
each separate 32-frame block must move. Independent code/evidence review passes.
Sparse publication needs 15 additional untimed maintenance frames. These smoke
results do not substitute for the formal warmed timing/allocation gates.

## Concrete production contract

Independent correctness and Ponytail reviews found no blocker in this bounded
contract, arithmetic proof and full-host controls. Both GridForge's capability
and Trailblazer's integration are implemented and verified below.

GridForge adds one synchronous `GridTracer.TryTraceRectangularSlabsInto`:

```csharp
public static bool TryTraceRectangularSlabsInto(
    GridWorld world, Vector3d start, Vector3d end,
    in GridCoveredAddressGeneration expectedGeneration,
    SwiftList<GridTraceInterval> results, GridTraceIntervalScratch scratch,
    Span<int> intervalSourceOrdinals, Func<GridTraceSlab, bool> continueTrace,
    int gridCandidateLimit, int addressCandidateLimit, int outputLimit,
    long candidateWorkLimit, out GridTraceIntervalReport report);
```

- Eligibility is exactly one active physical grid matching the expected
  generation/configuration/sequence, rectangular single-layer geometry, nonzero
  representable original X/Z deltas, and valid original extreme prisms/planar
  slab arithmetic. Ineligible returns `false` before charged discovery, clears
  output, and invokes no observer; the caller ignores its report and uses the
  complete authority. After qualification, all terminal outcomes return `true`.
- The existing optimized rectangular collector emits complete X slabs, in
  ascending X, over the original full Y/Z bounds and segment. Admission retains
  the original full candidate-range charge. Do not build the whole address list
  first. Each interval is mapped once before constructing the next slab.
- New immutable `GridTraceSlab` carries `XIndex`, `IntervalStart`,
  `IntervalCount`, `SeparatesEndpoints`, `WorldSpawnToken`,
  `WorldChangeSequence` and `GridCoveredAddressGeneration Generation`.
  Its constructor is internal; it holds no live grid. Separation uses actual
  opposing prism faces, checked endpoint differences, and exact
  `CompareProducts(gap, One, positiveDeltaX, MinIncrement) > 0` on both sides.
  This conservative one-raw-parameter margin excludes rounded endpoint peers.
- Returning `false` from the observer produces the appended
  `GridTraceIntervalStatus.StoppedAfterCompleteSlab`, not `Complete` or a
  navigation result. Output remains in emission order; tie count and continuous
  coverage flags are zero/false. The old eager method never returns this status.
- Continuation uses the current canonical heap sort, carrying one caller-owned
  integer source ordinal through every move, then unchanged tie/coverage
  finalization. The supplied span must fit `outputLimit`. Trailblazer permutes
  retained mapping records in place from these tags, without remapping or a
  second record array. Failed traces clear output; observer exceptions clear
  output/used tags, release locks and propagate.
- The observer runs under the existing world read lock. XML forbids world/grid
  reentry and mutation of results/scratch. A small scratch active-use guard
  rejects same-scratch reentry/`Clear` before destructive clearing. `finally`
  releases the guard and all temporary grid/address references. No cursor,
  retained lock, general frontier or host classification callback is added.
  That read lock does not freeze obstacle changes: capture the original world
  stamp before generation qualification (or capture/check them together under
  `ChangeSyncRoot`). Never adopt a later stamp that legitimizes a mutation
  between qualification and an empty-slab certificate.

Trailblazer keeps this internal and direct-only:

- `NavigationRayWork.AdvanceDirect(NavigationImmediateRayWorkspace)` uses a
  shared extracted `MapOne` also called by ordinary `MapIntervals`. Append
  internal `NavigationRayStatus.CertifiedObstruction`; generic evaluation never
  creates it. Completed work retains its terminal status until `Begin`/`Reset`,
  including a repeated call through either internal entry point; the service
  resets its immediate work before another operation. Do not add a second mode
  flag solely to change that terminal-status echo. Keep the distinction through
  proof and freshness validation;
  only the direct-heading service translates a valid certificate to its existing
  `Blocked` result. That service already uses `Blocked` for cost-ineligible
  direct travel, rather than promising a complete generic-ray result. Its
  existing initial/periodic steering branches implement the decision table
  above unchanged. Paired ray tests retain the certificate distinction, and
  controller tests verify the resulting lifecycle behavior. This simplification
  avoids a redundant controller outcome and preserves the frozen benchmark's
  existing heading-service assertions without editing its workload or harness.
- Only the immediate workspace gains `int[traceCapacity]`, one cached observer
  delegate and transient proof fields. Generic A*/Flow ray workspaces do not.
  Clear transient references/tags on all terminal, fallback and exception paths.
- Require Solid, no endpoint allowance/chain restriction, exactly one current
  materialized map, no automatic seam pairs and no explicit source owners.
  Native rectangular edges change X by at most one: an entirely unadmitted,
  complete strict slab separates all possible seed/finish records. Passable
  expensive cells remain admitted; cost does not establish an obstruction.
- Snapshot original ceilings before observation. With
  `N = min(traceCapacity, remainingTraceIntervals)`, require lookup headroom
  `1L + N`, page capacity `min(N, instance.PhysicalPageCount)`, and component
  capacity `min(N, singleMapSolidComponentRecordCount)`. These are computed
  immutable metadata counts, not new stored counters or per-ray scans. A present
  mapped slot belongs to the captured physical pages even when it lacks the
  requested medium; graph identity protects missing-slot/alternative absence.
- Preserve post-report geometric charges and debit each mapping lookup once.
  Do not recompute discovery ceilings after those debits. A deferred semantic
  failure disarms certification; full geometry finishes and retains ordinary
  failure priority before that mapping failure. Completed fallback has exact
  category equality; failed partially observed attempts may have already paid
  mapping work. No reset, refund or extra direct budget is allowed.
- Every slab maps once and checks the graph/world fence. If any record is
  admitted, the slab is not strict, or proof is disarmed, continue without
  accumulated dependency validation. Only an empty strict candidate runs that
  validation, then checks the graph/world fence again. Graph identity-only
  change means continuation, not manufactured stale. Commit the certificate
  only after the final fence; the outer trace world-sequence check still applies.
  Ordinary full completion keeps its existing final dependency validation.

## Measured outcome

The four candidate captures and two unchanged post-candidate baseline controls
are complete. Independent raw-data/provenance audit confirms the figures below;
the retention judgment accepts the two named exceptions, not a blanket pass.
Each capture uses the frozen eight cases and 3/10/9 protocol. Every candidate
replay matches its baseline case/cadence, and all measured frames have zero
recorded allocation and collection overlap. Each variant's runtime remained
frozen; workload, compiler and JIT options were identical across the comparison.

Independent raw recomputation validates all 4,800 stage records, 240 successful
child exits and 138,240 actual frames. It reconciles every summary with raw logs,
all exact signed-Int64 replay values, 606 baseline and 613 candidate frozen
inputs, three pinned tools, 630 child-file hashes and 670 inventory entries.
The audit preserves every launch/block/frame and the known baseline and candidate
variability; serial capture timestamps are not proof of an otherwise idle host.

For each case/cadence, references are independently the lowest block median and
frame P95 across Before 1, Before 2 and Post-control. Rounded table values are
for readability; target checks use full precision. A block contains 64 complete
moving frames, not 64 isolated ray calls.

- Original Flow/500: **459.831 ms** reference versus **347.445 / 304.583 ms**,
  a **24.44% / 33.76%** reduction.
- Balanced Flow/500: **454.282 ms** versus **328.391 / 330.299 ms**,
  a **27.71% / 27.29%** reduction.
- A*/100 and Flow/100 also improve in both repeats/cadences. The separate
  sparse and late-obstacle scenes improve; this is not only one authored wall.
- Two strict 10% non-regression targets miss: Original Clear100 candidate 1
  block median **+10.1025%** (repeat 2 **+5.4458%**), and Balanced Short100
  candidate 1 frame P95 **+10.8732%** (repeat 2 **+3.8421%**). These are not
  rounded into passes. Other block-median/P95 comparisons remain within 10%.
  Clear, short and ineligible controls are not advertised as speedups.
- No selected candidate case newly fails the 31.25 ms observed-frame limit.
  Original Flow/500, Sparse500 and Late100 now stay below it in both repeats;
  clear and ineligible controls still exceed it. Balanced Flow/500 still has
  every measured frame above 2 ms; the broader benchmark remains open.

### Original cadence

Values are `64-frame block median ms / observed frame P95 ms`.

| Case | Before 1 | Before 2 | Post-control | Candidate 1 | Candidate 2 |
| --- | ---: | ---: | ---: | ---: | ---: |
| A*/100 | 212.725 / 20.429 | 198.689 / 19.775 | 211.459 / 20.769 | 152.596 / 5.925 | 149.035 / 5.817 |
| Flow/100 | 88.211 / 9.914 | 85.941 / 9.576 | 90.708 / 10.219 | 62.695 / 4.285 | 59.422 / 4.156 |
| Flow/500 | 469.215 / 46.913 | 459.831 / 45.546 | 500.175 / 49.639 | 347.445 / 20.716 | 304.583 / 19.291 |
| Clear100 | 2271.680 / 552.488 | 2183.358 / 532.605 | 2276.499 / 554.425 | 2403.932 / 583.473 | 2302.259 / 562.659 |
| Short100 | 101.968 / 16.103 | 100.576 / 15.781 | 103.033 / 16.264 | 105.430 / 16.692 | 101.078 / 16.214 |
| Late100 | 648.567 / 21.088 | 622.441 / 20.486 | 673.601 / 21.779 | 112.046 / 17.079 | 105.045 / 16.524 |
| Ineligible100 | 650.986 / 21.109 | 625.622 / 20.444 | 657.071 / 21.332 | 680.911 / 22.439 | 639.063 / 21.651 |
| Sparse500 | 991.390 / 155.548 | 950.473 / 150.335 | 1017.511 / 160.762 | 430.539 / 18.718 | 404.477 / 18.117 |

Values are `maximum ms; frames >2 ms; frames >31.25 ms`. Each capture contains 1,728 actual frames per row.

| Case | Before 1 | Before 2 | Post-control | Candidate 1 | Candidate 2 |
| --- | ---: | ---: | ---: | ---: | ---: |
| A*/100 | 22.725; 1421; 0 | 20.619; 543; 0 | 24.541; 1427; 0 | 7.694; 1445; 0 | 8.449; 1554; 0 |
| Flow/100 | 12.184; 108; 0 | 12.402; 108; 0 | 13.917; 108; 0 | 5.784; 109; 0 | 4.735; 108; 0 |
| Flow/500 | 65.031; 1728; 108 | 61.724; 1728; 108 | 70.717; 1728; 108 | 26.852; 1728; 0 | 20.907; 1728; 0 |
| Clear100 | 595.440; 108; 108 | 562.376; 108; 108 | 611.177; 108; 108 | 624.450; 108; 108 | 597.372; 108; 108 |
| Short100 | 17.581; 108; 0 | 17.549; 108; 0 | 19.099; 108; 0 | 18.916; 108; 0 | 17.595; 108; 0 |
| Late100 | 562.827; 108; 27 | 538.048; 108; 27 | 656.062; 109; 27 | 20.354; 108; 0 | 17.762; 108; 0 |
| Ineligible100 | 558.595; 108; 27 | 553.149; 108; 27 | 573.513; 108; 27 | 592.319; 109; 27 | 554.943; 108; 27 |
| Sparse500 | 167.244; 1728; 108 | 163.482; 1728; 108 | 176.297; 1728; 108 | 22.068; 1728; 0 | 19.858; 1728; 0 |

### Balanced cadence

Values are `64-frame block median ms / observed frame P95 ms`.

| Case | Before 1 | Before 2 | Post-control | Candidate 1 | Candidate 2 |
| --- | ---: | ---: | ---: | ---: | ---: |
| A*/100 | 201.840 / 3.504 | 195.407 / 3.394 | 216.497 / 4.193 | 149.885 / 2.494 | 145.717 / 2.474 |
| Flow/100 | 89.192 / 1.542 | 86.849 / 1.470 | 95.521 / 1.899 | 61.300 / 1.056 | 61.883 / 1.047 |
| Flow/500 | 465.197 / 7.808 | 454.282 / 7.452 | 506.034 / 9.019 | 328.391 / 5.592 | 330.299 / 5.752 |
| Clear100 | 2308.204 / 40.981 | 2215.237 / 39.247 | 2380.733 / 42.867 | 2381.831 / 43.035 | 2264.967 / 40.544 |
| Short100 | 105.690 / 1.800 | 103.795 / 1.824 | 106.305 / 1.883 | 108.998 / 1.995 | 106.892 / 1.869 |
| Late100 | 265.949 / 31.324 | 250.521 / 29.659 | 271.734 / 32.013 | 114.960 / 2.372 | 107.271 / 1.892 |
| Ineligible100 | 265.833 / 31.120 | 252.750 / 29.882 | 276.525 / 31.846 | 275.675 / 31.723 | 263.462 / 30.866 |
| Sparse500 | 1002.965 / 16.383 | 961.560 / 15.651 | 1050.001 / 17.996 | 421.680 / 7.306 | 415.686 / 6.945 |

Values are `maximum ms; frames >2 ms; frames >31.25 ms`. Each capture contains 1,728 actual frames per row.

| Case | Before 1 | Before 2 | Post-control | Candidate 1 | Candidate 2 |
| --- | ---: | ---: | ---: | ---: | ---: |
| A*/100 | 5.446; 1728; 0 | 5.003; 1728; 0 | 5.327; 1728; 0 | 4.571; 1728; 0 | 4.619; 1728; 0 |
| Flow/100 | 2.099; 2; 0 | 1.989; 0; 0 | 3.199; 69; 0 | 1.174; 0; 0 | 1.478; 0; 0 |
| Flow/500 | 9.294; 1728; 0 | 8.115; 1728; 0 | 11.202; 1728; 0 | 6.055; 1728; 0 | 6.769; 1728; 0 |
| Clear100 | 43.376; 1728; 1728 | 41.386; 1728; 1541 | 47.431; 1728; 1728 | 45.912; 1728; 1728 | 43.692; 1728; 1687 |
| Short100 | 2.166; 1; 0 | 2.195; 6; 0 | 2.575; 42; 0 | 3.355; 85; 0 | 2.611; 30; 0 |
| Late100 | 41.886; 915; 95 | 38.912; 577; 54 | 42.670; 956; 105 | 4.413; 195; 0 | 2.482; 27; 0 |
| Ineligible100 | 41.391; 977; 79 | 39.587; 619; 55 | 43.715; 1113; 108 | 43.831; 1295; 101 | 43.668; 960; 73 |
| Sparse500 | 17.688; 1728; 0 | 17.022; 1728; 0 | 20.484; 1728; 0 | 9.419; 1728; 0 | 8.168; 1728; 0 |

### Initial-query production work counts

A separate ignored diagnostic loads the archived candidate child and invokes the
unchanged compiled fixtures' setup/cleanup. Each of the 1,600 original initial
queries runs the production complete ray and direct entry sequentially through
the same service workspace, with reset between them. All eight actually loaded
archive assemblies match their saved hashes. This is deterministic counter
evidence, not another timing run or a recreated navigation/geometry algorithm.

| Case | Certificates / queries | Trace intervals complete / direct | Lookup probes saved | Covered-address admission |
| --- | ---: | ---: | ---: | --- |
| A*/100 | 100 / 100 | 13,060 / 5,664 | 7,396 | Unchanged |
| Flow/100 | 100 / 100 | 13,060 / 5,664 | 7,396 | Unchanged |
| Flow/500 | 500 / 500 | 57,424 / 24,934 | 32,490 | Unchanged |
| Clear100 | 0 / 100 | 23,050 / 23,050 | 0 | Unchanged |
| Short100 | 0 / 100 | 700 / 700 | 0 | Unchanged |
| Late100 | 100 / 100 | 23,050 / 22,450 | 600 | Unchanged |
| Ineligible100 | 0 / 100 | 23,050 / 23,050 | 0 | Unchanged |
| Sparse500 | 500 / 500 | 200,616 / 15,085 | 185,531 | Unchanged |

Every paired service-equivalent outcome matches. Non-certificate queries use
complete authority, including ineligible eager fallback; that bucket is not an
eligibility rate. These initial-query counts do not represent every periodic
frame or a game-wide hit rate. Trace/lookup counters do not expose GridForge's
per-prism candidate work, so no such count is inferred. Late100 also avoids
canonical sorting and chain evaluation on certification: suffix mapping alone
does not describe all avoided work.

### Mandatory lifecycle work, measured separately

One unchanged `guided-phase-smoke` diagnostic per frozen variant checks 40
lifecycle records. Ray counts, live-guide counts, zero-heading retries and
publication outcomes match exactly. These single-run observations are not
statistical speedup claims and are not pooled with warmed formal frames.

| Scenario | Baseline ms | Candidate ms | Baseline / candidate allocated bytes |
| --- | ---: | ---: | ---: |
| Simultaneous initial requests | 501.308 | 513.823 | 772560 / 772560 |
| Overdue resume | 19.190 | 12.932 | 0 / 0 |
| Same-map rebake and ordered reacquisition | 315.892 | 307.078 | 1105152 / 1105152 |
| Pending retry/reacquisition | 253.742 | 256.072 | 771568 / 771568 |
| Profile replacement admission | 274.193 | 265.237 | 771568 / 771568 |
| End-to-end JSON restore preparation | 229.707 | 233.574 | 19597080 / 19645048 |
| First admission after restore | 21.867 | 12.958 | 21600 / 21600 |

JSON restore preparation recorded 47,968 additional bytes in the candidate's
single observation; this is disclosed without assigning an unproven cause.
Required acquisition/recovery remains expensive. No zero-allocation promise is
made for setup, population or cold guide acquisition. The extra bounded immediate
scratch is one integer per configured trace slot, plus a cached observer and
value-only transient state; it is context-owned, not allocated per agent/check.

### Retention judgment and remaining work

**Retain the implementation for user branch review.** Every Flow/500 repeat
exceeds the 10% improvement target against the fastest matching baseline, with
gains in both cadences and additional blocked workloads. Source correctness,
exact replay, bounded accounting, zero warmed allocations and exact local
coverage pass. The original calibration failures remain historical evidence,
not a veto on an implemented and measured feature.

This is an explicit exception to the two narrow 10% non-regression targets,
under the amended user instruction to exercise engineering judgment. Clear100
C1 and phased Short100 C1 are not passes, and their cause is not established.
Both repeats, all baseline controls and full-precision differences remain
visible. Baseline/candidate variability does not prove the overhead is harmless
or environmental. The cost of retaining this candidate is possible clear/fallback
overhead and uncertain short-path tail cost; the user can reject that tradeoff
when reviewing the branch.

`TRB-Benchmark-005` stays open. Balanced Flow/500 remains above the 2 ms allowance,
and mandatory acquisition/recovery remains expensive. A bounded source review
identified two candidates for a subsequent measured slice: avoid a world-sequence
lock on slabs already known unable to certify, while preserving both real-proof
fences and final authority checks; and investigate whether the shared slab
collector repeats invariant setup that could be hoisted. Neither hypothesis
proves the observed control differences. No such unmeasured edit is mixed into
this candidate. Further work needs its own before/after evidence, not another
open-ended profiling loop or revived withdrawn shortcut.

### Material execution rulings

1. Preserve all prior baselines and use unchanged post-candidate controls rather
   than the premature calibration-only veto. Cost if wrong: small uncertain
   benefits remain unproven; no variability is hidden or called a speedup.
2. Keep `CertifiedObstruction` internal and translate it only at the direct
   service boundary to the existing `Blocked` decision. Reuse steering behavior.
   Cost if wrong: stale/failure priority could be lost; paired ray/controller
   tests and independent review explicitly cover that boundary.
3. Preserve the workspace's existing terminal-result echo until `Begin`/`Reset`
   instead of adding a mode flag for mixed entry calls. Generic evaluation never
   creates a certificate. Cost if wrong: a future mixed-entry caller could read
   the previous result; ownership must start/reset each request correctly.
4. Include Gravitas's core project with its two executable hosts after the
   `netstandard2.1` output demonstrably selected released rather than local math.
   Cost if wrong: local dependency selection changes; both families, actual
   hashes and unchanged normal-package outputs were verified. No version or
   physics change was needed.
5. Bootstrap the candidate host hash using the unchanged helper's preflight
   switch at formal 3/10/9 settings, retaining that run as Original C1. Cost if
   wrong: an incorrect host pin would invalidate evidence; both manifests and
   every actual runtime/child hash were verified.
6. Retain with the two explicit target misses above. Cost if wrong: real fallback
   or tail overhead is accepted. Keep the controls and parent benchmark open;
   do not claim blanket non-regression or release readiness.

## Execution record

**Final closeout (2026-09-16):** Independent whole-branch correctness/Ponytail
review and the bounded measurement/Gravitas addendum have no actionable findings.
The independent measurement auditor confirms the raw observations and both target
misses; retention remains the explicit parent judgment above. Fresh full
Release/ReleaseLean solution tests pass on the final unchanged runtime: GridForge
981/981, Gravitas 3,950/3,895, Trailblazer core 2,658/2,586 and adapter 65/65,
with no failures or skipped tests. All eight exact coverage summaries were
rechecked, and all 32 saved local-stack provenance entries still match after
testing. The final Trailblazer DocFX build has zero warnings/errors; 62 local
documentation path/heading links and `git diff --check` pass. Earlier both-TFM
build and GridForge DocFX results remain valid for unchanged production source.
Raw final logs are `final-<repository>-<configuration>-tests.log` and
`final-docfx.log` under the retained artifact root. No merge, push, package
publication or cross-platform release validation was performed.

**Trailblazer implementation (2026-09-15):** Added the direct-only completed-slab
proof and shared interval mapper, with bounded in-place canonical continuation.
Only the immediate workspace gains the configured ordinal buffer and cached
observer. Generic evaluation remains complete; steering production code and the
frozen benchmark are unchanged. The service alone maps a validated certificate
to its existing blocked direct-travel decision. The two wiki corrections explain
observable stale/retry behavior and the existing scheduled-acquisition API.

The new 82-case suite covers strict margins, dense/sparse and reverse routes,
cost-neutral/expensive continuation, unsupported topology/media/connections,
exact and one-below meter/scratch/headroom limits, graph/world mutation,
unobserved-suffix differences, controller ownership and warmed zero allocations.
An exception regression caught retained request/dependency state before the
reset-and-rethrow correction. A cached-Flow recovery discrepancy was reproduced
against the original generic entry and pinned as its existing two-heading retry;
no steering workaround was introduced. Publication is not artificially injected
between the two final fence instructions: their order is source-reviewed, while
real mutation before observation and after the final observer is tested.

Full core tests pass 2,658 Release / 2,586 ReleaseLean; adapter tests pass 65 in
each. Both configurations have exact core coverage of 30,949 lines, 12,215
branches and 3,029 fully covered methods; adapter coverage is 186 lines, 28
branches and 11 methods. Both TFMs build without warnings/errors. All eight
core/adapter math and GridForge copies match the recorded local-stack hashes.
Independent correctness/Ponytail review found no actionable issue. Raw tests,
coverage, red controls and provenance remain under
`artifacts/direct-visibility-phase0/task-2/`. These are local Windows results,
not candidate timing or released-package/Linux validation.

**Additional cross-stack prerequisite:** Gravitas `GRV-Issue-071` corrects the
same local-stack math-resolution defect fixed earlier in Trailblazer and
GridForge, including its core `netstandard2.1` output. Three project files now
exclude competing package compile/runtime assets only in local mode. No physics
code or dependency version changed: live NuGet feeds confirmed the current LSF
pins already match the latest stable releases.

Both local configurations build both TFMs without warnings/errors. Full tests
pass 3,950 Release / 3,895 Lean; each has exact coverage of 56,091 lines, 15,906
branches and 5,340 fully covered methods. Parent rehashing verifies all twenty
final math/GridForge provenance entries. Normal package builds also pass and
retain byte-identical dependency/output selections. Local outputs were restored
afterward and match the coverage-tested hashes. This certifies the direct
benchmark parent, not generated Gravitas benchmark children; no physics timing
claim is made. Evidence remains in
`artifacts/direct-visibility-phase0/gravitas-host-check/`.

**GridForge implementation (2026-09-15, `4a6b1bd`):** Added the completed-slab API, original
world-stamp capture and shared geometry/admission/sort helpers. Existing eager
tracing retains its full-authority behavior. Release and ReleaseLean each pass
981 tests, with exact coverage of 9,100 lines, 3,949 branches and 1,132 methods;
both target frameworks build without warnings/errors. Actual local-stack math
hashes and runtime dependency manifests were checked before tests. DocFX is
warning-free. These are local Windows results, not a performance claim.

Behavioral tests first failed against an unsupported-only stub. Adversarial
tests cover strict raw-parameter margins, reversed and sparse traces, complete
source-ordinal association, original admission limits, world mutation, reentry,
exceptions and zero warmed allocations. Self-review caught a new-path accounting
error when snapped topology bounds reached a grid outside world discovery;
a zero-budget regression failed first, and the corrected path now completes
with zero charges and no observer. Independent review then found that a cleanup
assertion helper cleared scratch before inspecting it. Moving the assertions
first caused ten expected failures when automatic cleanup was removed; restoring
cleanup passed them. No production test hooks or coverage exclusions were added.
Full evidence is retained under GridForge `artifacts/direct-visibility/task1/`,
with final review fixes under `review-fix1/`. The frozen benchmark is unchanged.

**Resumed baseline freeze (2026-09-15):** Independent reconciliation confirms
the pre-branch `edge-ray-baseline-*` captures used the same original guided
fixture and unchanged runtime source, with the same measured FixedMathSharp
binary. They used 3/1/3 sampling; retain them as historical context, not pooled
samples for the current 3/10/9 setup. The current four `calibration-r1-*`
captures and `artifacts/dv-baseline/snapshot-manifest.json` are the frozen
pre-implementation reference for all eight cases per cadence. The parent
rehashed all 606 frozen inputs; independent audit rehashed 96 historical/current
primary binaries and reconciled 103,680 shared-case frame checksums without
mismatch. No benchmark source, workload, runtime option or analyzer change is
needed for the resumed comparison. Baseline setup is settled before runtime
implementation; the known variation remains visible rather than erased.

### Condensed baseline and calibration history

The original pre-branch guided fixture and measured math binary were unchanged.
Earlier `edge-ray-baseline-*` captures used 3/1/3 sampling; they are historical
context, not pooled with the frozen 3/10/9 comparison. Independent reconciliation
matched 96 historical/current primary binaries and 103,680 shared-case frame
checksums. No new baseline setup was needed when implementation resumed.

The final baseline is Trailblazer `e6391ee`, GridForge `be85b56`, FixedMathSharp
`29bfa28`, SwiftCollections `4b5715b`, and Gravitas `42af506`. Its detached
source/binary snapshot at `artifacts/dv-baseline` contains 606 immutable inputs.
The four preserved `calibration-r1-*` captures contain all eight cases per
cadence, with 480 stage records, 24 successful child exits and zero actual-frame
allocation each. The tables above retain every baseline observation.

Calibration missed its former 5% block-median agreement rule in two cases:
Original A*/100 **212.7254 / 198.6892 ms** (-6.5983%) and Balanced Late100
**265.9488 / 250.5213 ms** (-5.8009%). The other 14 median checks and all 16 P95
checks passed. This establishes variability, not its cause. The user rejected
the premature calibration-only no-go; implementation and unchanged before/after
measurement superseded it without erasing the observations or claiming the old
rule passed.

One earlier verifier correction concerned the generated benchmark-host hash:
1/1/1 preflight and formal 3/10/9 jobs embed different deterministic PDB paths
and module IDs. Independent inspection found identical bodies for all 657
methods and only 77 changed metadata/debug bytes. The formal baseline host is
`7DAAD4B43D77964B65A3BCCA326257209AA926C980C23E59846524E1AA99D17B`.
The rejected run and its preflight manifest remain preserved; neither its timing
nor a favorable subset replaced a formal result. Candidate repeat one instead
bootstraps its unknown host under the actual formal settings and remains part
of the comparison.

Before baseline freeze, the five new controls received adequate edge budgets
for their existing geometry and an 8,192-slot sparse dynamic capacity. Applied
publication receipts required bounded maintenance catch-up. These were fixture
corrections, not runtime fixes; the original guided fixture was untouched.
The completed-slabs feasibility spike then proved real partial execution,
full-authority continuation, raw endpoint margins, ties and stale fences.
Production tests independently cover the implemented code rather than treating
that ignored prototype as runtime evidence.

Local dependency prerequisites are separately recorded as `TRB-Issue-135`,
`GF-Issue-009` and `GRV-Issue-071`. The first two were committed before baseline
freeze. The Gravitas correction affects validation asset selection, not the
measured Trailblazer benchmark runtime. No dependency version was invented or
released. Generated standalone GridForge/Gravitas benchmark children are not
certified by their direct-parent fixes; this experiment uses Trailblazer's
separately verified prebuilt-child route.

### Reproduction and evidence

All raw evidence remains ignored under `artifacts/direct-visibility-phase0`:

- `calibration-r1-{original,balanced}-{1,2}`: preserved pre-implementation runs;
- `candidate-{original,balanced}-{1,2}`: implemented candidate repeats;
- `baseline-post-{original,balanced}`: unchanged drift controls;
- each label's `.log`, `-command.json`, `-summary.json` and `-children` archive;
- `comparison-original.json` / `comparison-balanced.json`: all five matching
  observations per case, independent baseline minima and target calculations;
- `baseline-lifecycle-smoke.log` / `candidate-lifecycle-smoke.log`: separate
  mandatory lifecycle diagnostics;
- `production-work-counts/production-work-counts-1.json`: paired initial-query
  counters, actual loaded assembly identities and service-equivalent outcomes;
- `task-2` and `gravitas-host-check`: exact tests, coverage and provenance;
- GridForge's `artifacts/direct-visibility/task1/review-fix1`: final upstream gates.

The unchanged capture command is:

```powershell
& artifacts/direct-visibility-phase0/capture-final.ps1 `
  -RepositoryRoot <frozen-checkout> -InputManifest <frozen-manifest> `
  -Label <unique-label> -Mode <Original-or-BalancedPhases>
```

Defaults are 3 launches, 10 warmup and 9 actual blocks. Candidate Original-1
uses the existing `-Preflight` switch only to discover its formal host hash;
its immutable `bootstrap-manifest.json` remains separate. Later candidate runs
pin `83A2ED4858574791EC1714BA2AA96F4A75AED833A570C687C1AD233B59D17D57`.
The candidate at `artifacts/dv-candidate` freezes Trailblazer `c70f51b`,
GridForge `4a6b1bd` and 613 inputs. All captures retain the same seven runtime
library identities within their variant and the same three original pinned
tools. Formal sequence: Original C1, Balanced C1, Balanced C2, Original C2,
Original post-baseline, Balanced post-baseline. No concurrent build, test or
profile ran during those captures.

- [x] Phase 0 baseline reconciliation complete under the amended rule.
- [x] Phases 1 and 2 implemented and independently reviewed.
- [x] Phase 3 final independent audit and retention disposition recorded.
- [x] Phase 4 closeout, final review and parent/release status reconciled.
