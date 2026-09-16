# Feature Work Overview

## Purpose

This document is a living overview of Trailblazer feature work. It tracks the
active scope, recently completed work, and deferred or evidence-gated plans. It
is a curated view rather than a backlog of every possible feature.

## Coordination Trackers

Keep these trackers empty when possible, and promote broad work into dated plans
instead of burying it in notes.

1. [`Benchmark Signal Hardening Backlog`](benchmark-signal-hardening-backlog.md)
   - Measured allocation or runtime-cost signals must be reproduced, resolved,
     or closed with a documented no-change decision.
2. [`Issue Tracker`](issue-tracker.md)
   - Bugs, correctness risks, documentation defects, and feature-work-discovered
     issues should be triaged, tested, and committed independently from feature
     design plans.

## Active Coordination

- Measured navigation performance
  - `TRB-Benchmark-005` remains the active performance signal in the
    [benchmark tracker](benchmark-signal-hardening-backlog.md). The implemented
    GridForge/FixedMathSharp geometry improvements and Trailblazer mapping,
    sampling and dependency-proof reuse have substantially reduced guided-frame
    costs while preserving deterministic replay, budgets and the default LOS
    cadence. The tracker retains the matched comparisons and their limitations;
    withdrawn experiments are not counted as shipped savings.
  - The open-ended micro-optimization sweep has ended; the last page-proof
    experiment remains withdrawn. Its tests and the preceding dependency-key
    and byte-accounting corrections are retained in the historical tracker.
  - The completed
    [Deterministic LOS Phase Spreading plan](done/navigationLineOfSightPhasePlan.md)
    is archived, with all four phases implemented and verified on
    `codex/los-phase-spreading` from `develop`. The full local Release/Lean matrix
    passes with exact coverage. Opt-in host-assigned phases
    preserve required admission/freshness/retry boundaries; schema 6 defines
    enabled restore behavior. The narrow pre-existing zero-radius steering defect
    (`TRB-Issue-125`) is corrected and independently verified. Independent
    measurement audit confirms **80.84% / 81.47%** Flow/500 P95 reductions, with
    **+2.98% / -1.07%** block-cost changes. All selected balanced steady frames
    fit 31.25 ms; Flow/500 maxima are **10.15 / 10.23 ms**. The 2 ms allowance
    still misses, and required acquisition/recovery can still burst. These are
    local headless observations, not portable guarantees. Documentation and
    examples are verified. Final correctness/Ponytail reviews are complete,
    including the construction-order/restored-replay proof and shared test-fixture
    cleanup. The user reviewed and merged the branch into `develop`; the
    follow-up investigation now targets ordinary controller/steering costs.
    Spreading periodic work does not reduce each ray's cost; the existing cold
    acquisition scheduler does not own Navigator LOS checks.
  - The follow-up ungrouped steering scan now uses only the useful avoidance
    radius, preserving grouped and negative-radius behavior. Repeated matched
    captures reduce Flow/500 block cost by **10.42–13.94%** with the original
    cadence and **11.59–15.28%** with balanced phases. Smaller Flow workloads
    are mixed; the tracker retains all results. Replay, allocation and exact
    Release/Lean coverage gates pass. The 2 ms allowance and mandatory recovery
    bursts remain unresolved; the next investigation concerns ray/sampling work
    or explicitly reviewed acquisition ownership, not discarded validation.
  - The subsequent rectangular body-interior experiment (`GF-Benchmark-010`)
    is withdrawn: matched Flow/500 gains did not satisfy the smaller-case
    non-regression gate. GridForge retains useful boundary tests and benchmark
    invocation guidance (`GF-Issue-008`), not the runtime shortcut. The subsequent
    [Certified Direct-Visibility investigation](navigationDirectVisibilityPlan.md)
    is active again after the user corrected a premature Phase 0 closeout.
    The bounded negative-only proof and single-meter continuation passed review.
    Baseline drift of **5.80% / 6.60%** in two cases remains recorded, but is not
    evidence against an unbuilt feature. Reconcile/freeze the baseline before
    implementation, then compare the same benchmark and unchanged drift control.
    Local host dependency corrections (`TRB-Issue-135` / `GF-Issue-009`) and five
    useful full-host controls remain on the review branches. The active plan
    owns the proof, baseline evidence and amended practical measurement approach.
    `TRB-Benchmark-005` retains the remaining 2 ms/recovery limits.
  - Earlier `TRB-Benchmark-002/003/004` and `GF-Benchmark-003` slices are closed
    locally. `GF-Benchmark-005` remains a withdrawn per-edge experiment.
    `TRB-Issue-119`, `GF-Issue-006` and `FMS-Issue-018` retain their separate
    incomplete/failing benchmark evidence without an established shared cause;
    `TRB-Issue-114` remains closed without a confirmed allocator fix.
- [Gravitas Kinematic Integration](gravitasIntegrationPlan.md)
  - Phase 0 preflight fixed the optional 3D adapter/package boundary, shared-
    world frame and lifetime order, first bounded wall fixture, and GridForge-
    Unity observation host. Gravitas's working tree now declares and passes its
    full matrix against the aligned 7.1.0/9.1.0 stack without a new physics API.
    Phase 1 supplies the independently owned fixed-profile adapter without a
    core-to-core dependency. The profile/collider replacement slice of
    integration Phase 2 is now complete, including stand/crouch/compact cycles,
    blocked growth, platform-attached mutation, restore/replay, and a post-commit
    notification boundary that prevents cross-library split state. Integration
    Phase 2 remains in progress for its broader movement scenarios.
    `TRB-Issue-102` provides the explicit serialized support-normal contract used
    by the adapter.
    `TRB-Issue-103` is resolved in the coordinated working trees through
    configuration-stable, explicitly versioned local project graphs; clean
    local-source and package-backed matrices are green.
- [First-Class 2D Navigation](twoDimensionalNavigationPlan.md)
  - Draft plan for native top-down and side-view navigation/controllers, planar
    body/support semantics, and the later Gravitas `SolidBody2D` adapter
    extension. Contract review precedes implementation; fixed-height projection
    alone does not complete 2D support.
- [`Cross-Stack Issue Resolution`](issue-tracker.md)
  - Resolve cross-stack issues in dependency order: `FixedMathSharp`,
    `SwiftCollections`, `GridForge`, then Trailblazer. Package references are
    the default; use `UseLocalLsfStack=true` only for coordinated validation of
    unreleased sibling changes, then revalidate against released packages.
- [`Benchmark Signal Hardening`](benchmark-signal-hardening-backlog.md)
  - Reproduce and close confirmed release-relevant signals alongside the owning
    library change. Do not broaden this into speculative optimization work.

## Recently Completed

- [Deterministic LOS Phase Spreading](done/navigationLineOfSightPhasePlan.md)
  - One opt-in host-assigned phase policy spreads A*/Flow periodic checks while
    preserving the default countdown and required admission/recovery boundaries.
    Both repeated relative performance gates pass, with about 81% lower Flow/500
    P95 and no observed balanced steady frame over 31.25 ms. Exact local coverage,
    serialization/replay, documentation/examples and independent reviews pass.
    The archived plan consolidates the measurements and remaining limits;
    `TRB-Benchmark-005` stays open for the 2 ms allowance and mandatory bursts.

- [Shared Convex Containment Performance Slice](benchmark-signal-hardening-backlog.md#follow-up-ordinary-frame-control-and-shared-containment-work)
  - FixedMathSharp widens an already-world-space query point once instead of
    repeating its general transform for every polygon edge. Public behavior
    stays unchanged; unused internal offset/forwarding machinery is removed.
    Eight isolated benchmark cases improve by 26-37%; selected guided-block
    medians improve by about 10%. Executed-child archives and source-checksum
    audits strengthen the evidence; correlated frame variability and the
    still-unmet frame budgets remain explicit. FixedMathSharp, GridForge,
    Gravitas and Trailblazer pass the full local Release/ReleaseLean matrix
    with exact 100% line, branch and fully covered method coverage.

- [Cold-Request Protocol Isolation](benchmark-signal-hardening-backlog.md#trb-benchmark-004---tiny-cold-flow-protocol-has-an-iteration-position-excursion)
  - Retained and independently checked 312 warmup and 918 actual observations
    across five ordered captures. Excursions move into warmup, change in the
    tiering-disabled diagnostic, and return with the original configuration.
    Closed narrowly as measurement-protocol sensitivity, without a production
    patch, runtime-setting recommendation or exact JIT attribution.

- [First Guided-Frame Performance Slice](benchmark-signal-hardening-backlog.md#trb-benchmark-005---guided-frames-spend-most-time-in-long-blocked-segment-tracing)
  - Added a replay-checked headless guided-host benchmark and raw-frame analyzer.
    A small GridForge rejection removes expensive exact math for provably
    disjoint candidates, preserving contacts, budgets and failure order.
    Observed maxima fall from roughly 1.25 s / 1.22 s / 4.12 s to
    147 ms / 116 ms / 501 ms for A*/100, Flow/100 and Flow/500. The narrow
    optimization is verified; the broader guided-frame signal remains active.

- [Focused Traversal Performance Pass](benchmark-signal-hardening-backlog.md#trb-benchmark-003---remaining-traversal-cost-needs-focused-isolation)
  - Existing whole-body bounds reject disjoint prisms before exact sweep math;
    native evaluation reuses already-built prisms. Selected obstructed A* drops
    from 164.125 to 84.491 ms and warm Flow acquire/sample from 3.737 to 1.273 ms.
    Allocation, semantic/work and full-coverage gates remain intact. Prefix
    skipping is explicitly rejected because it would change observable budgets,
    dependencies and failure order. `TRB-Issue-118` records adjacent allocation-
    guard cleanup without claiming attribution of an intermittent allocation.

- [First Focused Performance Pass](benchmark-signal-hardening-backlog.md#trb-benchmark-002---avoidable-controller-allocations-and-a-epoch-reads)
  - Live grid geometry replaces repeated offline normalization; all 900 measured
    100-Navigator candidate frames allocate zero bytes versus 11,200 before.
    A* avoids locking the live-world epoch when no stamp or completed proof
    requires it. Cold A* median improvements are modest and recorded with the
    unchanged Flow control and measurement limits. Remaining performance work
    has its own active signal rather than being declared solved.

- [Navigation Hardening](done/navigationHardeningPlan.md)
  - Closed all three phases: headless vertical/airborne acceptance, safe runtime
    profile/collider replacement and cold acquisition measurement. The baseline
    measurement record is consolidated into the archived plan. Engine-host
    visual playtesting remains deferred to engine-specific adapter work.

- [Deterministic Guide Acquisition Scheduling](done/navigationSchedulingPlan.md)
  - Closed `TRB-Benchmark-001` with focused geometry/validation improvements,
    opt-in deterministic A*/Flow scheduling, bounded reused Flow cleanup and
    repeated containing-frame evidence. Warm and scheduled observed P99 fit the
    selected allowance; rare excursions and long cold FIFO latency remain
    documented limits, not a universal timing guarantee. Profiling and optimization
    evidence is consolidated into the archived plan.
- [`Grid Topology Navigation Map Refactor`](done/gridTopologyNavigationMapRefactorPlan.md)
  - Replaced the dense chart/partition lattice with immutable per-grid maps,
    deterministic overlays, and one topology-native navigation graph. The final
    hard cut removed the compatibility surface and archived the release evidence.
- [`Closest-transition lookup research`](done/closestTransitionLookupPlan.md)
  - Superseded by the unified map/graph transition query and cache model.
- [`Managed-transition lifecycle research`](done/managedTransitionLifecyclePlan.md)
  - Superseded by explicit transition definitions, rules, and overlay publication.

## Deferred / Evidence-Gated

Engine-host visual playtesting remains deferred until engine-specific adapter
work. Individual performance changes remain evidence-gated: the archived
scheduling plan records remaining cold-readiness limits and useful next
measurements, not permission for speculative algorithm or cache changes.

## Recommended Execution Order

The plans share evidence, not competing implementations:

1. Execute the [direct-visibility feature](navigationDirectVisibilityPlan.md)
   with baseline first, implementation second and matching measurements third.
   The user authorizes reasonable experimental judgment; timing variability
   requires honest comparison, not automatic feature abandonment. The merged
   LOS phase-spreading runtime remains the control.
   Exact rectangular tracing, raw-coordinate geometry and
   per-sample Flow proof reuse are already implemented. Preserve their arithmetic,
   accounting and dependency guarantees, and keep the unphased cadence/control
   unchanged. Do not restart the open-ended micro-optimization sweep or revive
   withdrawn experiments without new evidence. Retain launch variability without
   inventing a cause.
   `TRB-Benchmark-004` and `GF-Benchmark-003` are
   closed locally; `GF-Benchmark-005` closes with its measured no-change decision;
   do not repeat their investigations without new evidence. Keep the distinct
   `TRB-Issue-119` setup exception visible and capture exact inputs if it recurs.
   Keep GridForge's separate `GF-Issue-006` benchmark failure visible as well;
   clean replays do not resolve either exception. FMS's separate `FMS-Issue-018`
   containment failures also remain unexplained; failed microcaptures are not
   replacement performance baselines.
   `TRB-Issue-114` requires no further investigation unless new evidence meets
   its documented reopening criteria.
2. Complete the remaining shared movement scenarios in Gravitas integration
   Phase 2 on the reviewed replacement boundary.
3. Close the 3D adapter's package, documentation, and joint-verification gates.
   Reference the same deterministic scenario evidence from both owning plans.
4. Execute the dedicated 2D plan after its contract review, using the reviewed
   motion and replacement boundaries. Add the planar adapter extension there;
   neither the initial 3D slice nor hardening requires completed 2D support.
5. After every active feature-work plan is complete, release any changed
   dependencies before their consumers and repeat released-package validation at
   each layer. Do not publish an intermediate feature-work checkpoint.

Runtime implementation includes the Gravitas adapter and complete hardening
Phases 1 and 2. Phase 3 itself added measurement rather than a new runtime API;
its separately approved scheduling follow-up now adds explicit frame-spread
guide acquisition. The broader Gravitas integration Phase 2 and first-class 2D
work remain incomplete; drafting or reviewing a plan does not mark
runtime checkboxes complete.
The cross-stack rules apply throughout:

1. Keep the benchmark backlog and issue tracker as intake buckets; promote new
   measured risks into dated plans only when they are broader than a focused
   patch.
2. Resolve a cross-stack defect in the repository that owns the behavior.
3. Validate downstream consumers with `UseLocalLsfStack=true` when coordinated
   source changes are required.
4. Release dependencies before their consumers, then restore package-based
   validation at each layer.
5. Run Trailblazer `Release`, `ReleaseLean`, coverage, replay, allocation, and
   relevant benchmark gates against the released package chain.

## Coordinated Release Gates

Hardening, acquisition scheduling and LOS phase spreading are complete locally
and archived; release readiness is a separate cross-stack checkpoint. Do not
infer these gates passed from the plans' exact local coverage or benchmark results.

- [ ] After all active feature work is complete, run Windows/Linux CI in
  Release and ReleaseLean against the exact intended release commit, including
  both library targets and exact reachable coverage.
- [ ] Release changed dependencies before their consumers, restore package-based
  references and repeat the full test, replay, allocation, coverage and relevant
  benchmark matrix against that final released chain.
- [ ] Revalidate docs/examples and package artifacts against that same chain;
  review remaining issue/benchmark dispositions before publishing Trailblazer.

These gates were transferred from the completed hardening/scheduling closeout;
they remain pending, not waived. Do not release an intermediate checkpoint.
