# Multi-World Trailblazer Context Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox
> (`- [ ]`) syntax for tracking.

**Goal:** Let one process host multiple independent Trailblazer navigation worlds backed by
explicit GridForge `GridWorld` instances.

**Architecture:** Introduce an explicit `TrailblazerWorldContext` as the owner of one
`GridWorld`, its simulation clock, pathing state, transition registry, guide caches, and navigation
coordination state. Existing static managers become temporary single-world facades over a default
context, then shrink or disappear before alpha.

**Tech Stack:** C# 11, `netstandard2.1`, `net8.0`, `GridForge.GridWorld`, `FixedMathSharp`,
`SwiftCollections`, xUnit v3, FluentAssertions, BenchmarkDotNet.

---

## Purpose

GridForge now supports explicit `GridWorld` instances instead of one process-wide static world.
Trailblazer currently adapts to that through `TrailblazerWorldManager`, but that bridge still
maintains one active world for the whole process. That means `PathManager.Register(world, chart)`
does not really create independent world state; it re-points Trailblazer's static runtime at a new
world while pathing registries, transition registries, guide caches, reachability snapshots, and
navigation coordination remain process-wide.

This plan defines the migration from "static Trailblazer over one active GridWorld" to "one
Trailblazer runtime context per GridWorld."

## Non-Negotiable Engineering Constraints

Trailblazer is a deterministic, lockstep-oriented character motor and pathing library. Multi-world
support must strengthen that goal, not dilute it.

- Preserve deterministic behavior before API convenience. Any change that affects iteration order,
  path scoring, cache keys, frame timing, rounding, or traversal ordering must be pinned by tests.
- Keep the runtime engine-agnostic. Contexts may bind to `GridWorld`, fixed-point math, and
  Trailblazer services, but they must not introduce Unity, Godot, Unreal, ECS, renderer, physics,
  thread scheduler, or wall-clock assumptions.
- Optimize the default path, not just the migration path. Context ownership should remove ambient
  lookups and global contention instead of adding per-call world dictionary probes in hot loops.
- Prefer context-local state over global maps keyed by world. A global `(world, key)` lookup is a
  fallback only when profiling and ownership rules prove it is better.
- Keep request creation, guide lookup, survey expansion, steering, and motor simulation free of
  avoidable steady-state allocations.
- Do not introduce "quick" compatibility behavior that becomes a second architecture. Compatibility
  facades must be temporary, obvious, and tracked toward removal before alpha.
- Reduce duplication by moving shared behavior into focused services or state containers, not by
  copy-pasting static-manager logic into instance-manager logic.
- Treat developer experience as part of correctness. The public API should make the owning context
  obvious, produce clear errors for unbound objects, and avoid overloads that look multi-world-safe
  while mutating default global state.

## Scope Control And Hardening Capture

This plan should stay focused on multi-world context ownership. If implementation uncovers an
important issue that is outside this migration, record it in
`docs/feature-work/hardeningPlans.md` with:

- the observed problem
- why it is outside the current phase
- the likely subsystem owner
- the smallest useful validation signal, such as a focused test, benchmark, or doc update

Do not fold unrelated cleanup into a phase just because the file is open. If the issue blocks the
phase, add it to the phase. If it does not block the phase, hardening-track it and keep moving.

## Context Ingested

- `README.md` documents explicit `GridWorld` creation and `TrailblazerManager.Initialize(world)`.
- `docs/wiki/OVERVIEW.md` originally described `PathManager` as a global chart registry and
  `TrailblazerManager` as the fixed-step owner; Phase 3 updates the pathing wording to the
  context-owned model.
- `src/Trailblazer/Support/TrailblazerWorldManager.cs` confirms the current bridge owns a single
  `_world`, forwards one active world's grid events, and exposes ambient world lookup helpers.
- `src/Trailblazer/Pathing/PathManager.cs` already has many `GridWorld world` overloads, but they
  call `TrailblazerWorldManager.AttachWorld(world)` and still mutate static dictionaries.
- `rg` found 73 source, test, and benchmark files referencing `TrailblazerWorldManager`; 28
  production files reference it directly.

## Original Failure Mode

The core problem at the start of this plan was not just public API shape. The mutable state behind
the API was shared:

| Area | Current owner | Multi-world issue |
| --- | --- | --- |
| Active world lookup | `TrailblazerWorldManager` | One `_world` for the process. |
| Chart registry | `PathManager._navigationChartMap` | Chart names, init state, and registration order are global. |
| Authored cell resolution | `PathManager._resolvedChartVoxelStates` | `WorldVoxelIndex` keys collide across separate `GridWorld` instances. |
| Partition pools | `PathManager.PartitionPool`, `VolumeChartPartitionPool` | Released partitions can be reused across worlds without context ownership. |
| Chart live state | `PathManager._navigationChartMap` registrations | Authored chart state is split from live state, but registrations remain process-wide until Phase 3. |
| Transition registry | `TraversalTransitionRegistry` | Manual/generated transitions and registry versions are process-wide. |
| Transition query caches | `TraversalTransitionQuery` | Cached directed-transition arrays are keyed only by one global registry version. |
| Volume medium rules | `VolumeMediumRules` | Host gas/liquid rules are global, not per world. |
| Guide caches | `PathGuideFactory` | Cache keys use voxel spawn tokens and registry versions without world ownership. |
| Reachability cache | `SolidPartitionReachability` | Snapshot data is built from global `PathManager.AllCharts`. |
| Lifecycle clock | `TrailblazerManager` | `FrameCount`, hooks, reset, frame rate, and cache culling are global. |
| Navigation groups | `MovementGroupCoordinator` | Group membership can collide across worlds using the same group ids. |
| Navigator identity | `NavigatorGlobalIdAllocator` | Reset and deterministic id order are global. |
| Navigator reset | `Navigator.Reset()` | Deregisters from the ambient active world, not the navigator's owning world. |

## Decision

Use an explicit context object as the long-term public API.

```csharp
var trail = TrailblazerWorldContext.Attach(world);

trail.Pathing.Register(chart);
trail.Transitions.Register(transition);

var navigator = new MyNavigator(trail);
navigator.Setup(start, size);

trail.Simulate();
navigator.Simulate();
navigator.CommitFrameMotion();
trail.LateSimulate();
```

The context owns all mutable world-specific state. Static classes can remain temporarily as a
single-world compatibility layer, but new multi-world work must use the context directly. This is
the clean path because it avoids global maps keyed by `(GridWorld, key)` on hot paths, avoids hidden
ambient-world switches, and makes lifecycle/reset semantics explicit.

Rejected approaches:

- Keep the current static managers and add `GridWorld` parameters everywhere. This still leaves
  shared registries, shared caches, and shared chart initialization flags unless every state
  dictionary is keyed by world. That would be harder to reason about and easier to misuse.
- Add a world id to request cache keys only. That protects one symptom but leaves chart ownership,
  transitions, grid events, lifecycle, and navigation coordination global.

## Core Invariants

- A `TrailblazerWorldContext` owns exactly one active `GridWorld`.
- A `Navigator`, path request, guide, transition registry entry, live chart registration, and
  partition belongs to exactly one `TrailblazerWorldContext`.
- No world-bound code resolves voxels through `TrailblazerWorldManager.World` after the migration.
- Per-frame work runs only for the contexts the host explicitly simulates.
- Resetting or disposing one context does not clear charts, caches, transitions, movement groups, or
  frame counters in another context.
- Request cache keys do not need to carry a world id when the cache is context-local. Any temporary
  static compatibility cache must include a context id until it is removed.
- Authored `NavigationChart` data can be reused, but live registration state is per context.
- Grid event handling is attached to the context's `GridWorld`; a reset event from world A resets
  only context A's pathing state.
- Hot-path operations do not pay O(number of contexts) costs. Context selection happens at API
  boundaries, then inner loops work against direct context-owned state.
- Context-local ordering is stable and explicit. Cross-world independence must not rely on process
  dictionary ordering or reference identity side effects.

## Target State

### Public Surface

- `TrailblazerWorldContext` is the primary host-facing runtime handle.
- `TrailblazerWorldContext.Attach(GridWorld world, bool takeOwnership = false)` attaches to a
  host-owned `GridWorld`.
- `TrailblazerWorldContext.CreateOwned(...)` creates a context-owned `GridWorld` for tests and
  standalone pathing scenarios.
- `TrailblazerWorldContext` exposes:
  - `GridWorld World`
  - `Fixed64 VoxelSize`
  - frame properties currently on `TrailblazerManager`
  - `Simulate()`, `LateSimulate()`, `Visualize()`, `Reset()`, `Dispose()`
  - `Pathing`, `Guides`, `Transitions`, and `VolumeRules` services
- Path requests are created with a context:
  - `AStarPathRequest.Create(context, origin, destination, unitSize, ...)`
  - `FlowFieldPathRequest.Create(context, origin, destination, unitSize, ...)`
  - `VolumePathRequest.Create(context, origin, destination, unitSize, ...)`
- `Navigator` is bound to a context at construction or initialization and cannot change context
  while initialized.

### Compatibility Surface

- `TrailblazerManager.Initialize(world)` creates and stores a default `TrailblazerWorldContext`.
- Existing static calls such as `PathManager.Register(chart)` route to the default context while
  migration is underway.
- Existing static calls that accept `GridWorld world` are either removed or changed to require an
  explicit context before alpha. `PathManager.Register(world, chart)` is especially misleading
  today because it looks multi-world-safe while it switches global state.
- `TrailblazerWorldManager` becomes an obsolete default-context adapter, then is deleted once source
  and tests no longer need ambient world lookup.

## File Structure Target

These names are the proposed final ownership boundaries.

| File | Responsibility |
| --- | --- |
| `src/Trailblazer/Main/TrailblazerWorldContext.cs` | Public context, lifecycle, ownership, reset, disposal. |
| `src/Trailblazer/Main/TrailblazerClock.cs` | Context-local frame rate, frame count, delta, accumulation. |
| `src/Trailblazer/Main/TrailblazerLifecycleHooks.cs` | Context-local ordered hook lists and registration. |
| `src/Trailblazer/Pathing/PathingWorldState.cs` | Context-local pathing registries, locks, counters, pools. |
| `src/Trailblazer/Pathing/TrailblazerPathingService.cs` | Instance replacement for host-facing `PathManager` operations. |
| `src/Trailblazer/Pathing/Chart/NavigationChartRegistration.cs` | Per-context chart registration order, initialized flag, generated transition state. |
| `src/Trailblazer/Pathing/Search/TrailblazerGuideService.cs` | Context-local guide request, return, cache, and culling operations. |
| `src/Trailblazer/Pathing/Transition/TraversalTransitionRegistryState.cs` | Per-context transition storage, indexes, and registry version. |
| `src/Trailblazer/Pathing/Transition/TraversalTransitionQueryCache.cs` | Per-context directed-transition query cache. |
| `src/Trailblazer/Pathing/Search/VolumeMediumRulesState.cs` | Per-context gas/liquid host rules and volume cache invalidation. |
| `src/Trailblazer/Pathing/Search/Support/Reachability/SolidPartitionReachabilityState.cs` | Per-context reachability snapshot storage. |
| `src/Trailblazer/Navigation/MovementGroups/MovementGroupCoordinatorState.cs` | Per-context movement group membership and formation state. |
| `tests/Trailblazer.Tests/Worlds/TrailblazerWorldContextIsolation.Tests.cs` | Cross-world isolation tests. |
| `tests/Trailblazer.Tests/Support/TrailblazerWorldFixture.cs` | Test helper for creating and disposing contexts. |

Pure stateless helpers can stay static. Static state that stores world-owned objects, chart names,
voxel indexes, request results, registry versions, or frame counters must move behind the context.

## Phase 0 - Baseline And Isolation Tests

**Status:** Complete  
**Goal:** Lock in the desired behavior before moving static state.

- [x] Add a small `tests/Trailblazer.Tests/Worlds` test area for world-context behavior.
- [x] Add a red test showing two contexts can register charts with the same chart name without
  collision.
- [x] Add a red test showing guide caches do not reuse results across two worlds with equivalent
  voxel spawn tokens or equivalent request coordinates.
- [x] Add a red test showing a `GridWorld.Reset()` event for world A clears only context A pathing
  state.
- [x] Add a red test showing `TrailblazerWorldContext.FrameCount` advances independently per world.
- [x] Add a red test showing `MovementGroupCoordinator` state is context-local when two worlds use
  the same group id.
- [x] Add a red test showing a navigator deregisters from its own context's world during reset.
- [x] Add `rg`-based test or CI note that tracks remaining production references to
  `TrailblazerWorldManager` so removal progress is visible.
- [x] Capture current steady-state allocation expectations for request cache keys, warm guide
  checkout/return, and navigator steady-state steering so the migration does not mask regressions.
- [x] Add a short architecture test or review checklist that fails any new production reference to
  engine-specific namespaces or wall-clock timing APIs in simulation code.

Phase notes:

- Phase 0 acceptance coverage lives in `MultiWorldPhase0AcceptanceTests`. Five future-phase
  behaviors remain skipped with `Category=MultiWorldPhase0Red`; independent context frame counts are
  now unskipped and passing after Phase 1.
- Baselines and focused verification commands are recorded in
  `multiWorldPhase0Baseline.md`.

Exit criteria:

- The new tests clearly fail against the current single-world bridge for the expected reasons.
- Existing full `Release` tests still pass when the red tests are excluded or marked with a clear
  trait until implementation begins.
- Baseline allocation and benchmark signals are recorded before implementation changes the shape of
  the hot paths.

## Phase 1 - Introduce The Context Shell

**Status:** Complete  
**Goal:** Add the public context type without moving all pathing state at once.

- [x] Create `TrailblazerWorldContext` with `GridWorld`, `VoxelSize`, ownership, `Reset()`, and
  `Dispose()` semantics.
- [x] Add `Attach(GridWorld world, bool takeOwnership = false)` and `CreateOwned(...)` factories.
- [x] Extract context-local clock data from `TrailblazerManager` into `TrailblazerClock`.
- [x] Extract lifecycle hook storage into `TrailblazerLifecycleHooks`.
- [x] Let `TrailblazerWorldContext.Simulate()` run its own ordered simulate hooks; guide cache
  culling remains in the guide-cache migration phase once caches move behind the context.
- [x] Keep `TrailblazerManager` as a default-context facade for current tests and examples.
- [x] Mark `TrailblazerWorldManager` as a compatibility bridge in XML docs and route it through the
  default context where possible.
- [x] Add tests for context construction, ownership disposal, independent frame rate, independent
  frame count, and default-context facade behavior.
- [x] Verify context construction and simulation do not introduce engine-specific dependencies,
  background threads, timers, or wall-clock behavior.
- [x] Document facade deprecation in XML comments so developers see the context-first path from
  IntelliSense.

Phase notes:

- `TrailblazerManager.Initialize(world)` now creates a default `TrailblazerWorldContext` and keeps
  the existing `TrailblazerWorldManager` attached for compatibility.
- `TrailblazerManager` clock properties delegate to the active default context when one exists;
  otherwise they use a compatibility static clock for existing tests that initialize no world.
- No pathing registries, guide caches, transitions, or navigator state moved in this phase.

Exit criteria:

- Hosts can create two `TrailblazerWorldContext` instances and advance their clocks independently.
- No pathing behavior has been changed yet except through compatibility routing.
- `TrailblazerManager` tests pass through the default-context facade.
- The context shell adds no avoidable per-frame allocations.

## Phase 2 - Split Authored Charts From Live Registrations

**Status:** Complete  
**Goal:** Make `NavigationChart` reusable across contexts.

- [x] Add `NavigationChartRegistration` to hold a `NavigationChart`, registration order,
  initialized state, managed generated transition ids, and generated transition prefix.
- [x] Move `NavigationChart.IsInitialized` and `NavigationChart.RegistrationOrder` reads and writes
  out of core pathing logic and into `NavigationChartRegistration`.
- [x] Change overlap resolution to use registration data from the owning context.
- [x] Keep authored chart cell data, bounds, priority, and cached authored-cell indexes on
  `NavigationChart`.
- [x] Update tests that assert chart initialization to query the context registration state instead
  of chart instance fields.
- [x] Add a test that the same `NavigationChart` instance can be represented by independent live
  registrations with independent initialization state.
- [x] Confirm same-priority overlap resolution still runs in stable O(number of local owners for the
  voxel) time and never scans charts from other contexts.

Phase notes:

- `NavigationChart` now represents authored data only; initialization state, registration order,
  generated transition id ownership, and generated transition prefixes live on
  `NavigationChartRegistration`.
- The static `PathManager` registry now stores registrations while it remains the compatibility
  facade. Phase 3 moves that registration map into `PathingWorldState` so the same chart name and
  the same authored chart instance can be registered through separate context-owned pathing
  services.
- Overlap resolution still uses per-voxel owner state, priority, and registration order, so
  same-priority ties remain deterministic without scanning unrelated charts.

Exit criteria:

- Authored chart data is world-agnostic.
- Live chart state is no longer stored on authored chart instances; full context locality lands when
  Phase 3 moves the registration map behind `TrailblazerWorldContext`.
- Same-priority overlap tie behavior remains deterministic within each context.

## Phase 3 - Move Pathing State Behind The Context

**Status:** Complete  
**Goal:** Make chart registration, partition ownership, grid event handling, and pathing utilities
world-local.

- [x] Add `PathingWorldState` and move these `PathManager` fields into it:
  `_navigationChartMap` as the `NavigationChartRegistration` map, `_resolvedChartVoxelStates`,
  `_initializedChartTouchCountsByGridIndex`, `_activeAuthoredGasCellCount`,
  `_activeAuthoredLiquidCellCount`, `_nextChartRegistrationOrder`, and the chart lock.
- [x] Move `PartitionPool` and `VolumeChartPartitionPool` into `PathingWorldState` so partition
  object lifetime is owned by the context that attached the partition.
- [x] Convert `PathManagerExternalGridBridge` into a context-owned event bridge subscribed to that
  context's `GridWorld`.
- [x] Change pathing methods to operate on `TrailblazerPathingService` or `PathingWorldState`
  instead of static fields.
- [x] Keep static `PathManager` as a thin default-context facade while source migration continues.
- [x] Update `SolidChartPartition` and `VolumeChartPartition` so voxel lookup and pool release use
  their owning context, not `TrailblazerWorldManager`.
- [x] Ensure `ClearLiveGridState`, chart unload, chart mutation, generated transition refresh, and
  external grid rebuild operate only on the owning context.
- [x] Add multi-world tests for same chart names, same `WorldVoxelIndex` values, independent chart
  unload, independent grid rebuild, and independent chart mutation.
- [x] Review every moved dictionary/list for expected complexity and allocation behavior. Prefer
  direct context-owned indexes over repeated scans when the data can grow with chart or grid count.
- [x] If a required optimization is discovered but does not block correctness, record it in
  `docs/feature-work/hardeningPlans.md` rather than hiding it as a partial migration shortcut.

Phase notes:

- `TrailblazerWorldContext.Pathing` now owns `PathingWorldState`, including chart registrations,
  resolved voxel state, initialized chart touch counts, authored volume counters, partition pools,
  registration ordering, and grid bridge diagnostics.
- `PathingWorldGridBridge` subscribes directly to one `GridWorld` and forwards grid add/remove/change
  and reset events through the owning state. The old static bridge remains as a compatibility wrapper
  over the active state for existing focused diagnostics tests.
- `SolidChartPartition` and `VolumeChartPartition` now remember their owning pathing state for voxel
  lookup and pool release.
- Direct `PathManager.Register(world, ...)` calls now throw with context-first guidance unless they
  are executing inside a context-owned pathing service. Static `PathManager.Register(chart)` remains
  the single-world default-context facade.
- No out-of-scope hardening item was added in this phase; transition registry, guide caches,
  reachability state, and volume medium rules were intentionally deferred to Phase 4.

Exit criteria:

- `PathingWorldState` owns all mutable chart and live voxel pathing state.
- A grid event from one `GridWorld` cannot rebuild or reset charts in another context.
- `PathManager.Register(world, chart)` no longer exists as a misleading multi-world API, or it
  throws an explicit message that callers must use `TrailblazerWorldContext`.

## Phase 4 - Scope Transitions, Volume Rules, Reachability, And Guides

**Status:** Complete  
**Goal:** Remove the highest-risk cross-world cache and registry collisions.

- [x] Convert `TraversalTransitionRegistry` storage into `TraversalTransitionRegistryState`.
- [x] Move `TraversalTransitionQuery` caches into `TraversalTransitionQueryCache` owned by the
  registry state.
- [x] Make transition registration, generated-transition registration, suppression, active-state
  rebuild, and resolved endpoint lookup context-local.
- [x] Convert `VolumeMediumRules` storage into `VolumeMediumRulesState`.
- [x] Ensure gas/liquid host rules invalidate only the owning context's volume guide cache and
  managed manual transitions.
- [x] Convert `SolidPartitionReachability` into `SolidPartitionReachabilityState`.
- [x] Move `PathGuideFactory` caches, guide pools, hybrid route plan caches, and stale eviction into
  `TrailblazerGuideService`.
- [x] Keep guide object pools context-local unless profiling proves cross-context pooling is needed.
- [x] Add tests for transition ids reused across contexts, registry versions scoped per context,
  volume rules scoped per context, reachability snapshots scoped per context, and cache invalidation
  scoped per context.
- [x] Add a benchmark preflight that creates two contexts in one process and verifies both still
  resolve non-trivial routes after all setup is complete.
- [x] Preserve or improve warm-guide allocation behavior. Any unavoidable regression must be
  measured, explained in the phase notes, and tracked with a follow-up benchmark target.
- [x] Keep transition lookup indexes context-local and prefiltered by the dimensions used by the
  current query path; avoid introducing broad scans across all registered transitions.

Phase notes:

- `TrailblazerWorldContext` now exposes `Transitions`, `VolumeRules`, and `Guides` services over
  the same owning `PathingWorldState` used by `Pathing`.
- `TraversalTransitionRegistry` and `TraversalTransitionQuery` remain static default-context
  facades, but their mutable storage, indexes, registry version, and directed-query caches now live
  in `TraversalTransitionRegistryState` and `TraversalTransitionQueryCache`.
- `VolumeMediumRules` remains a default-context facade over `VolumeMediumRulesState`. Host gas and
  liquid rules invalidate only the owning context's volume cache and managed manual transitions.
- `PathGuideFactory` remains a default-context facade over context-owned guide caches and guide
  pools. `TrailblazerWorldContext.Simulate()` now culls stale guides using the owning context's
  frame count.
- `SolidPartitionReachability` snapshot data is context-owned and builds from the active context's
  chart registry and `GridWorld`, not from ambient global chart/world state.
- The Phase 0 guide-cache acceptance test is now unskipped. Movement groups and navigator reset
  remain intentionally skipped for Phase 6.
- No out-of-scope hardening item was added in this phase. Request creation and surveyor ambient-world
  lookup remain intentionally tracked in Phase 5; the Phase 4 preflight attaches the compatibility
  ambient world only at the request-creation boundary.

Exit criteria:

- Request/guide reuse cannot cross context boundaries.
- Transition fallback in one world cannot see transitions registered in another world.
- Volume medium host rules in one world cannot change request validity or guide results in another
  world.

## Phase 5 - Bind Requests And Surveyors To Context

**Status:** Complete  
**Goal:** Remove ambient world lookup from path request creation and hot path survey logic.

- [x] Add `TrailblazerWorldContext Context` to `IPathRequest` or a shared internal request base.
- [x] Add context-aware factories for `AStarPathRequest`, `FlowFieldPathRequest`,
  `VolumePathRequest`, and `HybridPathRequest`.
- [x] Remove default-voxel-size request overloads or route them through the default context facade
  with obsolete guidance.
- [x] Change `PathRequest.UpdateRequest`, `TrySetOrigin`, `TrySetDestination`, and
  `TrySetUnitSize` to use the request's context.
- [x] Convert `SolidVoxelFinder`, `VolumeVoxelFinder`, `EndpointVoxelResolver`,
  `AlternativeVoxelFinder`, and direct `GridTracer.TraceLine(...)` calls to receive a context or
  context-local pathing service.
- [x] Convert `AStarSurveyor.Shared`, `FlowFieldSurveyor.Shared`, and `VolumeSurveyor.Shared` into
  context-owned services or stateless entry points with context-owned scratch state.
- [x] Ensure cache keys remain allocation-free and deterministic. Prefer context-local caches over
  adding a world id to every key.
- [x] Add allocation tests for steady-state request cache keys after the context field is added.
- [x] Add cross-world tests where two equivalent requests in separate contexts produce independent
  cache entries and independent invalidation.
- [x] Keep survey scratch state owned by the context or rented from pools that do not retain
  world-specific references after release.
- [x] Confirm endpoint resolution performs one context selection at request creation/update and then
  works against direct `GridWorld`/pathing-state references in inner loops.

Exit criteria:

- Production path request and survey code no longer references `TrailblazerWorldManager`.
- Request creation cannot accidentally resolve endpoints against a different world.
- Warm guide paths remain allocation-free or any measured regression has an explicit follow-up
  benchmark entry.

Phase 5 notes:

- `IPathRequest.Context` now binds every request to the `TrailblazerWorldContext` that resolved its
  endpoints. Existing compatibility factories route through the configured default context, while
  new context-aware factories are available for `AStarPathRequest`, `FlowFieldPathRequest`,
  `VolumePathRequest`, and internal `HybridPathRequest`.
- Request mutation, solid/volume endpoint resolution, alternative voxel search, volume line tracing,
  flow-field sampling helpers, and volume clearance checks now use the request or partition owner
  context instead of ambient world lookup.
- `TrailblazerGuideState` owns the A*, FlowField, and Volume surveyor instances for each context.
  Production guide and hybrid-route paths use those context-owned surveyors; the old `.Shared`
  surveyors remain only as compatibility/test entry points.
- Cache keys remain world-id-free because the caches are context-local. The new
  `ContextBoundPathRequestTests.RequestCacheKeys_ShouldNotAllocateSteadyState_WhenRequestsCarryContext`
  coverage pins steady-state request-key allocation after adding context ownership.
- `rg -n "TrailblazerWorldManager" src/Trailblazer/Pathing/Search` and
  `rg -n "AStarSurveyor\\.Shared|FlowFieldSurveyor\\.Shared|VolumeSurveyor\\.Shared" src/Trailblazer/Pathing/Search`
  return no production hits. Remaining `TrailblazerWorldManager` production references are in the
  compatibility facade and Phase 6 navigation/movement-group areas.

## Phase 6 - Bind Navigation To Context

**Status:** Not started  
**Goal:** Make navigators, steering, movement groups, and deterministic ids world-local.

- [ ] Add context binding to `Navigator`, either through constructor injection or an explicit
  initialization parameter.
- [ ] Store the bound context on `Navigator` and reject simulation before a context is assigned.
- [ ] Change `Navigator.Reset()` and occupant registration/deregistration to use the navigator's
  context world.
- [ ] Change `NavigatorPathRequestFactory`, `GuidedVolumeExitPlanner`, and guided climb/exit helpers
  to use the navigator/request context.
- [ ] Convert `MovementGroupCoordinator` into `MovementGroupCoordinatorState` owned by the context.
- [ ] Convert `NavigatorGlobalIdAllocator` into context-local state so reset and deterministic id
  order are independent per world.
- [ ] Update `NavSteering` to read frame count, voxel size, guide services, and movement group state
  from the navigator context.
- [ ] Decide whether a navigator can be moved between contexts after reset. Recommended answer:
  disallow moving initialized navigators; allow reuse only after `Reset()` and explicit rebind.
- [ ] Update navigation serialization docs so hosts create and bind navigators to a context before
  Chronicler populates state. Do not serialize the context itself.
- [ ] Add tests for two worlds with identical group ids, identical navigator ids after per-world
  reset, independent frame counts, and context-correct reset/deregister behavior.
- [ ] Confirm steering and motor code stay engine-agnostic and keep fixed-step, fixed-point
  semantics. No wall-clock timing, engine callbacks, or floating-point simulation shortcuts.
- [ ] Re-run focused NavSteering steady-state allocation tests after context binding lands.

Exit criteria:

- Navigation code no longer depends on the active default world for runtime behavior.
- Two worlds can simulate navigators with the same group ids and destinations without interacting.
- Serialization remains populate-existing-instance and host-bound context state remains outside the
  serialized payload.

## Phase 7 - Retire The Ambient World Bridge

**Status:** Not started  
**Goal:** Remove the single active world as a production dependency.

- [ ] Remove production dependencies on `TrailblazerWorldManager` outside the compatibility facade.
- [ ] Replace test fixtures that call `TrailblazerWorldManager.Setup()` with context fixtures.
- [ ] Update benchmark fixtures to use one context per benchmark class or explicit multiple
  contexts where the scenario requires it.
- [ ] Remove or obsolete static overloads that hide context selection.
- [ ] Update `README.md`, `docs/wiki/OVERVIEW.md`, `PATHING.MD`, `PATHMANAGER.MD`,
  `PATHGUIDES.MD`, `TRANSITIONS.MD`, `VOLUMETRAVERSAL.MD`, and navigation docs with context-first
  examples.
- [ ] Add a migration note from old single-world static usage to new context usage.
- [ ] Run `rg -n "TrailblazerWorldManager" src/Trailblazer` and remove every production reference
  except the compatibility file if it still exists.
- [ ] Run full `Release` build and test.
- [ ] Run benchmark preflight tests and at least one short pathing benchmark group.
- [ ] Review `docs/feature-work/hardeningPlans.md` and either close, link, or explicitly defer any
  out-of-scope issues recorded during the migration.

Exit criteria:

- Trailblazer supports multiple active worlds in one process without static world switching.
- Context-first docs are the source of truth.
- Ambient default-world APIs are either compatibility-only or removed before alpha.

## Verification Plan

Use focused tests as each phase lands, then the full suite before closing the work:

```bash
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~TrailblazerWorldContextIsolation
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~Pathing
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~Navigator
dotnet test Trailblazer.slnx --configuration Release
```

Benchmark checks after pathing and guide phases:

```bash
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- guide-cache --job short --runtimes net8.0
dotnet run --project tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net8.0 -- nav-steering --job short --runtimes net8.0
```

Allocation checks should include the existing request-cache-key and warm-path coverage. If a phase
changes steady-state allocation behavior, include the measured before/after in the phase notes
before moving on.

## Design Confirmations

These are recommended defaults unless implementation uncovers a sharper constraint:

- Keep `TrailblazerLogger` process-wide. It is diagnostic plumbing, not world simulation state.
- Do not add a process-wide "simulate all contexts" loop as the primary API. Hosts should decide
  which contexts advance and in what order.
- Do not support moving an initialized navigator between contexts. Reset and rebind is simpler and
  easier to test.
- Let `TrailblazerWorldContext` optionally own the `GridWorld`, but default to host-owned worlds.
- Prefer context-local pools over global pools for world-owned objects. Pure scratch pools can stay
  static only when they do not retain world-specific references after release.
- Keep the public API context-first even when default-context compatibility exists. Developer
  experience should guide hosts toward correct multi-world ownership.
- Favor data structures that scale with the local context's active charts, grids, transitions, and
  navigators. Avoid process-wide scans and hidden global registries in runtime code.
- Record worthwhile out-of-scope issues in `docs/feature-work/hardeningPlans.md`; do not solve them
  with partial "while we are here" fixes inside this migration.

## Risks And Mitigations

| Risk | Mitigation |
| --- | --- |
| Large refactor touches pathing and navigation at once | Land phases independently with red isolation tests first. |
| Static facade hides accidental default-context use | Mark compatibility APIs obsolete early and add `rg` tracking. |
| Cache key regressions add allocations | Keep caches context-local and preserve existing hash-builder patterns. |
| Chart registration split changes overlap behavior | Add same-priority overlap tests before moving `RegistrationOrder`. |
| Grid event routing misses resets | Instance event bridge subscribes directly to one `GridWorld` and tests reset isolation. |
| Serialization accidentally captures context | Keep context as host binding and update serialization docs/tests. |
| Benchmarks accidentally reuse stale worlds | Add preflight coverage for multiple contexts in one process. |
| Context routing adds hidden hot-path overhead | Select context at API boundaries and pass direct state references through inner loops. |
| Compatibility APIs become permanent | Mark them obsolete early and include removal in Phase 7 exit criteria. |
| Out-of-scope issues derail migration phases | Track non-blocking issues in `hardeningPlans.md` with a validation signal. |

## Completion Definition

The migration is complete when:

- A host can create two `TrailblazerWorldContext` instances over two `GridWorld` instances.
- Both contexts can register same-named charts and same-named transitions independently.
- Both contexts can simulate path requests and navigators in the same process without cache, group,
  transition, frame, or reset interference.
- `README.md` and `docs/wiki/OVERVIEW.md` show context-first examples.
- Full `Release` tests pass.
- Short guide-cache and nav-steering benchmark groups still show no obvious allocation regression on
  warm paths.
- The context-first API remains engine-agnostic and deterministic by construction.
- Any non-blocking issues found during implementation are recorded in
  `docs/feature-work/hardeningPlans.md` instead of being left as tribal knowledge.
