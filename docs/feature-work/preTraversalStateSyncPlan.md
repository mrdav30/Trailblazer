# Pre-Traversal State Sync Plan

## Purpose

This document captures a narrow hardening slice for the pre-traversal traversal-state handoff between
hosts, `Navigator`, and `NavMotor`.

The goal is not to change motor behavior. The goal is to make an already-supported seam more obvious
and harder to misuse when hosts learn about medium or surface changes before the next simulation
step begins.

## Why This Still Matters

The current runtime already supports this handoff:

- hosts can update `_frameCondition` through `Navigator.SetGroundContact(...)`,
  `SetAirborne(...)`, `SetWaterContact(...)`, `SetTrekCondition(...)`, or
  `ReplaceTrekCondition(...)`
- hosts can push the updated condition into the motor immediately through
  `NavMotor.UpdateTraversal(...)`
- the navigator helpers expose that via `updateMotorState: true`

That is functionally correct.

The hardening issue is that the seam is implicit:

- `UpdateTraversal(...)` reads like a generic state mutation method rather than a named
  pre-traversal sync step
- `updateMotorState: true` is a behavior switch on unrelated contact helpers rather than an
  explicit lifecycle operation
- tests and custom integrations can easily miss when the motor must see the new traversal state
  before the next `Simulate()` or `TryTraversal(...)`

The mantle-to-solid handoff is the clearest example. The runtime behaves correctly when the host
pushes the updated solid state into the motor before the next traversal step, but the required call
path is easy to discover only after reading the implementation or existing tests.

## Recommendation

Keep the current semantics and add a clearer named sync seam.

Do not add automatic dirty-state syncing inside `Navigator.Simulate()` or `NavMotor.TryTraversal(...)`.
That would hide ordering rules and make host timing less explicit.

Instead:

1. Expose a named pre-traversal sync method on `NavMotor`.
2. Expose a navigator-level helper that forwards the current `_frameCondition` through that method.
3. Route the existing `updateMotorState: true` helpers through the named sync method so behavior
   stays unchanged while the intended lifecycle becomes clearer in code and docs.

## Proposed Shape

### 1. Add a clearer `NavMotor` entry point

Recommended direction:

- add `SyncTraversalState(TrekCondition newCondition, bool isInitializing = false)`

Behavior:

- identical to the current `UpdateTraversal(...)`
- explicitly documented as the way to push a new traversal snapshot into the motor before the next
  traversal phase begins

Compatibility:

- keep `UpdateTraversal(...)` for now
- either forward it to `SyncTraversalState(...)` or make `SyncTraversalState(...)` the documented
  preferred path while leaving `UpdateTraversal(...)` as a compatibility alias

This keeps public behavior stable while improving readability.

### 2. Add a navigator-level helper

Recommended direction:

- add `Navigator.SyncCurrentTrekConditionToMotor()`

Behavior:

- pushes the current `_frameCondition` into the motor immediately
- no mutation, no probing, no hidden refresh
- just an explicit lifecycle handoff

Why this helps:

- high-level integrations should not have to call `Motor.UpdateTraversal(...)` directly for a
  normal pre-simulation contact handoff
- tests can express intent more clearly

### 3. Keep existing contact helpers, but route through the explicit seam

For:

- `SetGroundContact(...)`
- `SetAirborne(...)`
- `SetWaterContact(...)`
- `SetTrekCondition(...)`
- `ReplaceTrekCondition(...)`

keep `updateMotorState: true` behavior unchanged, but implement it through the named navigator or
motor sync helper.

This preserves compatibility while making the lifecycle clearer internally.

## Non-Goals

- changing when `FinalizeTraversal(...)` runs
- auto-syncing traversal state every frame
- adding a new traversal-state cache or dirty-flag system
- changing the semantics of `SetGroundContact(...)` or `SetTrekCondition(...)`
- widening this into a general locomotion-mode refactor

## Implementation Slice

### Phase 1. API Clarity

1. Add the named `NavMotor` sync method.
2. Add the named navigator helper for syncing `_frameCondition`.
3. Route the `updateMotorState: true` code paths through the named helper.
4. Leave runtime behavior unchanged.

### Phase 2. Coverage

Add focused tests that prove:

1. the named navigator helper updates `Motor.CurrentState` before the next simulation step
2. the named `NavMotor` sync method preserves current `UpdateTraversal(...)` behavior
3. the mantle-to-solid pre-sim handoff can be expressed through the named sync seam instead of a
   low-level ad hoc mutation path

### Phase 3. Documentation

Update:

- `docs/NAVMOTOR.MD`
- `docs/NAVIGATOR.MD`

The docs should describe this as an explicit pre-traversal sync seam rather than only as a boolean
option on contact helpers.

## Acceptance Criteria

This hardening slice is complete when:

1. there is an explicit, named way to push traversal state into the motor before the next
   traversal phase
2. navigator integrations do not need to call `Motor.UpdateTraversal(...)` directly for the common
   high-level handoff case
3. existing host behavior remains backward compatible
4. focused tests cover the seam
5. `NAVIGATOR.MD` and `NAVMOTOR.MD` describe the lifecycle clearly
