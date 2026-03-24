# Harden Traversable State Plan

Related docs:

- [CHARTS.MD](../CHARTS.MD)
- [PATHMANAGER.MD](../PATHMANAGER.MD)
- [TRANSITIONS.MD](../TRANSITIONS.MD)
- [VOLUMETRAVERSAL.MD](../VOLUMETRAVERSAL.MD)

## Status

This is a living design note for hardening how Trailblazer authors and registers traversable state across:

- `NavigationChart` and `NavigationChartCell`
- `VolumeTraversalRules`
- `TraversalTransitionRegistry`
- live voxel `PathPartition` ownership

The core direction is sound: authoring land, water, open volume, and transitions currently feels more fragmented than it should. The implementation should improve that authoring story without blurring the runtime responsibilities that already work well.

## Problem Statement

Trailblazer currently has a good execution model, but a split authoring model:

- charts describe chart-backed surface traversal
- volume rules describe constrained raw-volume traversal such as water
- transitions describe authored handoffs between chart and volume space

Those are all valid concepts, but today they must be assembled manually and somewhat independently. That increases friction, makes mixed-medium setups harder to reason about, and leaves transition authoring more verbose than it needs to be.

## Review Summary

### Direction I agree with

- We should add a more cohesive authoring layer for land, water, open volume, and transition setup.
- We should support voxel-scoped transition authoring with an optional exact world-position override.
- We should make authored map input easier for tests, samples, and lightweight host setup.
- We should treat traversable-state setup as a first-class workflow instead of a collection of unrelated calls.

### Direction I would adjust

- Do not rename `NavigationChart` in the first pass. The naming concern is real, but a rename right now creates large API and docs churn without fixing correctness on its own.
- Do not replace explicit `TraversalTransition` records with flags alone. Flags can help generate transitions, but they do not carry direction, transition type, volume mode, path cost, or stable ids.
- Do not let `string` tokens leak into runtime pathing. If we add symbolic authoring input, parse it once during setup and convert to the existing structured runtime types.
- Do not broaden from `Open` and `Water` into arbitrary custom volume media in the same pass unless we are ready to redesign `VolumeTraversalMode` and all request/planning APIs that depend on it.

## Important Observations From The Current Code

- `NavigationChartCell.Flags` and `PathPartition.ChartFlags` are not dead code. They are already propagated during chart initialization and covered by tests.
- Those flags are currently metadata only. They do not drive transition planning yet, which makes them a good fit for authored hints, not a full transition replacement.
- `VolumeTraversalRules` is currently a runtime classifier for raw volume, not a chart builder. That separation is useful and should remain intact even if setup becomes more unified.
- `TraversalTransitionAnchor` currently stores a single world position. That is the right seam to extend if we want voxel-wide anchors with optional exact points.
- `TraversalTransitionAnchor` currently splits anchor identity between `TraversalTransitionAnchorKind` and `VolumeTraversalMode`. That works, but it is awkward because `VolumeMode` is only meaningful for volume anchors and defaults to `Open` for chart anchors.

## Goals

- Add one coherent authoring flow for mixed traversable state.
- Preserve deterministic runtime behavior and stable request semantics.
- Keep hot-path runtime code free of string parsing and avoidable allocations.
- Preserve the current public request model: chart requests stay chart requests, and volume requests stay volume requests.
- Make transition authoring less brittle for shoreline, dock, jump-gap, and landing scenarios.

## Non-Goals

- Renaming `NavigationChart` in the first implementation pass.
- Replacing `TraversalTransitionRegistry` with chart flags.
- Redesigning all traversal APIs around arbitrary custom media beyond the current `Open` and `Water` modes.
- Moving setup logic into per-frame pathing code.

## Proposed Direction

### 1. Add an authoring layer above the current runtime primitives

Introduce a new builder-style authoring API that produces existing runtime objects instead of replacing them.

Tentative responsibilities:

- build one or more `NavigationChart` instances
- describe which authored cells participate in raw-volume setup
- generate explicit `TraversalTransition` definitions
- optionally apply the generated output through `PathManager`, `VolumeTraversalRules`, and `TraversalTransitionRegistry`

This keeps the current runtime architecture intact while giving hosts a single place to express traversable intent.

### 2. Start with tokenized authoring, but keep tokens out of runtime

Support a lightweight authoring input for tests and host bootstrapping, but keep it setup-only.

Recommended first pass:

- support `string[,,]` token input at the builder layer
- parse once into a compact structured representation before any runtime registration
- avoid shipping both `char[,,]` and `string[,,]` public builder APIs in the first pass unless a real need appears
- resolve tokens through a legend/config object during setup

Why `string[,,]` is the better builder input for this feature:

- combined tokens such as `L!` / `W!` are clearer than splitting marker state across multiple arrays
- validation rules can stay explicit and readable
- the extra allocation cost is acceptable at setup time as long as parsing does not leak into runtime pathing

### 3. Keep runtime chart and volume concepts separate

The authoring layer can unify setup, but the runtime concepts should stay distinct:

- `NavigationChart` remains the chart-backed surface model
- `VolumeTraversalRules` remains the raw-volume classifier
- `TraversalTransitionRegistry` remains the explicit handoff store

That separation matters because these systems answer different runtime questions.

### 4. Clean up anchor space modeling

The current `Kind` plus `VolumeMode` split is workable, but it is harder to read than it needs to be.

Preferred direction:

- replace `TraversalTransitionAnchorKind` plus anchor-local `VolumeTraversalMode` with a single anchor-space enum in the transition layer
- keep `VolumeTraversalMode` for actual raw-volume requests and planner legs
- map from anchor-space enum to `VolumeTraversalMode` only when a transition leg actually becomes a volume request

Tentative enum shape:

- `Chart`
- `OpenVolume`
- `WaterVolume`

This would remove the confusing "chart anchor with `VolumeMode.Open`" state without forcing a broader volume-system redesign.

### 5. Extend transition anchors instead of replacing them

Add support for anchors that identify a voxel first and optionally specify a more precise world-space point.

Desired behavior:

- the anchor can represent "this shoreline voxel" without forcing many duplicate point-to-point transitions
- hosts can optionally refine the usable point later
- planners still end up with explicit resolved transition records

This is a better fit than asking chart flags to become the runtime transition system.

### 6. Use chart flags as authoring hints

`NavigationChartCellFlags` and `PathPartition.ChartFlags` should help the authoring layer discover or emit transitions.

Good uses:

- mark candidate source cells
- mark candidate destination cells
- drive builder-time pairing rules

Avoid:

- treating flags as the final runtime transition record
- asking surveyors to infer transition behavior from flags alone

## Proposed Data Model

Tentative builder-side concepts:

- `TraversalLegend`
- `TraversalLegendEntry`
- `TraversalAuthoringMap`
- `TraversalBuildResult`
- `TraversalTransitionAnchorSpace` or equivalent

Possible legend meanings in the first pass:

- `L` = chart-backed surface cell
- `W` = water-volume cell
- `O` = open-volume cell or no constrained medium
- `!` or paired marker metadata = candidate transition hint

Important scope note:

- `W` is realistic for the current codebase because `VolumeTraversalMode.Water` already exists.
- A token like `M` for mud is not just a chart concern. Supporting that cleanly would likely require a broader traversal-mode redesign and should be treated as follow-up work, not bundled into this first hardening pass.

## Phased Implementation Plan

### Phase 1. Define the authoring seam

- Add a new authoring/builder namespace under pathing support.
- Keep `NavigationChart.From3D(...)` unchanged.
- Add a builder that can produce `NavigationChartCell[,,]` from tokenized input plus a legend.
- Add print/debug helpers for the new authoring form.

Primary files likely involved:

- `src/Trailblazer/Pathing/Support/Chart/NavigationChart.cs`
- new authoring files under `src/Trailblazer/Pathing/Support`
- `src/Trailblazer/Pathing/Support/Chart/NavigationChartExtensions.cs`

### Phase 2. Harden transition anchor authoring

- Replace the current `TraversalTransitionAnchorKind` plus anchor-local `VolumeTraversalMode` split with one anchor-space enum in the transition layer.
- Extend `TraversalTransitionAnchor` to support voxel-scoped anchors with optional point overrides.
- Update `TraversalTransitionRegistry` registration/resolution so voxel identity remains explicit and deterministic.
- Preserve current direct point-based construction as a compatibility path where possible.
- Keep `VolumeTraversalMode` as the type used by `VolumePathRequest`, `RawVoxelFinder`, and other true volume-traversal APIs.

Primary files likely involved:

- `src/Trailblazer/Pathing/Support/Transition/TraversalTransitionAnchor.cs`
- `src/Trailblazer/Pathing/Support/Transition/TraversalTransitionRegistry.cs`
- transition-aware tests under `tests/Trailblazer.Tests/Pathing`

### Phase 3. Add generated transition support

- Let the authoring layer emit explicit `TraversalTransition` records from authored hints.
- Require generation to happen at setup time, not lazily during path requests.
- Keep manual transition registration supported and authoritative.
- Defer live mid-simulation regeneration until cache invalidation and in-flight path behavior are explicitly designed.

Primary files likely involved:

- new builder/generation files
- `src/Trailblazer/Pathing/Support/Transition/*`
- `tests/Trailblazer.Tests/Pathing/AStarTransitionFallback.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/FlowFieldTransitionFallback.Tests.cs`

### Phase 4. Improve setup integration

- Add an optional convenience path that applies built charts, transitions, and volume-rule setup in a controlled order.
- Keep the lower-level direct APIs available.
- Preserve explicit host ownership of initialization order and reset behavior.

Primary files likely involved:

- builder output/application helpers
- `src/Trailblazer/Pathing/PathManager.cs`
- `src/Trailblazer/Pathing/Support/VolumeTraversalRules.cs`

### Phase 5. Docs and compatibility pass

- Update docs to explain the new authoring flow without rewriting the underlying runtime model.
- Keep existing examples valid where possible.
- If a later rename from `NavigationChart` to `SurfaceChart` still feels worthwhile, handle that as a separate compatibility-focused proposal.

Primary docs to update when implementation starts:

- `README.md`
- `docs/OVERVIEW.md`
- `docs/CHARTS.MD`
- `docs/TRANSITIONS.MD`
- `docs/VOLUMETRAVERSAL.MD`

## Test Plan

Add or update tests for:

- tokenized authored maps building the expected `NavigationChartCell` payload
- chart metadata propagation into `PathPartition.PathCostModifier` and `ChartFlags`
- voxel-scoped transition anchors resolving deterministically
- optional point overrides on transition anchors
- generated chart-to-chart transitions
- generated chart-to-water and water-to-chart transitions
- no regression in existing A* / FlowField transition fallback
- reset behavior for global chart, transition, and volume-rule state

Existing coverage to build on:

- `tests/Trailblazer.Tests/Pathing/PathingNavigationMap.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/TraversalTransitionRegistry.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/AStarTransitionFallback.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/FlowFieldTransitionFallback.Tests.cs`

## Risks To Watch

- accidental API churn from renaming stable public types too early
- conflating chart ownership with raw-volume membership
- introducing runtime string parsing into hot paths
- auto-generated transitions that are ambiguous or nondeterministic
- broadening volume semantics beyond what `VolumeTraversalMode` currently supports
- unnecessary complexity in the authoring API that doesn't clearly improve ergonomics
- code bloat from supporting multiple builder input forms without a clear need
- high time complexity in the builder that slows down setup or makes iteration difficult
- drift between transition-anchor enum meaning and true volume-traversal mode meaning

## Working Decisions

- Builder input should start with `string[,,]`, not `char[,,]`.
- Builder validation should require one unambiguous base token per cell and reject invalid combinations early.
- Transition anchors should move toward one transition-layer enum for anchor space instead of carrying both `TraversalTransitionAnchorKind` and an always-present `VolumeTraversalMode`.
- Default generated transition points should use the canonical authored voxel position already used by the grid/chart data, with an explicit point override when authors need finer placement.
- Manual transitions should override generated transitions for the same resolved pair to avoid ambiguity. Rehydrating generated transitions after manual removal can be treated as follow-up, not phase-one scope.
- Auto-generated transitions should require explicit transition markers on both compatible adjacent cells. In other words: use marker opt-in plus adjacency, not adjacency alone.
- Auto-generation rules should stay deterministic in full 3D space, and the first pass should only consider the 6 perpendicular neighbors for automatic handoff generation.

## Current Recommendation

Proceed with a focused hardening pass built around a new authoring/builder layer, voxel-scoped transition anchors, and generated explicit transitions.

Defer the `NavigationChart` rename and any generalization beyond the current `Open` / `Water` volume model until the authoring workflow is proven and the API impact can be evaluated separately.
