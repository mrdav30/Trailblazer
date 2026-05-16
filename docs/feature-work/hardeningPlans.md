# Hardening Plans

## Purpose

This document captures the hardening plans for Trailblazer.

## On-hold Hardening Plans

These are valid future hardening items, but they should stay demand-driven rather than automatic.

1. If profiling shows the current closest-transition lookup is still hot inside large single-grid
  transition sets, add a more granular spatial index instead of relying only on filtered caches and
  grid-bounds pruning.
  Tracked here: [closestTransitionLookupPlan.md](./closestTransitionLookupPlan.md)
2. If hosts need richer automatic lifecycle behavior for manually registered transitions, revisit
  managed manual regeneration and whether `NavigationChartRegistration` should share a broader
  general managed transition dependency model.
  Tracked here: [managedTransitionLifecyclePlan.md](./managedTransitionLifecyclePlan.md)

## Issues to Track

These are the issues we should track and close as they arise.

When implementation work uncovers an issue outside the active plan, record it here instead of
folding unrelated cleanup into the current patch. Include the observed problem, why it is outside
scope, the likely subsystem owner, and the smallest useful validation signal such as a focused test,
benchmark, or doc update.

- N/A
