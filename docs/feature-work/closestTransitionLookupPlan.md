# Closest Transition Lookup Hardening Plan

## Purpose

This plan covers optional runtime follow-up item 1 from `hardeningPhasePlan.md`:

> If profiling shows the current closest-transition lookup is still hot inside large single-grid
> transition sets, add a more granular spatial index instead of relying only on filtered caches and
> grid-bounds pruning.

The goal is to keep this work demand-driven while preserving a ready implementation path if alpha
adopters or host tooling start leaning on nearest-transition queries heavily.

## Current State

`PathManager.TryGetClosestActiveTransition(...)` currently does three useful things already:

1. Filters by `TraversalTransitionType` up front through `TraversalTransitionQuery`.
2. Searches the origin grid first when the query point resolves to a live grid.
3. Prunes whole grids by distance to grid bounds before scanning that grid's candidates.

That is a sound baseline, and the focused tests around the helper are currently green.

Current limitation:

- when many candidate transitions of the same type live inside one source grid, the query still
  linearly scans every directed candidate in that grid
- grid-bounds pruning cannot reduce that cost when the hot set is concentrated in a single grid
- the helper is currently a public query surface, not a proven internal frame hot path

## Relevance Assessment

This follow-up is still relevant, but it is not an immediate alpha blocker.

Why it still matters:

- the current algorithm has an obvious worst-case ceiling in dense single-grid transition sets
- the gap is structural, not hypothetical; current pruning stops at the grid boundary, not within it
- a better nearest-source index would also improve host usability for any tooling or gameplay code
  that asks "what is the nearest jump / landing / swim exit from here?"

Why it should stay optional:

- there are currently no internal runtime call sites for `TryGetClosestActiveTransition(...)`
- the present implementation is correct, deterministic, and covered by focused tests
- adding a spatial index increases cache invalidation and memory-management complexity in a
  transition system that already has active-versus-suppressed lifecycle rules

Recommendation:

- keep this item active as a profiling-gated hardening task
- do not implement it speculatively before a benchmark or real host usage shows the lookup is hot

## Phased Plan

### Phase 1. Benchmark And Trigger Definition

Add a small benchmark or targeted perf harness around nearest-transition lookup before changing the
implementation.

Cover at least:

- dense single-grid transitions of one type
- sparse multi-grid transitions where current bounds pruning already helps
- queries that originate outside every grid
- bidirectional transitions and same-distance ties

Deliverables:

- baseline timing numbers for representative transition counts
- a simple trigger threshold for when the spatial index becomes justified
- one explicit deterministic test that pins tie behavior so a future index cannot silently change it

### Phase 2. Add A Deterministic Source-Space Index

Introduce a lookup structure dedicated to nearest-source queries instead of broadening
`TraversalTransitionRegistry`'s existing registry indexes.

Preferred shape:

- lazy, versioned cache parallel to `TraversalTransitionQuery`
- keyed by `(TraversalTransitionType, sourceGridIndex, spatialCellKey)`
- stores directed transitions in deterministic `TraversalTransitionOrdering` order inside each bucket

Preferred bucket source:

- source voxel index first
- optional coarser quantized cell grouping later only if voxel buckets prove too sparse

Reasoning:

- transition sources already resolve through canonical voxels
- voxel bucketing keeps invalidation and determinism simpler than a generic tree structure
- this improves average lookup cost without forcing a non-deterministic nearest-neighbor data
  structure into the runtime

### Phase 3. Integrate The Indexed Query Path

Update `PathManager.TryGetClosestActiveTransition(...)` to use the new index only for the
single-grid candidate scan, while preserving the current whole-grid pruning strategy.

Target query flow:

1. Resolve the origin grid when possible.
2. Use the indexed local search for that grid.
3. Expand through neighboring source buckets in deterministic order until the next bucket-ring
   lower bound can no longer beat the current best distance.
4. Fall back to the existing grid-level pruning logic for other grids.
5. Preserve the current exact result set, including reversed bidirectional views.

Important rule:

- equal-distance ties must remain deterministic and explicitly use `TraversalTransitionOrdering`
  rather than depending on incidental iteration order

### Phase 4. Verification And Documentation

After the index lands:

- extend `PathingNavigationMap.Tests` with dense same-grid cases and tie cases
- add a focused cache-invalidation test that exercises suppress/reactivate and registry-version
  refresh
- document the new nearest-transition acceleration path in `docs/wiki/PATHMANAGER.MD`
- mention the profiling gate in `docs/wiki/feature-work/hardeningPhasePlan.md` so the item stays
  obviously optional

## Guardrails

Do not take this change wider than necessary.

Specifically:

- do not refactor `HybridRoutePlanner` at the same time unless separate profiling shows benefit
- do not replace existing registry indexes that already serve outgoing/incoming and grid-scoped
  transition queries well
- do not trade deterministic exact nearest results for approximate nearest behavior
- do not introduce per-query allocations

## Risks

Main failure modes to defend against:

- stale spatial buckets after transition suppression, reactivation, unregister, or grid rebuild
- memory bloat from over-indexing sparse transition sets
- incorrect nearest results when point overrides differ inside the same voxel
- silent tie-breaking changes after moving from array scans to bucketed search

## Exit Criteria

This item is ready to implement when all of the following are true:

- profiling shows nearest-transition lookup is materially hot in a real scenario
- the hot scenario is dominated by dense same-grid candidate sets
- Phase 1 benchmarks show the current grid-pruning path is no longer sufficient
- the implementation can preserve current deterministic results with focused regression coverage
