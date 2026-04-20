# Active Mantle Validation Plan

## Purpose

This document captures a narrow hardening slice for optional host validation while mantle is already
in progress.

The goal is not to change the default mantle behavior. The goal is to provide an opt-in seam for
hosts that need active mantle to cancel or slip when the top-out state becomes invalid after mantle
has started.

## Why This Still Matters

The current mantle runtime is intentionally self-contained once it begins:

- climb attachment is resolved through `IClimbAffordanceResolver`
- mantle starts from a compatible ledge snapshot with a fixed mantle target
- once `ClimbLocomotion.IsMantling` is true, `NavMotor.UpdateClimbState(...)` stops consulting the
  climb resolver for continuation data
- active mantle only stops when:
  - the traversal medium becomes `Unknown`
  - the navigator enters liquid
  - the mantle target is reached within tolerance
  - the host has already transitioned the navigator to solid

That is deterministic and simple, and it is still a good default.

The gap is that some hosts may need stricter runtime invalidation during mantle, for example:

- the top-out space becomes blocked after mantle starts
- the ledge or climbable moves or disappears
- a scripted world event invalidates the mantle destination
- the host wants mantle to fail early instead of riding the current target until completion

Today those hosts cannot express that through the existing resolver without changing traversal
medium state in a more indirect way.

## Recommendation

Keep the current self-contained mantle path as the default.

Do not make active mantle re-query the normal climb resolver automatically for all hosts.

Instead, add an optional mantle-validation seam that hosts can implement when they need dynamic
cancel-or-slip behavior after mantle has started.

## Why Not Reuse `IClimbAffordanceResolver` Directly

The current resolver contract is request-driven:

- `TryResolveClimbAffordance(TrekRequest request, TransitState currentState, out ClimbAffordanceSnapshot snapshot)`

That is the right shape for climb start and climb continuation while the request is still actively
driving attachment.

It is a weaker fit for active mantle because:

- mantle is already engaged and no longer primarily request-driven
- hosts may allow mantle to continue even if the player is no longer holding climb input
- reusing the same method would blur together two different responsibilities:
  climb acquisition versus mantle validation

For that reason, the recommended hardening path is a separate optional seam rather than silently
changing the meaning of the existing climb resolver contract.

## Proposed Shape

### 1. Keep mantle self-contained by default

No behavior change for existing hosts:

- if a host does nothing, mantle continues exactly as it does today
- current tests and integrations remain valid

### 2. Add an optional mantle-validation contract

Recommended direction:

- add a separate optional interface implemented by hosts that want mantle-time validation

Example shape:

```csharp
public interface IActiveMantleValidator
{
    bool TryValidateActiveMantle(
        TransitState currentState,
        ActiveMantleState activeMantle,
        out MantleValidationSnapshot snapshot);
}
```

Key point:

- keep it data-only and deterministic
- do not pass engine objects or mutable host handles through the runtime seam

### 3. Add a narrow active-mantle state payload

The validator needs the current mantle state without exposing `ClimbLocomotion` itself as a mutable
runtime object.

Recommended data:

- active affordance id when available
- climb kind
- attachment point
- surface normal
- up direction
- fixed mantle target position

This can be a small readonly struct such as `ActiveMantleState`.

### 4. Make validation cancel-only in the first pass

The first hardening slice should validate whether the current mantle may continue.

It should not:

- retarget mantle mid-run
- morph mantle speed or direction from host callbacks
- reopen climb acquisition logic during mantle

Recommended first-pass behavior:

- if validation is enabled and the validator reports failure, force a climb slip
- if validation succeeds, continue using the originally latched mantle target and completion logic

That keeps the seam narrow and deterministic.

### 5. Gate it behind an explicit config toggle

Recommended direction:

- add a climb config field such as `ValidateActiveMantleWithHost`

Default:

- `false`

Reasoning:

- existing simple hosts should keep the current low-complexity runtime
- dynamic hosts can opt into stricter validation intentionally

## Runtime Integration

Recommended runtime behavior:

1. Mantle starts exactly as it does today from a compatible ledge snapshot.
2. If active mantle validation is disabled, keep the current self-contained path.
3. If validation is enabled and the configured resolver supports the optional mantle validator
   interface:
   - query the mantle validator once per frame while `IsMantling` is true
   - if the validator fails or reports `CanContinueMantle == false`, call `StopClimb(wasForced: true)`
   - otherwise continue normally
4. Keep existing completion rules:
   - host transitions to solid
   - mantle target reached within tolerance
   - liquid / unknown medium failure

## Non-Goals

- changing the default mantle runtime for all hosts
- making mantle continuously retarget against moving geometry
- making mantle depend on held climb input after it has started
- turning mantle into a separate locomotion
- broadening `IClimbAffordanceResolver` in a way that breaks existing implementations

## Implementation Slice

### Phase 1. Contract

1. Add the optional mantle validator interface.
2. Add the readonly active mantle state payload.
3. Add the readonly mantle validation snapshot payload.
4. Add the explicit climb config toggle for active mantle validation.

### Phase 2. NavMotor Integration

1. Keep the current mantle path as the default.
2. Add an opt-in validation branch while `ClimbLocomotion.IsMantling` is true.
3. On validation failure, force a climb slip through the existing stop path.
4. Do not retarget mantle in this slice.

### Phase 3. Coverage

Add focused tests that prove:

1. default mantle behavior is unchanged when validation is disabled
2. active mantle slips when the validator invalidates the top-out state
3. active mantle continues when validation succeeds
4. the normal mantle completion path still works when validation is enabled

### Phase 4. Documentation

Update:

- `docs/NAVMOTOR.MD`
- any climb-specific API reference that describes the host affordance seam

Document that active mantle validation is optional and cancel-only in the first pass.

## Acceptance Criteria

This hardening slice is complete when:

1. existing mantle behavior remains unchanged by default
2. hosts can opt into per-frame mantle validation after mantle starts
3. invalid mantle continuation can force a slip without requiring an indirect medium hack
4. the seam is data-only and deterministic
5. focused tests cover both the default path and the opt-in validation path
