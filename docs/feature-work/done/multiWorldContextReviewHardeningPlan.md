# Multi-World Context Review Hardening Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the correctness, lifecycle, performance, and deduplication
issues found while reviewing commits `c81eb7ba6bcfdfd047fd4ff34c2ac250e326c090`
through `958ba9883fd023129a4d81d7faca6e4aa48d8010`.

**Architecture:** Keep the context-owned design. Fix ownership leaks by routing
callbacks and guide returns through the owning `TrailblazerWorldContext` or
`PathingWorldState`, not by adding global maps or ambient fallback behavior.

**Tech Stack:** C# 11, `netstandard2.1`, `net8.0`, GridForge `GridWorld`,
FixedMathSharp, SwiftCollections, xUnit v3, FluentAssertions, BenchmarkDotNet.

---

## Review Findings

The `Release` test suite passes, but the current coverage does not pin several
edge cases introduced or exposed by the context migration:

- **P0:** `SolidChartPartition.HandleChange(...)` invalidates reachability
  through `SolidPartitionReachability.Invalidate()`, which reads
  `PathManager.ActiveState`. Obstacle callbacks can fire from host/GridForge
  code outside a `PathManager.EnterState(...)` scope, so this can throw or
  invalidate the wrong context.
- **P0:** `TrailblazerGuideService.ReturnGuide(...)` accepts any guide while
  selecting the receiver's context state. Returning a guide to the wrong context
  can decrement the wrong in-use counter, return a wrapper to the wrong pool,
  and leave the owning cache thinking the result is still checked out.
- **P1:** `Navigator.Reset()` drops the context without asking `NavSteering` to
  return an active guide. Reusing or discarding a guided navigator can leak
  checked-out guide results.
- **P1:** Multiple `TrailblazerWorldContext` instances can attach to the same
  `GridWorld`. Those contexts subscribe to the same grid events and mutate the
  same live voxel partitions, which violates the "one live pathing owner per
  world" assumption.
- **P2:** Reset and teardown language is inconsistent.
  `TrailblazerWorldContext.Reset()` resets clock and navigation state only,
  `TrailblazerPathingService.Reset()` is internal, while docs still describe
  `Reset()`/`PathManager.Reset()` as pathing teardown.
- **P2:** Context-owned caches and locks are reset on dispose but not disposed.
  This is low immediate risk, but repeated short-lived contexts can accumulate
  undisposed `ReaderWriterLockSlim` and cache lock resources.
- **P3:** Some public/shared helper surfaces still preserve ambient patterns
  (`AlternativeVoxelFinder.Shared`, survey-result factories that infer the
  active state, and static flow-field helpers without context). Production paths
  mostly avoid them, but they are easy to misuse and add test-only escape
  hatches. Because Trailblazer is pre-alpha, remove these compatibility bridges
  instead of preserving or obsoleting them for external compatibility.

## Phase 1 - P0 Context Ownership Fixes

**Files:**

- Modify: `src/Trailblazer/Pathing/Partition/SolidChartPartition.cs`
- Modify:
  `src/Trailblazer/Pathing/Search/Support/Reachability/SolidPartitionReachability.cs`
- Modify: `src/Trailblazer/Pathing/Search/TrailblazerGuideService.cs`
- Modify: `src/Trailblazer/Pathing/Search/PathGuideFactory.cs`
- Modify:
  `src/Trailblazer/Pathing/Search/Support/Survey/ReusableSurveyResultCache.cs`
- Test: `tests/Trailblazer.Tests/Pathing/SolidChartPartition.Tests.cs`
- Test: `tests/Trailblazer.Tests/Worlds/ContextBoundPathRequestTests.cs`

- [x] **Step 1: Add a failing obstacle-callback ownership test**

  Add a test that creates a standalone `TrailblazerWorldContext`, registers an
  initialized solid chart, fetches the registered voxel, and calls
  `TryAddObstacle(...)` without keeping a `PathManager.EnterState(...)` scope
  open. The expected result is no exception and the owning context's
  reachability version changes.

  Run:

  ```bash
  dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~SolidChartPartition
  ```

  Expected before implementation: failure from missing active pathing state or
  unchanged owner stats.

- [x] **Step 2: Route reachability invalidation through the partition owner**

  Add an overload such as
  `SolidPartitionReachability.Invalidate(PathingWorldState state)` and update
  `SolidChartPartition.HandleChange(...)` to use `OwnerState`. Keep the existing
  parameterless `Invalidate()` only for code already executing inside a selected
  pathing state.

- [x] **Step 3: Add a failing wrong-context guide-return test**

  Request an `AStarGuide` from `contextA`, then attempt to return it through
  `contextB.Guides`. The test should assert a clear `InvalidOperationException`
  and then verify that returning through `contextA.Guides` restores
  `contextA.Guides.InUseAStarGuideCount` to zero.

- [x] **Step 4: Enforce guide ownership on return**

  Teach `TrailblazerGuideService.ReturnGuide(...)` or
  `PathGuideFactory.ReturnGuide(...)` to resolve the guide's owner from its
  survey result context before mutating any cache or pool. Reject wrong-context
  returns with a deterministic exception message. Keep null returns as no-ops.

- [x] **Step 5: Run focused verification**

  ```bash
  dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~SolidChartPartition
  dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~ContextBoundPathRequestTests
  ```

## Phase 2 - P1 Navigator And World Attachment Lifecycle

**Files:**

- Modify: `src/Trailblazer/Main/Navigator.cs`
- Modify: `src/Trailblazer/Navigation/Steering/NavSteering.cs`
- Modify: `src/Trailblazer/Main/TrailblazerWorldContext.cs`
- Modify: `src/Trailblazer/Pathing/PathingWorldGridBridge.cs`
- Test: `tests/Trailblazer.Tests/Worlds/ContextBoundNavigatorTests.cs`
- Test: `tests/Trailblazer.Tests/Worlds/TrailblazerWorldContextTests.cs`

- [x] **Step 1: Add a navigator reset guide-return regression test**

  Create a guided navigator, let steering acquire a guide, call
  `navigator.Reset()`, and assert the owning context no longer reports the guide
  as in use.

- [x] **Step 2: Add an explicit steering clear/reset API**

  Add an internal `NavSteering.Reset()` or `ClearActiveGuide()` method that
  releases `_trailGuide`, removes movement-group membership, and clears
  `_currentRequest` without firing arrival events. Call it from
  `Navigator.Reset()` before `_context` is cleared.

- [x] **Step 3: Add a duplicate-attach regression test**

  Attach one `GridWorld` to `contextA`, then attempt
  `TrailblazerWorldContext.Attach(sameWorld)`. The expected behavior should be a
  clear exception unless the first context has been disposed.

- [x] **Step 4: Track active world ownership**

  Add a small ownership guard for `TrailblazerWorldContext.Attach(...)`. Prefer
  a weak or disposal-aware registry so disposed contexts do not permanently
  reserve a host-owned `GridWorld`. Do not put this registry on a hot path; it
  should only run during context construction/disposal.

- [x] **Step 5: Run focused verification**

  ```bash
  dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~ContextBoundNavigatorTests
  dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~TrailblazerWorldContextTests
  ```

## Phase 3 - P2 Reset, Disposal, And Docs Alignment

**Files:**

- Modify: `src/Trailblazer/Main/TrailblazerWorldContext.cs`
- Modify: `src/Trailblazer/Pathing/TrailblazerPathingService.cs`
- Modify: `src/Trailblazer/Pathing/PathingWorldState.cs`
- Modify: `src/Trailblazer/Pathing/Search/TrailblazerGuideState.cs`
- Modify:
  `src/Trailblazer/Pathing/Transition/TraversalTransitionRegistryState.cs`
- Modify: `docs/wiki/PathManager.md`
- Modify: `docs/wiki/VolumeTraversal.md`
- Modify: `docs/wiki/Transitions.md`
- Test: `tests/Trailblazer.Tests/TrailblazerWorldContextLifecycle.Tests.cs`

- [x] **Step 1: Pin reset semantics**

  Keep `TrailblazerWorldContext.Reset()` as clock/navigation reset and make
  `TrailblazerPathingService.Reset()` public for pathing teardown. This
  preserves the existing context lifecycle tests while giving hosts a
  context-local replacement for the now-internal `PathManager.Reset()` surface.

- [x] **Step 2: Add the reset test before changing implementation**

  Add tests proving that `contextA.Pathing.Reset()` clears only context A's
  chart registry, transition registry, volume rule state, guide cache, and
  reachability state, while leaving context B intact. Keep the existing
  `context.Reset()` tests focused on frame count, hooks, movement groups, and
  navigator id allocator behavior.

- [x] **Step 3: Dispose context-owned disposable state**

  Implement disposal for `PathingWorldState` and `TrailblazerGuideState`.
  Dispose `ReaderWriterLockSlim` instances and `ReusableSurveyResultCache<T>`
  instances after force-flushing cached results. Keep disposal idempotent.

- [x] **Step 4: Update docs**

  Replace stale `PathManager.Reset()` references with `context.Pathing.Reset()`
  for host-facing teardown. Keep `GridWorld.Reset()` behavior documented as a
  world-event pathing teardown.

- [x] **Step 5: Run focused verification**

  ```bash
  dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~TrailblazerWorldContextLifecycleTests
  ```

## Phase 4 - P3 API Cleanup And Deduplication

**Files:**

- Modify: `src/Trailblazer/Pathing/TrailblazerPathingService.cs`
- Modify: `src/Trailblazer/Pathing/Search/TrailblazerGuideService.cs`
- Modify: `src/Trailblazer/Pathing/Transition/TrailblazerTransitionService.cs`
- Modify:
  `src/Trailblazer/Pathing/Search/Support/VoxelFinder/AlternativeVoxelFinder.cs`
- Modify: `src/Trailblazer/Pathing/Search/AStar/AStarSurveyResult.cs`
- Modify: `src/Trailblazer/Pathing/Search/FlowField/FlowFieldSurveyResult.cs`
- Modify: `src/Trailblazer/Pathing/Search/Volume/VolumeSurveyResult.cs`
- Test: `tests/Trailblazer.Tests/Architecture/RuntimeArchitectureGuardTests.cs`

- [x] **Step 1: Deduplicate context-scope wrappers**

  Add a focused helper on each service, or a shared internal helper, for the
  repeated `EnsureUsable(); using (PathManager.EnterState(State)) ...` pattern.
  Keep it allocation-free by using direct methods rather than delegate-heavy
  hot-path wrappers where the call is performance sensitive.

- [x] **Step 2: Remove ambient public helper paths**

  Remove `AlternativeVoxelFinder.Shared` and any other ambient helper bridge
  that exists only for legacy or test convenience. Prefer context-owned
  `context.Pathing.State.AlternativeVoxelFinder` in production and update tests
  to construct or access a context-owned finder.

- [x] **Step 3: Make survey-result context ownership explicit**

  Replace public factory overloads that infer `PathManager.ActiveState` with
  explicit context overloads. Delete active-state factory overloads that exist
  only as test convenience or compatibility bridges.

- [x] **Step 4: Expand architecture guards**

  Add guard coverage for new production references to
  `PathManager.TryGetActiveState(...)` outside known implementation facades.
  This should steer new code toward explicit context parameters.

## Final Verification

- [x] Run the full `Release` suite:

  ```bash
  dotnet test Trailblazer.slnx --configuration Release
  ```

- [x] Run `git diff --check`.
- [ ] Re-run any benchmark allocation checks touched by guide return or context
      reset behavior.
- [ ] If `ReleaseLean` remains in scope, resolve the current `Chronicler.Lean`
      restore issue or document why lean packaging is intentionally unavailable
      before alpha.
