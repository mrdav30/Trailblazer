# Hardening Phase 2 Plan

## Purpose

The original pathing hardening pass is complete. This document captures the remaining follow-up
work so the completed Phase 1 plan can be retired cleanly.

## Completed In Phase 1

The completed hardening work covered:

- managed transition lifecycle hardening
- resolved-cell and closest-transition public queries
- overlap and chart-state optimization
- external `GlobalGridManager` lifecycle hardening
- reset/live-state tightening
- shared transition-query and hybrid-candidate narrowing

Those tracks are now part of the runtime baseline and should no longer be treated as open planning
work.

## Remaining Follow-Up

### 1. Coverage And Documentation Execution

Primary remaining work is now coverage and supporting documentation alignment.

Tracked here:

- [coverageHardeningPlan.md](./coverageHardeningPlan.md)

This is the main active follow-up track.

## 2. Fast Follow-Up Issues

Track any smaller follow-up issues that are straightforward to execute and don't require significant design or profiling work here:

### 3. Optional Runtime Follow-Ups

These are valid future hardening items, but they should stay demand-driven rather than automatic.

- Phase 5 coverage work showed that the remaining CRAP overlap is now concentrated in
  `NavSteering.GetHeading`, `FlowFieldGuide.TryGetStagedMovementDirection`, and clustered
  `NavMotor` helpers. If those do not drop below target through natural branch coverage, prefer
  extracting grouped steering/staged-guide/motor support logic over piling more helper branches
  into the existing classes.
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

## Retirement Note

The original `pathingHardeningPlan.md` is intentionally retired after this handoff to avoid keeping
two competing “active” hardening plans around at once.
