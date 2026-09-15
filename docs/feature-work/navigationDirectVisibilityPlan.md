# Certified Navigator Direct-Visibility Rejection Implementation Plan

> **For agentic workers:** Use subagent-driven development or executing-plans
> task-by-task, with independent correctness and Ponytail review at each phase
> exit. Keep decisions and condensed evidence here, not in another plan.

**Created:** 2026-09-15

**Status:** Draft for review. Source feasibility investigation complete;
implementation and all phase exits remain unverified. The user requested this
investigation and plan, not a runtime cutover in this documentation pass.

**Source baseline:** Trailblazer `5f44495`; GridForge `02aa2de`, both on
`develop`. Record fresh sibling revisions and executed binaries before a probe.

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
Phase 0 must resolve its explicit feasibility gates and freeze exact interfaces
here before any production integration. A failed gate stops the feature.

## Why investigate this

[`TRB-Benchmark-005`](benchmark-signal-hardening-backlog.md#trb-benchmark-005---guided-frames-spend-most-time-in-long-blocked-segment-tracing)
remains open. The prior measured-frame Flow/500 profile attributes about **45%**
of sampled controller time to direct-heading checks. This is historical local
attribution, not a new timing run or a prediction of recoverable time. The
body-interior experiment remains withdrawn; its mixed results do not justify
reviving it. The 2 ms navigation allowance and mandatory recovery bursts are
not solved by this proposal.

The present pipeline is important:

1. `TrailblazerGuideService.TryGetDirectHeading` leases the graph and serialized
   immediate workspace, then starts one query meter.
2. `NavigationRayWork.Trace` eagerly calls `GridTracer.TraceIntervalsInto` for
   the **complete centerline**, not a swept-body stream.
3. `MapIntervals` maps all results and records dependencies before `EvaluateChain`
   can reject travel. Later portal/prism checks certify radius/height clearance;
   volume traversal has additional body certification.
4. Success must be cost-neutral and pass final dependency/heading validation.

A nearby decisive obstruction therefore does not currently spare far-segment
candidate discovery, prism/interval evaluation, sorting or mapping. That is a
real structural opportunity. Whether a cheap, complete negative proof can spare
enough of it is **not established** by source inspection.

### Approaches considered

| Approach | Decision |
| --- | --- |
| Cast shorter rays, or stop at the first blocked interval | Reject. Artificial endpoints change body/portal validity and fixed-point interval peers; another representation or explicit corridor may remain passable. |
| Bounded negative certificate using original-segment geometry | Investigate first. A strict separating obstruction may suffice; otherwise a complete bounded prefix must rule out every continuation. Unsupported cases use the full authority. |
| General ordered, resumable tracing plus a persistent reachable frontier | Defer. It adds cross-grid ordering, tie closure, state and lifetime machinery. Require a separate scope review if the narrow proof cannot work without it. |

## Global constraints

Follow [AGENTS.md](../../AGENTS.md) for coding, tests, exact coverage, documentation,
issue tracking and worktree ownership, and the [overview](feature-work-overview.md)
for coordinated release gates. This documentation pass authorizes no git or
release operations. Feature-specific constraints:

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

- [ ] Capture current source identities and the unchanged complete-ray outcomes,
  meter categories, dependency evidence and controller state table. Read the
  [benchmark README](../../tests/Trailblazer.Benchmarks/README.md),
  [publication](../wiki/MapPublication.md) and [serialization](../wiki/Serialization.md)
  boundaries before touching state or lifecycle.
- [ ] Build one bounded, ignored diagnostic against the complete ray. Record
  actual candidate visits/prism evaluations, interval output/sorting and map
  work before/after the earliest **certifiable** obstruction. Separate admitted
  range counts from work actually performed; do not claim a saving from the
  first blocked voxel's index. Include the existing guided wall and a separate
  long oblique/sparse blocked scene plus open/late-blocked controls.
- [ ] Prove original-parameter discovery completeness and the narrow eligibility
  envelope with literal adversarial examples from the acceptance matrix.
  Prefer a strict separator; try a closed bounded prefix only if necessary.
- [ ] Demonstrate the fallback budget rule on clear/inconclusive and exact-budget
  inputs, including unrelated publication during proof. Reject a prototype that
  needs reset/refund or repeated charged work.
- [ ] Settle the deliberate suffix-failure precedence, controller result mapping,
  certificate scope, exact interfaces, capacities and reset behavior here. Have
  independent correctness and Ponytail reviewers challenge both proof and scope;
  obtain user sign-off on the semantic difference before production changes.
- [ ] Calibrate and freeze the measurement protocol below before candidate timing.
  Record a go/no-go decision here: useful avoided work in both blocked scenes,
  bounded proof/ownership, passing fallback accounting, and an actionable small
  lower-stack interface. Diagnostic code is not production completion.

**Exit:** Reviewed concrete proof, accounting and interfaces with reproducible
work-count evidence. Source feasibility alone does not complete Phase 0. If every
useful proof needs full discovery or general frontier machinery, stop with a
no-change decision and return the evidence for a separate scope review.

## Phase 1 - Implement the minimum geometric capability, if required

- [ ] Write failing GridForge completeness, original-parameter, ordering, stale
  and limit tests for the frozen interface; run them before implementation.
- [ ] Add the smallest capability using current topology/prism/interval math and
  caller-owned scratch. Every emitted prefix must carry its actual completeness
  and world/run stamp. No navigation semantics belong upstream.
- [ ] Verify all generic trace regressions still pass, including far disjoint
  unrepresentable geometry and rounded collinear endpoint peers. Cover reset,
  terminal reuse, exact retained bytes and warmed allocation.
- [ ] Run focused then full GridForge Release/Lean checks and independent review.
  Record why no new API was necessary instead if existing primitives suffice.

**Exit:** Exact, bounded, independently tested geometry contract, with the
unchanged eager APIs intact. This is not yet a Navigator performance improvement.

## Phase 2 - Integrate the certified direct-only decision

- [ ] Write failing paired complete-ray/certificate behavior tests and controller
  state assertions. Use a separate expectation for intentional unseen-suffix
  differences; never change generic-ray tests to conceal a contract change.
- [ ] Implement the internal proof and full-authority continuation using Phase 0's
  single-meter design. Keep the same lock/lease and reuse existing semantic/body
  primitives. Integrate both initial and periodic Navigator checks together.
- [ ] Complete the acceptance matrix, including fallback budget parity,
  publication races, held actions, restore, and both LOS cadence modes.
- [ ] Verify exact coverage and warmed allocations; run independent correctness,
  lifetime/accounting and Ponytail reviews. Remove prototype scaffolding or
  dead branches; leave no parallel public navigation story.

**Exit:** No false obstruction/visibility; the only controller behavior differences
are the explicitly approved suffix-precedence cases; generic consumers unchanged.

## Phase 3 - Measure containing frames and make the retain/withdraw decision

- [ ] Run the frozen protocol below serially with executed-child archives and
  raw observations. Report work avoided, eligibility/fallback rates and total
  cost including inconclusive attempts, not only successful certificates.
- [ ] Check isolated early/late/open/short and ineligible controls, then complete
  guided frames in original and balanced cadence. Verify exact replay within
  each contract and baseline/candidate replay where no semantic difference is
  exercised. Do not waive movement/guide validation to meet a timing threshold.
- [ ] Measure initial checks and simultaneous invalidation/recovery separately.
  Keep acquisition cost visible even if visibility becomes cheaper. No pooled
  ordinary-frame percentile may conceal a recovery burst.
- [ ] Retain only if all gates pass; otherwise withdraw the candidate and keep
  useful behavioral tests and the concise disposition. An inconclusive capture
  is not a pass and does not authorize an endless profiling loop.

**Exit:** Independently audited, repeatable containing-frame benefit or a bounded
no-change decision. Neither outcome automatically closes `TRB-Benchmark-005`.

## Phase 4 - Documentation and cross-stack closeout

- [ ] Run full GridForge, Gravitas and Trailblazer Release/Lean builds/tests,
  both library targets, exact coverage, replay and allocation gates against
  the coordinated source stack. Include changed lower libraries if necessary.
- [ ] Document any observable direct-decision change in evergreen guidance/XML,
  complete repository documentation gates and independent final review.
- [ ] Consolidate decisions/results here and mark/move the plan to `done` only
  after its required gates pass or a reviewed no-change closeout is recorded.
  Keep the parent signal open unless its full 2 ms/recovery criteria are met
  or the user separately approves a documented change to that goal.

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

Use the [benchmark README](../../tests/Trailblazer.Benchmarks/README.md) for
commands, analysis and executed-child archiving. Phase 0 must record the exact
frozen commands and artifact locations here, including inherited local-stack
properties for generated sibling benchmarks; do not infer them from outer builds.

## Execution record

- 2026-09-15: Read-only source investigation and independent geometry/semantics
  audits support a gated negative-only feasibility spike. No new runtime API,
  tests, benchmark capture or speedup is claimed.
- Independent correctness and Ponytail plan reviews completed. Review clarified
  budget-safe fallback after unrelated publication and removed speculative file
  scaffolding and duplicate guidance. Local file/anchor checks pass for all 48
  links across this plan and its two coordination documents; `git diff --check`
  passes. No builds or tests were run for this documentation-only change.
- [ ] Phase 0 exit recorded and reviewed.
- [ ] Phase 1 exit recorded and reviewed, or no-new-API decision substantiated.
- [ ] Phase 2 exit recorded and reviewed.
- [ ] Phase 3 retain/withdraw decision recorded and independently audited.
- [ ] Phase 4 closeout recorded; parent benchmark and release status reconciled.
