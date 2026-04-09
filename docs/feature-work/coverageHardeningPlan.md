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

- line coverage: `91.00%` (`8915 / 9796`)
- branch coverage: `81.47%` (`3272 / 4016`)

### Subsystem Snapshot

| Subsystem | Line | Branch |
| --- | ---: | ---: |
| `Main` | `93.98%` | `84.72%` |
| `Navigation` | `90.81%` | `83.88%` |
| `Pathing` | `91.11%` | `80.37%` |
| `Serialization` | `82.00%` | `67.65%` |
| `Support` | `93.83%` | `80.00%` |

### Biggest File Gaps By Missed Lines

These are the highest-value files to target first.

| File | Line | Branch | Missed Lines | Missed Branches |
| --- | ---: | ---: | ---: | ---: |
| `Pathing/PathManager.cs` | `91.22%` | `81.48%` | `105` | `97` |
| `Navigation/Support/NavigatorPathRequestFactory.cs` | `68.48%` | `50.00%` | `98` | `36` |
| `Navigation/Steering/NavSteering.cs` | `89.60%` | `82.50%` | `61` | `35` |
| `Navigation/Motor/NavMotor.cs` | `93.07%` | `85.60%` | `39` | `72` |
| `Pathing/Search/PathGuideFactory.cs` | `85.76%` | `76.11%` | `37` | `32` |
| `Pathing/Search/Hybrid/HybridRoutePlanner.cs` | `88.95%` | `72.91%` | `37` | `26` |
| `Serialization/PathRequestRecord.cs` | `82.00%` | `67.64%` | `36` | `22` |
| `Navigation/Support/GuidedVolumeExitPlanner.cs` | `83.98%` | `64.06%` | `33` | `23` |
| `Main/Navigator.cs` | `92.50%` | `84.55%` | `30` | `21` |
| `Pathing/Transition/TraversalTransitionRegistry.cs` | `94.12%` | `88.54%` | `29` | `22` |

### Highest Observed CRAP Hotspots

These are approximate CRAP candidates derived from Cobertura method complexity and method
line-coverage. They are good prioritization signals even though the current workflow does not yet
publish CRAP directly.

| Method | Approx. CRAP | Notes |
| --- | ---: | --- |
| `NavigatorPathRequestFactory.TryCreateGasLandingHandoff` | `156` | still completely uncovered |
| `TraversalAuthoringMapExtensions.PrintXZPlane` | `110` | zero-coverage helper; likely Phase 5 triage material |
| `NavSteering.GetHeading` | `94.4` | still a major runtime hotspot |
| `PathManager.SuppressAllManagedGeneratedTransitions` | `72` | currently uncovered and looks dead-path adjacent |
| `PathGuideFactory.AddChartKeys` | `72` | uncovered helper inside an otherwise improved factory |
| `NavigatorPathRequestFactory.TryGetDirectVolumePathCost` | `72` | uncovered routing logic |
| `NavMotor.get_StateChanged` | `72` | zero-coverage event helper |
| `NavigatorPathRequestFactory.TryCreateVolumeExitHandoffIfNeeded` | `64.48` | still a high-value factory branch target |
| `FlowFieldGuide.TryGetStagedMovementDirection` | `48.34` | branch-heavy guide logic remains |
| `NavMotor.MaxHoritzontalSpeedInDirection` | `48.21` | still a meaningful motor branch hotspot |
| `PathManager.TryApplyChartCellUpdate` | `48.11` | critical pathing mutation branch matrix remains worth pinning |
| `PathRequestRecord.RestoreWaypointIndex` | `42` | serialization branch gap that Phase 5 may surface |

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

### Phase 5. Final Gap Closure And Exclusion Audit

Near-100% coverage usually stalls on tiny helpers, debug utilities, or defensive branches that are
hard to hit naturally. This phase is for disciplined cleanup, not denominator gaming.

Targets:

- small public/internal helpers still below threshold after Phases 1-4
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

1. finish the remaining `NavigatorPathRequestFactory` and `GuidedVolumeExitPlanner` coverage debt
2. decide whether any remaining `PathManager` dead-looking defensive branches should be tested or
   retired in Phase 5
3. move into the final gap-closure and exclusion-audit phase with the updated hotspot list

That sequence should keep us moving toward near-total coverage without inflating the suite around
branches that may no longer represent real runtime behavior.
