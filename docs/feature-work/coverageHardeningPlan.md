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
  --collect:"XPlat Code Coverage" \
  --results-directory artifacts/coverage-current
```

Current Trailblazer snapshot:

- line coverage: `93.06%` (`9088 / 9766`)
- branch coverage: `83.66%` (`3348 / 4002`)

### Subsystem Snapshot

| Subsystem | Line | Branch |
| --- | ---: | ---: |
| `Main` | `94.05%` | `84.93%` |
| `Navigation` | `93.08%` | `86.06%` |
| `Pathing` | `92.85%` | `82.09%` |
| `Serialization` | `96.00%` | `91.18%` |
| `Support` | `93.83%` | `80.00%` |

### Biggest File Gaps By Missed Lines

These are the highest-value files to target first.

| File | Line | Branch | Missed Lines | Missed Branches |
| --- | ---: | ---: | ---: | ---: |
| `Pathing/PathManager.cs` | `94.62%` | `83.73%` | `62` | `83` |
| `Navigation/Steering/NavSteering.cs` | `89.61%` | `82.50%` | `61` | `35` |
| `Navigation/Support/NavigatorPathRequestFactory.cs` | `86.17%` | `70.83%` | `43` | `21` |
| `Pathing/Search/Hybrid/HybridRoutePlanner.cs` | `88.96%` | `72.92%` | `37` | `26` |
| `Navigation/Motor/NavMotor.cs` | `93.61%` | `86.80%` | `36` | `66` |
| `Main/Navigator.cs` | `92.50%` | `84.56%` | `30` | `21` |
| `Pathing/Transition/TraversalTransitionRegistry.cs` | `94.13%` | `88.54%` | `29` | `22` |
| `Navigation/Support/GuidedVolumeExitPlanner.cs` | `88.83%` | `76.56%` | `23` | `15` |
| `Pathing/Search/FlowField/FlowFieldGuide.cs` | `88.04%` | `73.58%` | `22` | `28` |
| `Pathing/Search/Support/PathHeap.cs` | `84.78%` | `83.33%` | `21` | `7` |

### Highest Observed CRAP Hotspots

These are approximate CRAP candidates derived from Cobertura method complexity and method
line-coverage. They are good prioritization signals even though the current workflow does not yet
publish CRAP directly.

| Method | Approx. CRAP | Notes |
| --- | ---: | --- |
| `NavSteering.GetHeading` | `94.38` | still the dominant runtime hotspot after the easy CRAP cleanup landed |
| `FlowFieldGuide.TryGetStagedMovementDirection` | `52.98` | staged-plan progression still carries the biggest remaining guide overlap |
| `NavMotor.MaxHoritzontalSpeedInDirection` | `48.20` | flight/motor rate logic remains branch-heavy |
| `PathManager.TryApplyChartCellUpdate` | `48.09` | chart mutation validation matrix is still the main pathing hotspot |
| `NavMotor.GetDesiredVelocity` | `46.26` | still dense despite high line coverage |
| `NavMotor.ComputeMovementForces` | `44.03` | complexity is now the main driver rather than missing lines |
| `NavMotor.HandleFallState` | `42.26` | still sits above goal line on complexity alone |
| `NavSteering.CheckStuckStatus` | `39.93` | lower than `GetHeading`, but still worth treating as part of the same steering cluster |
| `MovementGroupCoordinator.UpdateTarget` | `38.10` | almost entirely a complexity issue now |
| `NavMotor.GetFlightSpeedMultiplier` | `31.60` | small but still just above the Phase 5 target line |
| `NavAnimationUpdater.UpdateAnimationParameters` | `30.68` | barely above target; likely a Phase 6 cleanup decision |

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

Status: first pass landed.

Current results from this pass:

- overall snapshot: `93.06%` line, `83.66%` branch
- `NavigatorPathRequestFactory.cs`: `86.17%` line, `70.83%` branch
- `GuidedVolumeExitPlanner.cs`: `88.83%` line, `76.56%` branch
- `PathGuideFactory.cs`: `93.12%` line, `82.56%` branch
- `PathRequestRecord.cs`: `96.00%` line, `91.18%` branch
- `HybridWaypointFlattener.cs` (new internal extraction): `94.17%` line, `89.58%` branch
- `TraversalAuthoringMap.Extensions.cs`: `100.00%` line, `90.00%` branch
- `NavigationChart.Extensions.cs`: `100.00%` line, `100.00%` branch

Notes:

- the first Phase 5 pass removed the zero-coverage hotspot cluster around
  `NavigatorPathRequestFactory`, `PathGuideFactory.AddChartKeys`, `PathRequestRecord`,
  `NavMotor.StateChanged`, and the print helpers
- the remaining CRAP debt is now concentrated in the real runtime heavyweights rather than small
  uncovered helpers
- the next Phase 5 pass should stay focused on `NavSteering`, staged `FlowFieldGuide` logic,
  selective `NavMotor` helpers, and `PathManager.TryApplyChartCellUpdate`

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

Why fifth:

- by this point the broad coverage work has already reduced the easy gaps
- the remaining high CRAP scores are where maintenance risk and test debt still overlap most
- tackling them before the final exclusion audit keeps Phase 6 focused on truly marginal leftovers

### Phase 6. Final Gap Closure And Exclusion Audit

Near-100% coverage usually stalls on tiny helpers, debug utilities, or defensive branches that are
hard to hit naturally. This phase is for disciplined cleanup, not denominator gaming.

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

1. continue Phase 5 with the concentrated runtime hotspots in `NavSteering`, `FlowFieldGuide`,
   `NavMotor`, and `PathManager`
2. prefer grouped helper extraction when those classes start accumulating same-purpose logic rather
   than forcing more branch-only tests into already large files
3. move into the final Phase 6 gap-closure and exclusion-audit pass only after the remaining CRAP
   exceptions are clearly understood

That sequence should keep us moving toward near-total coverage without inflating the suite around
branches that may no longer represent real runtime behavior.
