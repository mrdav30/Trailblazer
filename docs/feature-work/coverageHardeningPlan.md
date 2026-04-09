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

- line coverage: `82.91%` (`8122 / 9796`)
- branch coverage: `72.23%` (`2901 / 4016`)

### Subsystem Snapshot

| Subsystem | Line | Branch |
| --- | ---: | ---: |
| `Main` | `86.60%` | `83.33%` |
| `Navigation` | `82.55%` | `75.71%` |
| `Pathing` | `82.77%` | `69.84%` |
| `Serialization` | `82.00%` | `67.65%` |
| `Support` | `85.19%` | `70.00%` |

### Biggest File Gaps By Missed Lines

These are the highest-value files to target first.

| File | Line | Branch | Missed Lines | Missed Branches |
| --- | ---: | ---: | ---: | ---: |
| `Pathing/PathManager.cs` | `87.63%` | `76.53%` | `148` | `123` |
| `Navigation/Support/NavigatorPathRequestFactory.cs` | `53.38%` | `37.50%` | `145` | `45` |
| `Pathing/Search/Hybrid/HybridPathRequest.cs` | `30.95%` | `26.56%` | `116` | `47` |
| `Navigation/Motor/Locomotion/LocomotionHandler.cs` | `55.33%` | `38.28%` | `88` | `79` |
| `Pathing/Search/Hybrid/HybridGuide.cs` | `0.00%` | `0.00%` | `76` | `42` |
| `Pathing/Search/FlowField/FlowFieldGuide.cs` | `58.86%` | `43.14%` | `72` | `58` |
| `Pathing/Search/AStar/AStarGuide.cs` | `32.14%` | `38.64%` | `57` | `27` |
| `Pathing/Search/Volume/VolumeGuide.cs` | `29.87%` | `27.78%` | `54` | `26` |
| `Navigation/Support/GuidedVolumeExitPlanner.cs` | `74.27%` | `54.69%` | `53` | `29` |
| `Pathing/Search/PathGuideFactory.cs` | `80.38%` | `67.16%` | `51` | `44` |

### Highest Observed CRAP Hotspots

These are approximate CRAP candidates derived from Cobertura method complexity and method
line-coverage. They are good prioritization signals even though the current workflow does not yet
publish CRAP directly.

| Method | Approx. CRAP | Notes |
| --- | ---: | --- |
| `LocomotionHandler.SetLocomotion` | `1406` | zero coverage and very high complexity |
| `NavigatorPathRequestFactory.TryCreateGasLandingHandoff` | `156` | zero coverage |
| `FlowFieldGuide.TryGetFallbackDirection` | `156` | zero coverage |
| `FlowFieldGuide.TryGetStagedFallbackDirection` | `156` | zero coverage |
| `NavigatorPathRequestFactory.TryCreate` | `132` | zero coverage |
| `LocomotionProfile.get_InstalledKinds` | `110` | zero coverage |
| `FlowFieldSurveyor.TryGetNearestFlowAnchor` | `110` | zero coverage |
| `HybridGuide.TryGetFallbackDirection` | `110` | zero coverage |
| `LocomotionHandler.GetLocomotion` | `95` | partially covered, high complexity |
| `NavSteering.GetHeading` | `94.4` | already important runtime hotspot |
| `NavigatorPathRequestFactory.TryCreateVolumeExitHandoffIfNeeded` | `64.48` | low coverage, medium-high complexity |
| `HybridGuide.TryGetStagedMovementDirection` | `56.04` | large guide gap |
| `TraversalAuthoringMap.ParseCell` | `49.08` | parser branch saturation gap |
| `PathManager.TryApplyChartCellUpdate` | `48.11` | critical pathing mutation branch matrix |
| `VolumePathRequest.TrySetOrigin` / `TrySetDestination` | `44.81` | request validation gaps |

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

1. fix the coverage workflow misconfiguration
2. cover `HybridGuide`, `HybridPathRequest`, `AStarGuide`, `FlowFieldGuide`, and `VolumeGuide`
3. move into `NavigatorPathRequestFactory` and `GuidedVolumeExitPlanner`
4. attack `LocomotionHandler.SetLocomotion` before any broad refactor there

That sequence should improve both raw totals and the highest CRAP hotspots quickly without jumping
straight into the deepest `PathManager` branch matrix.
