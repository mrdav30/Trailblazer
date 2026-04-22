# Hardening Phase Plan

## Purpose

This document captures the remaining follow-up work so we can release to alpha.

## Remaining Follow-Up

These are valid future hardening items, but they should stay demand-driven rather than automatic.

1. If profiling shows the current closest-transition lookup is still hot inside large single-grid
  transition sets, add a more granular spatial index instead of relying only on filtered caches and
  grid-bounds pruning.
  Tracked here:
  - [closestTransitionLookupPlan.md](./closestTransitionLookupPlan.md)
2. If hosts need richer automatic lifecycle behavior for manually registered transitions, revisit
  managed manual regeneration and whether `ManagedChartTransitionState` should broaden into a more
  general managed transition dependency model.
  Tracked here:
  - [managedTransitionLifecyclePlan.md](./managedTransitionLifecyclePlan.md)
3. If climbing and other attached locomotions expose enough overlap after implementation, revisit
  whether the motor should grow a generalized movement-mode abstraction for mutually exclusive
  controlled modes such as swim, fly, and climb. Do not preemptively refactor toward this before
  the climb runtime proves the real common shape.
  Tracked here:
  - [movementModeAbstractionPlan.md](./movementModeAbstractionPlan.md)
4. If new motor capability-query families emerge later, consider a dedicated structured query seam
  that remains separate from notification events. The current known cases already route through
  authoritative frame snapshots, so do not grow a broader motor host interface preemptively.
  Tracked here:
  - [structuredMotorHostQueryPlan.md](./structuredMotorHostQueryPlan.md)
