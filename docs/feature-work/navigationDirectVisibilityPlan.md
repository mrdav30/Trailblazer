# Certified Navigator Direct-Visibility Rejection Implementation Plan

> **For agentic workers:** Use subagent-driven development or executing-plans
> task-by-task, with independent correctness and Ponytail review at each phase
> exit. Keep decisions and condensed evidence here, not in another plan.

**Created:** 2026-09-15

**Status:** Approved for execution on `codex/certified-direct-visibility`,
branched from `develop` at `785b801`. Phase 0 is in progress; production
integration and all phase exits remain unverified.

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

Independent correctness and Ponytail reviews approve this bounded production
contract, arithmetic proof and full-host controls. Final-harness calibration
still precedes the Phase 0 exit; this approval is not a performance result.

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
  internal `NavigationRayStatus.CertifiedObstruction`; generic `Advance` never
  returns it. The direct service and initial/periodic steering paths retain the
  distinction and apply the existing decision table above.
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
- Every slab maps once. If any record is admitted, the slab is not strict, or
  proof is disarmed, continue immediately. Only an empty strict candidate runs
  accumulated dependency validation: graph/world fence, existing validation,
  then graph/world fence again. Graph identity-only change means continuation,
  not manufactured stale. Commit the certificate only after the final fence;
  the outer trace world-sequence check still applies. Ordinary full completion
  keeps its existing final dependency validation.

## Execution record

- Full-host control preflight caught a fixture-sizing limit before timing:
  the 256-by-192 late scene exhausted 320,000 evaluated edges after 13,545 of
  49,152 nodes; ordinary Flow acquisition returned `BudgetExceeded`, not a
  route/visibility defect. Both cadences and the matched ineligible scene agree.
  Before any capture, scale only the new controls' edge budget to
  `max(320000, 32 * width * length)` (1,572,864 for large controls), following
  the existing cold-guide benchmark's per-node sizing. All other budgets and
  the original primary fixture remain unchanged. Baseline and candidate must
  use this same frozen sizing. Sparse's initial 4,096 dynamic-slot cap also
  prevented its 4,798-cell baseline from materializing; its approved 8,192 cap
  resolves that fixture limit. All controls now pass both cadences in Release
  and Lean. Raw failed/successful smoke logs, parser red/green checks and
  expanded BDN discovery remain under `artifacts/direct-visibility-phase0/task-0c`.
- Before seeing any new-control timing results, freeze this protocol extension:
  after the reviewed five-control harness is committed, create an ignored
  baseline source/runtime snapshot and repeat identical-baseline calibration
  for all eight selected cases per cadence (original three plus five controls).
  The prior four passing captures remain pre-controls evidence; they do not
  calibrate the added cases or rebuilt benchmark host. Use the same 3/10/9
  protocol and 5% median / 10% P95 agreement gates. The one permitted correction
  is still unused and is not reset by this scope extension. Only then run fresh
  B1/C1/C2/B2 comparisons. This is a predeclared harness-scope extension, not a
  response to an unfavorable candidate result. Preserve the original method in
  unphased primary captures and pair it with the controls' explicit unphased
  method; use balanced-phase methods for both in the other cadence.
- Aligned validation prerequisite `TRB-Issue-135` is resolved in `a65c118`:
  local executable hosts now select the sibling math implementation instead of
  the competing released package asset. Full solution builds/tests pass with
  verified local hashes: Release 2,576 core / 65 adapter, Lean 2,504 / 65,
  both library targets, zero warnings/errors or failed/skipped tests. The
  `direct-visibility-aligned-release[lean].trx` files and
  `artifacts/direct-visibility-phase0/aligned-release[lean]-*.log` preserve the
  matrix. Independent correctness/minimality review approved the three-project
  fix; normal package selection and generated prebuilt children remain intact.
  This is a separate validation fix, not a visibility speedup.
- The aligned actual partial-execution spike passes 15 scenes. Guided wall
  discovery visits/maps 73 of 145 candidates; the separate sparse oblique wall
  visits/maps 53 of 436. Add two extreme-prism qualification checks to each.
  Full address admission remains 5,049 / 47,244; successful certificates perform
  no canonical sorting or chain evaluation. Late/reverse sparse walls require
  414 visits, so no direction-aware near-start saving is claimed. Full forced
  continuation matches all intervals/ties, mapped records, dependency sets,
  meter categories and successful selected cost/heading. Exact-budget eligible
  cases continue without remapping. Capacity-vs-semantic-failure priority and
  unrelated publication after dependency validation are exercised. Deliberate
  eager-only, remapping and missing-final-fence controls fail as intended.
- The final arithmetic addition passes 22 rows: 16 literal one-raw-margin
  combinations at both ends/directions, one-raw oblique slopes forward/reverse,
  exact horizontal bypass, unrepresentable deltas forward/reverse and two
  actual prism representation rejections. Half-raw peers round to endpoint
  parameters but cannot certify separation. Near-zero oblique controls genuinely
  succeed and map five intervals once; unsupported cases have zero observer
  charges before full authority. Three deliberate weakened-guard/eager-only
  controls fail. The 15-scene regression remains green afterward.
  Reproduce with `dotnet build artifacts/direct-visibility-phase0/probe/Phase0Probe.csproj
  -c Release`, then its `bin/Release/net8.0/Trailblazer.Tests.dll` with output
  path and `--partial` or `--arithmetic`. It uses frozen calibration-child
  references, not mutable parent outputs. Latest raw files are
  `artifacts/direct-visibility-phase0/partial-arithmetic-regression.json`
  (SHA256 `5EAE7FECA016381CB4F50CEEE61E312A52B91643665FC13B2BA1E20421CDC5A2`)
  and `arithmetic.json`
  (`9676E01C323AD6344D7243164D7366F6AD58B6986EFC667686D3DAC88C86041B`).
  Earlier output/binary snapshots and all red logs remain preserved. Reflection,
  diagnostic source copies and eight nullable warnings in linked benchmark
  support make this a feasibility harness, not production/allocation/timing
  evidence. Production tests must cover their own implementation and lifecycle.
- 2026-09-15: User approved the plan and its deliberate unseen-suffix contract,
  authorized implementation/checkpoint commits and requested independent reviews.
  Created `codex/certified-direct-visibility` from clean `develop` at `785b801`,
  using the existing checkout as requested. GridForge `02aa2de` and Gravitas
  `42af506` also start clean. Exact geometry/accounting interfaces remain a
  Phase 0 gate; approval does not broaden the fallback budget or tracing scope.
- Baseline `dotnet test Trailblazer.slnx -c Release -p:UseLocalLsfStack=true
  --logger 'trx;LogFileName=direct-visibility-baseline-release.trx'` passed:
  2,576 core and 65 adapter tests, no failures/skips. Both library targets built.
  TRX evidence is under each test project's ignored `TestResults` directory.
- The same baseline command in `ReleaseLean` with
  `direct-visibility-baseline-releaselean.trx` passed 2,504 core and 65 adapter
  tests, no failures/skips, with both library targets built.
- Binary audit qualifies those baseline tests: their host output contains
  released FixedMathSharp `010be577`, whereas the measured children contain
  sibling `29bfa28`. The core sources differ, so the baseline tests are not
  evidence for the entire current sibling stack. The ignored diagnostic is
  being aligned with the frozen child dependencies before its feasibility
  results are accepted; final local-stack verification must resolve this too.
- Initial ignored diagnostic completed 14 scenes and compared every replayed
  interval with the real complete trace. The guided wall admits 5,049 addresses
  but evaluates 145 actual candidates; the long sparse oblique wall admits
  47,244 but evaluates 436. Complete provisional empty X slabs leave 72 and
  383 candidate evaluations respectively. These are posthoc opportunities, not
  implemented savings. Raw predecessor evidence is retained at
  `artifacts/direct-visibility-phase0/phase0.json`, including its separate
  runtime fingerprint. The quarter-radius oblique open control already rejects
  at portal-pair clearance; the eighth-radius oblique and quarter-radius
  axis-aligned open controls succeed. Do not attribute every rejection to the
  authored wall or infer a geometry bug from that control alone.
- Independent source correctness and Ponytail reviews support a narrower
  prototype: exactly one matching materialized rectangular map/physical grid,
  Solid medium, no explicit connections or seams, and the existing optimized
  oblique X-slab path only. All unsupported cases retain the original ray.
  Retain mapped records plus one ordinal permutation for complete fallback;
  no cursor, general frontier, symmetric axis walker or second mapping store.
  Before interleaving mapping, metadata must prove lookup/page/component
  headroom. Provisional stale evidence disarms certification and finishes the
  full authority with its original failure precedence. Actual partial-work
  continuation, precise interfaces and adversarial proof remain open gates.
- Unchanged-binary calibration passed on its first protocol attempt. Each
  capture used three launches, ten warmup blocks and nine actual blocks per
  case/launch, 32 preparation frames and 64 measured frames per operation.
  All captures have 81 actual, 90 warmup and nine diagnostic records; actual
  frames allocate zero bytes. Runs were serial, without competing builds,
  tests or profiles. No calibration correction or discarded rerun was used.

  | Cadence / case | Block median 1 / 2 (ms per 64 frames) | Change | Frame P95 1 / 2 (ms) | Change |
  | --- | ---: | ---: | ---: | ---: |
  | Original A*/100 | 206.117 / 197.509 | -4.18% | 20.306 / 19.782 | -2.58% |
  | Original Flow/100 | 88.419 / 86.652 | -2.00% | 10.007 / 9.763 | -2.43% |
  | Original Flow/500 | 466.833 / 451.468 | -3.29% | 46.146 / 45.503 | -1.39% |
  | Balanced A*/100 | 201.425 / 201.400 | -0.01% | 3.483 / 3.567 | +2.42% |
  | Balanced Flow/100 | 87.426 / 87.861 | +0.50% | 1.477 / 1.502 | +1.68% |
  | Balanced Flow/500 | 458.425 / 455.170 | -0.71% | 7.532 / 7.853 | +4.27% |

  Exact captures use the existing ignored helper:
  `artifacts/los-phase/capture.ps1 -Label <label> -Mode <mode>
  -Launches 3 -Warmups 10 -Iterations 9`. Serial order was
  `visibility-calibration-original-1` (`Original`),
  `visibility-calibration-balanced-1` (`BalancedPhases`),
  `visibility-calibration-balanced-2` (`BalancedPhases`), then
  `visibility-calibration-original-2` (`Original`). Each label under
  `artifacts/los-phase` retains its command/source JSON, source diff, raw log,
  analyzer summary and `-children` archive/manifest. The command records contain
  exact method filters; the existing helper selects A*/100, Flow/100 and
  Flow/500, never A*/500. Generated children inherit
  `/p:UseLocalLsfStack=true /p:UsePrebuiltLocalLsfStack=true /m:1`.
  Trailblazer, GridForge, FixedMathSharp, SwiftCollections and benchmark assembly
  hashes match across all four archives. Independent audit reproduced the six
  comparisons, matched all 720 raw stage records to summaries and all 684
  warmup/actual measurements to BenchmarkDotNet JSON, verified 36 successful
  child exits, exact replay and 252 archived runtime-file hashes. Individual
  launches still show some settling: balanced capture 2 Flow/500 launch 1's
  early-three versus last-three block median falls 6.89%. Preserve that sequence;
  aggregate agreement is not proof that every launch is stationary. The helper
  checks launcher exit codes and its outer command returned zero; per-capture
  launcher exit is not separately persisted as a JSON field. No rerun was
  recommended. This calibration is not candidate performance evidence.
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
