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
     or closed with a documented accepted outcome and retained limitations.
2. [`Issue Tracker`](issue-tracker.md)
   - Pre-existing bugs, correctness risks, documentation defects, and issues
     outside the current feature should be triaged and verified independently.
     Regressions introduced by a feature branch belong in that feature's active
     plan, including their fixes and verification; they are not deferred here.

## Active Coordination

- [First-Class 2D Navigation](twoDimensionalNavigationPlan.md)
  - Phase 0 contract review is complete. FixedMathSharp 8.0.0 and
    GridForge 10.0.0 ship exact planar capsule clearance and layer-bound body
    tracing. Pinned Gravitas source supplies native support queries and atomic
    2D posture replacement. The September contact and hex issues are resolved.
    Trailblazer now references GridForge 10.0.0 in both package families.
    Released-package validation of the adapter awaits Gravitas 1.1.0.
    [Phase 0 evidence](twoDimensionalNavigationPlan.md#2026-11-09---phase-0-contract-closeout)
  - Phase 1 publishes layer-bound top-down cells through the shared graph
    while keeping spatial queries separate. Strict circle placement and
    immediate A*/Flow routes cover the obstacle and fitted portal cases.
    Retain one graph, A*/Flow pipeline, and scheduler. The 2D controller uses
    native geometry and preserves the 3D frame, ownership, and replay
    guarantees. Hard physics-query budgets remain a separate 2D/3D workstream.
- [`Cross-Stack Issue Resolution`](issue-tracker.md)
  - Resolve cross-stack issues in dependency order: `FixedMathSharp`,
    `SwiftCollections`, `GridForge`, then Trailblazer. Package references are
    the default; use `UseLocalLsfStack=true` only for coordinated validation of
    unreleased sibling changes, then revalidate against released packages.
  - `TRB-Issue-119` is closed as
    unreproduced, with historical evidence, bounded verification and recurrence
    capture instructions retained. GridForge's `GF-Issue-006` and
    FixedMathSharp's `FMS-Issue-018` are likewise closed as unreproduced, not
    repaired; no shared cause is established. Reopen #119 on a matching
    recurrence and preserve exact inputs and executed-binary identities before
    rebuilding. `TRB-Issue-114` remains closed unless new evidence meets its
    documented reopening criteria.
- [`Benchmark Signal Hardening`](benchmark-signal-hardening-backlog.md)
  - Reproduce and close confirmed release-relevant signals alongside the owning
    library change. Do not broaden this into speculative optimization work.

## Recently Completed

- Shared Deterministic Simulation Timing (canonical plan in Chronicler:
  `docs/feature-work/done/deterministicSimulationTimingPlan.md`)
  - All seven phases are complete. Trailblazer and its Gravitas adapter use the
    shared long-frame/wide-time contract; `TRB-Issue-124`, the held-jump units
    defect `TRB-Issue-153`, and stale commit ordering `TRB-Issue-154` are resolved.
    The separate final-jump policy defect `TRB-Issue-155` is also resolved:
    new-jump admission no longer cancels the current held-input extension.
  - Cross-stack Windows/Linux Release/ReleaseLean suites pass, introduced timing
    code and downstream gates retain exact coverage, and all four API sites
    build cleanly. Independent review and isolated normal-package consumers
    pass with verified Standard/Lean dependencies and assembly provenance.
    The archived plan retains exact evidence and legacy Chronicler coverage
    gaps. Private fixture packages are not released-package proof; final
    versions, dependency floors and public release validation remain below.

- [Gravitas Kinematic Integration](done/gravitasIntegrationPlan.md)
  - All four phases are complete locally. The optional 3D adapter commits
    collision-accepted motion without a core-to-core dependency. Real-body
    stairs, ladders, portals/groups, airborne movement, wall/corner/slope
    controls, dynamic pushes, posture changes, platform replay, and
    restore/teardown are verified. The
    [joint movement record](done/gravitasIntegrationPlan.md#2026-09-20---joint-movement-hardening)
    retains the owning fixes and limits.
  - Standard/Lean packaging and evergreen integration/API documentation are in
    place. The behavioral tests serve as executable examples; the standalone
    sample and its runner were not retained after user review. Windows/Linux
    matrices retain exact 100% core/adapter coverage. Independent review and
    clean DocFX pass; historical private-consumer trace evidence remains recorded.
    [Phase 3 evidence](done/gravitasIntegrationPlan.md#2026-09-21---package-and-consumer-closeout)
    records isolated private-package provenance, not public release proof.
    Final dependency pins and released-chain verification remain in the
    coordinated release gates below. Native 2D integration stays in its own plan.

- [Guided-Frame Performance Campaign — TRB-Benchmark-005](benchmark-signal-hardening-backlog.md#trb-benchmark-005---guided-frames-spend-most-time-in-long-blocked-segment-tracing)
  - Closed on **2026-09-20 by explicit user acceptance** of a reasonable stopping
    point. The final paired Flow/500 comparison improves measured block cost by
    **7.28%**, reaching **4.39 ms median / 5.01 ms P95 / 5.53 ms maximum** for the
    complete headless navigation update of all 500 agents. Balanced LOS phases
    spread periodic checks; every agent still updates each 32 Hz simulation tick.
    These observations fit its **31.25 ms** tick budget on the tested host, not
    a whole-game or cross-platform guarantee.
  - The **2 ms navigation allowance is unmet and explicitly deferred**, not
    retroactively passed. It would require about **54%** more reduction from
    the final median. Earlier accepted geometry, mapping, native-edge reuse,
    LOS spreading, direct visibility and acquisition/recovery work remain intact.
    Withdrawn experiments are not counted as shipped savings.
  - The tracker and archived plans retain matched results, control exceptions,
    acquisition/recovery tradeoffs, allocation/replay proofs, exact coverage,
    independent reviews and source-stack provenance. Reopen for a representative
    target-hardware budget miss, a reproducible regression, or a concrete profiled
    opportunity with a justified benefit. Release verification remains separate.

- [Navigator Acquisition And Recovery](done/navigationRecoveryPlan.md)
  - Completed, user-reviewed and merged as `f5d53a5`. Opt-in Navigator ownership
    uses the existing bounded shared-work scheduler for acquisition and recovery.
    The archived plan retains lower peak-cost measurements alongside readiness
    latency, cumulative-work tradeoffs, correctness fixes and unresolved setup
    evidence. Its acceptance does not establish a universal timing guarantee.

- [Certified Navigator Direct-Visibility Rejection](done/navigationDirectVisibilityPlan.md)
  - GridForge exposes bounded completed-slab observation; Trailblazer certifies
    only a strict obstruction and otherwise continues the complete authority.
    Existing Navigator lifecycle, public request and generic ray contracts remain
    intact. Both cadence comparisons show substantial blocked-workload benefit.
    The plan records the two explicit target exceptions, exact local coverage,
    independent reviews and source-stack provenance. The user has reviewed and
    merged the work. The broader campaign is now closed as accepted above;
    its recorded exceptions and coordinated release gates remain unchanged.

- [Deterministic LOS Phase Spreading](done/navigationLineOfSightPhasePlan.md)
  - One opt-in host-assigned phase policy spreads A*/Flow periodic checks while
    preserving the default countdown and required admission/recovery boundaries.
    Both repeated relative performance gates pass, with about 81% lower Flow/500
    P95 and no observed balanced steady frame over 31.25 ms. Exact local coverage,
    serialization/replay, documentation/examples and independent reviews pass.
    The archived plan consolidates the measurements and remaining limits;
    those historical limits remain recorded under the accepted campaign closeout.

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
    optimization is verified; the broader campaign is now closed as accepted above.

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
    unchanged Flow control and measurement limits. Subsequent work and its
    accepted stopping point are recorded under `TRB-Benchmark-005`.

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

## Queued Feature Work

- [Bounded Physics Queries — 2D And 3D](physicsQueryBudgetPlan.md)
  - Scope captured; implementation has not started. Give both dimensions explicit
    query work/storage limits and complete-result semantics through the existing
    spatial indexes and native geometry. Includes support and posture-clearance
    consumers, potential GridForge/SwiftCollections prerequisites, and matched
    existing-query controls. Keep existing synchronous behavior intact; no
    scheduler, mixed-query expansion or new benchmark framework is implied.
  - Separate from native 2D delivery and not a prerequisite for its support/posture
    implementation. Review its contract and agree execution/release sequencing
    when taking it up. Scope capture assigns no release milestone and does not
    waive the existing coordinated-release policy.

## Deferred / Evidence-Gated

The unmet **2 ms** Flow/500 navigation allowance is a deferred stretch target,
not an active optimization assignment. The accepted closeout preserves the
existing benchmark and its limits; reopen only with the evidence described in
the tracker. Acquisition/recovery latency and total-work tradeoffs remain
documented, not certified for every host or silently waived.

Engine-host visual playtesting remains deferred until engine-specific adapter
work. Individual performance changes remain evidence-gated: the archived
scheduling plan records remaining cold-readiness limits and useful next
measurements, not permission for speculative algorithm or cache changes.

## Recommended Execution Order

The performance campaign is closed by user acceptance; the unmet 2 ms stretch
target is deferred. Keep its existing measurements and reopening criteria in
the [benchmark tracker](benchmark-signal-hardening-backlog.md#trb-benchmark-005---guided-frames-spend-most-time-in-long-blocked-segment-tracing).
The remaining feature sequence is:

1. Continue the 2D plan's Phase 1 with retained planar guides and accepted
   controller motion for the reviewed top-down fixtures. Capture matched 3D
   controls before changing shared runtime paths. Validate the adapter against
   the pinned source stack as its phase begins.
2. After every active feature-work plan is complete, release any changed
   dependencies before their consumers and repeat released-package validation at
   each layer. Do not publish an intermediate feature-work checkpoint.

The 3D Gravitas integration, navigation hardening, and separately approved
scheduling/performance work are complete locally. First-class 2D implementation
remains incomplete; drafting or reviewing its plan does not complete its
implementation checkboxes. Public release validation remains separate.
The shared physics query budget plan is queued separately; settle its execution
and release sequencing explicitly when taking it up. It does not gate native 2D
implementation or reopen TRB-Benchmark-005.
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

Hardening, acquisition scheduling, LOS phase spreading, certified direct
visibility, and 3D Gravitas integration are complete locally and archived;
release readiness is a separate cross-stack checkpoint. Do not infer these
gates passed from exact local coverage, private-package consumers, or benchmark
results.

- [ ] After all active feature work is complete, run Windows/Linux CI in
  Release and ReleaseLean against the exact intended release commit, including
  both library targets and exact reachable coverage.
- [ ] Release changed dependencies before their consumers, restore package-based
  references and repeat the full test, replay, allocation, coverage and relevant
  benchmark matrix against that final released chain. Select final upstream
  version pins containing the coordinated changes; private fixture packages
  bearing existing version numbers are not evidence for public packages.
- [ ] Revalidate docs/examples and package artifacts against that same chain;
  verify standard/Lean package dependencies and core-only isolation, retain
  combined core/adapter coverage gates; build both API assemblies with DocFX.
  Review remaining issue/benchmark dispositions before publishing Trailblazer.

These gates were transferred from the hardening/scheduling and 3D integration closeouts;
they remain pending, not waived. Do not release an intermediate checkpoint.
