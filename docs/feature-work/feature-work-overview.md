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
  - Planned execution order: combine vertical traversal acceptance scenarios
    with movement-quality playtesting through a Gravitas-backed 3D host; add safe
    runtime navigation-profile replacement; then measure cold public guide
    acquisition. Includes posture cycles and fall/flight/controlled-descent
    sequences. Implementation has not started.
- [Gravitas Kinematic Integration](gravitasIntegrationPlan.md)
  - Phase 0 preflight fixed the optional 3D adapter/package boundary, shared-
    world frame and lifetime order, first bounded wall fixture, and GridForge-
    Unity observation host. Gravitas's working tree now declares and passes its
    full matrix against the aligned 7.1.0/9.1.0 stack without a new physics API.
    The adapter remains independently owned and introduces no core-to-core
    dependency. Phase 1 package-backed work is gated by the aligned Gravitas
    release. `TRB-Issue-102` is resolved in the Trailblazer working tree through
    an explicit serialized support-normal contract; package-backed slope work
    requires the Trailblazer release containing it.
    `TRB-Issue-103` separately tracks the lower-stack duplicate-version defect
    found by solution-wide local-source validation.
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

Frame-spread guide scheduling is evidence-gated by Phase 3 of the
[Navigation Hardening Plan](navigationHardeningPlan.md#phase-3---cold-public-guide-acquisition-measurement).
That phase delivers measurements and a decision; scheduler implementation
requires a justified follow-up scope and explicit approval. No separate deferred
scheduler implementation plan is currently active.

## Recommended Execution Order

The plans share evidence, not competing implementations:

1. Resolve `TRB-Issue-103`, then review and release the dependency-aligned
   Gravitas working tree and release Trailblazer with the resolved
   `TRB-Issue-102` contract. Capture the Gravitas adapter's initial fixed-profile
   wall regression, then use that wall slice and the explicit support normal for
   hardening Phase 1 before interpreting movement-quality results.
2. Complete hardening Phase 1 with that host, then Phase 2 profile replacement
   and the adapter's corresponding collider-change tests. Run hardening Phase 3
   measurements; scheduler implementation remains separately evidence-gated.
3. Close the 3D adapter's package, documentation, and joint-verification gates.
   Reference the same scenario/playtest evidence from both owning plans.
4. Execute the dedicated 2D plan after its contract review, using the reviewed
   motion and replacement boundaries. Add the planar adapter extension there;
   neither the initial 3D slice nor hardening requires completed 2D support.

Runtime implementation remains unstarted; Gravitas Phase 0 is a completed
preflight, not an adapter. Drafting or reviewing a plan does not mark runtime
checkboxes complete. The cross-stack rules apply throughout:

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
