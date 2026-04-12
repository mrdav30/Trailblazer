# Coverage Hardening Plan

## Goal

Drive Trailblazer toward `100%` line and branch coverage without bloating the suite or
papering over design issues. The immediate milestone is to raise branch coverage above `92%`
in `Release`, then keep grinding toward `100%` by closing real behavior gaps, tightening
invariants, and removing dead code where coverage-only tests would be dishonest.

Current branch budget:

- current branch coverage: `92.04%` (`3573 / 3882`)
- immediate `> 92%` milestone: reached
- branches needed to reach `100%`: `+309`

## Fresh Baseline

Fresh snapshot captured on April 12, 2026.

Baseline workflow:

```bash
dotnet restore Trailblazer.sln
dotnet build Trailblazer.sln --configuration Release
dotnet test Trailblazer.sln --configuration Release --no-build \
  --collect:"XPlat Code Coverage" \
  --results-directory artifacts/coverage/2026-04-12-release
```

Fresh artifact:

- [coverage.cobertura.xml](../../artifacts/coverage/2026-04-12-release/coverage.cobertura.xml)

Fresh run result:

- test result: `612` passed, `0` failed, `0` skipped
- line coverage: `96.21%` (`7684 / 7986`)
- branch coverage: `88.91%` (`3464 / 3896`)

## Latest Snapshot

Phase 2 refresh captured on April 12, 2026 after the surveyor/resolver sweep.

Refresh workflow:

```bash
dotnet test Trailblazer.sln --configuration Release
dotnet test Trailblazer.sln --configuration Release \
  --collect:"XPlat Code Coverage" \
  --results-directory artifacts/coverage/phase2-final-2026-04-12-c
```

Refresh artifact:

- [coverage.cobertura.xml](../../artifacts/coverage/phase2-final-2026-04-12-c/a64dd8af-3495-4b58-ad33-88caef41a516/coverage.cobertura.xml)

Refresh result:

- test result: `703` passed, `0` failed, `0` skipped
- line coverage: `97.37%` (`7765 / 7974`)
- branch coverage: `92.04%` (`3573 / 3882`)
- branch gain from phase 1 refresh: `+60`
- branch gain from baseline: `+109`

## What The Numbers Say

### Biggest File Gaps By Missed Branches

These are the highest-value files after Phase 2. The top five files now account for
`174 / 309` missed branches, or about `56.3%` of the remaining branch gap.

| File | Line | Branch | Missed Lines | Missed Branches |
| --- | ---: | ---: | ---: | ---: |
| `Navigation/Motor/NavMotor.cs` | `94.51%` | `87.19%` | `29` | `63` |
| `Pathing/PathManager.cs` | `97.40%` | `90.46%` | `23` | `45` |
| `Main/Navigator.cs` | `92.41%` | `80.88%` | `27` | `26` |
| `Navigation/Steering/NavSteering.cs` | `96.47%` | `90.09%` | `18` | `20` |
| `Pathing/Transition/TraversalTransitionRegistry.cs` | `94.68%` | `89.58%` | `20` | `20` |
| `Navigation/Motor/Locomotion/PlatformLocomotion.cs` | `98.58%` | `85.59%` | `2` | `17` |
| `Pathing/Search/FlowField/FlowFieldGuide.cs` | `94.11%` | `89.09%` | `9` | `12` |
| `Pathing/Search/PathGuideFactory.cs` | `96.80%` | `90.47%` | `4` | `8` |
| `Navigation/MovementGroups/MovementGroupCoordinator.cs` | `94.69%` | `89.39%` | `6` | `7` |
| `Navigation/Motor/Locomotion/LocomotionHandler.cs` | `98.92%` | `93.75%` | `2` | `6` |
| `Pathing/Transition/GeneratedTraversalTransitionBuilder.cs` | `97.76%` | `83.33%` | `4` | `6` |
| `Pathing/Search/Hybrid/HybridPathRequest.cs` | `98.57%` | `92.18%` | `2` | `5` |
| `Pathing/Search/Volume/VolumePathRequest.cs` | `98.29%` | `92.18%` | `2` | `5` |
| `Pathing/Search/Support/Request/PathRequest.cs` | `97.01%` | `88.63%` | `2` | `5` |
| `Pathing/Search/AStar/AStarSurveyor.cs` | `98.91%` | `95.55%` | `2` | `4` |
| `Pathing/Transition/TraversalTransitionQuery.cs` | `97.90%` | `93.33%` | `2` | `4` |

### Coverage Review Notes

- Phase 2 succeeded by retiring most of the local surveyor/resolver debt before touching the
  heaviest runtime files. The remaining branch budget is now dominated by runtime and
  transition-lifecycle code, not by small pathing helpers.
- `Pathing/Search/FlowField/FlowFieldSurveyor.cs` is effectively retired as a hotspot at
  `99.05%` branch, and `Pathing/Search/Hybrid/HybridRoutePlanner.cs` is now at `100%`.
- `Pathing/Search/AStar/AStarGuide.cs`, `Pathing/Search/Volume/VolumeSurveyor.cs`,
  `Pathing/Search/Support/VoxelFinder/VolumeVoxelFinder.cs`, and
  `Pathing/Partition/SolidChartPartition.cs` all moved out of the "fastest branch win" tier.
- The next decisive branch gains are now clearly in Phase 3 and Phase 4 targets:
  `PathManager`, `TraversalTransitionRegistry`, `NavMotor`, `Navigator`, `NavSteering`,
  and `PlatformLocomotion`.
- `FlowFieldGuide` and `PathGuideFactory` still offer worthwhile local cleanup value, but they
  no longer gate milestone planning the way the surveyor/resolver cluster did.

## Phased Battle Plan

### Phase 1. Request, Cache, And Helper Sweep

Current branch budget in this phase: about `86` missed branches.

Status on April 12, 2026:

- phase result: materially landed, full `Release` suite green, coverage moved from `88.91%` to `90.16%`
- branch budget remaining to `92%`: `+72`
- this phase also surfaced and fixed three correctness issues instead of only adding tests:
  cache hits in `ReusableSurveyResultCache<T>` now properly checkout reused results,
  `PathGuideFactory.RequestFlowField(...)` now returns borrowed cached results when a shared field
  does not cover the caller's start voxel, and `VolumePathRequest.TrySetUnitSize(...)` now
  revalidates endpoints instead of only swapping the stored size

Phase 1 file movement from the refreshed coverage snapshot:

| File | Baseline Branch | Current Branch |
| --- | ---: | ---: |
| `Pathing/Search/Support/PathHeap.cs` | `83.33%` | `95.23%` |
| `Pathing/Search/Volume/VolumePathRequest.cs` | `79.03%` | `92.18%` |
| `Pathing/Search/Hybrid/HybridPathRequest.cs` | `75.00%` | `92.18%` |
| `Pathing/Search/Support/Survey/ReusableSurveyResultCache.cs` | `83.93%` | `96.42%` |
| `Serialization/PathRequestRecord.cs` | `86.76%` | `95.58%` |
| `Pathing/Search/PathGuideFactory.cs` | `81.40%` | `90.47%` |
| `Pathing/Search/FlowField/FlowFieldGuide.cs` | `85.45%` | `89.09%` |

Targets:

- `Pathing/Search/Support/PathHeap.cs`
- `Pathing/Search/Volume/VolumePathRequest.cs`
- `Pathing/Search/Hybrid/HybridPathRequest.cs`
- `Pathing/Search/Support/Survey/ReusableSurveyResultCache.cs`
- `Serialization/PathRequestRecord.cs`
- `Pathing/Search/PathGuideFactory.cs`
- `Pathing/Search/FlowField/FlowFieldGuide.cs`

Why this phase comes first:

- these files have meaningful branch debt but mostly local behavior
- many of the missing branches look reachable through focused deterministic tests
- this is the lowest-risk way to move branch coverage quickly

Planned tactics:

- add `PathHeap` capacity-growth tests so `Resize(...)` actually executes
- expand request equality, `TrySetOrigin`, `TrySetDestination`, and search-range reset coverage
- add direct cache tests for `ReusableSurveyResultCache<T>` covering:
  - hit while in-use
  - miss creation
  - LRU eviction
  - full-cache but all entries in use
  - dispose path
- add `PathRequestRecord` permutations for unsupported kind, failed request recreation,
  guide recreation failure, and waypoint restoration branches
- add staged `FlowFieldGuide` tests for fallback failures, stage exhaustion, stage guide reuse,
  and non-`FlowFieldGuide` inner segment behavior
- add `PathGuideFactory` tests for hybrid build failure, direct volume failure, cache invalidation,
  and transition-fallback short-circuits

Refactor or dead-code checks:

- if `PathGuideFactory.RequestHybrid(...)` null-route-plan or flatten-failure branches are
  impossible through maintained construction paths, tighten the invariant or remove the dead guard
  instead of building fake test scaffolding around it

Expected outcome:

- project branch coverage should move into the low `91%` range if most of this phase lands cleanly

### Phase 2. Surveyor And Resolver Sweep

Current branch budget in this phase: about `75` missed branches.

Status on April 12, 2026:

- phase result: landed, full `Release` suite green, coverage moved from `90.16%` to `92.04%`
- immediate milestone: cleared the planned `> 92%` branch target
- branch gain in this phase: `+60`
- branch gain from baseline: `+109`
- remaining branches to `100%`: `+309`
- this phase mixed focused helper-level tests with small invariant cleanups so we did not need
  oversized scene scaffolding for branches that were really local decision logic

Phase 2 file movement from the latest coverage snapshot:

| File | Phase 1 Branch | Current Branch |
| --- | ---: | ---: |
| `Pathing/Search/AStar/AStarSurveyor.cs` | `87.75%` | `95.55%` |
| `Pathing/Search/Volume/VolumeSurveyor.cs` | `88.23%` | `98.91%` |
| `Pathing/Search/Support/VoxelFinder/VolumeVoxelFinder.cs` | `87.14%` | `95.71%` |
| `Pathing/Search/AStar/AStarGuide.cs` | `81.81%` | `100.00%` |
| `Pathing/Search/FlowField/FlowFieldSurveyor.cs` | `92.45%` | `99.05%` |
| `Pathing/Partition/SolidChartPartition.cs` | `81.66%` | `96.66%` |
| `Pathing/Chart/NavigationChart.cs` | `97.22%` | `98.61%` |
| `Pathing/Search/Hybrid/Support/HybridWaypointFlattener.cs` | `87.50%` | `93.75%` |
| `Pathing/Search/Hybrid/HybridRoutePlanner.cs` | `91.02%` | `100.00%` |

Targets:

- `Pathing/Search/AStar/AStarSurveyor.cs`
- `Pathing/Search/Volume/VolumeSurveyor.cs`
- `Pathing/Search/Support/VoxelFinder/VolumeVoxelFinder.cs`
- `Pathing/Search/AStar/AStarGuide.cs`
- `Pathing/Search/FlowField/FlowFieldSurveyor.cs`
- `Pathing/Partition/SolidChartPartition.cs`
- `Pathing/Chart/NavigationChart.cs`
- `Pathing/Search/Hybrid/Support/HybridWaypointFlattener.cs`
- `Pathing/Search/Hybrid/HybridRoutePlanner.cs`

Why this phase is the milestone phase:

- Phases 1 and 2 together represent about `161` current missed branches
- if we retire most of that debt, branch coverage clears the `92%` target before we touch
  the largest navigation-runtime files

Planned tactics:

- expand small-grid scene coverage for:
  - no-route cases
  - diagonal corner-cut rejection
  - blocked clearance
  - mixed chart-owner paths
  - staged transition routes
  - nearest-anchor fallback
- deepen `VolumeSurveyor` coverage beyond the current minimal set:
  - `ProcessNeighbors`
  - `ProcessNeighbor`
  - `BuildWaypoints`
  - chart-owner collection
- add direct `VolumeVoxelFinder` clearance matrices for unit-size and medium combinations
- add missing `AStarGuide` and `FlowFieldSurveyor` edge cases where existing tests already do
  most of the heavy lifting

Refactor checks:

- if surveyor branches are hard to hit only because environment setup is oversized, extract the
  decision logic into smaller internal helpers and test them directly

Expected outcome:

- achieved: this phase was the planned boundary for getting branch coverage above `92%`

### Phase 3. Managed Transition Lifecycle Hardening

Current branch budget in this phase: about `65` missed branches.

Targets:

- `Pathing/PathManager.cs`
- `Pathing/Transition/TraversalTransitionRegistry.cs`

Why this phase is separate:

- these are correctness-sensitive chart ownership and transition lifecycle files
- branch gaps cluster around add/remove/suppress/refresh logic, not around trivial guards
- careless coverage work here risks obscuring the actual invariants

Planned tactics:

- add deterministic scenarios for:
  - generated transition registration
  - overlap masking
  - unload and reload
  - local chart-cell mutation
  - manual versus generated precedence
  - obsolete transition cleanup
  - suppression and unsuppression deltas
- prefer extracting pure diff helpers over writing giant integration-only tests
- verify empty-array, missing-state, and rollback branches directly

Likely refactors:

- separate managed transition delta calculation from side effects
- isolate suppression-set computation so it can be tested without rebuilding full world state
- simplify or delete branches that only defend against impossible managed-state combinations

Expected outcome:

- branch coverage should move into the mid `94%` range while materially improving confidence
  in chart mutation and transition invalidation behavior

### Phase 4. Navigation Runtime Hardening

Current branch budget in this phase: about `133` missed branches.

Targets:

- `Main/Navigator.cs`
- `Navigation/Steering/NavSteering.cs`
- `Navigation/Motor/NavMotor.cs`
- `Navigation/Motor/Locomotion/PlatformLocomotion.cs`
- `Navigation/MovementGroups/MovementGroupCoordinator.cs`

Why this phase comes later:

- this is the largest remaining branch budget
- many gaps depend on fine-grained traversal state combinations and host-style lifecycle flow
- determinism and hot-path behavior matter more here than raw coverage speed

Planned tactics:

- add focused matrix tests around:
  - inactive guards
  - delta accumulation and zero-delta short-circuits
  - pending guided volume exit handoff activation
  - movement-group prewarm and reset
  - platform release and landing transitions
  - gas exit transitions
  - flight, liquid, and grounded acceleration selection
  - speed-multiplier helpers
  - steering stuck-recovery and dispose paths
- reuse existing mock navigator and motor helpers instead of creating giant end-to-end scenes
- keep hot-path allocations flat while refactoring for testability

Dead-code and invariant audit:

- `NavMotor.GetMaxAcceleration()` fallback returning `Fixed64.MAX_VALUE` must be explicitly
  justified, removed, or turned into an invariant failure instead of being left as zombie code
- one-sided guard branches in `Navigator` and `NavSteering` should be reviewed the same way:
  if the state is impossible after `Setup` and `Initialize`, tighten the invariant rather than
  preserve a branch only for coverage accounting

Expected outcome:

- push coverage into the high `90%` range with stronger confidence in real runtime behavior,
  not just helper correctness

### Phase 5. Tail Sweep To 100 Or Justified Exclusions

Expected remaining branch budget after Phases 1 through 4: about `73` branches.

Likely tail files:

- `Pathing/Search/Volume/VolumeGuide.cs`
- `Navigation/Motor/Locomotion/LocomotionHandler.cs`
- `Pathing/Search/Support/Request/PathRequest.cs`
- `Pathing/Transition/GeneratedTraversalTransitionBuilder.cs`
- `Pathing/Transition/TraversalTransitionOrdering.cs`
- `Pathing/Transition/TraversalTransitionQuery.cs`
- `Main/TrailblazerManager.cs`
- remaining one-to-four-branch helpers across support, transitions, and locomotion

Rules:

- prefer real tests first
- if a branch is unreachable under the supported invariants, delete it or replace it with an
  assertion or clearer failure path
- only use `[ExcludeFromCodeCoverage]` when the code is genuinely non-runtime or structurally not
  worth exercising, and document the reason in the change set
- do not blanket-exclude large files to make the dashboard look better

Expected outcome:

- either `100%` line and branch coverage, or a very short list of explicitly justified exclusions
  or deleted dead branches

## Execution Strategy

### Recommended Order

1. Land Phase 1 and rerun full `Release` coverage.
2. Land Phase 2 to clear the `> 92%` branch target.
3. Land Phase 3 managed-transition hardening.
4. Land Phase 4 navigation-runtime hardening.
5. Finish with the Phase 5 tail sweep and dead-code cleanup.

### Verification Cadence

For each phase:

1. add focused tests for the target file cluster
2. run the smallest relevant test slice first
3. rerun `dotnet test Trailblazer.sln --configuration Release`
4. rerun the full coverage snapshot in `Release`
5. update this plan with the new totals and the next exposed hotspots

### Stop Conditions

Pause and reassess before forcing coverage higher if the remaining debt is dominated by:

- impossible defensive branches that should be deleted or tightened instead
- coverage-only refactors that would increase hot-path allocations or obscure deterministic flow
- test setups whose ceremony outweighs the runtime value of the branch being exercised

The target is total confidence, not synthetic tests that make the suite harder to trust.
