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

- None at the moment

## 3. Remaining TODO Items

### 3.1 In-Code TODO Items

These are TODOs found across the source and test files, ordered by estimated importance. Each entry
references the file where the TODO lived and includes enough context to act on it without re-reading
the original comment.

1. **`NavMotor` ceiling-level bypass via external forces or platform movement**
   (`src/Trailblazer/Navigation/Motor/NavMotor.cs`)
   `CheckJumpStatus` guards against exceeding `CeilingLevel` when upward velocity is detected, but
   external forces and platform movement go through separate code paths (`FinalizePlatformMovement`
   and whatever drives `FrameVelocity` externally) that do not pass through this check. Before
   alpha, verify whether any of those paths can put the navigator above `CeilingLevel` without
   triggering the clamp, and either route them through the existing ceiling-check helper or add
   equivalent guards at the affected sites.

2. **`PathManager.UnloadChart` missing volume partition neighbor rebinding**
   (`src/Trailblazer/Pathing/PathManager.cs`)
   During chart unload, `SolidChartPartition` neighbors are rebound after voxel state is removed,
   but the equivalent rebind is not performed for `VolumeChartPartition`. The original comment notes
   this was likely an oversight because neighbor rebinding was originally added for clearance checks
   driven by the navigator unit size, which only applies to solid partitions. Confirm whether volume
   partitions require the same rebind step for correctness, and either add it or document why it is
   intentionally omitted.

3. **`NavSteering` test false positive on unit-size repath**
   (`tests/Trailblazer.Tests/Navigation/Steering/NavSteering.Tests.cs`)
   The test for mid-path unit-size change only asserts that `CurrentRequest.UnitSize` was updated,
   but `CurrentRequest` mutates in-place when `TrySetUnitSize` is called, so the assertion does not
   prove that a new path was actually requested. Revise the test to verify that a repath was
   triggered: assert that the guide is invalidated or that a new survey is performed, then confirm
   the resulting path is valid for the new unit size.

4. **Surveyor post-search collection clearing for GC**
   (`src/Trailblazer/Pathing/Search/AStar/AStarSurveyor.cs`,
   `src/Trailblazer/Pathing/Search/FlowField/FlowFieldSurveyor.cs`,
   `src/Trailblazer/Pathing/Search/Volume/VolumeSurveyor.cs`)
   All three surveyor types leave their working collections populated after returning a result. If
   surveyor instances are pooled or long-lived, clearing at the end of each pass is the safer
   default to avoid retaining stale node data between surveys. If they are allocated per-survey the
   cost is deferred to GC anyway. Evaluate the actual lifetime model, apply a consistent clear
   policy across all three surveyors, and consider adding a `dirty` flag to distinguish an
   in-progress search from a freshly reset instance if clearing up-front proves cheaper.

5. **`LocomotionHandler`: consolidate locomotion properties into `SwiftBucket<ILocomotion>`**
   (`src/Trailblazer/Navigation/Motor/Locomotion/LocomotionHandler.cs`)
   Each locomotion type is currently exposed as a named property (`Move`, `Platform`, `Jump`,
   `Fall`, `Slide`, etc.). Replacing these with a `SwiftBucket<ILocomotion>` would allow dynamic
   iteration without per-type dispatch, reduce composition and lookup boilerplate, and make it
   easier to add new locomotion types without modifying the handler. The named properties are more
   convenient for current call sites; weigh that convenience cost against the flexibility gain,
   especially if new locomotion types are anticipated before or during alpha.

6. **`NavigatorPathRequestFactory` code duplication**
   (`src/Trailblazer/Navigation/Support/NavigatorPathRequestFactory.cs`)
   The `TryCreate` switch repeats a nearly identical null-check, configuration, and
   out-parameter-assignment pattern for each `GuidedPathMode` case. Extract the shared
   post-creation configuration step (e.g. setting `MaxClimbHeight`, returning false on null) into a
   small private helper to reduce the risk of a future case diverging silently.

7. **`ITransient` and `LifecycleHookHandler` placement**
   (`src/Trailblazer/Support/Transient/ITransient.cs`,
   `src/Trailblazer/Support/HookHandler/LifecycleHookHandler.cs`)
   Both types are general-purpose utilities with no hard dependency on the navigation runtime and
   were flagged as candidates for extraction into a separate utility project (potentially alongside
   or within Chronicler). Before extracting, confirm whether any Trailblazer-internal types depend
   on these interfaces through non-public surface area, and decide whether the Chronicler project is
   the right home or whether a dedicated `Trailblazer.Support` NuGet boundary is more appropriate.

8. **`AlternativeVoxelFinder` static singleton: make lazy disposable**
   (`src/Trailblazer/Pathing/Search/Support/VoxelFinder/AlternativeVoxelFinder.cs`)
   `AlternativeVoxelFinder.Instance` is a plain static field. Implement as a `Lazy<T>` property if
   deferred construction matters, or document why eager static allocation is intentional and leave
   the field as-is. The type has no unmanaged resources so full `IDisposable` teardown is only
   needed if the instance must be replaced or reset between test runs.

9. **`NavigationChart` layout: evaluate single-layer-high XZ design**
    (`src/Trailblazer/Pathing/Chart/NavigationChart.cs`)
    The comment raises whether restricting charts to one layer high (using the Y axis only for
    vertical stacking rather than as a primary index dimension) would yield a more cache-friendly
    XZ-first layout and simplify indexing for typical game scenarios. This is a significant
    data-layout decision that affects the entire chart and partition system. Treat as a post-alpha
    architectural investigation; revisit only if profiling data or host feedback indicates the
    current layout is a real hotspot.

### 3.2 Optional Runtime Follow-Ups

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

## Retirement Note

The original `pathingHardeningPlan.md` is intentionally retired after this handoff to avoid keeping
two competing “active” hardening plans around at once.
