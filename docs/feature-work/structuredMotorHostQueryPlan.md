# Structured Motor Host Query Plan

## Purpose

This plan covers the remaining structured motor host-query follow-up from
`hardeningPhasePlan.md`:

> Continue the structured motor host-query cleanup by collapsing the remaining climb veto callbacks
> into authoritative frame snapshots when host integrations justify it. Jump affordability already
> moved to `TrekRequest`, so the remaining value is in separating the last climb permission seams
> from notification events without growing a monolithic host interface.

The goal is to reduce host-side callback chaining and make motor permission inputs easier to reason
about without replacing the existing notification events or introducing a broad catch-all host
interface.

## Current State

`NavMotor` already consumes most host state as snapshots:

- `TrekRequest` provides the frame's intent
- `TrekRequest.CanAffordJump` now carries the authoritative per-frame jump affordability answer
- `FinalizeTraversal(...)` supplies the refreshed `TrekCondition`
- climb affordances already come through `IClimbAffordanceResolver` as an immutable
  `ClimbAffordanceSnapshot`
- active mantle validation already uses an immutable `MantleValidationSnapshot`

The remaining host query seams that still matter are callback-shaped:

- `Events.CanStartClimb`
- `Events.CanContinueClimb`

Important nuance:

- jump affordability is now sourced only from the frame snapshot
- climb already has `CanStartClimb` and `CanContinueClimb` inside `ClimbAffordanceSnapshot`, but the
  motor still allows extra late veto callbacks on top of that snapshot
- `NavMotorEvents` currently mixes notification hooks with behavior-gating queries

The host-facing contract is cleaner than it was, but climb still straddles immutable frame
snapshots and late callback decisions.

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
- treat Phase 1 and Phase 2 as complete
- focus the remaining work on climb veto ownership
- remove duplicated climb permission seams instead of carrying both paths forward

## Phased Plan

### Phase 1. Separate Behavioral Queries From Notifications

Completed.

Outcome:

- `NavMotorEvents` now documents climb query callbacks separately from notification hooks
- jump affordability is treated as frame-owned query data rather than a notification concern

### Phase 2. Move Jump Affordability To A Frame Snapshot

Completed.

Outcome:

- `TrekRequest.CanAffordJump` carries the host-owned answer once per frame
- `NavMotor` keeps its internal jump eligibility logic intact and consumes only the snapshot
- the old jump callback path was removed instead of preserved as a second authority source

### Phase 3. Collapse Climb Vetoes Into The Affordance Snapshot When Possible

If host integrations still use `Events.CanStartClimb` or `Events.CanContinueClimb`, prefer moving
that logic into the existing climb affordance result instead of layering more callback gates on top.

Preferred direction:

- the climb resolver returns the final `CanStartClimb` and `CanContinueClimb` answers for the frame
- the motor treats that snapshot as authoritative for climb permission
- the old callback veto path is removed in the same slice instead of carried as a second path

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

- preserve focused tests for jump affordability through the snapshot path
- add or update climb tests if climb veto ownership moves fully into the resolver snapshot
- update `docs/NAVMOTOR.MD` to distinguish notification hooks from host query inputs
- link this plan from `docs/feature-work/hardeningPhasePlan.md`

## Guardrails

Keep this work narrow and explicit.

Specifically:

- do not replace action notifications such as `OnStartJump` or `OnStartClimb`
- do not turn the remaining climb veto cleanup into a heavyweight host interface
- do not duplicate the same permission data in multiple live seams without one clear source of truth
- do not add per-frame allocations to carry host query data
- do not keep deprecated callback paths alive once the snapshot path lands

## Risks

Main failure modes to defend against:

- over-engineering a broad query framework for only one or two simple inputs
- creating conflicting authority between request snapshots, resolver snapshots, and leftover
  callbacks
- making host integration harder by moving too many concerns at once
- blurring the line between deterministic frame data and imperative event notifications

## Exit Criteria

This item is ready to implement when all of the following are true:

- at least one real host integration would benefit from replacing callback gates with frame-owned
  query data beyond the jump affordability snapshot that already landed
- a remaining narrow capability input can be represented cleanly as immutable snapshot data
- notification events remain separate from behavioral query inputs
- focused motor tests can pin current jump and climb behavior before the remaining climb ownership
  moves fully into snapshots
