# External Grid Bridge Hardening Plan

## Purpose

This plan covers the merged external-grid bridge follow-up track from `hardeningPhasePlan.md`.

It combines the previous item 2 and item 4 concerns:

- using `GridSpawnToken` and `GridVersion` for stale-event guards or dedup in the
  `PathManager` grid bridge
- revisiting whether external grid change rebuilds should become more precise than the current
  bounds-targeted chart rebuild path

The goal is to reduce redundant rebuild work and lower the cost of legitimate grid-driven rebuilds
without changing the current deterministic rebuild model.

## Current State

`PathManager` currently subscribes directly to `GlobalGridManager` lifecycle events and reacts to
grid add, remove, and change notifications immediately.

Current flow:

1. `HandleExternalGridChange(...)` receives `GridEventInfo`.
2. It ignores everything except `BoundsMin` / `BoundsMax`.
3. It rebuilds the initialized charts whose authored bounds overlap the event bounds.

Relevant current behavior:

- the bridge is synchronous and immediate
- it already early-outs when no initialized charts overlap the event bounds
- rebuild order follows chart registration order for deterministic restoration
- rebuild cost is still substantial because intersecting charts are cleared and reinitialized

Important limitations:

- `GridEventInfo.GridSpawnToken` and `GridEventInfo.GridVersion` are currently ignored
- exact duplicate notifications therefore trigger the same rebuild work again
- repeated noisy change events on one grid can amplify a rebuild path that already clears and
  reinitializes live chart state
- legitimate distinct grid changes still rebuild every intersecting initialized chart at chart-bounds
  granularity rather than a more precise affected-region scope

## Relevance Assessment

This merged item is still relevant, but the two subproblems have different value cases.

Why it is relevant:

- Trailblazer currently pays the full rebuild cost on every qualifying external grid event
- the rebuild path is not cheap, so duplicate events have a real amplification effect
- `GridSpawnToken` and `GridVersion` already exist in GridForge, so the guard data is available
- for legitimate grid changes, the current bounds-targeted rebuild path can still do more work than
  necessary when only a smaller portion of a chart is affected

Why it is not a current alpha blocker:

- the present bridge is synchronous, so stale-event correctness bugs are not evident in the current
  direct subscription model
- focused external-grid lifecycle tests are currently green
- more precise rebuild targeting depends on the change payload being rich enough to justify the added
  complexity

Recommendation:

- keep this as one shared hardening track for the external-grid bridge
- treat dedup and rebuild precision as separate levers on the same surface
- only implement the parts that profiling or host usage proves worthwhile

## Phased Plan

### Phase 1. Measure Event Noise And Rebuild Cost

Add lightweight instrumentation around the grid bridge before changing semantics.

Measure at least:

- external grid events received by kind: add, remove, change
- rebuilds actually executed after bounds filtering
- duplicate signatures seen for the same grid slot and grid instance
- rebuild cost for repeated events on the same grid in one frame or short burst
- rebuild cost for legitimate distinct changes on large overlapping charts

Deliverables:

- a concrete threshold for what counts as noisy churn
- evidence separating duplicate events from legitimate distinct version changes
- a clear signal on whether dedup, rebuild precision, or both are the better next optimization

### Phase 2. Add Exact-Duplicate Event Guards

Introduce a small bridge-local dedup cache keyed by grid slot and instance identity.

Preferred shape:

- one record per `GridEventInfo.GridIndex`
- each record stores the last processed `GridSpawnToken`, `GridVersion`, event kind, and bounds key
- exact duplicates are ignored before chart rebuild selection starts

Safe dedup rules:

- ignore change events only when `GridSpawnToken`, `GridVersion`, and bounds are unchanged
- ignore repeated add or remove notifications only when they describe the same grid instance
- never dedup across a spawn-token change, because that means the grid slot now refers to a
  different grid instance

Reasoning:

- `GridVersion` distinguishes real structural mutations on the same live grid instance
- `GridSpawnToken` protects against reusing a grid slot after remove/add churn
- this keeps the optimization O(1) and local to the bridge

### Phase 3. Add More Precise Rebuild Targeting When The Payload Justifies It

If profiling shows the expensive path is legitimate distinct grid changes rather than duplicate
notifications, reduce the rebuild scope before adding more bridge machinery.

Preferred direction:

- keep the bridge-local dedup rules from Phase 2 separate from this work
- use richer grid-change payload when available instead of only `BoundsMin` / `BoundsMax`
- rebuild only the initialized charts and chart-local authored regions actually affected by the
  grid change

Potential implementation shapes:

- finer chart-selection than full chart-bounds overlap
- finer live-state refresh inside a chart rather than full clear-and-reinitialize for every touched
  authored owner
- localized managed-transition reevaluation that matches the reduced rebuild scope

Important constraint:

- preserve deterministic overlap restoration and cache invalidation semantics

### Phase 4. Optional Burst Coalescing

If profiling shows that duplicates arrive in bursts rather than isolated repeats, add a deferred
coalescing path on top of the exact-dedup guard.

Preferred shape:

- queue pending dirty-grid records instead of rebuilding immediately
- process the queue once per `PathManager.Tick()`
- merge repeated events for the same `(GridIndex, GridSpawnToken)` pair

Merge rules:

- the newest `GridVersion` wins for change events
- remove overrides earlier change events for the same grid instance
- a new spawn token replaces any pending record for the old grid instance in that grid slot

Important constraint:

- keep the current deterministic chart rebuild order once the queued records are expanded into the
  final chart set

### Phase 5. Verification And Documentation

After bridge hardening lands:

- add focused tests for duplicate change events and grid-slot reuse with a new spawn token
- add focused tests for reduced rebuild scope if Phase 3 lands
- add a regression test proving deferred charts still stay inert under bridged grid churn
- document the bridge hardening behavior in `docs/PATHMANAGER.MD`
- link this plan from `docs/feature-work/hardeningPhasePlan.md`

## Guardrails

Keep this work tightly scoped to the bridge.

Specifically:

- do not alter the meaning of a legitimate new `GridVersion`
- do not let dedup suppress remove-then-add churn for a new grid instance in the same slot
- do not broaden precision work into an unrelated `PathManager` lifecycle refactor
- do not introduce per-event allocations on the steady-state path
- do not reduce rebuild scope in ways that weaken current cache invalidation or transition refresh
  correctness

## Risks

Main failure modes to defend against:

- skipping a legitimate rebuild because the dedup signature is too coarse
- carrying stale bridge state across grid-slot reuse
- hiding real structural changes that arrive with a higher `GridVersion`
- adding bridge complexity that saves little if event duplication is rare
- reducing rebuild scope too aggressively and leaving live chart state partially stale

## Exit Criteria

This item is ready to implement when all of the following are true:

- profiling shows duplicate or bursty grid notifications are causing redundant rebuilds
- or profiling shows legitimate distinct grid changes are paying too much for broad rebuild scope
- exact-dedup rules can be defined in terms of `GridSpawnToken` and `GridVersion` without changing
  legitimate rebuild behavior
- and/or the available grid-change payload is rich enough to support a narrower rebuild target
- focused regression coverage proves slot reuse and version bumps still rebuild correctly
