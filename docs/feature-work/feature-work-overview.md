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

- [Navigation Hardening](navigationHardeningPlan.md)
  - Phases 1 and 2 are complete with reviewed vertical traversal, airborne
    transitions, deterministic accepted-motion evidence, and safe runtime
    navigation-profile replacement through the Gravitas-backed 3D host. Phase 3
    is complete: the [acquisition execution record](navigationAcquisitionMeasurements.md)
    rejects the provisional large-field in-frame workload and recommends focused
    acquisition profiling before scheduler design (`TRB-Benchmark-001`). Local
    closeout evidence is recorded; final CI/released-package gates remain pending.
    Engine-host visual playtesting
    is deferred to future engine-specific adapter/package work.
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

- [`Grid Topology Navigation Map Refactor`](done/gridTopologyNavigationMapRefactorPlan.md)
  - Replaced the dense chart/partition lattice with immutable per-grid maps,
    deterministic overlays, and one topology-native navigation graph. The final
    hard cut removed the compatibility surface and archived the release evidence.
- [`Closest-transition lookup research`](done/closestTransitionLookupPlan.md)
  - Superseded by the unified map/graph transition query and cache model.
- [`Managed-transition lifecycle research`](done/managedTransitionLifecyclePlan.md)
  - Superseded by explicit transition definitions, rules, and overlay publication.

## Deferred / Evidence-Gated

Frame-spread guide scheduling is informed by the completed Phase 3 of the
[Navigation Hardening Plan](navigationHardeningPlan.md#phase-3---cold-public-guide-acquisition-measurement).
Large cold requests do not fit the provisional frame allowance, and four warm
100K requests also exceed its 2 ms navigation share. Profile acquisition cost
first; scheduler implementation still requires a justified follow-up scope and
explicit approval. No separate deferred
scheduler implementation plan is currently active.

## Recommended Execution Order

The plans share evidence, not competing implementations:

1. Recommended next: investigate `TRB-Benchmark-001` with focused acquisition
   profiling against the retained Phase 3 workloads. Optimization and scheduler
   implementation are follow-up scope, not automatically started by measurement.
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

Runtime implementation includes the fixed-profile Gravitas adapter and complete
hardening Phases 1 and 2; Phase 3 measurement and its follow-up decision are also
complete, with no new runtime API. The broader Gravitas integration Phase 2 and first-
class 2D work remain incomplete; drafting or reviewing a plan does not mark
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
