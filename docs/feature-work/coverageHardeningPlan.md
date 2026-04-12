# Coverage Hardening Plan

## Goal

Drive Trailblazer toward `100%` line and branch coverage without bloating the suite or
papering over design issues. The immediate milestone is to raise branch coverage above `92%`
in `Release`, then keep grinding toward `100%` by closing real behavior gaps, tightening
invariants, and removing dead code where coverage-only tests would be dishonest.

Current branch budget:

- current branch coverage: `90.16%` (`3513 / 3896`)
- branches needed to reach `92%`: `+72`
- branches needed to reach `100%`: `+383`

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

Phase 1 refresh captured on April 12, 2026 after the first request/cache/helper pass.

Refresh workflow:

```bash
dotnet test Trailblazer.sln --configuration Release
dotnet test Trailblazer.sln --configuration Release --no-build \
  --collect:"XPlat Code Coverage" \
  --results-directory artifacts/coverage/phase1-current
```

Refresh artifact:

- [coverage.cobertura.xml](../../artifacts/coverage/phase1-current/015d344c-0f57-4c33-ad15-0d35d7258753/coverage.cobertura.xml)

Refresh result:

- test result: `626` passed, `0` failed, `0` skipped
- line coverage: `96.69%` (`7724 / 7988`)
- branch coverage: `90.16%` (`3513 / 3896`)
- branch gain from baseline: `+49`

## What The Numbers Say

### Subsystem Snapshot

| Subsystem | Line | Branch |
| --- | ---: | ---: |
| `Main` | `93.78%` | `81.51%` |
| `Navigation` | `97.45%` | `90.45%` |
| `Pathing` | `95.78%` | `88.64%` |
| `Serialization` | `95.56%` | `86.76%` |
| `Support` | `97.03%` | `85.71%` |

### Biggest File Gaps By Missed Branches

These are the highest-value files to target first. The top five files alone account for
`174 / 432` missed branches, or about `40.3%` of the remaining branch gap.

| File | Line | Branch | Missed Lines | Missed Branches |
| --- | ---: | ---: | ---: | ---: |
| `Navigation/Motor/NavMotor.cs` | `94.52%` | `87.20%` | `29` | `63` |
| `Pathing/PathManager.cs` | `97.41%` | `90.47%` | `23` | `45` |
| `Main/Navigator.cs` | `92.42%` | `80.88%` | `27` | `26` |
| `Pathing/Transition/TraversalTransitionRegistry.cs` | `94.68%` | `89.58%` | `20` | `20` |
| `Navigation/Steering/NavSteering.cs` | `96.48%` | `90.10%` | `18` | `20` |
| `Navigation/Motor/Locomotion/PlatformLocomotion.cs` | `98.58%` | `85.59%` | `2` | `17` |
| `Pathing/Search/FlowField/FlowFieldGuide.cs` | `92.81%` | `85.45%` | `11` | `16` |
| `Pathing/Search/Hybrid/HybridPathRequest.cs` | `95.00%` | `75.00%` | `7` | `16` |
| `Pathing/Search/PathGuideFactory.cs` | `94.40%` | `81.40%` | `7` | `16` |
| `Pathing/Search/Volume/VolumePathRequest.cs` | `92.37%` | `79.03%` | `9` | `13` |
| `Pathing/Search/AStar/AStarSurveyor.cs` | `95.81%` | `87.76%` | `8` | `12` |
| `Pathing/Search/Volume/VolumeSurveyor.cs` | `93.83%` | `88.24%` | `10` | `12` |
| `Pathing/Partition/SolidChartPartition.cs` | `96.23%` | `81.67%` | `4` | `11` |
| `Serialization/PathRequestRecord.cs` | `95.56%` | `86.76%` | `8` | `9` |
| `Pathing/Search/Support/Survey/ReusableSurveyResultCache.cs` | `92.86%` | `83.93%` | `6` | `9` |

### Coverage Review Notes

- The fastest path to `> 92%` branch does not require starting with the riskiest runtime files.
  The current branch gap in request/cache/helper and surveyor/resolver files is about `161`
  branches, which is enough on paper to move the project from `88.91%` to about `93.04%`
  before we take on the heaviest navigation-runtime work.
- `Pathing/Search/Support/PathHeap.cs` still has an unhit `Resize(...)` path even though it
  already has a dedicated test file. That is a strong sign of a low-ceremony branch win.
- `Pathing/Search/Support/Survey/ReusableSurveyResultCache.cs` appears to have no dedicated
  test file today. Cache hit, eviction, in-use, and dispose behavior should be tested directly
  instead of waiting for incidental coverage through `PathGuideFactory`.
- `Pathing/Search/Volume/VolumeSurveyor.cs` currently has only a few focused tests, which lines
  up with the remaining branch debt in neighbor rejection, waypoint building, and chart-owner
  collection logic.
- `PathManager` and `TraversalTransitionRegistry` branch gaps cluster around managed generated
  transition lifecycle code. Those are correctness-sensitive areas; they deserve small refactors
  or isolated diff helpers if the current shape makes coverage awkward.
- `NavMotor.GetMaxAcceleration()` still ends in a fallback path that returns `Fixed64.MAX_VALUE`
  with a comment that it "should never be hit." That branch should be audited before we spend
  time manufacturing artificial tests around it.

### Highest Observed CRAP Hotspots

These are approximate CRAP candidates derived from Cobertura method complexity and method
line coverage. They are useful prioritization signals, but not all of them are equally urgent.

| Method | Approx. CRAP | Current Coverage | Why It Matters |
| --- | ---: | --- | --- |
| `NavMotor.HandlePlatformTransitions` | `27.31` | `77.78%` line / `86.36%` branch | High complexity and real runtime-state combinations still missing. |
| `NavMotor.GetMaxAcceleration` | `24.33` | `91.67%` line / `66.67%` branch | Strong dead-code-or-missing-matrix candidate. |
| `TraversalTransitionRegistry.SetManagedTransitionsSuppressed` | `22.25` | `92.00%` line / `77.27%` branch | Managed transition lifecycle logic is still under-exercised. |
| `PathRequestRecord.TryCreateRequest` | `22.16` | `93.10%` line / `81.82%` branch | Serialization restore permutations still have gaps. |
| `VolumeVoxelFinder.IsDirectPathClear` | `20.78` | `87.50%` line / `85.00%` branch | Core direct-travel clearance logic still needs matrix tests. |
| `TraversalTransitionRegistry.UnregisterRange` | `20.23` | `91.67%` line / `80.00%` branch | Mixed registered/unregistered range handling remains thin. |
| `PathHeap.Resize` | `20.00` | `0.00%` line / `0.00%` branch | Very cheap branch debt to retire early. |
| `NavSteering.RecordData` | `26.00` | `99.12%` line / `96.15%` branch | High complexity, but only a small residual gap. Lower priority than the rows above. |

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

- this is the planned phase boundary for getting branch coverage above `92%`

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
