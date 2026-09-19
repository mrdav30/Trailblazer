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
   - Pre-existing bugs, correctness risks, documentation defects, and issues
     outside the current feature should be triaged and verified independently.
     Regressions introduced by a feature branch belong in that feature's active
     plan, including their fixes and verification; they are not deferred here.

## Active Coordination

- Measured navigation performance
  - `TRB-Benchmark-005` remains the active performance signal in the
    [benchmark tracker](benchmark-signal-hardening-backlog.md). The implemented
    GridForge/FixedMathSharp geometry improvements and Trailblazer mapping,
    sampling and dependency-proof reuse have substantially reduced guided-frame
    costs while preserving deterministic replay, budgets and the default LOS
    cadence. The tracker retains the matched comparisons and their limitations;
    withdrawn experiments are not counted as shipped savings.
  - The 2026-09-19 lockstep-preserving scan, occupancy, and ray-cleanup follow-up
    from `6e4124a` is closed with no runtime change. Four implemented matched
    comparisons fail the same repeatability/improvement gate; useful ordering,
    lifetime, and exact-geometry tests and the full evidence remain in the tracker.
    The preceding accepted native-edge/node reuse is intact. This bounded local
    pass did not close the 2 ms allowance; larger work needs an explicit
    observation-order and ownership contract, not a silently changed frame model.
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
    [Certified Direct-Visibility implementation](done/navigationDirectVisibilityPlan.md)
    has been reviewed and merged into `develop` across the three repositories.
    Its bounded negative proof reduces Flow/500 block cost by **24.44–33.76%**
    in the original cadence and **27.29–27.71%** with balanced phases, against the
    fastest matching preserved baseline. All measured original Flow/500 frames
    now fit 31.25 ms.
    Two narrow 10% non-regression targets miss: clear-path median **+10.1025%**
    and phased short-path P95 **+10.8732%**, each in the first candidate repeat.
    Retention explicitly accepts those exceptions; their cause is not established.
    The archived plan owns all repeated observations, post-baseline controls,
    separate lifecycle diagnostics and independent audit. Local exact coverage,
    replay and warmed allocation gates pass across the changed source stack.
    Local dependency selection is corrected in `TRB-Issue-135`, `GF-Issue-009`
    and `GRV-Issue-071`; no dependency version or Gravitas physics change was needed.
    `TRB-Benchmark-005` retains the 2 ms/recovery limits and focused clear/fallback
    cost follow-ups; no release readiness or game-wide performance is claimed.
  - The subsequent per-slab fence refactor was implemented and measured, then
    withdrawn: no attributable complete-frame benefit was demonstrated. Useful
    publication/mutation tests remain; production source is unchanged. The tracker
    preserves the fresh controls, historical variability and five balanced P95
    target misses. The next boundary was addressed by the
    [Navigator Acquisition And Recovery plan](done/navigationRecoveryPlan.md),
    completed and squashed into `develop` as `f5d53a5` on 2026-09-18 after user
    review. Opt-in
    Navigator ownership uses the existing shared-work scheduler, with waiting,
    current-foot adoption, cancellation and independent guide ownership.
    Earlier source correctness/Ponytail reviews are approved; a refreshed review
    found a connector-only publication gap inside A* direction hooks. Its three
    RED cases now pass after a same-lock freshness correction, with independent
    re-review approval and 307 Release / 293 Lean focused cases passing. Independent evidence
    review accepts the measurements with concerns. Repeated same-build Flow/500
    cold peak medians fall
    **96.32% / 96.50%** with explicit B work limits, but cumulative host-frame
    cost rises **39.86% / 33.34%** and last-ready latency becomes 57 frames.
    Same-map rebake avoids a duplicate build and lowers that cumulative cost
    **27.03% / 30.15%**. A matched Flow-only larger allowance reaches 28 frames
    with higher peaks; runtime defaults are unchanged. Immediate guided-frame
    block medians initially increased **2.2–9.0%** across repeated controls;
    an earlier complete final-source comparison, including corrected occupancy
    ownership, leaves AStar/100 **1.4–1.6%**, Flow/100 **3.3–3.5%** and Flow/500
    **3.2–5.0%** slower than both untouched develop controls.
    A separate identical-correctness comparison places Flow medians between
    corrected controls and AStar below both, identifying no two-sided scheduling regression
    under equal occupancy behavior; it does not replace the total comparison.
    Slower thin-wrapper and lease-layout experiments were reverted. Native hardware watchpoints now attribute the exact
    retained 6,008-byte Release failure to background-GC counter bookkeeping,
    with no managed allocation in the measured loop. The shared allocation
    measurement is corrected without relaxing assertions; both full normal
    Windows configurations and exact coverage pass. A standalone runtime probe
    then reproduced successful-but-inactive no-GC admission. The corrected test
    setup drains pending collection and validates admission without retries or
    relaxed counters; fresh Windows/Linux Release/Lean suites, exact coverage
    and documentation checks all pass. The final timing sequence stopped before
    the candidate ran: unchanged develop threw inside interval sorting during
    A* setup. Twelve bounded native-debugger launches did not reproduce that
    retained, unresolved failure. A separate six-arm fixed-protocol comparison
    completed cleanly but still fails original total cost. A live-validated
    occupancy-ticket hint also failed its five-arm comparison and was removed;
    useful public occupancy tests remain and fresh v4 validation passes.
    Independent source review found no safe local deletion of the remaining
    callback/publication boundary checks. A retained Flow-entry experiment was
    also rejected: its small 100-agent gain did not extend to both 500-agent
    controls. Portable behavior tests remain. A narrower A* same-root dependency
    walk optimization retains both world checks and the live graph boundary.
    Its reviewed implementation passes focused Release/Lean checks, and the
    five-arm comparison improves AStar/100 **23.2–23.6%** against the parent and
    **21.3–22.0%** against the originals. It is retained; fresh full validation
    remains required. Flow/100 still costs **4.4–5.1%** more and Flow/500
    **3.1–5.7%** more than the original controls. A lazy group-steering reciprocal
    was rejected after both Flow populations were slower than both parent controls;
    its stronger signed-group tests remain. A public sparse-grid regression now
    reproduces an inherited edge-order mismatch: successful Flow guidance can
    point away from its destination when an authored neighbor is physically absent.
    The initial sampler-only correction was incomplete across physical publication.
    Reviewed structural numbering now aligns acquisition, sampling and constrained
    recovery rays; three public regressions and 973 Release / 968 Lean focused
    cases pass. Subsequent reuse of already computed Flow prisms improves the
    corrected parent by **1.4–3.7%**, with no added state or relaxed checks. The
    prism-reuse comparison retains **23.4–23.6%** AStar improvement, but
    Flow remains **2.8–3.2% slower** than the originals. Fresh Windows/Linux
    validation passes **3,046 Release / 2,968 Lean** core tests and 65 adapter
    tests per configuration, with exact 100% line/branch/full-method coverage
    and documentation checks green. Subsequent approved GridForge hardening
    moves occupancy tracking onto its owning world, fixing a reproduced
    strong-reference retention bug. Its five-arm timing is inconclusive: all
    candidate medians fall between drifting corrected-parent controls. It is
    retained for ownership correctness, not claimed as a speedup or performance
    equivalence; full costs and validation status remain in the recovery plan.
    On 2026-09-18 the user authorized Trailblazer feature-branch commits for review
    and indicated that modest cost for hardened behavior is acceptable. Recovery
    implementation is complete and fresh Release/Lean precommit suites pass;
    the user has now reviewed and approved the squash, and the plan is archived.
    This does
    not prove that every observed difference is caused by fixes or bounded by
    2–3%; the latest drifting original comparisons are wider and remain in the
    plan. The original-baseline sorting exception remains unexplained, without
    an established recovery-change cause or a claimed fix. Other historical
    guard failures are not individually attributed. `TRB-Benchmark-005` resumes
    from the hardened implementation and retains the broader frame-cost and
    useful-guidance-latency work. No push or release is authorized.
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

- [Certified Navigator Direct-Visibility Rejection](done/navigationDirectVisibilityPlan.md)
  - GridForge exposes bounded completed-slab observation; Trailblazer certifies
    only a strict obstruction and otherwise continues the complete authority.
    Existing Navigator lifecycle, public request and generic ray contracts remain
    intact. Both cadence comparisons show substantial blocked-workload benefit.
    The plan records the two explicit target exceptions, exact local coverage,
    independent reviews and source-stack provenance. The user has reviewed and
    merged the work; the broader benchmark and coordinated release gates stay open.

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

1. The completed [direct-visibility feature](done/navigationDirectVisibilityPlan.md)
   and its two explicit control-target exceptions have been reviewed and merged.
   The follow-up per-slab fence experiment is withdrawn after matched measurement;
   the invariant-setup idea remains unmeasured, not an established opportunity.
   [Navigator-owned acquisition/recovery](done/navigationRecoveryPlan.md) is
   complete and was squashed into `develop` as `f5d53a5`. Its bounded scheduling
   addresses acquisition/recovery spikes, not steady-state tracing costs.
   For the remaining `TRB-Benchmark-005` work, validation audits found no safe small
   synchronization deletion; the managed sample shares are not lock-cost estimates.
   Both turn-target reuse and the measured scan-distance early-rejection experiment
   were withdrawn. Binding-local reuse of one native Flow edge, its immutable
   node/prism geometry, and its exact payload lookup is now implemented, with
   unchanged freshness/body checks and complete work-meter behavior. The final
   interleaved comparison reduces Flow/500 blocks by **11.53%** and its median
   complete frame from **5.10 to 4.50 ms**, at **856 additional bytes per guide**.
   A*/100's measured +2.53% block / +1.91% P95 control remains explicit. See the
   [benchmark backlog](benchmark-signal-hardening-backlog.md) for matched evidence,
   mutation-tested boundaries, memory accounting, and independent reviews. This
   is retained progress, not closure: the **2 ms** Flow/500 allowance is still unmet.
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

Hardening, acquisition scheduling, LOS phase spreading and certified direct
visibility are complete locally and archived; release readiness is a separate
cross-stack checkpoint. Do not
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
