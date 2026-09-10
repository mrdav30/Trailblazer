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
work. Further performance work is evidence-gated: the archived scheduling plan
records remaining cold-readiness limits and useful next measurements, not an
approved new implementation scope.

## Recommended Execution Order

The plans share evidence, not competing implementations:

1. Complete the remaining shared movement scenarios in Gravitas integration
   Phase 2 on the reviewed replacement boundary.
2. Close the 3D adapter's package, documentation, and joint-verification gates.
   Reference the same deterministic scenario evidence from both owning plans.
3. Execute the dedicated 2D plan after its contract review, using the reviewed
   motion and replacement boundaries. Add the planar adapter extension there;
   neither the initial 3D slice nor hardening requires completed 2D support.
4. After every active feature-work plan is complete, release any changed
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
