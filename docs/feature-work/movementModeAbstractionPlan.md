# Movement Mode Abstraction Plan

## Purpose

This plan covers optional runtime follow-up item 4 from `hardeningPhasePlan.md`:

> If climbing and other attached locomotions expose enough overlap after implementation, revisit
> whether the motor should grow a generalized movement-mode abstraction for mutually exclusive
> controlled modes such as swim, fly, and climb. Do not preemptively refactor toward this before
> the climb runtime proves the real common shape.

The goal is to reduce duplicated mode-handling logic in `NavMotor` only if the overlap is strong
enough to justify a shared abstraction without obscuring the meaningful differences between swim,
flight, and climb.

## Current State

The current motor now has real implementations for all three locomotion branches:

- `SwimLocomotion`
- `FlyLocomotion`
- `ClimbLocomotion`

There is visible overlap in `NavMotor`:

- mode-specific desired-velocity selection
- mode-specific acceleration selection
- mode-specific gravity or buoyancy handling
- mutual exclusion with other transient locomotion states
- runtime transient flags such as `IsSwimming`, `IsFlying`, and `IsClimbing`

But the control models are still materially different:

- swim is medium-driven and partly passive
- flight is request-driven active control in gas
- climb is attachment-driven, host-resolved, and mantle-aware

Important nuance:

- climb did prove that overlap exists
- it did not prove that a single generalized movement-mode model is the right design yet

## Relevance Assessment

This item is still relevant, but it should remain conditional.

Why it is relevant:

- `NavMotor` now contains repeated branches for fly and climb around acceleration, gravity
  compensation, control ownership, and transient-state exclusion
- future attached locomotions would likely add more of the same style of branch logic
- a carefully chosen shared seam could improve usability and reduce code duplication in the motor

Why a full abstraction is not justified yet:

- swim, fly, and climb are not just three variants of the same behavior
- climb includes affordance resolution, attachment continuity, detach jump rules, and mantle
  behavior that do not map cleanly onto swim or flight
- swim remains partly environmental and medium-owned rather than purely controlled-mode-owned
- the current code is readable enough that forcing a common model now would likely hide important
  differences instead of simplifying them

Recommendation:

- keep this item active
- do not introduce a generalized movement-mode abstraction yet
- if overlap grows, start with narrow helper extraction or an internal policy seam before creating a
  new public or broad runtime abstraction

## Phased Plan

### Phase 1. Track Concrete Duplication Instead Of Abstract Similarity

Before changing the shape of the motor, identify the exact duplicated decisions that recur across
controlled modes.

Measure or catalogue at least:

- desired-velocity computation overlap
- acceleration-source selection overlap
- gravity-compensation handling overlap
- mutual-exclusion and transient-state clearing overlap
- control-ownership logic overlap

Deliverables:

- a short list of duplicated motor branches with file/line references
- a call on whether the duplication is behavioral or only superficial
- a decision on whether the next step should be helper extraction or no change

### Phase 2. Extract Narrow Internal Helpers First

If the duplicated logic is real, start with small internal seams instead of a new mode framework.

Preferred examples:

- a helper for gravity-compensated controlled movement
- a helper for mutually exclusive transient-state clearing
- a helper for mode-specific acceleration resolution

Important rule:

- helpers should make the existing logic easier to read without forcing swim, fly, and climb into a
  false inheritance or interface hierarchy

### Phase 3. Introduce An Internal Controlled-Mode Policy Only If The Shape Repeats

If future work adds another attached controlled locomotion and the duplication still grows, consider
an internal-only policy seam.

Preferred shape:

- an internal policy object or struct for mutually exclusive controlled modes
- queried by `NavMotor` for only the shared concerns
- mode-specific attachment, resolver, or medium semantics stay in the concrete locomotion paths

Shared concerns that may fit:

- max acceleration while active
- gravity compensation while active
- active-state priority and exclusion behavior
- desired-velocity projection for controlled movement

Concerns that should likely remain separate:

- climb affordance resolution and mantle lifecycle
- swim dive and drowning state
- flight request gating and air-control specifics

### Phase 4. Reevaluate Whether A Real Movement-Mode Abstraction Exists

Only after the internal seam has proven useful should the motor consider a broader abstraction.

Use this gate:

- if three or more controlled locomotions truly share the same activation model, state shape, and
  force-resolution path, then broaden carefully
- if the differences still dominate, keep the narrower helpers and explicit branches

This is the key restraint for the item.

### Phase 5. Verification And Documentation

After any shared seam lands:

- extend motor tests to cover cross-mode exclusivity and transition behavior
- add tests proving helper extraction does not change swim, fly, or climb output
- document the chosen internal seam in `docs/NAVMOTOR.MD` only if it materially changes how the
  motor is structured
- link this plan from `docs/feature-work/hardeningPhasePlan.md`

## Guardrails

Keep this track pragmatic.

Specifically:

- do not invent a shared interface just because several locomotions expose speed and acceleration
- do not fold climb affordance resolution into a generic movement-mode contract
- do not make swim less medium-driven just to fit a shared model
- do not trade explicit deterministic control flow for indirection that is harder to debug
- do not change serialization shape unless the runtime structure actually changes

## Risks

Main failure modes to defend against:

- building an abstraction around the names of fields rather than the real runtime behavior
- making climb harder to evolve because it is forced into a shared mode contract
- hiding important motor invariants behind too many indirection layers
- paying abstraction cost without meaningfully reducing complexity or branching

## Exit Criteria

This item is ready to implement when all of the following are true:

- the duplicated mode logic is concrete and recurring, not just conceptually similar
- a narrow helper or internal policy seam would reduce complexity without flattening important
  swim/fly/climb differences
- focused motor tests can pin current behavior before refactoring
- the resulting design is simpler to reason about than the current explicit branches
