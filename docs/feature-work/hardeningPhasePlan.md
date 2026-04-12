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

- None at the moment

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

## Recommended Order

1. Execute the phased coverage plan.
2. Keep docs aligned as coverage work lands.
3. Revisit the optional runtime follow-ups only if profiling or host usage shows a real need.
