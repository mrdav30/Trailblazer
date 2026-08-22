# Managed Transition Lifecycle Plan

> Historical note: the chart registration and transition registry described
> here were removed by the grid-topology navigation-map refactor. Current
> dynamic transitions are addressed overlay operations; this plan is retained
> only as design history.

## Purpose

This plan covers optional runtime follow-up item 3 from `hardeningPhasePlan.md`:

> If hosts need richer automatic lifecycle behavior for manually registered
> transitions, revisit managed manual regeneration and whether
> `NavigationChartRegistration` should share a broader general managed
> transition dependency model.

The goal is to improve host ergonomics for manually owned transition sets
without weakening the current deterministic transition model or broadening
abstractions before there is a proven common shape.

## Current State

Trailblazer currently has two managed transition stories:

1. Chart-generated transitions These are tracked per registered chart through
   `NavigationChartRegistration` in `PathManager`.
2. Manually registered transitions These live in `TraversalTransitionRegistry`
   and participate in managed suppression/reactivation, but they do not have a
   higher-level owner-scoped regeneration model.

What manual transitions already do well:

- they register once and stay registered until explicit unregister or
  `PathManager.Reset()`
- they start suppressed if either resolved endpoint medium is currently
  unsupported
- they reactivate automatically when local chart/grid/media state makes their
  endpoints valid again
- local chart mutations refresh only the touched manual transitions through
  voxel dependency indexes

Important current limitation:

- manual transitions are lifecycle-managed only at the individual transition
  level
- there is no owner/group abstraction for host-managed transition sets
- there is no host-facing regeneration path comparable to chart-generated
  transitions
- `NavigationChartRegistration` is chart-registration-specific today: it stores
  chart identity, registration order, initialization state, a generated
  transition id prefix, and the current registered generated transition ids

## Relevance Assessment

This item is still relevant, but it is not an alpha blocker.

Why it is relevant:

- hosts may eventually want dynamic transition sets that are logically owned by
  one gameplay system rather than registered as many independent transitions
- grouped lifecycle management would improve usability for doors, ladders,
  scripted jump links, moving affordances, or other host-authored seams that
  appear and disappear together
- a richer owner/dependency model could reduce host-side unregister/re-register
  churn and centralize transition invalidation rules

Why it is not urgent:

- the current manual model already handles the important correctness case:
  active versus suppressed behavior tracks endpoint support correctly
- focused manual-transition lifecycle tests are currently green
- broadening `NavigationChartRegistration` into a shared host-managed transition
  model today would be speculative because the overlap between chart-generated
  and host-managed transitions is still shallow

Recommendation:

- keep this item active as a host-driven ergonomics and lifecycle track
- do not generalize `NavigationChartRegistration` yet
- only unify the chart-generated and host-managed state shapes if later
  implementation proves the shared abstraction is real rather than aspirational

## Phased Plan

### Phase 1. Define The Host Use Cases First

Before changing the model, define which manual-transition workflows are actually
missing.

Capture at least:

- grouped register/unregister of related transitions
- regeneration from a host-owned source of truth
- dependency invalidation beyond endpoint voxel support
- whether hosts need partial refresh for one group without touching unrelated
  manual transitions

Deliverables:

- one or two concrete target scenarios
- a clear statement of which current API pain points are host-side only versus
  runtime-side
- a decision on whether the missing feature is group lifecycle, regeneration, or
  both

### Phase 2. Add A Host-Managed Transition Group Model

If the use cases hold up, add a host-facing managed group concept without
changing the chart-generated implementation first.

Preferred shape:

- a separate owner/group record keyed by stable host identifier
- each group stores priority, current transition ids, and explicit dependency
  metadata
- dependency metadata starts small: endpoint voxels, optional volume-medium
  dependency, and optional host invalidation token or callback key

Why separate first:

- chart-generated transitions already have a stable chart-local generation model
- manual groups likely need different inputs and invalidation triggers than
  chart-owned pairs
- separating the first implementation reduces the risk of distorting the
  chart-generated path

### Phase 3. Add Regeneration And Group-Level Lifecycle APIs

Once a group model exists, add the minimum APIs hosts would need.

Possible surface:

- register or replace a managed manual transition group from explicit
  transitions
- refresh one managed group when its host source changes
- unregister one managed group without tracking ids manually
- optionally mark one group dirty and let Trailblazer rebuild it on the next
  safe maintenance step

Important rule:

- preserve deterministic precedence and registration behavior when regenerated
  transitions overlap with other managed or manual transitions

### Phase 4. Optimize Dependency Refresh

If grouped manual transitions land, keep refresh localized.

Preferred behavior:

- endpoint-voxel changes still refresh only groups touching those voxels
- volume-medium rule changes may still require broader reevaluation, but only
  for groups that declare a relevant volume dependency
- do not fall back to scanning every managed group for local chart edits when
  dependency indexes can keep the work O(k) in the affected set

### Phase 5. Reevaluate The Shared Abstraction

Only after the host-managed group model is real, decide whether
`NavigationChartRegistration` should delegate to or share a dedicated
dependency-state type.

Use this gate:

- if chart-generated and host-managed groups end up sharing the same owner
  metadata, id tracking, invalidation inputs, and delta-application flow, then
  unify them
- if they still differ materially, keep two focused implementations

This is the most important restraint in the plan.

### Phase 6. Verification And Documentation

After any new lifecycle model lands:

- add tests for grouped unregister and grouped regeneration
- add tests proving local voxel changes refresh only relevant managed manual
  groups
- add tests for precedence interactions between regenerated manual groups and
  generated transitions
- document the owner/group lifecycle in `docs/wiki/Transitions.md`
- link this plan from `docs/wiki/feature-work/hardeningPhasePlan.md`

## Guardrails

Keep this track narrow and practical.

Specifically:

- do not replace the current individual manual registration APIs
- do not unify chart-generated and host-managed state purely for aesthetic
  symmetry
- do not introduce hidden per-frame regeneration
- do not weaken the current endpoint-support suppression rules
- do not turn localized manual refresh into a full-registry scan for normal
  chart edits

## Risks

Main failure modes to defend against:

- over-abstracting around hypothetical host needs that never materialize
- making chart-generated transition management harder to reason about just to
  share code
- letting group regeneration accidentally reorder precedence or identity
  semantics
- losing the current local O(k) voxel-scoped refresh behavior for manual
  transitions

## Exit Criteria

This item is ready to implement when all of the following are true:

- there is a concrete host scenario that is awkward with independent manual
  transition ids
- grouped lifecycle or regeneration would materially simplify host integration
- the dependency inputs can be modeled explicitly enough to refresh affected
  groups deterministically
- the implementation can preserve current precedence, suppression, and
  local-refresh behavior
