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
  - `TRB-Benchmark-005` is the active priority in the
    [benchmark tracker](benchmark-signal-hardening-backlog.md). The new fixture
    measures real guided movement and periodic line-of-sight rechecks.
    GridForge now narrows eligible rectangular candidate geometry and uses
    exact slab intervals; FixedMathSharp removes general rotation/projection
    work from raw-coordinate predicates and skips unnecessary capsule solves.
    Trailblazer Flow sampling reuses per-call immutable dependency validation
    while retaining live-world and cache invalidation checks.
    Against the refreshed baseline, recheck medians fall from about
    106 / 88 / 355 ms to **28 / 19 / 90 ms** for A*/100, Flow/100 and Flow/500.
    Whole-block medians improve **58% / 72% / 67%**, with exact replay and zero
    frame allocations. APIs, budgets, dependencies and LOS cadence are unchanged.
    This is implemented source work, not another profiling-only checkpoint.
    A subsequent two-condition Trailblazer fast path skips structural membership
    lookup when both closure sets are empty. Its fresh before/after capture
    reduces recheck medians another **6.7% / 8.9% / 5.3%** to about
    **27 / 18 / 88 ms**, with no cache or changed validation contract.
    The next ray-cost cleanup reads effective semantic cells without rebuilding
    already-admitted node geometry. Original/candidate/control/confirmation
    captures do **not** establish an additional repeatable frame-time win;
    the initial apparent 2–3% gains are not counted. Map-local lookup reuse and
    early loop guards are withdrawn, and world-sequence synchronization stays
    intact. Overlay cost regression tests cover Solid, Gas and Liquid.
    A subsequent Flow-only change remembers a successful dependency proof for
    the bound lease's current graph version, while preserving every live cache
    and world check. Original/candidate/control/confirmation captures show
    **31-36% lower ordinary Flow-frame medians** and **6-10% lower LOS-recheck
    medians**. No old graph is retained, and pooled bindings clear the proof.
    This is a repeatable improvement, not a 2 ms tail guarantee: the final
    100-agent Flow capture still has 49/540 ordinary frames above that allowance.
    The tracker retains serial-run variability: unchanged A* ordinary medians
    move -3.3% then +3.4% across the latest two comparisons; no A* speedup is
    attributed to this Flow-only patch.
    The next coordinated slice avoids discarded node/prism construction during
    ray mapping, reuses evaluated source prisms and uses an exact .NET 8
    leading-zero operation in FixedMathSharp, retaining its unchanged
    `netstandard2.1` fallback. Repeated LOS medians improve another **9-11%**
    to **25.1 / 15.4 / 73.6 ms**. Ordinary Flow/100 improvement does not repeat;
    its tail remains above the 2 ms allowance. Exact replay, physical/semantic
    admission, dependencies, work budgets, world synchronization and cadence
    remain unchanged. A further GridForge heap-sort cleanup reduces interval
    payload copying, with no new API/cache and the same portable implementation
    on both targets. Repeated LOS medians improve another **5-8%** to
    **23.7 / 14.5 / 67.5 ms**. Short-protocol Flow/100 complete-block medians
    regress in both pairs; a separate ten-warmup/twenty-actual comparison improves
    its blocks **5.8%**, with ordinary medians near 0.79 ms on both versions.
    Both results are retained: this demonstrates protocol sensitivity, not a
    confirmed JIT cause or a uniform containing-frame win.
    The next GridForge change calculates only the coordinate used by rectangular
    candidate probes, preserving exact saturating arithmetic and all tracing
    contracts. Repeated LOS medians improve **6-11%** to **22.6 / 13.1 / 63.8 ms**,
    with lower complete-block medians in both pairs. Short Flow/100 ordinary
    tails worsen; a separate longer-warmed pair improves LOS/blocks **7% / 4%**
    with ordinary medians **3% slower**. These qualifications remain in the
    tracker; no ordinary-frame or universal timing guarantee is claimed.
    Full-frame acceptance remains incomplete: the latest candidate still has
    **0 / 0 / 36** of each case's 36 measured rechecks exceeding 31.25 ms.
    Cold-acquisition scheduling does not
    bound these synchronous checks. The original planar geometry cost is much
    smaller; remaining work must target the complete guided frame without
    weakening geometry, freshness checks or failure semantics.
    The separate per-edge rejection experiment (`GF-Benchmark-005`) remains
    withdrawn; its mixed results are not counted as implemented savings.
    The earlier performance slices (`TRB-Benchmark-002/003`, `GF-Benchmark-003`)
    are closed locally. `TRB-Benchmark-004` now closes with ordered protocol
    evidence and no runtime change; short cold-cache controls are not warmed
    steady-state throughput. In the [issue tracker](issue-tracker.md),
    `TRB-Issue-119` retains a separate, not-yet-reproduced A* setup exception;
    GridForge `GF-Issue-006` preserves an incomplete strict-miss benchmark
    baseline, without an established common cause or fix.
    FixedMathSharp's `FMS-Issue-018` preserves two failed containment microbaselines;
    they are excluded from comparisons, with no isolated-math speedup claim.
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

1. Continue `TRB-Benchmark-005` before broader features. Exact rectangular
   tracing, raw-coordinate geometry and per-sample Flow proof reuse are implemented; do not
   repeat their original profiling-only investigation. Full-frame budget
   acceptance is still unmet, particularly Flow/500 synchronous rechecks.
   Preserve full-domain arithmetic, candidate accounting, dependency validation
   and LOS cadence when reducing remaining work; do not revive the withdrawn
   per-edge guard. Retain launch variability without inventing a cause.
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

Hardening and scheduling are complete locally and archived; release readiness
is a separate cross-stack checkpoint. Do not infer these gates passed from the
plans' exact local coverage or benchmark results.

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
