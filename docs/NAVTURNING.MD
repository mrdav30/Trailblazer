# NavTurning Reference

This document is the detailed reference for Trailblazer's deterministic facing and turn-buffering layer.

If you only need the high-level architecture, read `OVERVIEW.md`.
If you need movement execution after a heading is chosen, read `NAVMOTOR.MD`.

The code referenced here lives primarily in:

- `src/Trailblazer/Navigation/Turning/NavTurning.cs`
- `src/Trailblazer/Navigation/Turning/Support/ITurn.cs`

## 1. What NavTurning Is

`NavTurning` is the layer that converts a desired facing direction into deterministic rotation updates over time.

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

The important design choice is that `NavTurning` is buffered.

The turning flow is:

1. A caller requests a turn direction through `RequestTurnDirection(...)`, or collision handling buffers one internally.
2. The request is stored as a pending target rotation.
3. `SimulateTurn(...)` consumes that pending target when the controller is idle.
4. The controller interpolates rotation toward `TargetRotation` across simulation steps.
5. Once close enough, the controller snaps to the final target and marks the turn complete.

Two details matter here:

- the buffered target is not the same thing as the active `TargetRotation`
- the controller only tracks one buffered target at a time; it is not a turn queue

## 3. Public Surface

The main entry points are:

- `CreateNew(Fixed64 radius)`
- `OnInitialize(Fixed64 radius)`
- `SimulateTurn(ITurn navigator)`
- `RequestTurnDirection(Vector3d curDirection, Vector3d targetDirection, Fixed64? interpolation = null)`
- `NeedsTurn(Vector3d currentForward, Vector3d targetDirection, Fixed64? minAngle = null)`
- `StopTurn()`
- `NotifyCollision()`

Important public state includes:

- `CanTurn`
- `TurnRate`
- `TargetReached`
- `TargetRotation`
- `OnApplyRotation`
- `CanTurnOnCollision`

## 4. Host Contract

`NavTurning` operates against `ITurn`. The host must supply:

- `Position`
- `LastPosition`
- `Rotation`
- `Forward`
- `ApplyRotation(FixedQuaternion rot)`

In normal `Navigator` usage, the turning integration looks like this:

1. `Navigator.Simulate()` asks steering for a heading.
2. `Navigator.Simulate()` calls `Turning.RequestTurnDirection(Forward, FrameRequest.Direction)`.
3. `Navigator.Simulate()` calls `Turning.SimulateTurn(this)`.

If you use `NavTurning` directly, the essential rule is:

- request or buffer a turn first
- then call `SimulateTurn(...)` once per fixed step

## 5. Initialization and Thresholds

### 5.1 OnInitialize(...)

`OnInitialize(Fixed64 radius)` must be called before `SimulateTurn(...)`.

It does three important things:

- computes the collision auto-turn movement threshold
- marks the controller as arrived
- resets `TargetRotation` to identity

If `SimulateTurn(...)` is called before initialization, it throws.

### 5.2 Minimum Turn Threshold

`NavTurning` does not rotate for tiny direction changes.

It uses a private minimum angle threshold:

- `_minTurnRequiredAngle`

`NeedsTurn(...)` compares the current forward direction to the requested target direction and returns `true` only when the angular difference exceeds that threshold.

That keeps steering noise and tiny directional jitter from generating meaningless turn requests.

### 5.3 Collision Auto-Turn Threshold

Collision auto-turning uses a movement-distance threshold derived from:

- navigator radius
- `TrailblazerManager.FrameRate`

That threshold is stored as squared distance in `_collisionTurnThreshold`.

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
- the request is only promoted to the active target when `SimulateTurn(...)` consumes it

### 6.2 NeedsTurn(...)

`NeedsTurn(...)` compares two directions by:

- converting both to quaternions through `FixedQuaternion.FromDirection(...)`
- measuring the angular difference with `FixedQuaternion.Angle(...)`
- comparing that angle against the minimum threshold

This is the low-level gate that keeps turn buffering from happening for near-identical directions.

## 7. Per-Frame Update: SimulateTurn(...)

`SimulateTurn(ITurn navigator)` is the central turning update.

### 7.1 Preconditions

The method:

- throws if `OnInitialize(...)` has not been called
- returns immediately if `CanTurn` is false

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
- the method returns immediately afterward

This means collision auto-turning is intentionally staged:

- one call can buffer the turn
- the next call consumes the buffer and begins the actual rotation

## 7.3 Interpolation Phase

Once the turn is active, `NavTurning` computes an interpolation factor `t`:

- use `_pendingInterpolation` if it is greater than zero
- otherwise use `TurnRate * TrailblazerManager.DeltaTime`

That value is clamped into `[0, 1]` and applied through:

- `FixedQuaternion.Slerp(...)`

The next rotation becomes either:

- the fully arrived target rotation
- or an intermediate rotation step toward it

## 7.4 Completion Phase

Turn completion is measured by:

- `FixedQuaternion.Angle(next, TargetRotation)`

If the remaining difference is within `_arriveThresholdAngle`:

- `OnApplyRotation` fires with `TargetRotation`
- `navigator.ApplyRotation(TargetRotation)` is called
- `StopTurn()` marks the turn complete

Otherwise:

- `OnApplyRotation` fires with the intermediate rotation
- `navigator.ApplyRotation(next)` is called

## 8. Collision Auto-Turn

Collision auto-turning is a separate pathway from normal steering-driven turns.

### 8.1 NotifyCollision()

`NotifyCollision()` does not rotate immediately.

It only sets an internal `_isColliding` flag so that the next idle `SimulateTurn(...)` can evaluate whether an auto-turn should happen.

### 8.2 CheckAutoTurn(...)

When the controller is idle and collision has been flagged, `CheckAutoTurn(...)`:

1. computes the movement delta from `position - lastPosition`
2. checks whether that movement exceeds `_collisionTurnThreshold`
3. checks whether `CanTurnOnCollision` vetoes the turn
4. normalizes the movement delta
5. requests a buffered turn from the current direction toward that delta

Important detail:

- the collision turn uses actual movement delta, not an arbitrary fallback axis
- if the movement is too small, `_isColliding` stays true and the controller retries next frame

### 8.3 Collision Predicate

`CanTurnOnCollision` is an optional predicate hook.

If it returns `false`, collision auto-turning is skipped even when the movement threshold is satisfied.

This is useful when:

- some collision states should not alter facing
- the host wants additional control over when collision response changes orientation

## 9. Stop and Arrival Semantics

### 9.1 StopTurn()

`StopTurn()` only does one thing:

- set `TargetReached = true`

It does not:

- clear `_pendingTarget`
- clear `_pendingInterpolation`
- reset `TargetRotation`

That behavior is important when reasoning about buffered state.

### 9.2 TargetReached

`TargetReached` means:

- there is no currently active interpolation in progress

It does not necessarily mean:

- there is no buffered turn waiting to start

This is one of the easiest parts of the class to misread.

## 10. Events and Hooks

`NavTurning` does not use a separate events container.

Instead it exposes two direct hooks:

### 10.1 OnApplyRotation

`OnApplyRotation` fires every time a rotation step is applied, including:

- intermediate slerp steps
- final snap-to-target application

This is useful for:

- animation sync
- visual debugging
- recording or mirroring turn state

### 10.2 CanTurnOnCollision

`CanTurnOnCollision` is a policy hook for collision-driven turns.

It does not affect ordinary `RequestTurnDirection(...)` calls.

## 11. Common Integration Pattern

With a concrete `Navigator`, the usual flow is:

```csharp
TrailblazerManager.Simulate();

var heading = navigator.Steering.GetHeading(navigator);
navigator.Turning.RequestTurnDirection(navigator.Forward, heading);
navigator.Turning.SimulateTurn(navigator);
```

With a lower-level `ITurn` implementation, the minimum pattern is:

```csharp
var turning = new NavTurning(radius);
turning.RequestTurnDirection(currentForward, desiredForward);
turning.SimulateTurn(host);
```

If you want a collision-driven auto-turn:

```csharp
turning.NotifyCollision();
turning.SimulateTurn(host); // may only buffer
turning.SimulateTurn(host); // may begin actual rotation
```

## 12. Common Gotchas

### Calling SimulateTurn(...) before OnInitialize(...)

This throws by design because the collision threshold has not been configured yet.

### Assuming RequestTurnDirection(...) rotates immediately

It does not. It only buffers a turn. `SimulateTurn(...)` applies it.

### Assuming StopTurn() clears pending turns

It does not. If a pending turn is already buffered, a later `SimulateTurn(...)` can still consume it.

### Assuming collision auto-turn applies on the first collision tick

Not always. The first call may only buffer the turn. The next call may actually start rotating.

### Assuming tiny direction changes should always rotate

They do not. `NeedsTurn(...)` intentionally filters out tiny angle changes.

### Assuming multiple collision notifications create a queue

They do not. `NavTurning` only tracks one buffered turn state.

## 13. Testing References

Current coverage around turning behavior is concentrated in:

- `tests/Trailblazer.Tests/Navigation/Turning/NavTurning.Tests.cs`
- `tests/Trailblazer.Tests/Navigation/Turning/Support/MockTurnAgent.cs`

Those tests currently cover:

- initialization requirements
- minimum-angle filtering
- explicit turn requests
- instant interpolation
- idle behavior after arrival
- collision auto-turn buffering
- collision veto behavior
- repeated collision notifications
- manual `StopTurn()` behavior

If you change turn buffering, completion thresholds, collision auto-turning, or interpolation behavior, update those tests in the same pass.
