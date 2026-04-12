# Coverage Hardening Plan

## Goal

Drive Trailblazer toward near-100% line and branch coverage without bloating the test suite or
forcing low-value tests. Prioritize:

1. restoring trustworthy coverage reporting
2. closing the biggest real coverage gaps first
3. reducing high-CRAP hotspots where low coverage and high complexity overlap

This plan is intended to guide Track 7 from
[hardeningPhase2Plan.md](./hardeningPhase2Plan.md).

## Current Baseline

### Current Reporting State

The checked-in coverage pipeline is now pointed at `Trailblazer` correctly through
`coverlet.runsettings`.

Command used:

```bash
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj \
  --configuration Debug \
  --collect:"XPlat Code Coverage"
```

Current Trailblazer snapshot:

- line coverage: `94.65%` (`9482 / 10017`)
- branch coverage: `85.47%` (`3383 / 3958`)

### Subsystem Snapshot

| Subsystem | Line | Branch |
| --- | ---: | ---: |
| `Main` | `94.05%` | `84.93%` |
| `Navigation` | `95.31%` | `87.11%` |
| `Pathing` | `94.29%` | `84.44%` |
| `Serialization` | `96.00%` | `91.18%` |
| `Support` | `96.59%` | `90.00%` |

### Biggest File Gaps By Missed Lines

These are the highest-value files to target first.

| File | Line | Branch | Missed Lines | Missed Branches |
| --- | ---: | ---: | ---: | ---: |
| `Pathing/PathManager.cs` | `94.58%` | `83.33%` | `64` | `85` |
| `Navigation/Steering/NavSteering.cs` | `91.77%` | `83.17%` | `51` | `34` |
| `Pathing/Search/Hybrid/HybridRoutePlanner.cs` | `89.85%` | `73.96%` | `34` | `25` |
| `Navigation/Motor/NavMotor.cs` | `95.31%` | `87.20%` | `31` | `63` |
| `Main/Navigator.cs` | `92.50%` | `84.56%` | `30` | `21` |
| `Pathing/Transition/TraversalTransitionRegistry.cs` | `94.13%` | `88.54%` | `29` | `22` |
| `Navigation/Support/GuidedVolumeExitPlanner.cs` | `88.83%` | `76.56%` | `23` | `15` |
| `Pathing/Search/FlowField/FlowFieldGuide.cs` | `91.54%` | `81.82%` | `17` | `20` |
| `Pathing/Search/Support/PathHeap.cs` | `91.30%` | `85.71%` | `16` | `6` |
| `Pathing/Search/Volume/VolumeSurveyor.cs` | `92.72%` | `88.24%` | `15` | `12` |
| `Navigation/Support/NavigatorPathRequestFactory.cs` | `95.64%` | `87.88%` | `13` | `8` |

### Highest Observed CRAP Hotspots

These are approximate CRAP candidates derived from Cobertura method complexity and method
line-coverage. They are good prioritization signals even though the current workflow does not yet
publish CRAP directly.

| Method | Approx. CRAP | Notes |
| --- | ---: | --- |
| `PathRequest.TrySetDestination` | `28.85` | now the highest remaining approximate hotspot, but already below the Phase 5 threshold |
| `NavMotor.HandlePlatformTransitions` | `26.97` | still the densest remaining motor helper after the grouped cleanup pass |
| `NavSteering.RecordData` | `26.00` | serialization branch matrix is now the top steering holdout |
| `NavMotor.GetMaxAcceleration` | `24.21` | still branchy, but no longer near the danger zone |
| `NavSteering.ComputeCombinedSteering` | `24.05` | movement-group filtering remains the main steering branch cluster |
| `TraversalTransitionRegistry.SetManagedTransitionsSuppressed` | `22.95` | managed transition lifecycle is now well below the previous hotspot line |
| `FlowFieldGuide.TryGetStagedMovementDirection` | `21.70` | staged guide progression dropped under the Phase 5 target after the extraction pass |
| `PathManager.TryGetClosestActiveTransition` | `22.01` | query complexity is now moderate rather than hotspot-level |
| `HybridRoutePlanner.TryPlan` | `21.95` | still worth watching in Phase 6 if hybrid planning gets more branching |
| `PathManager.UnloadChart` | `20.08` | chart unload lifecycle no longer stands out as CRAP-heavy |

## Phased Plan

### Phase 0. Restore Trustworthy Coverage Reporting

Status: resolved in the current repo state. Keep this as a guardrail.

Targets:

- confirm `.github/workflows/coverage.yml` publishes a non-zero Trailblazer report
- extend the workflow artifact to publish machine-readable summary output if possible
- if practical, add a small local script or documented command to regenerate the current summary

Exit criteria:

- CI coverage report reflects Trailblazer rather than zero
- line and branch totals are visible and stable
- future coverage planning can rely on the published artifact

### Phase 1. Low-Coverage Leaf Types And Request/Guide Objects

Status: first pass landed.

Current results from this pass:

- `AStarGuide.cs`: `94.04%` line, `77.27%` branch
- `FlowFieldGuide.cs`: `86.85%` line, `75.49%` branch
- `HybridGuide.cs`: `89.47%` line, `69.04%` branch
- `HybridPathRequest.cs`: `94.64%` line, `73.43%` branch
- `PathRequest.cs`: `85.36%` line, `68.18%` branch
- `VolumeGuide.cs`: `92.20%` line, `77.77%` branch
- `VolumePathRequest.cs`: `92.30%` line, `66.12%` branch
- `TraversalTransitionOrdering.cs`: `75.86%` line, `71.87%` branch

Notes:

- the biggest remaining Phase 1 leaf gaps are now concentrated in branch-only cleanup for
  `HybridGuide`, `PathRequest`, `VolumePathRequest`, and `TraversalTransitionOrdering`
- `HybridPathRequest` moved from a major file gap into a much healthier state, so it no longer
  needs to anchor the next coverage pass

These files are low-risk and currently under-covered enough to move totals quickly.

Primary targets:

- `Pathing/Search/Hybrid/HybridGuide.cs`
- `Pathing/Search/Hybrid/HybridPathRequest.cs`
- `Pathing/Search/AStar/AStarGuide.cs`
- `Pathing/Search/FlowField/FlowFieldGuide.cs`
- `Pathing/Search/Volume/VolumeGuide.cs`
- `Pathing/Search/Volume/VolumePathRequest.cs`
- `Pathing/Search/Support/Request/PathRequest.cs`
- `Pathing/Transition/TraversalTransitionOrdering.cs`

Why first:

- these are relatively self-contained
- several are currently at or near zero coverage
- they carry large missed line and branch counts without requiring deep lifecycle scaffolding

CRAP overlap:

- `FlowFieldGuide.TryGetFallbackDirection`
- `FlowFieldGuide.TryGetStagedFallbackDirection`
- `HybridGuide.TryGetFallbackDirection`
- `VolumePathRequest.TrySetOrigin`
- `VolumePathRequest.TrySetDestination`

### Phase 2. Planner Entry Points And Request Factories

Status: first pass landed.

Current results from this pass:

- `NavigatorPathRequestFactory.cs`: `68.48%` line, `50.00%` branch
- `GuidedVolumeExitPlanner.cs`: `83.98%` line, `64.06%` branch
- `PathGuideFactory.cs`: `85.76%` line, `76.11%` branch
- `TrailblazerManager.cs`: `98.90%` line, `87.50%` branch

Notes:

- `TrailblazerManager` is effectively out of the Phase 2 danger zone after direct lifecycle-hook
  coverage
- `GuidedVolumeExitPlanner` and `PathGuideFactory` both moved into healthier territory, but still
  have branch debt left in transition-aware fallback paths
- `NavigatorPathRequestFactory` improved meaningfully, but it remains the biggest orchestration gap
  inside this phase and may warrant a second branch-focused cleanup pass before moving too far past
  navigation support

These are high-value orchestration seams that affect multiple runtime paths.

Primary targets:

- `Navigation/Support/NavigatorPathRequestFactory.cs`
- `Navigation/Support/GuidedVolumeExitPlanner.cs`
- `Pathing/Search/PathGuideFactory.cs`
- `Main/TrailblazerManager.cs`

Why second:

- they currently have large absolute gaps
- they connect host-facing API behavior to underlying planners
- closing them will improve confidence in request routing and hybrid/volume handoffs

CRAP overlap:

- `NavigatorPathRequestFactory.TryCreate`
- `NavigatorPathRequestFactory.TryCreateGasLandingHandoff`
- `NavigatorPathRequestFactory.TryCreateVolumeExitHandoffIfNeeded`
- `GuidedVolumeExitPlanner.TryGetTransitionAwareChartCost`

### Phase 3. Locomotion And Runtime State Coverage

Status: first pass landed.

Current results from this pass:

- `LocomotionHandler.cs`: `100.00%` line, `96.09%` branch
- `LocomotionProfile.cs`: `100.00%` line, `100.00%` branch
- `LocomotionProfileBuilder.cs`: `100.00%` line, `100.00%` branch
- `NavMotor.cs`: `93.07%` line, `85.60%` branch

Notes:

- the dedicated locomotion composition surfaces are no longer meaningful coverage hotspots after
  this pass
- the remaining Phase 3 debt is almost entirely `NavMotor` branch saturation rather than profile or
  handler composition behavior

This phase targets the navigation runtime branch matrix where complexity is high and under-testing is
still visible.

Primary targets:

- `Navigation/Motor/Locomotion/LocomotionHandler.cs`
- `Navigation/Motor/Locomotion/LocomotionProfile.cs`
- `Navigation/Motor/Locomotion/LocomotionProfileBuilder.cs`
- follow-up branch saturation in `Navigation/Motor/NavMotor.cs`

Why third:

- `LocomotionHandler` has the single highest observed CRAP hotspot by a wide margin
- locomotion selection and sync behavior are correctness-sensitive and easy to miss in smoke tests
- improving locomotion coverage should also reduce risk in motor behavior regressions

CRAP overlap:

- `LocomotionHandler.SetLocomotion`
- `LocomotionHandler.GetLocomotion`
- `LocomotionHandler.SyncTransientState`
- `LocomotionProfile.get_InstalledKinds`

### Phase 4. Core Pathing Branch Saturation

Status: second pass landed.

Current results from this pass:

- `TraversalAuthoringMap.cs`: `92.30%` line, `89.13%` branch
- `SolidChartPartition.cs`: `95.93%` line, `81.03%` branch
- `PathManager.cs`: `91.22%` line, `81.48%` branch
- `FlowFieldSurveyor.cs`: `96.61%` line, `90.56%` branch
- `VolumeSurveyor.cs`: `92.71%` line, `88.23%` branch
- `TraversalTransitionRegistry.cs`: `94.12%` line, `88.54%` branch

Notes:

- the second pass pushed `PathManager` over 91% line coverage and brought `FlowFieldSurveyor`
  above 90% branch coverage without touching runtime code
- `VolumeSurveyor`, `TraversalTransitionRegistry`, and `TraversalAuthoringMap` benefited the most
  from direct edge-case coverage
- the remaining Phase 4 debt is now much more concentrated in selective `PathManager` lifecycle
  helpers and dead-looking defensive branches than in the surveyors or registry surfaces

This phase is the heavier pathing pass. It should come after the simpler guide/request/runtime
surfaces above.

Primary targets:

- `Pathing/PathManager.cs`
- `Pathing/Transition/TraversalTransitionRegistry.cs`
- `Pathing/Search/FlowField/FlowFieldSurveyor.cs`
- `Pathing/Search/Volume/VolumeSurveyor.cs`
- `Pathing/Partition/SolidChartPartition.cs`
- `Pathing/Authoring/TraversalAuthoringMap.cs`

Why fourth:

- these files already have decent line coverage, but they still carry the largest absolute branch
  debt
- they are the most behavior-sensitive pathing surfaces
- improving them should focus on targeted branch matrices, not broad random test growth

CRAP overlap:

- `PathManager.TryApplyChartCellUpdate`
- `PathManager.RebuildInitializedChartsAgainstCurrentGrids`
- `NavSteering.GetHeading`
- `TraversalAuthoringMap.ParseCell`

### Phase 5. CRAP Hotspot Reduction And Complexity Cleanup

Status: completed.

Current results from this pass:

- overall snapshot: `93.36%` line, `83.54%` branch
- `NavigatorPathRequestFactory.cs`: `86.17%` line, `70.83%` branch
- `GuidedVolumeExitPlanner.cs`: `88.83%` line, `76.56%` branch
- `PathGuideFactory.cs`: `93.12%` line, `82.56%` branch
- `PathRequestRecord.cs`: `96.00%` line, `91.18%` branch
- `HybridWaypointFlattener.cs` (new internal extraction): `94.17%` line, `89.58%` branch
- `TraversalAuthoringMap.Extensions.cs`: `100.00%` line, `90.00%` branch
- `NavigationChart.Extensions.cs`: `100.00%` line, `100.00%` branch
- `NavSteering.cs`: `91.77%` line, `83.17%` branch
- `NavMotor.cs`: `95.31%` line, `87.20%` branch
- `FlowFieldGuide.cs`: `90.05%` line, `74.55%` branch
- `PathManager.cs`: `94.58%` line, `83.33%` branch
- `MovementGroupCoordinator.cs`: `93.83%` line, `90.90%` branch

Notes:

- the first Phase 5 pass removed the zero-coverage hotspot cluster around
  `NavigatorPathRequestFactory`, `PathGuideFactory.AddChartKeys`, `PathRequestRecord`,
  `NavMotor.StateChanged`, and the print helpers
- the second Phase 5 pass split the remaining heavy runtime methods into smaller traversal,
  finalize, locomotion-slot, movement-group, and animation helpers without broad subsystem churn
- the fresh approximate hotspot list no longer has any method above `30`, so the Phase 5 CRAP
  target is satisfied
- the remaining high-value work is now coverage-gap cleanup rather than hotspot reduction

This phase sits between broad coverage expansion and the final exclusion audit. Its purpose is to
drive down the highest remaining CRAP scores by combining targeted coverage with small, disciplined
complexity cleanup where coverage alone will not get the score below the goal line.

Primary objective:

- reduce every tracked hotspot from `Highest Observed CRAP Hotspots` and the phase-level
  `CRAP overlap` lists to below `30` where that is realistically possible without bloating the test
  suite or weakening the runtime design

Targets:

- `NavSteering.GetHeading`
- `NavSteering.CheckStuckStatus`
- `NavMotor.MaxHoritzontalSpeedInDirection`
- `NavMotor.GetDesiredVelocity`
- `NavMotor.ComputeMovementForces`
- `NavMotor.HandleFallState`
- `NavMotor.GetFlightSpeedMultiplier`
- `FlowFieldGuide.TryGetStagedMovementDirection`
- `PathManager.TryApplyChartCellUpdate`
- `MovementGroupCoordinator.UpdateTarget`
- any remaining still-relevant CRAP overlap methods from Phases 1-4 after the latest runtime
  hardening is reflected in the next coverage snapshot

Approach:

- start by regenerating the hotspot list after the latest hardening pass so we do not optimize
  methods that were already removed or simplified
- prefer targeted coverage first when the method is structurally sound and the score is inflated
  mostly by low coverage
- simplify or split methods only when the remaining score is being driven by real complexity rather
  than missing tests
- remove or retire dead defensive branches instead of writing artificial tests for them
- keep runtime changes small and local; this phase is not permission for broad architectural churn

Exit criteria:

- each tracked hotspot is below `30`, or
- the remaining exceptions are explicitly documented with why pushing below `30` would require
  disproportionate ceremony, artificial tests, or runtime churn

Outcome:

- satisfied in the current snapshot; no approximate hotspot remains above `30`

Why fifth:

- by this point the broad coverage work has already reduced the easy gaps
- the remaining high CRAP scores are where maintenance risk and test debt still overlap most
- tackling them before the final exclusion audit keeps Phase 6 focused on truly marginal leftovers

### Phase 6. Final Gap Closure And Exclusion Audit

Near-100% coverage usually stalls on tiny helpers, debug utilities, or defensive branches that are
hard to hit naturally. This phase is for disciplined cleanup, not denominator gaming.

Status: eighth pass landed.
Current results from eighth pass:

- overall snapshot: `96.11%` line, `88.62%` branch
- `AlternativeVoxelFinder.cs`: `95.5%` → `100.0%` line, `85.7%` → `97.6%` branch
- `GuidedVolumeExitPlanner.cs`: `99.4%` → `99.4%` line, `89.1%` → `98.3%` branch
- `PathManager.cs`: `95.8%` → `96.5%` line, `87.4%` → `88.1%` branch
- 605 total tests (597 → 605, +8 new tests)

- the eighth pass stayed disciplined around the two remaining branch-heavy holdouts instead of
  opening a new front
- `AlternativeVoxelFinder` now covers the positive-X and positive-Z ring-advance branches plus the
  bounded search exhaustion return, leaving only the structurally awkward null-short-circuit on the
  local candidate check uncovered
- `GuidedVolumeExitPlanner` now routes both request-type wrappers through one shared
  transition-aware chart-cost helper, then directly pins the null, zero-transition, and
  transition-present helper cases in tests
- `PathManager` shed another dead defensive branch in `AllCharts`, and direct tests now pin the
  public null-chart unload no-op plus the unsupported-medium default arm in
  `HasAuthoredVolumeMedium(...)`

Current results from seventh pass:

- overall snapshot: `96.00%` line, `88.26%` branch
- `HybridRoutePlanner.cs`: `89.8%` → `97.4%` line, `74.0%` → `91.0%` branch
- `GuidedVolumeExitPlanner.cs`: `88.8%` → `99.4%` line, `76.6%` → `89.1%` branch
- `NavSteering.cs`: `93.7%` → `96.5%` line, `86.1%` → `90.1%` branch
- 597 total tests (586 → 597, +11 new tests)

- the seventh pass concentrated on the two remaining scenario-heavy planner holdouts instead of
  widening scope again
- `HybridRoutePlanner` now has direct A* plan coverage, single solid-transition coverage, and a
  dual-medium chooser test that proves the cheaper local transition-pair plan wins
- `GuidedVolumeExitPlanner` now covers zero-displacement chart legs, out-of-grid target failure,
  and the disabled transition-aware fallback path for both A* and FlowField chart legs
- `NavSteering` picked up real multi-step runtime coverage for reacquiring direct volume
  line-of-sight and using guide-provided fallback directions during stuck recovery
- both planner files received small branch-reducing cleanups that collapse duplicate local/global
  success handling without changing routing behavior

Current results from sixth pass:

- overall snapshot: `95.38%` line, `87.03%` branch
- `VolumeChartPartition.cs`: new test file — `87.0%` → `98.1%` line, `50.0%` → `88.9%` branch
- `PathRequest.cs`: `85.5%` → `97.6%` line, `70.4%` → `86.4%` branch
- `AStarPathRequest.cs`: `95.9%` → `100.0%` line, `71.4%` → `85.7%` branch
- `MoveLocomotion.cs`: `92.3%` → (above threshold) line, `66.7%` → (above threshold) branch
- `TransientStateUtility.cs`: `93.5%` → (above threshold) line, `75.0%` → (above threshold) branch
- `GuidedVolumeExitHandoff.cs`: `94.5%` → (above threshold) line, `70.0%` → (above threshold) branch
- `HybridGuide.cs`: `90.8%` → (above threshold) line, `69.0%` → (above threshold) branch
- `AlternativeVoxelFinder.cs`: `89.3%` → `92.8%` line, `76.2%` → `83.3%` branch (partial improvement)
- 586 total tests (570 → 586, +16 new tests)
Current results from fourth pass:

- overall snapshot: `94.65%` line, `85.47%` branch
- `NavigatorPathRequestFactory.cs`: `89.1%` → `95.6%` line, `77.3%` → `87.9%` branch
- `SolidVoxelFinder.cs`: `98.8%` line, `80.0%` → `90.0%` branch
- `ParsedTraversalCell.cs`: `100.0%` line, `50.0%` → `100.0%` branch
- `TraversalBuildResult.cs`: `100.0%` line, `50.0%` → `100.0%` branch
- `TraversalLegend.cs`: `89.3%` → `100.0%` line, `50.0%` → `100.0%` branch
- `HybridRoutePlan.cs`: `100.0%` line, `50.0%` → `100.0%` branch
- `FlowFieldPathRequest.cs`: `95.1%` → `100.0%` line, `64.3%` → `85.7%` branch
- 558 total tests (539 → 558, +19 new tests)

Current results from third pass:

- overall snapshot: `94.39%` line, `84.80%` branch
- `ITransient.Extensions.cs`: `50.0%` → `100.0%` line, `100.0%` branch
- `EndpointVoxelResolver.cs`: `81.4%` → `95.2%` line, `92.1%` branch
- `SolidVoxelFinder.cs`: `83.9%` → `98.8%` line, `80.0%` branch
- `NavigatorPathRequestFactory.cs`: `89.5%` → `89.1%` line, `75.0%` → `77.3%` branch
- 539 total tests (528 → 539, +11 new tests)

Current results from second pass:

- overall snapshot: `94.27%` line, `85.39%` branch
- `HybridRoutePlanner.cs`: `89.0%` → `94.8%` line, `72.9%` → `80.2%` branch
- `FlowFieldGuide.cs`: `90.0%` → `92.8%` line, `74.5%` → `85.4%` branch
- `NavigatorPathRequestFactory.cs`: `86.2%` → `89.5%` line, `70.8%` → `75.0%` branch
- `TraversalTransitionAnchor.cs`: `93.8%` → `91.8%` line (TraversalTransitionRegistry.Reset cleanup)
- `TraversalTransitionOrdering.cs`: `93.1%` → `91.8%` line (ditto)
- 528 total tests (517 → 528, +11 new tests)

Notes:

- the fifth pass focused on the remaining real branch families in `NavigatorPathRequestFactory`,
  `PathManager`, and `NavSteering` instead of widening scope again
- `NavigatorPathRequestFactory` is now fully covered after adding direct tests for the internal
  invalid-mode default, missing-target-voxel fallback, precise target / snapped-endpoint landing
  choice, cheaper gas-landing choice, and zero-displacement direct-cost branch
- `PathManager` got a small cleanup pass while adding coverage: dead private null checks were
  removed from `CompareChartsByRegistrationOrder(...)` and `TryApplyChartCellUpdate(...)`, and the
  resolved-state update helper now treats "nothing to remove" as a true no-op instead of a special
  false return
- the new `PathManager` tests pin no-op chart updates, last-owner removal to empty, deferred chart
  mutation while the backing grid is missing, and non-intersecting grid-version changes
- `NavSteering` picked up two cheap-but-real scenario dents: `CanMove == false` early return and
  the `TryEnsureCurrentRequest(...)` arrival path when movement is requested without a live request

Current results from fifth pass:

- overall snapshot: `94.94%` line, `86.06%` branch
- `NavigatorPathRequestFactory.cs`: `95.6%` → `100.0%` line, `87.9%` → `100.0%` branch
- `PathManager.cs`: `94.6%` → `95.5%` line, `83.3%` → `85.8%` branch
- `NavSteering.cs`: `91.8%` → `92.6%` line, `83.2%` → `84.7%` branch
- 570 total tests (558 → 570, +12 new tests)

- the fourth pass combined one more targeted cleanup pass with direct value-object coverage rather
  than forcing heavy new steering/path-manager harnesses
- `NavigatorPathRequestFactory` dropped the now-unnecessary `TryAssignRequest(...)` helper and got
  dedicated snapped-endpoint handoff coverage for aerial and swim orchestration branches
- `TraversalLegend` shed an actually dead private `allowEmpty` branch in token normalization, then
  got direct null-token normalization tests so the simplified behavior is still pinned
- the smaller support/pathing types (`ParsedTraversalCell`, `TraversalBuildResult`,
  `HybridRoutePlan`) are no longer carrying low-value branch debt
- `FlowFieldPathRequest` now has direct factory/equality coverage rather than relying only on
  indirect surveyor/navigation tests
- the third pass moved transient reflection/caching into a focused internal
  `TransientStateUtility`, then routed both the interface default methods and public extension API
  through that shared implementation
- `ITransientExtensions.SyncTransientState(...)` now has a direct public-API test, so the file is no
  longer carrying uncovered convenience surface
- `NavigatorPathRequestFactory` had the dead-looking null branch removed from direct volume-cost
  evaluation, leaving the remaining gap concentrated in real handoff orchestration branches
- the pass also exposed and fixed a correctness bug in `SolidVoxelFinder.GetClosestVoxelForSize(...)`
  where the wrapper was resolving from `origin` instead of `target`
- `HybridRoutePlanner` received dedicated FlowField-kind, Liquid-pair, and Gas-pair tests that
  exercise `TryCreateFlowFieldStep`, `TryCreateVolumeStep`, and `TryCreateAStarStep` zero-displacement
  path — the three previously uncovered private methods
- `FlowFieldGuide` got two new tests: goal-position zero-direction (covers `direction == Vector3d.Zero`
  branch in `TryGetMovementDirection`) and staged-AStarGuide `FlowFieldContainsPosition` (covers the
  non-`FlowFieldGuide` staged cast path)
- `NavigatorPathRequestFactory` got null-chart tests for both the simple and internal `TryCreate`
  overloads covering AStar null, FlowField null, Aerial null, and Swim null paths, plus a dedicated
  Swim / `TryCreateGasLandingHandoff` medium!=Gas early-return test
- `ITransient.Extensions.cs` was evaluated for dead-code removal; the `ClearTransientState` extension
  IS called from concrete locomotion types via `this.ClearTransientState()` (default interface methods
  are not accessible without casting), so the file was retained — only the `SyncTransientState`
  extension is currently uncovered (no concrete type calls it directly)

Remaining known gaps after Phase 6 eighth pass:

- `PathManager.cs`: `96.5%` line / `88.1%` branch — the remaining debt is now concentrated in
  narrow mutation/invalidation helper combinations rather than chart lifecycle or defensive guards

Newly resolved from the previous gap list:

- `AlternativeVoxelFinder.cs`: now above threshold after saturating the ring-advance and
  search-exhaustion branches
- `GuidedVolumeExitPlanner.cs`: now above threshold after the shared transition-aware helper pass
- `HybridRoutePlanner.cs`: now above threshold after the focused planner pass and helper cleanup
- `NavSteering.cs`: now above threshold after direct LOS refresh and stuck fallback coverage

Previously resolved from the gap list:

- `VolumeChartPartition.cs`: line and branch coverage now above threshold; new test file covers HasAnyOwners, HandleChange, SupportsMedium default arm, BelongsTo, and ApplyAuthoredState
- `PathRequest.cs`: TrySetDestination success path now covered after adding a destination-change test
- `AStarPathRequest.cs`: Equals(object) and Equals(AStarPathRequest) overloads now fully covered
- `MoveLocomotion.cs`: IsEnabled setter ClearTransientState path now covered
- `TransientStateUtility.cs`: empty-delegate fast path (no transient properties) and static-property default lookup now covered
- `GuidedVolumeExitHandoff.cs`: IsValid=false early return, AStar-null create failure, and FlowField-null create failure branches now covered
- `HybridGuide.cs`: null/empty ActiveWaypoints guards and exhausted-index return now covered

Remaining known gaps after Phase 6 fifth pass:

- `PathManager.cs`: `95.5%` line / `85.8%` branch — now mostly narrow mutation/invalidation helper
  combinations rather than any broad chart-lifecycle holes
- `NavSteering.cs`: `92.6%` line / `84.7%` branch — the remaining misses are concentrated in
  line-of-sight refresh, stuck recovery, fallback-direction handling, and other multi-step
  simulation-state branches
- `SolidVoxelFinder.cs`: `98.8%` line / `90.0%` branch — no longer an urgent holdout, but the
  remaining branch debt is still inside the private `StarCast(...)` fallback path

Newly resolved from the previous gap list:

- `NavigatorPathRequestFactory.cs`: now fully covered

Newly resolved from the previous gap list:

- `ParsedTraversalCell.cs`: now fully covered
- `TraversalBuildResult.cs`: now fully covered
- `TraversalLegend.cs`: now fully covered after removing the dead normalization branch
- `HybridRoutePlan.cs`: now fully covered
- `FlowFieldPathRequest.cs`: no longer a meaningful final-gap candidate

Resolved from the previous gap list:

- `ITransient.Extensions.cs`: now fully covered
- `EndpointVoxelResolver.cs`: no longer a notable gap after dedicated policy/fallback tests

First pass results (preserved):

- overall snapshot: `94.04%` line, `84.32%` branch
- `MotorOutput.cs`: `0%` → `100%` line, `100%` branch
- `NavigationChartCell.cs`: `70%` → `100%` line, `100%` branch
- `VolumeMediumRules.cs`: `80.9%` → `100%` line, `100%` branch
- `ChartOwnerUtility.cs`: `83.3%` → `100%` line, `100%` branch
- `ManagedChartTransitionState.cs`: `85.7%` → `100%` line, `100%` branch
- `FlyLocomotion.cs`: `82.9%` → `100%` line, `100%` branch
- `TraversalTransitionAnchor.cs`: `81.5%` → `93.8%` line, `90.0%` branch
- `TraversalTransitionOrdering.cs`: `75.9%` → `93.1%` line, `87.5%` branch
- `PathHeap.cs`: `84.8%` → `91.3%` line, `85.7%` branch
- `NavSteering.cs`: `91.8%` line, `83.2%` branch (RecordData idle/no-request branches covered)
- 47 new tests added first pass (470 → 517 total)

Targets:

- small public/internal helpers still below threshold after Phases 1-5
- branch-only gaps in otherwise well-covered files
- convenience or debug-facing helpers such as `PrintXZPlane` extensions

Rules:

- prefer real tests first
- only exclude from coverage if the code is genuinely non-runtime, debug-only, or structurally not
  worth exercising through tests
- document every exclusion reason if any are added

## Execution Strategy

### Testing Order

For each phase:

1. add focused tests for the target file(s)
2. run the smallest relevant test slice first
3. rerun the full coverage snapshot after the phase lands
4. update this plan with new totals and newly exposed hotspots

### Stop Conditions

We should pause and reconsider before pushing to 100% if any remaining gap is mostly caused by:

- debug/dev-only helpers
- impossible defensive branches
- extremely high ceremony for tiny value

The goal is near-total confidence, not artificial tests that make the suite harder to maintain.

## Suggested Next Moves

Recommended immediate order:

1. keep Phase 6 focused on the genuinely scenario-heavy holdouts: `PathManager`, `NavSteering`,
   `Navigator`, and `HybridRoutePlanner`
2. treat `SolidVoxelFinder` as optional polish only if we can close the remaining private `StarCast`
   fallback branch without adding brittle harness code
2. only add more runtime cleanup when a remaining gap is clearly dead or structurally unreachable,
   like the `TraversalLegend` normalization branch that was removed in the fourth pass
3. if the remaining misses shrink down to tiny private/default paths, make an explicit keep-vs-exclude
   call instead of forcing high-ceremony tests around them

That sequence should keep us moving toward near-total coverage without inflating the suite around
branches that may no longer represent real runtime behavior.
