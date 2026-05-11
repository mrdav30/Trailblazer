# Hardening Plans

## Purpose

This document captures the remaining follow-up work so we can release to alpha.

## Remaining Follow-Up

These are valid future hardening items, but they should stay demand-driven rather than automatic.

1. If profiling shows the current closest-transition lookup is still hot inside large single-grid
  transition sets, add a more granular spatial index instead of relying only on filtered caches and
  grid-bounds pruning.
  Tracked here: [closestTransitionLookupPlan.md](./closestTransitionLookupPlan.md)
2. If hosts need richer automatic lifecycle behavior for manually registered transitions, revisit
  managed manual regeneration and whether `ManagedChartTransitionState` should broaden into a more
  general managed transition dependency model.
  Tracked here: [managedTransitionLifecyclePlan.md](./managedTransitionLifecyclePlan.md)

## Issues to Track

These are the issues we should track and close before we can release to alpha.

When implementation work uncovers an issue outside the active plan, record it here instead of
folding unrelated cleanup into the current patch. Include the observed problem, why it is outside
scope, the likely subsystem owner, and the smallest useful validation signal such as a focused test,
benchmark, or doc update.

- N/A
