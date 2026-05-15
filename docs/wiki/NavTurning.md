# NavTurning Reference

This document is the detailed reference for Trailblazer's deterministic facing layer.

If you only need the high-level architecture, read `OVERVIEW.md`.
If you need movement execution after a heading is chosen, read `NavMotor.md`.

The code referenced here lives primarily in:

- `src/Trailblazer/Navigation/Turning/NavTurning.cs`
- `src/Trailblazer/Navigation/Turning/NavTurning.Serialization.cs`

`NavTurning` is implemented as a partial class: `NavTurning.cs` contains the runtime turning
state and simulation behavior, while `NavTurning.Serialization.cs` contains the Chronicler
`RecordData(...)` implementation.

## 1. What NavTurning Is

`NavTurning` converts a desired facing direction into deterministic rotation updates over time.

It is responsible for:

- deciding whether a turn is actually needed
- buffering requested turns until the next simulation step
- interpolating from the current rotation toward a target rotation
- detecting turn completion
- optionally deriving an auto-turn direction after a collision

It is not responsible for:

- choosing a movement heading
- applying movement forces
- pathfinding
- environment probing

Those responsibilities belong to `NavSteering`, `NavMotor`, and the host navigator.

## 2. Core Design Model

`NavTurning` is buffered.

The turning flow is:

1. A caller requests a turn direction through `RequestTurnDirection(...)`, or collision handling buffers one internally.
2. The request is stored as a pending target rotation.
3. `TrySimulateTurn(...)` consumes that pending target when the controller is idle.
4. The controller interpolates rotation toward `TargetRotation` across simulation steps.
5. If a new rotation was produced, the host applies the returned quaternion.
6. Once close enough, the controller snaps to the final target and marks the turn complete.

Two details matter here:

- the buffered target is not the same thing as the active `TargetRotation`
- the controller only tracks one buffered target at a time; it is not a turn queue

## 3. Public Surface

The main entry points are:

- `CreateNew(Fixed64 radius)`
- `OnInitialize(Fixed64 radius)`
- `TrySimulateTurn(Vector3d position, Vector3d lastPosition, Vector3d forward, FixedQuaternion rotation, out FixedQuaternion appliedRotation)`
- `RequestTurnDirection(Vector3d curDirection, Vector3d targetDirection, Fixed64? interpolation = null)`
- `NeedsTurn(Vector3d currentForward, Vector3d targetDirection, Fixed64? minAngle = null)`
- `StopTurn()`
- `NotifyCollision()`

Important public state includes:

- `CanTurn`
- `TurnRate`
- `TargetReached`
- `TargetRotation`
- `CanTurnOnCollision`

## 4. Host Contract

`NavTurning` now operates on snapshots instead of a host interface. The host must supply:

- the current world position
- the previous world position
- the current forward direction
- the current rotation

If `TrySimulateTurn(...)` returns `true`, the host should apply the returned `appliedRotation`.

In normal `Navigator` usage, the turning integration looks like this:

1. `Navigator.Simulate()` asks steering for a heading when guided.
2. `Navigator.Simulate()` calls `Turning.RequestTurnDirection(Forward, _frameRequest.Direction)`.
3. `Navigator.Simulate()` calls `Turning.TrySimulateTurn(...)`.
4. If the call returns `true`, `Navigator` writes the returned rotation into its current frame state.

If you use `NavTurning` directly, the essential rule is:

- request or buffer a turn first
- then call `TrySimulateTurn(...)` once per fixed step
- only apply the returned rotation when the method reports success

## 5. Initialization and Thresholds

### 5.1 OnInitialize(...)

`OnInitialize(Fixed64 radius)` must be called before `TrySimulateTurn(...)`.

It does three important things:

- caches the navigator radius used for collision auto-turn thresholds
- marks the controller as arrived
- resets `TargetRotation` to identity

If `TrySimulateTurn(...)` is called before initialization, it throws.

### 5.2 Minimum Turn Threshold

`NavTurning` does not rotate for tiny direction changes.

It uses a private minimum angle threshold:

- `_minTurnRequiredAngle`

`NeedsTurn(...)` compares the current forward direction to the requested target direction and returns `true` only when the angular difference exceeds that threshold.

That keeps steering noise and tiny directional jitter from generating meaningless turn requests.

### 5.3 Collision Auto-Turn Threshold

Collision auto-turning uses a movement-distance threshold derived from:

- navigator radius
- the owning context's `FrameRate`

That threshold is recomputed from the current frame rate instead of being frozen once at initialization.

This means collision auto-turning only triggers after the navigator has actually moved far enough since the previous frame.

## 6. Request Lifecycle

### 6.1 RequestTurnDirection(...)

This is the normal entry point for a turn request.

It:

- checks `NeedsTurn(...)`
- ignores requests below the minimum turn threshold
- stores a buffered target rotation in `_pendingTarget`
- stores an optional interpolation override in `_pendingInterpolation`

Important detail:

- this does not immediately update `TargetRotation`
- the request is only promoted to the active target when `TrySimulateTurn(...)` consumes it

### 6.2 NeedsTurn(...)

`NeedsTurn(...)` compares two directions by:

- converting both to quaternions through `FixedQuaternion.FromDirection(...)`
- measuring the angular difference with `FixedQuaternion.Angle(...)`
- comparing that angle against the minimum threshold

This is the low-level gate that keeps turn buffering from happening for near-identical directions.

## 7. Per-Frame Update: TrySimulateTurn(...)

`TrySimulateTurn(...)` is the central turning update.

### 7.1 Preconditions

The method:

- throws if `OnInitialize(...)` has not been called
- returns `false` if `CanTurn` is false

If `CanTurn` is false, any already-buffered turn remains buffered until turning is enabled again.

### 7.2 Idle Phase

If `TargetReached` is true, the controller is considered idle.

At that point it does one of two things:

#### Consume a buffered target

If `_pendingTarget` exists:

- `TargetRotation` becomes the buffered target
- `_pendingTarget` is cleared
- `TargetReached` becomes `false`

Then the same call continues directly into interpolation.

#### Check collision auto-turn

If there is no buffered target:

- `CheckAutoTurn(...)` runs
- the method returns `false`

This means collision auto-turning is intentionally staged:

- one call can buffer the turn
- the next call consumes the buffer and begins the actual rotation

### 7.3 Interpolation Phase

Once the turn is active, `NavTurning` computes an interpolation factor `t`:

- use `_pendingInterpolation` if it is greater than zero
- otherwise use `TurnRate * DeltaTime` from the owning context

That value is clamped into `[0, 1]` and applied through `FixedQuaternion.Slerp(...)`.

The next rotation becomes either:

- the fully arrived target rotation
- or an intermediate rotation step toward it

### 7.4 Completion Phase

Turn completion is measured by:

- `FixedQuaternion.Angle(next, TargetRotation)`

If the remaining difference is within `_arriveThresholdAngle`:

- `appliedRotation` becomes `TargetRotation`
- `StopTurn()` marks the turn complete
- the method returns `true`

Otherwise:

- `appliedRotation` becomes the intermediate quaternion
- the method returns `true`

If no rotation should be applied this frame, the method returns `false` and leaves `appliedRotation` as identity.

## 8. Collision Auto-Turn

Collision auto-turning is a separate pathway from normal steering-driven turns.

### 8.1 NotifyCollision()

`NotifyCollision()` does not rotate immediately.

It only sets an internal `_isColliding` flag so that the next idle `TrySimulateTurn(...)` can evaluate whether an auto-turn should happen.

When you are driving turning through `Navigator`, prefer calling `navigator.NotifyCollision()` so collision handling stays centralized at the host-facing orchestration layer.

### 8.2 CheckAutoTurn(...)

When the controller is idle and collision has been flagged, `CheckAutoTurn(...)`:

1. computes the movement delta from `position - lastPosition`
2. checks whether that movement exceeds the collision threshold
3. checks whether `CanTurnOnCollision` vetoes the turn
4. normalizes the movement delta
5. requests a buffered turn from the current direction toward that delta

Important detail:

- the collision turn uses actual movement delta, not an arbitrary fallback axis
- if the movement is too small, `_isColliding` stays true and the controller retries next frame

### 8.3 Collision Predicate

`CanTurnOnCollision` is an optional predicate hook.

If it returns `false`, collision auto-turning is skipped even when the movement threshold is satisfied.

## 9. Stop and Arrival Semantics

### 9.1 StopTurn()

`StopTurn()` only does one thing:

- set `TargetReached = true`

It does not:

- clear `_pendingTarget`
- clear `_pendingInterpolation`
- reset `TargetRotation`

### 9.2 TargetReached

`TargetReached` means:

- there is no currently active interpolation in progress

It does not necessarily mean:

- there is no buffered turn waiting to start

## 10. Common Integration Pattern

With a `Navigator`, the usual flow is:

```csharp
context.Simulate();

if (navigator.IsGuideded)
{
    Vector3d heading = navigator.Steering.GetHeading(navigator);
    navigator.Turning.RequestTurnDirection(navigator.Forward, heading);
}

if (navigator.Turning.TrySimulateTurn(
    navigator.Position,
    navigator.LastPosition,
    navigator.Forward,
    navigator.Rotation,
    out FixedQuaternion appliedRotation))
{
    // Apply the returned rotation to your host state.
}
```

With lower-level usage, the minimum pattern is:

```csharp
var turning = new NavTurning(radius);
turning.RequestTurnDirection(currentForward, desiredForward);

if (turning.TrySimulateTurn(position, lastPosition, currentForward, currentRotation, out var appliedRotation))
{
    currentRotation = appliedRotation;
}
```

If you want a collision-driven auto-turn:

```csharp
turning.NotifyCollision();
turning.TrySimulateTurn(position, lastPosition, forward, rotation, out _); // may only buffer
turning.TrySimulateTurn(position, lastPosition, forward, rotation, out _); // may begin actual rotation
```

Or, with a `Navigator`:

```csharp
navigator.NotifyCollision();
navigator.Simulate(); // may only buffer
navigator.Simulate(); // may begin actual rotation
```

## 11. Common Gotchas

### Calling TrySimulateTurn(...) before OnInitialize(...)

This throws by design because the collision threshold has not been configured yet.

### Assuming RequestTurnDirection(...) rotates immediately

It does not. It only buffers a turn. `TrySimulateTurn(...)` applies it.

### Assuming StopTurn() clears pending turns

It does not. If a pending turn is already buffered, a later `TrySimulateTurn(...)` can still consume it.

### Assuming collision auto-turn applies on the first collision tick

Not always. The first call may only buffer the turn. The next call may actually start rotating.

### Assuming tiny direction changes should always rotate

They do not. `NeedsTurn(...)` intentionally filters out tiny angle changes.

### Assuming multiple collision notifications create a queue

They do not. `NavTurning` only tracks one buffered turn state.

## 12. Testing References

Current coverage around turning behavior is concentrated in:

- `tests/Trailblazer.Tests/Navigation/Turning/NavTurning.Tests.cs`
- `tests/Trailblazer.Tests/Navigation/Turning/TestDoubles/MockTurnAgent.cs`

Those tests cover:

- initialization requirements
- minimum-angle filtering
- explicit turn requests
- interpolation and arrival behavior
- collision auto-turn buffering
- collision veto behavior
- repeated collision notifications
- manual `StopTurn()` behavior

If you change turn buffering, completion thresholds, collision auto-turning, or interpolation behavior, update those tests in the same pass.
