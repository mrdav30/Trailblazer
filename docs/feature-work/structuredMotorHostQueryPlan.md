# Structured Motor Host Query Plan

## Purpose

This plan covers optional runtime follow-up item 5 from `hardeningPhasePlan.md`:

> If the climb affordance resolver pattern lands cleanly, revisit whether narrow capability events
> such as `CanAffordJump` should move toward a more structured host query contract as well, so the
> motor consumes deterministic frame snapshots instead of accumulating permission and environment
> state through callback chains.

The goal is to reduce host-side callback chaining and make motor permission inputs easier to reason
about without replacing the existing notification events or introducing a broad catch-all host
interface.

## Current State

`NavMotor` already consumes most host state as snapshots:

- `TrekRequest` provides the frame's intent
- `FinalizeTraversal(...)` supplies the refreshed `TrekCondition`
- climb affordances already come through `IClimbAffordanceResolver` as an immutable
  `ClimbAffordanceSnapshot`
- active mantle validation already uses an immutable `MantleValidationSnapshot`

The remaining host query seams are still callback-shaped:

- `Events.CanAffordJump`
- `Events.CanStartClimb`
- `Events.CanContinueClimb`

Important nuance:

- jump affordability is still a late `Func<bool>` veto at the end of `CanApplyJumpForce(...)`
- climb already has `CanStartClimb` and `CanContinueClimb` inside `ClimbAffordanceSnapshot`, but the
  motor still allows extra late veto callbacks on top of that snapshot
- `NavMotorEvents` currently mixes notification hooks with behavior-gating queries

This works today and the relevant jump and climb tests are green, but the host-facing contract is
split across immutable frame snapshots and late callback decisions.

## Relevance Assessment

This item is still relevant, but it should stay narrow and demand-driven.

Why it is relevant:

- the climb resolver pattern did prove that a structured, immutable host query seam fits the motor
- moving capability decisions into frame-owned snapshots would reduce callback-chain indirection for
  hosts
- separating notifications from behavioral queries would make the API easier to understand and test
- a snapshot-first model is a cleaner deterministic story than mixing frame data with late delegate
  vetoes

Why it is not urgent:

- the current callback gates are simple and do not appear to be a correctness problem
- there is no evidence that these delegates are a meaningful runtime hot path
- the main benefit here is host usability and state ownership clarity, not raw frame-time savings

Recommendation:

- keep this item active
- do not build a large generalized motor host interface
- start with the narrowest proven case: jump affordability
- treat climb veto callbacks as compatibility seams, and only retire them if the climb snapshot
  fully covers the real host needs

## Phased Plan

### Phase 1. Separate Behavioral Queries From Notifications

Before changing the runtime shape, explicitly inventory which `NavMotorEvents` members are true
notifications and which ones are behavior gates.

Deliverables:

- a short map of gating callbacks versus post-state notifications
- a decision on which gating inputs are frame-owned and should be snapshot data
- a decision on which callbacks remain action notifications and should stay in `NavMotorEvents`

Important rule:

- do not conflate "host needs to be notified" with "host needs to answer a question"

### Phase 2. Move Jump Affordability To A Frame Snapshot

Handle the narrowest case first.

Preferred direction:

- replace `Events.CanAffordJump` with data the host computes once for the frame
- carry that result through a small immutable snapshot, preferably attached to `TrekRequest` or a
  closely related frame-owned motor input type
- keep the motor's internal jump checks intact; only the host-owned affordability decision moves

Why this is the right first slice:

- jump affordability is currently the cleanest standalone callback gate
- it does not need geometry resolution the way climb does
- it improves host usability without forcing a broader abstraction decision yet

### Phase 3. Collapse Climb Vetoes Into The Affordance Snapshot When Possible

If host integrations still use `Events.CanStartClimb` or `Events.CanContinueClimb`, prefer moving
that logic into the existing climb affordance result instead of layering more callback gates on top.

Preferred direction:

- the climb resolver returns the final `CanStartClimb` and `CanContinueClimb` answers for the frame
- the motor treats that snapshot as authoritative for climb permission
- any remaining callback veto stays only as a temporary compatibility bridge

Important constraint:

- do not force unrelated jump and climb concerns into one shared type yet

### Phase 4. Introduce A Dedicated Query Provider Only If More Cases Emerge

If more narrow capability queries appear later, consider a dedicated host-query seam.

Preferred shape:

- a focused provider for motor capability queries
- each query returns immutable frame data or immutable query results
- the provider remains separate from notification events

What not to do:

- do not create a monolithic `INavMotorHost`
- do not make the motor depend on a live callback chain for per-frame decisions
- do not generalize until two or more query families actually share the same shape

### Phase 5. Verification And Documentation

After any structured query work lands:

- add focused tests for jump affordability through the new snapshot path
- preserve compatibility coverage if legacy callback shims remain temporarily
- add or update climb tests if climb veto ownership moves fully into the resolver snapshot
- update `docs/NAVMOTOR.MD` to distinguish notification hooks from host query inputs
- link this plan from `docs/feature-work/hardeningPhasePlan.md`

## Guardrails

Keep this work narrow and explicit.

Specifically:

- do not replace action notifications such as `OnStartJump` or `OnStartClimb`
- do not turn one simple jump-affordability callback into a heavyweight host interface
- do not duplicate the same permission data in multiple live seams without one clear source of truth
- do not add per-frame allocations to carry host query data
- do not remove compatibility callbacks until the snapshot path has proven sufficient

## Risks

Main failure modes to defend against:

- over-engineering a broad query framework for only one or two simple inputs
- creating conflicting authority between request snapshots, resolver snapshots, and compatibility
  callbacks
- making host integration harder by moving too many concerns at once
- blurring the line between deterministic frame data and imperative event notifications

## Exit Criteria

This item is ready to implement when all of the following are true:

- at least one real host integration would benefit from replacing callback gates with frame-owned
  query data
- jump affordability or another narrow capability input can be represented cleanly as immutable
  snapshot data
- notification events remain separate from behavioral query inputs
- focused motor tests can pin current jump and climb behavior before the compatibility seams change
