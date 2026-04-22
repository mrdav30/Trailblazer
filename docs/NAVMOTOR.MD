# NavMotor Reference

This document is the detailed reference for Trailblazer's deterministic movement motor.

If you only need the high-level architecture, read `OVERVIEW.md`.
If you need the vertical-force model specifically, read `GRAVITY.MD`.

The code referenced here lives primarily in:

- `src/Trailblazer/Navigation/Motor/NavMotor.cs`
- `src/Trailblazer/Navigation/Motor/Locomotion/*`
- `src/Trailblazer/Navigation/Motor/Support/*`

## 1. What NavMotor Is

`NavMotor` is the movement-execution layer for a `Navigator`.

It is responsible for:

- turning a frame's movement request into deterministic motion deltas
- applying locomotion rules for ground, air, water, slopes, slides, jumps, controlled flight, and moving platforms
- tracking traversal-state transitions between frames
- producing deterministic movement output using fixed-point math and a fixed simulation timestep

It is not responsible for:

- pathfinding
- steering decisions
- turn-target selection
- environment probing or collision detection

Those responsibilities belong to `NavSteering`, `NavTurning`, and the host navigator or world code.

## 2. Core Design Model

`NavMotor` is intentionally two-phase:

1. `TryTraversal(TrekRequest request, out Vector3d velocityDelta, out Vector3d positionDelta, out FixedQuaternion rotationDelta)` computes this frame's movement deltas.
2. `FinalizeTraversal(...)` reconciles the result after the host has applied those deltas and refreshed traversal state.

That split is what lets Trailblazer stay deterministic while still depending on host-owned surface, water, ceiling, and platform data.

Another important detail:

- names like `_forceOutput` are historical
- in practice, `NavMotor` behaves more like a velocity-oriented movement accumulator than a classical rigidbody force solver

The motor starts from `Handler.Move.FrameVelocity`, adjusts that output according to locomotion rules, then converts it into per-frame deltas.

## 3. Public Surface

The main entry points are:

- `CreateNew(TrekCondition initialCondition)`
- `OnInitialize(TrekCondition condition)`
- `TryTraversal(TrekRequest request, out Vector3d velocityDelta, out Vector3d positionDelta, out FixedQuaternion rotationDelta)`
- `FinalizeTraversal(Vector3d newPosition, Vector3d lastPosition, FixedQuaternion newRotation, TrekCondition conditionRefresh, Vector3d? newFootPosition = null)`
- `AbortTraversalFrame()`
- `SyncTraversalState(TrekCondition newCondition, bool isInitializing = false)`
- `SetVelocity(Vector3d velocity)`
- `GetVerticalJumpSpeed()`
- `IsTooSteep(Fixed64 angle)`
- `IsOnSlope(Fixed64 angle)`

Important public state includes:

- `Handler`
- `Events`
- `ClimbResolver`
- `CurrentState`
- `FrameSlopeAngle`
- `TraversalInProgress`
- `IsInitialized`
- `IsOnSolid`
- `WasOnSolid`
- `IsInGas`
- `WasInGas`
- `IsInLiquid`
- `WasInLiquid`
- `InLimbo`
- `Handler.Platform.InteriaApplied`
- `Handler.Platform.IsActive`
- `Handler.Platform.IsLockedToPlatform`

## 4. Host Contract

`NavMotor` operates on snapshots rather than a host interface. The host must supply:

- a populated `TrekRequest`, including any frame-owned query answers such as `CanAffordJump`
- the current post-application position and last position
- the current post-application rotation
- the refreshed `TrekCondition`
- the current foot position when platform tracking is needed

This is the expected contract around the two phases:

1. Populate a `TrekRequest` for the frame.
2. Call `TryTraversal(...)`.
3. If the call returns `true`, apply the returned velocity, position, and rotation deltas.
4. Re-evaluate the environment and refresh `TrekCondition`.
5. Call `FinalizeTraversal(...)` with the updated state snapshot.

If the host needs to push a new traversal state into the motor before the next traversal phase, it
should use `SyncTraversalState(...)` directly or go through `Navigator.SyncCurrentTrekConditionToMotor()`.
Helpers like `Navigator.SetGroundContact(..., updateMotorState: true)` and the lower-level
`Navigator.SetTrekCondition(updateMotorState: true)` route through that same explicit seam.

`NavMotorEvents` still contains a few climb-specific query callbacks, but jump affordability is not one of them.
Hosts should answer jump affordability through `TrekRequest`.

### 4.1 Climb Affordance And Mantle Validation

Climb integration is host-owned.

`NavMotor` does not probe climbable geometry itself. Instead:

- `ClimbResolver` may provide climb affordance snapshots through `IClimbAffordanceResolver`
- the host may still veto climb start or continuation through `Events.CanStartClimb` and
  `Events.CanContinueClimb`

Active mantle validation is a separate optional seam.

When `Handler.Climb.ValidateActiveMantleWithHost` is `true` and `ClimbResolver` also implements
`IActiveMantleValidator`, the motor will query that validator once per frame while mantle is active.
That validation is cancel-only in the current slice:

- returning failure from `TryValidateActiveMantle(...)` stops the climb with a forced slip
- returning `CanContinueMantle = false` also stops the climb with a forced slip
- successful validation keeps the originally latched mantle target and completion rules unchanged

If the toggle is off, or the resolver does not implement `IActiveMantleValidator`, mantle keeps the
default self-contained behavior.

## 5. Initialization and State Ownership

`NavMotor` owns runtime locomotion state through `LocomotionHandler`.

By default the handler installs the built-in set:

- `Move`
- `Platform`
- `Jump`
- `Fall`
- `Slide`
- `Swim`
- `Fly`
- `Climb`

`Move` and `Fall` are the required core locomotions. `Platform`, `Jump`, `Slide`, `Swim`, `Fly`,
and `Climb` are optional and can be installed or removed through a locomotion profile.

These locomotion objects are per-motor, not global. That means different navigators can run different movement parameters at the same time, and different navigator types can use different locomotion compositions entirely.

Examples:

- different gravity values per navigator
- different jump heights
- different platform-transfer rules
- different buoyancy or swim drag
- different installed locomotion sets such as a humanoid versus a move-and-fall-only boulder

`CurrentState` is stored as a `TransitState`, which mirrors the current `TrekCondition` plus previous-state data used for transition logic.

## 6. Phase 1: TryTraversal(...)

`TryTraversal(...)` is the movement-output phase.

### 6.1 Preconditions

`TryTraversal(...)` returns `false` if:

- the motor is not initialized
- traversal has already started earlier in the same frame

This pending-finalization guard is intentional. The motor is designed to process movement exactly once per simulation frame.
If a previous frame is still pending when a new frame starts, the motor throws an explicit error instead of silently continuing.

### 6.2 Frame Setup

At the start of traversal, the motor:

1. reads the supplied `TrekRequest`
2. computes `FrameSlopeAngle` from `CurrentState.GetSignedSlopeAngle(...)`
3. seeds `_forceOutput` from `Handler.Move.FrameVelocity`
4. updates platform velocity through `Handler.Platform.UpdatePlatformVelocity()`

This is important because the motor starts from the previous frame's actual movement state, not from zero.

### 6.3 Limbo and Cooldown Handling

Before applying movement:

- `InLimbo` disables control until the motor regains a valid traversal context
- jump cooldown is advanced through `Handler.Jump.UpdateCooldown()`

`InLimbo` means the motor is not grounded, not clearly swimming, and not in an active jump or fall state. It is effectively a guarded transitional state.

### 6.4 ComputeMovementForces()

This is where the motor derives the movement intent for the frame.

Responsibilities:

- decide whether the navigator currently has control
- detect steep-slope sliding
- activate controlled air flight when the frame request asks for it and the installed profile supports it
- clear or preserve vertical output depending on grounded or airborne state
- compute desired velocity
- apply friction in grounded idle cases
- clamp acceleration toward the desired velocity

Key behaviors:

- grounded steep slopes can force `Handler.Slide.IsSliding = true`
- air control is reduced or disabled depending on locomotion state
- controlled flight can own airborne movement without classifying intentional descent as a fall
- grounded movement can be projected along the surface normal
- slope speed multipliers can modify horizontal speed
- horizontal speed selection is evaluated in the request's local frame, so negative local forward movement uses the configured backward-speed cap

Supporting helpers involved here:

- `GetDesiredVelocity()`
- `GetHorizontalVelocity()`
- `MaxHoritzontalSpeedInDirection(...)`
- `GetMaxAcceleration()`

### 6.5 ApplyEnvironmentalForces()

This phase applies vertical environmental behavior:

- grounded downward stick
- airborne gravity
- flight gravity compensation
- terminal-velocity clamping
- water buoyancy
- held-jump gravity offset

This is the gravity and buoyancy stage, not the initial jump-impulse stage.

For the full vertical model, see `GRAVITY.MD`.

### 6.6 ApplyJumpForce()

If jumping is allowed this frame, `ApplyJumpForce()`:

- validates jump eligibility
- blocks jumping during active falls
- checks water-breach rules
- checks `TrekRequest.CanAffordJump` for host-owned jump resource gating
- computes the jump direction
- clears existing downward output
- injects jump force
- updates jump locomotion state

Ground jumps can be influenced by the current surface normal.
Water jumps use `Handler.Swim.BreachJumpMultiplier`.

### 6.7 Platform Influence Output

If the navigator is moving with a platform, `TryTraversal(...)` can include:

- `rotationDelta`
- `positionDelta`

These deltas are skipped if the navigator has just jumped, because platform velocity transfer is handled separately.

### 6.8 Output Deltas

At the end of `TryTraversal(...)`, the motor returns three deltas:

```csharp
if (motor.TryTraversal(frameRequest, out var velocityDelta, out var positionDelta, out var rotationDelta))
{
    // Apply the returned deltas here.
}
```

The host then applies:

- `velocityDelta`
- `positionDelta`
- `rotationDelta`

## 7. Phase 2: FinalizeTraversal(...)

`FinalizeTraversal(...)` is the state-reconciliation phase.

By the time it runs, the host should already have:

- applied all position, rotation, and velocity deltas
- refreshed traversal state from environment probing

### 7.1 FrameVelocity Refresh

The motor first recomputes `Handler.Move.FrameVelocity` from:

- `newPosition`
- `lastPosition`
- `TrailblazerManager.InvDeltaTime`

This becomes the authoritative runtime velocity for the next frame.

### 7.2 TransitState Update

The motor then updates `CurrentState` from the refreshed `TrekCondition`, preserving the previous state for transition logic.

This is where medium transitions become visible to the motor:

- ground to air
- air to ground
- water entry and exit
- platform changes

### 7.3 CheckJumpStatus(...)

This stage handles jump interruption by ceilings.

If the navigator is still moving upward and crosses `CurrentState.CeilingLevel`, the motor:

- zeroes the upward movement component
- stops the active jump
- clears held-jump extension

### 7.4 HandlePlatformChange()

The motor updates platform ownership and transforms by:

- copying the current `GroundCondition`
- detecting whether the active platform changed
- refreshing same-id platform transforms without treating them as a platform swap
- caching active and previous transforms
- resetting `IsNewPlatform` appropriately

This prepares the platform subsystem for transition handling in the current and next frames.

### 7.5 HandlePlatformTransitions()

This stage handles platform inertia and release timing.

Examples:

- inheriting platform velocity when leaving ground
- waiting briefly after landing on a new platform before trusting its motion
- subtracting platform velocity on landing to avoid double-applying it
- releasing stale held-platform references after the configured hold window

This logic is what makes `MotionTransfer.None`, `InitTransfer`, `PermaTransfer`, and `PermaLocked` meaningful in practice.

### 7.6 Movement Transition Handling

This stage processes movement lifecycle events.

It covers:

- ending jumps on landing
- stopping water breaches
- firing `OnLandedFall`
- clearing swim state when leaving water

### 7.7 HandleSwimState(...)

This stage manages water-state bookkeeping.

It can:

- clear transient locomotion state on first entering water
- toggle `Handler.Swim.IsSwimming`
- track diving versus surface-level swimming
- update underwater time
- trigger drowning events

### 7.8 HandleFallState(...)

This is the fall-classification stage.

It:

- starts falls when the navigator enters true downward freefall
- avoids treating ordinary downhill motion as a fall
- records `FallStart` and `FallEnd`
- emits max-fall-height and landing events
- suppresses fall state while in water

### 7.9 HandlePlatformMovement(...)

This final stage caches the navigator's current foot point and rotation relative to the active platform.

Those cached values are used on the next frame to:

- compute platform velocity
- reconstruct local or platform-relative movement
- keep the navigator attached consistently to a moving transform

### 7.10 Close Traversal

At the end of finalization, `TraversalInProgress` is cleared so the next simulation step can run.

## 8. Locomotion Responsibilities

Each locomotion module owns a specific slice of behavior.

### 8.1 Move

Owns:

- base movement speeds
- ground and air acceleration
- slope limit
- slope speed scaling
- gravity
- passive water drag
- terminal velocity
- frame velocity storage

### 8.2 Jump

Owns:

- jump height
- extra held-jump height
- cooldown
- jump count
- jump control multiplier
- frame jump direction
- avoid-grounding timer

### 8.3 Fall

Owns:

- whether the navigator is currently falling
- start and end fall heights
- max fall height
- fall control multiplier

### 8.4 Platform

Owns:

- active platform tracking
- platform transforms
- platform velocity
- movement transfer mode
- platform hold and release state
- cached local attachment points

### 8.5 Slide

Owns:

- sliding speed
- sideways control during slides
- speed control during slides
- the current sliding state

### 8.6 Swim

Owns:

- buoyancy
- swim acceleration and speed
- breach-jump multiplier
- diving and underwater timers
- drowning rules

## 9. Events

`NavMotorEvents` exposes climb-specific query callbacks and state-notification hooks for external
systems.

Jump and water events:

- `OnStartJump`
- `OnStopJump`
- `OnStartWaterBreach`
- `OnStopWaterBreach`

Fall and survival events:

- `OnLandedFall`
- `OnStartFall`
- `OnStopFall`
- `OnMaxFallHeightReached`
- `OnDrowning`

## 10. Common Integration Pattern

With a `Navigator`, the usual order is:

```csharp
TrailblazerManager.Simulate();
navigator.Simulate();
navigator.CommitFrameMotion();
```

With lower-level motor usage, the equivalent pattern is:

```csharp
if (motor.TryTraversal(frameRequest, out var velocityDelta, out var positionDelta, out var rotationDelta))
{
    // Host applies the deltas here.
}

// Host refreshes TrekCondition here.

motor.FinalizeTraversal(newPosition, lastPosition, newRotation, refreshedCondition, newFootPosition);
```

The essential rule is:

- `TryTraversal(...)` consumes the current state
- `FinalizeTraversal(...)` accepts the updated state for next frame
- `AbortTraversalFrame()` is the explicit escape hatch when the host must discard a started traversal

## 11. Common Gotchas

### Calling TryTraversal(...) twice in one frame

The second call will do nothing because `TraversalInProgress` is still true for the current frame.

### Forgetting FinalizeTraversal(...)

If the next frame starts while traversal is still pending, the motor will throw an explicit error.
Use `AbortTraversalFrame()` only when you intentionally need to discard an opened traversal.

### Finalizing on a later frame

`FinalizeTraversal(...)` must run in the same simulation frame that opened traversal.
If it runs on a later frame, the motor throws and requires `AbortTraversalFrame()` before traversal can resume.

### Treating traversal refresh as optional

It is not optional. `NavMotor` depends on host-provided `TrekCondition` data for medium changes, surfaces, water, ceilings, and platforms.

### Assuming jump, fall, and airborne traversal are interchangeable

They are related, but distinct:

- `IsInGas` describes the traversal medium
- `Handler.Jump.IsJumping` describes an active jump state
- `Handler.Fall.IsFalling` describes a classified fall state

### Assuming platform movement is just positional parenting

It is more involved. The motor tracks relative transforms, inertia transfer, delayed release, and landing correction to avoid double-applying platform movement.

### Assuming the motor is a full physics engine

It is not. It is a deterministic locomotion system with gameplay-oriented rules and host-supplied traversal probes.

## 12. Testing References

The current test coverage around `NavMotor` behavior is concentrated in:

- `tests/Trailblazer.Tests/Navigation/Motor/MoveLocomotion.Tests.cs`
- `tests/Trailblazer.Tests/Navigation/Motor/JumpLocomotion.Tests.cs`
- `tests/Trailblazer.Tests/Navigation/Motor/FallLocomotion.Tests.cs`
- `tests/Trailblazer.Tests/Navigation/Motor/PlatformLocomotion.Tests.cs`
- `tests/Trailblazer.Tests/Navigation/Motor/SwimLocomotion.Tests.cs`
- `tests/Trailblazer.Tests/Navigation/Motor/SlideLocomotionTests.cs`

If you change the phase order, transition logic, or locomotion interaction rules, update or extend those tests in the same change.
