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

- [`Grid Topology Navigation Map Refactor`](gridTopologyNavigationMapRefactorPlan.md)
  - Replace the dense cubic chart/partition pathing lattice with independently
    baked per-grid navigation maps plus deterministic runtime semantic overlays,
    composed into a topology-native graph supporting dense/sparse rectangular
    and hex-prism GridForge grids. This is an intentional breaking-change track
    with no compatibility surface.
- [`Cross-Stack Issue Resolution`](issue-tracker.md)
  - Resolve cross-stack issues in dependency order: `FixedMathSharp`,
    `SwiftCollections`, `GridForge`, then Trailblazer. Package references are
    the default; use `UseLocalLsfStack=true` only for coordinated validation of
    unreleased sibling changes, then revalidate against released packages.
- [`Benchmark Signal Hardening`](benchmark-signal-hardening-backlog.md)
  - Reproduce and close confirmed release-relevant signals alongside the owning
    library change. Do not broaden this into speculative optimization work.

## Recently Completed

No recently completed work is currently documented here. Add new items when they
are verified and released.

## Deferred / Evidence-Gated

1. If profiling shows the current closest-transition lookup is still hot inside
   large single-grid transition sets, add a more granular spatial index instead
   of relying only on filtered caches and grid-bounds pruning. Tracked here:
   [closestTransitionLookupPlan.md](./closestTransitionLookupPlan.md)
2. If hosts need richer automatic lifecycle behavior for manually registered
   transitions, revisit managed manual regeneration and whether
   `NavigationChartRegistration` should share a broader general managed
   transition dependency model. Tracked here:
   [managedTransitionLifecyclePlan.md](./managedTransitionLifecyclePlan.md)

## Recommended Execution Order

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
