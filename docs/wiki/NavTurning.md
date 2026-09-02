# NavTurning

`NavTurning` converts a desired direction into deterministic fixed-step
rotation. It buffers one requested target, interpolates toward it, detects
arrival, and can optionally create a turn after a collision.

Most applications should let `Navigator` drive turning. Use `NavTurning`
directly only when the host needs facing control without the full Navigator
lifecycle.

## What the host supplies

Turning works from snapshots rather than an engine interface. Each update uses:

- current world position;
- previous world position;
- current forward direction;
- current rotation.

If `TrySimulateTurn(...)` returns `true`, apply the returned quaternion. A
`false` result means no rotation should be applied for that frame.

## Request, then simulate

A turn request is buffered. It does not rotate the body immediately.

```csharp
var turning = new NavTurning(context, bodyRadius);

turning.RequestTurnDirection(currentForward, desiredForward);

if (turning.TrySimulateTurn(
        position,
        lastPosition,
        currentForward,
        currentRotation,
        out FixedQuaternion appliedRotation))
{
    currentRotation = appliedRotation;
}
```

The example variables are host snapshots. In a real integration, call
`TrySimulateTurn(...)` once per fixed step and apply the returned rotation to
the authoritative body.

`RequestTurnDirection(...)` accepts an optional interpolation override. When no
positive override is supplied, turning uses `TurnRate * context.DeltaTime`.
Interpolation is clamped to the closed range `[0, 1]`.

## Small changes are ignored

`NeedsTurn(...)` compares current and requested facing and ignores differences
within the minimum angle. This prevents tiny steering changes from producing
visible jitter or endless turn requests.

You can call the static helper before coordinating your own facing logic:

```csharp
if (NavTurning.NeedsTurn(currentForward, desiredForward))
    turning.RequestTurnDirection(currentForward, desiredForward);
```

`RequestTurnDirection(...)` already performs the same check, so the extra call
is needed only when the host wants to make another decision from the result.

## Buffered and active state

`TargetReached` means no interpolation is currently active. It does not promise
that no buffered request is waiting to start.

When an idle update finds a buffered target, that same update promotes it and
begins interpolation. `TargetRotation` then exposes the active target.

`StopTurn()` marks the active interpolation complete. It does not clear a
buffered request that has not started yet, so a later update may still consume
that request.

Only one target is buffered. `NavTurning` is not a queue.

## Collision-driven turning

`NotifyCollision()` records that an automatic turn should be considered. It
does not rotate immediately.

On a later idle update, turning compares the frame's actual movement with a
radius- and frame-rate-derived threshold. When the movement is large enough,
it buffers a turn toward that movement direction. The following update may
then begin rotation.

```csharp
turning.NotifyCollision();

// The first update may only buffer the collision turn.
turning.TrySimulateTurn(
    position,
    lastPosition,
    forward,
    rotation,
    out _);
```

Set `CanTurnOnCollision` when host state needs to veto automatic collision
turning. Returning `false` defers the pending collision turn; a later idle
update evaluates it again without introducing an engine dependency into
Trailblazer.

When driving a full controller, call `navigator.NotifyCollision()` instead so
the notification remains at the host-facing orchestration boundary.

## Navigator integration

Navigator owns the normal order:

1. steering produces a desired movement direction;
2. Navigator buffers that direction on `Turning`;
3. Navigator advances turning once;
4. `CommitFrameMotion()` applies the frame's rotation state.

Do not independently simulate `navigator.Turning` in the same frame. Use
`navigator.Simulate()` and `navigator.CommitFrameMotion()` so steering, motor,
and turning remain coordinated.

## Serialization

Navigator persistence records public turning configuration and state through
Chronicler. The owning context is restored separately and must already be
available when the existing Navigator shell is populated.

## Common mistakes

### Expecting an immediate turn

`RequestTurnDirection(...)` buffers a target. Rotation happens during
`TrySimulateTurn(...)` or the owning Navigator's simulation.

### Using `TargetReached` as “nothing is pending”

It describes active interpolation only. One buffered target may still exist.

### Expecting collision turning on the notification frame

Collision turning is staged: notify, evaluate and buffer, then simulate the
turn.

### Simulating Navigator-owned turning twice

Navigator already advances its turning controller. Calling the lower-level
method again can make frame behavior diverge from the intended lifecycle.

## Related guides

- [Navigator](Navigator.md)
- [NavSteering](NavSteering.md)
- [NavMotor](NavMotor.md)
- [Gravity](Gravity.md)
