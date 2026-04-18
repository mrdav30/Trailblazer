# Hardening Phase Plan

## Purpose

This document captures the remaining follow-up work so we can release to alpha.

## Remaining Follow-Up

### 1. Coverage And Documentation Execution

Primary remaining work is now coverage and supporting documentation alignment.

Tracked here:

- [coverageHardeningPlan.md](./coverageHardeningPlan.md)

This is the main active follow-up track.

## 2. Fast Follow-Up Issues

Track any smaller follow-up issues that are straightforward to execute and don't require significant design or profiling work here:

- Revisit whether `NavMotor` should expose a clearer pre-traversal traversal-state sync for hosts
  that learn about medium changes before simulation begins. The current mantle path is correct when
  hosts call `UpdateTraversal(...)` as part of that handoff, but the seam is implicit and easy to
  miss in tests or custom integrations.

### 3. Optional Runtime Follow-Ups

These are valid future hardening items, but they should stay demand-driven rather than automatic.

- If profiling shows the current closest-transition lookup is still hot inside large single-grid
  transition sets, add a more granular spatial index instead of relying only on filtered caches and
  grid-bounds pruning.
- If external grid churn becomes noisy, consider using `GridSpawnToken` and `GridVersion` for
  stale-event guards or dedup in the `PathManager` grid bridge.
- If hosts need richer automatic lifecycle behavior for manually registered transitions, revisit
  managed manual regeneration and whether `ManagedChartTransitionState` should broaden into a more
  general managed transition dependency model.
- If the new GridForge payload makes it worthwhile later, revisit whether external grid change
  rebuilds should become more precise than the current bounds-targeted chart rebuild path.
- If climbing and other attached locomotions expose enough overlap after implementation, revisit
  whether the motor should grow a generalized movement-mode abstraction for mutually exclusive
  controlled modes such as swim, fly, and climb. Do not preemptively refactor toward this before
  the climb runtime proves the real common shape.
- If the climb affordance resolver pattern lands cleanly, revisit whether narrow capability events
  such as `CanAffordJump` should move toward a more structured host query contract as well, so the
  motor consumes deterministic frame snapshots instead of accumulating permission and environment
  state through callback chains.

## Recommended Order

1. Execute the phased coverage plan.
2. Keep docs aligned as coverage work lands.
3. Revisit the optional runtime follow-ups only if profiling or host usage shows a real need.
