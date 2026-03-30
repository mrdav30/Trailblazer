# Pathing Hardening Plan

Related docs:

- [traversalBoundaryAuthoringPlan.md](./traversalBoundaryAuthoringPlan.md)
- [CHARTS.MD](../CHARTS.MD)
- [AUTHORING.MD](../AUTHORING.MD)
- [VOLUMETRAVERSAL.MD](../VOLUMETRAVERSAL.MD)
- [PATHMANAGER.MD](../PATHMANAGER.MD)
- [TRANSITIONS.MD](../TRANSITIONS.MD)

Relevant code:

- `src/Trailblazer/Pathing/PathManager.cs`
- `src/Trailblazer/Pathing/Chart/ResolvedChartVoxelState.cs`
- `src/Trailblazer/Pathing/Chart/NavigationChart.cs`
- `src/Trailblazer/Pathing/Chart/NavigationChartCell.cs`
- `src/Trailblazer/Pathing/Transition/TraversalTransitionRegistry.cs`
- `src/Trailblazer/Pathing/Transition/TraversalTransitionOwnershipKind.cs`
- `src/Trailblazer/Pathing/Transition/RegisteredTraversalTransition.cs`
- `src/Trailblazer/Pathing/Transition/ManagedChartTransitionState.cs`
- `src/Trailblazer/Pathing/Transition/GeneratedTraversalTransitionBuilder.cs`
- `src/Trailblazer/Pathing/Authoring/TraversalAuthoringMap.cs`

## Scope

This is not a second authoring plan.

The boundary-authoring work already established the intended runtime direction. This note is about
hardening that direction for alpha:

- clarify the public query surface hosts actually need
- tighten transition ownership and regeneration rules
- remove obvious avoidable runtime bloat in the new overlap-resolution state
- close correctness and coverage gaps before widening the feature set again

When the authoring rationale matters, defer to
[traversalBoundaryAuthoringPlan.md](./traversalBoundaryAuthoringPlan.md). This document should stay
focused on runtime hardening, API clarity, and risk reduction.

## Hardening Goals

1. Keep one deterministic precedence model for overlap, mutation, and any future convenience APIs.
2. Make query and transition ownership contracts explicit instead of implied.
3. Keep new runtime state lean in both memory footprint and update complexity.
4. Favor correctness and observability over feature expansion.

## Out Of Scope

These may still matter later, but they are not the point of this plan:

- a dedicated override stack
- broader mixed-media combinations such as `GL` or `SGL`
- more inferred mixed-media generation rules
- replacing explicit traversal transitions as route-family authority
- large unrelated refactors across pathing and navigation

## Current Baseline

Already landed from the recent boundary-authoring work:

- overlap resolves to one effective authored cell per voxel instead of additive merge
- `NavigationChart.Priority` plus registration order defines deterministic precedence
- live chart mutation reuses the resolved-cell path and only reapplies touched voxels
- mixed `SL`, `SG`, `SL!`, and `SG!` cell authoring is explicit
- transition precedence now uses explicit transition priority plus stable registration order
- managed generated transitions stay registered while inactive and are suppressed instead of churned
- any registered chart carrying generated-transition media now participates in the same managed
  generated-transition lifecycle regardless of registration path
- overlap masking and inert registration can suppress managed generated transitions without losing
  their registration identity
- local chart mutation refreshes only the affected adjacent generated-transition pairs

That gives us a usable base. The remaining work is mostly about contract clarity, query coverage,
and making sure the new runtime state does not quietly become a long-term hotspot.

## Main Risks

### 1. Missing Public Query Surface

The runtime now knows the winning effective cell and active transition set, but hosts still lack a
clean public way to ask high-value questions such as:

- what is the resolved effective cell here?
- who currently owns this voxel?
- from this position, what is the closest transition of type `X`?

That creates pressure for hosts to reach into lower-level implementation details or duplicate logic.

### 2. `ResolvedChartVoxelState` Is Correct But Not Yet Tight

Current shape in [ResolvedChartVoxelState.cs](/mnt/f/gamedevrepos/Trailblazer/src/Trailblazer/Pathing/Chart/ResolvedChartVoxelState.cs):

- stores both a chart-owner set and a chart-to-cell dictionary
- recomputes the winning owner by rescanning current contributors on every add and remove

That is fine for the current alpha baseline, but it is the most obvious new place where avoidable
storage duplication and unnecessary rescans could accumulate if layered worlds become common.

### 3. Transition Ownership Is Still Only Partially Explicit

Current behavior is much stronger now, but the contract is not fully rounded out yet:

- chart-managed generated transitions are now lifecycle-managed regardless of registration path
- mutation and overlap masking both drive local suppression/reactivation
- raw manual transitions still stay host-managed by default
- the explicit managed-manual lane is still not implemented
- `ManagedChartTransitionState` still only models chart-owned generated dependencies, not the broader
  managed transition shape we may want if manual lifecycle support lands

The biggest remaining gap is the managed-manual lane, followed by host-facing query coverage.

## Track Status

### Track 1. Harden Transition Ownership And Regeneration

Status: core landed. Managed-manual follow-up still deferred until we prove we need it.

We should explicitly define what Trailblazer owns versus what the host owns.

Locked direction:

- transition precedence should use explicit transition priority plus stable order, not
  manual-versus-generated source type
- generated transitions should inherit the owning chart priority
- generated transition lifecycle should derive from chart cell transition metadata, not from whether
  the chart was registered as a `TraversalBuildResult` or plain `NavigationChart`
- manual transitions should use an explicit priority with a documented default
- ownership semantics should stay separate from precedence semantics
- manual transitions may participate in the same managed lifecycle if they are registered through an
  explicit managed lane
- generated transitions remain managed and chart-owned regardless of chart registration path when
  the chart carries transition-generating cell metadata
- raw manual registration should remain host-managed by default

Lifecycle direction:

- managed transitions should be active only when their dependency state is valid in the current
  effective world state
- if a higher-precedence chart masks a managed transition's supporting authored pair, that
  transition should deactivate
- if the masking chart goes away and the original effective pair returns, the managed transition
  should reactivate
- reevaluation should trigger from effective-state change in `PathManager`, not only direct chart
  mutation
- reevaluation should stay local and pair-based around the changed voxel
- cross-chart generated transition building is still out of scope
- cross-chart manual transitions should be supportable through the managed lifecycle model because
  they already have explicit authored anchors

Architecture direction:

- `TraversalTransitionRegistry` should stay primarily responsible for registration, indexing, query,
  and active-state rebuild
- the current `GeneratedChartTransitionState` should likely evolve into a broader managed transition
  lifecycle state rather than staying generated-only
- that managed state should track dependency ownership strongly enough to deactivate and reactivate
  transitions without guessing from live partitions alone
- the registry should keep managed transitions registered even while they are currently inactive or
  suppressed
- managed lifecycle state should drive suppression and reactivation without unregistering and
  re-registering transitions
- live partitions are active-state mirrors, not the authority for dormant managed transitions, so
  `ChartFlags` should not become the primary lifecycle truth
- active query indexes and snapshots should continue to expose only currently active transitions
  even if the registry retains suppressed managed registrations

Precedence direction:

- the current `TraversalTransitionRegistrationSource` split is not a strong long-term precedence
  model by itself
- if the registry keeps a source or ownership field, it should describe lifecycle ownership, not win
  ordering
- active-state rebuild should prefer higher transition priority first and then fall back to stable
  registration order for deterministic ties

Recommended implementation sequence:

Completed:

1. Added explicit transition priority and removed manual-versus-generated source type as the win
   rule.
2. Kept managed transitions registered while inactive, with active indexes exposing only the active
   subset.
3. Renamed `GeneratedChartTransitionState` to `ManagedChartTransitionState` and expanded it enough
   to own chart-managed generated lifecycle metadata.
4. Triggered local reevaluation from effective-state change in `PathManager`, not only direct chart
   mutation.
5. Applied the same generated-transition lifecycle to any registered chart carrying
   transition-generating cell metadata, regardless of registration path.

Deferred:

6. Add an explicit managed-manual lane only after generated managed lifecycle behavior is stable.

Acceptance criteria:

- transition lifecycle behavior is documented for mutation, overlap, unload, and registration
- managed transitions have predictable local refresh rules on both mutation and effective-state
  masking changes
- manual transitions are never silently converted into managed transitions
- transition precedence remains stable even when managed transitions deactivate and later reactivate

Current note:

- Track 1 is complete for chart-managed generated transitions.
- Managed-manual lifecycle support is still intentionally deferred so we can validate the generated
  lifecycle shape first.

### Track 2. Add Public Query APIs

Status: next / high priority after Track 1.

The most useful public surface is still query-oriented, but it should land after the active versus
inactive transition model is stable.

Primary targets:

- resolved effective-cell query by voxel or world position
- closest-transition query by world position and transition type

Recommendation:

- keep the public entry point on `PathManager`, because hosts usually start from world positions and
  navigation context
- let `TraversalTransitionRegistry` remain the lower-level transition index that the higher-level
  query can build on

Likely acceptance criteria:

- hosts can ask for the effective cell and effective owner without reading partition internals
- hosts can ask for the closest active transition of a given type without rebuilding their own search
- docs clearly state whether the query returns active transition state, registered transition state,
  or both

### Track 3. Tighten `ResolvedChartVoxelState`

Status: next / medium-high priority after Tracks 1 and 2.

This track is intentionally not a broad refactor. It is a focused cleanup pass if there is obvious
room to reduce code bloat or steady-state work.

Likely targets:

- collapse duplicate owner bookkeeping if one structure can serve both needs cleanly
- avoid full winner rescans when a non-winning owner changes and the current winner is unchanged
- keep precedence checks deterministic and readable

Acceptance criteria:

- no behavior change relative to the current overlap rules
- no new public surface area required
- tests pin winner changes, non-winner updates, winner removal, and same-priority ties

### Track 4. Coverage And Documentation Hardening

Status: continuous.

Coverage still worth adding as the above tracks land:

- transition priority and stable ordering coverage
- managed suppression and reactivation coverage
- plain-chart generated lifecycle coverage
- resolved-cell query coverage
- closest-transition query coverage
- larger multi-chart overlap cases
- transition lifecycle coverage when precedence changes without chart unload
- focused regression coverage around generated-transition refresh boundaries

Documentation should stay aligned in:

- [PATHMANAGER.MD](../PATHMANAGER.MD)
- [CHARTS.MD](../CHARTS.MD)
- [TRANSITIONS.MD](../TRANSITIONS.MD)
- [README.md](../../README.md)

## Recommended Ordering

1. Implement the transition lifecycle refactor first, especially explicit transition priority,
   managed suppression, and effective-state-driven reevaluation.
2. Add the public query contract after Track 1 stabilizes, especially the closest-transition query.
3. Tighten `ResolvedChartVoxelState` if there is obvious lean-up work with low behavior risk.
4. Expand tests and docs around whichever of the above lands.
5. Revisit override convenience only if the query and ownership work still leave a real gap.

## Current Decisions

- Should the resolved-cell query expose only the winning cell, or also the losing contributing
  owners for debugging and tooling?
  - Decision: expose separate APIs for those two use cases. The default public query should stay
    lean and return the winning effective state only.
- Should the closest-transition query search only active transitions, or offer an opt-in way to
  inspect inactive registered transitions too?
  - Decision: the primary query should search active transitions only. A separate lower-level query
    for registered or inactive transitions can be added later if host demand justifies it.
- Should managed transitions deactivate when their supporting authored pair is masked by a
  higher-precedence effective state?
  - Decision: yes. Managed transitions should deactivate while masked and reactivate when the
    supporting effective pair returns.
- Should generated transition lifecycle depend on chart registration path?
  - Decision: no. If a registered chart carries transition-generating cell metadata, it falls under
    the same managed generated-transition lifecycle regardless of how the chart was built,
    registered, or initialized.
- Should cross-chart neighboring-pair generation be added for generated transitions?
  - Decision: no, not as part of this hardening pass. Generated transitions stay chart-local.
- Should cross-chart manual transitions be supportable in the managed lifecycle model?
  - Decision: yes. Their explicit anchors make them a reasonable fit for managed lifecycle support.
- Should transition precedence continue to derive from manual-versus-generated source type?
  - Decision: no. Precedence should move to explicit transition priority plus stable registration
    order. Ownership or lifecycle kind should stay separate.
- Should the registry keep managed transitions registered even while they are inactive?
  - Decision: yes. The registry should retain managed registrations and stable ordering, while
    managed lifecycle state controls suppression and reactivation and active indexes expose only the
    currently active subset.

## Issues And Potential Improvements

- `ResolvedChartVoxelState` is the clearest candidate for cleanup if we want to reduce code bloat or
  steady-state cost from the recent overlap work.
- Public query APIs will likely remove pressure to expose internal owner bookkeeping directly.
- Transition discovery may be more useful to hosts than resolved-cell inspection, so it should not
  be treated as a minor follow-up.
- If overlap masking should deactivate managed transitions, that should be enforced by explicit
  lifecycle ownership and dependency rules, not by broad transition heuristics or partition flags
  alone.
- `ManagedChartTransitionState` is a better fit than the old generated-only name, but it is still
  chart-generated-specific. If managed-manual support lands later, this type may need one more
  generalization pass.
- Any future override convenience should still route through the existing precedence and mutation
  path instead of creating a second live-state pipeline.
