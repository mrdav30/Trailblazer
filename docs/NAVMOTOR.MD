# NavMotor Reference

This document is the detailed reference for Trailblazer's deterministic movement motor.

If you only need the high-level architecture, read `OVERVIEW.md`.
If you need the vertical-force model specifically, read `GRAVITY.MD`.

The code referenced here lives primarily in:

- `src/Trailblazer/Navigation/Motor/NavMotor.cs`
- `src/Trailblazer/Navigation/Motor/Locomotions/*`
- `src/Trailblazer/Navigation/Motor/Support/*`

## 1. What NavMotor Is

`NavMotor` is the movement execution layer for a `Navigator`.

It is responsible for:

- turning a frame's movement request into motion deltas
- applying locomotion rules for ground, air, water, slopes, slides, jumps, and moving platforms
- tracking traversal state transitions between frames
- producing deterministic movement output using fixed-point math and a fixed simulation timestep

It is not responsible for:

- pathfinding
- steering decisions
- turn-target selection
- environment probing or collision detection

Those responsibilities belong to `NavSteering`, `NavTurning`, and the host navigator or world code.

## 2. Core Design Model

The important design choice is that `NavMotor` is two-phase:

1. `Traverse(TrekRequest request)` computes this frame's `MotorOutput`.
2. `FinalizeTraversal(...)` reconciles the result after the host has applied those deltas and refreshed traversal state.

That split is what lets Trailblazer stay deterministic while still depending on host-owned surface, water, ceiling, and platform data.

Another important detail:

- names like `_forceOutput` are historical
- in practice, `NavMotor` works more like a velocity-oriented movement accumulator than a classical rigidbody force solver

The motor starts from `Locomotions.Move.FrameVelocity`, adjusts that output according to locomotion rules, then converts it into per-frame deltas.

## 3. Public Surface

The main entry points are:

- `CreateNew(TrekCondition initialCondition)`
- `OnInitialize(TrekCondition condition)`
- `Traverse(TrekRequest request)`
- `FinalizeTraversal(Vector3d newPosition, Vector3d lastPosition, FixedQuaternion newRotation, TrekCondition conditionRefresh, Vector3d? newFootPosition = null)`
- `UpdateTraversal(TrekCondition newCondition, bool isInitializing = false)`
- `SetVelocity(Vector3d velocity)`
- `GetVerticalJumpSpeed()`
- `IsTooSteep(Fixed64 angle)`
- `IsOnSlope(Fixed64 angle)`

Important public state includes:

- `Locomotions`
- `Events`
- `CurrentState`
- `FrameSlopeAngle`
- `IsFrameLocked`
- `IsInitialized`
- `IsGrounded`
- `WasGrounded`
- `IsInAir`
- `WasInAir`
- `IsInWater`
- `WasInWater`
- `InLimbo`
- `IsPlatformInteriaApplied`
- `Locomotions.Platform.IsActive`
- `Locomotions.Platform.IsLockedToPlatform`

## 4. Host Contract

`NavMotor` now operates on snapshots rather than a host interface. The host must supply:

- a populated `TrekRequest`
- the current post-application position and last position
- the current post-application rotation
- the refreshed `TrekCondition`
- the current foot position when platform tracking is needed

This is the expected contract around the two phases:

1. Populate a `TrekRequest` for the frame.
2. Call `Traverse(...)` and capture the returned `MotorOutput`.
3. Apply the output deltas to position, rotation, and velocity.
4. Re-evaluate the environment and refresh `TrekCondition`.
5. Call `FinalizeTraversal(...)` with the updated state snapshot.

If the host needs to push a new traversal state into the motor before `FinalizeTraversal(...)`, it can call `UpdateTraversal(...)` directly. This is what `Navigator.SetTrekCondition(updateMotorState: true)` is for.

## 5. Initialization and State Ownership

`NavMotor` owns runtime locomotion state through `LocomotionHandler`.

That handler contains:

- `MoveLocomotion`
- `PlatformLocomotion`
- `JumpLocomotion`
- `FallLocomotion`
- `SlideLocomotion`
- `SwimLocomotion`

These locomotion objects are per-motor, not global. That means different navigators can run different movement parameters at the same time.

Examples:

- different gravity values per navigator
- different jump heights
- different platform-transfer rules
- different buoyancy or swim drag

`CurrentState` is stored as a `TransitState`, which mirrors the current `TrekCondition` plus previous-state data used for transition logic.

## 6. Phase 1: Traverse(...)

`Traverse(...)` is the movement-output phase.

### 6.1 Preconditions

`Traverse(...)` returns immediately if:

- the motor is not initialized
- the frame is already locked

This frame lock is intentional. The motor is designed to process movement exactly once per simulation frame.

### 6.2 Frame Setup

At the start of traversal, the motor:

1. reads the supplied `TrekRequest`
2. computes `FrameSlopeAngle` from `CurrentState.GetSignedSlopeAngle(...)`
3. seeds `_forceOutput` from `Locomotions.Move.FrameVelocity`
4. updates platform velocity through `Locomotions.Platform.UpdatePlatformVelocity()`

This is important because the motor starts from the previous frame's actual movement state, not from zero.

### 6.3 Limbo and Cooldown Handling

Before applying movement:

- `InLimbo` disables control until the motor regains a valid traversal context
- jump cooldown is advanced through `Jump.UpdateCooldown()`

`InLimbo` means the motor is not grounded, not clearly swimming, and not in an active jump or fall state. It is effectively a guarded transitional state.

### 6.4 ComputeMovementForces()

This is where the motor derives the movement intent for the frame.

Responsibilities:

- decide whether the navigator currently has control
- detect steep-slope sliding
- clear or preserve vertical output depending on grounded/airborne state
- compute desired velocity
- apply friction in grounded idle cases
- clamp acceleration toward the desired velocity

Key behaviors:

- grounded steep slopes can force `Slide.IsSliding = true`
- air control is reduced or disabled depending on locomotion state
- grounded movement can be projected along the surface normal
- slope speed multipliers can modify horizontal speed

Supporting helpers involved here:

- `GetDesiredVelocity()`
- `GetHorizontalVelocity()`
- `MaxHoritzontalSpeedInDirection(...)`
- `GetMaxAcceleration()`

### 6.5 ApplyEnvironmentalForces()

This phase applies vertical environmental behavior:

- grounded downward stick
- airborne gravity
- terminal-velocity clamping
- water buoyancy
- held-jump gravity offset

This is the gravity/buoyancy stage, not the initial jump-impulse stage.

For the full vertical model, see `GRAVITY.MD`.

### 6.6 ApplyJumpForce()

If jumping is allowed this frame, `ApplyJumpForce()`:

- validates jump eligibility
- blocks jumping during active falls
- checks water-breach rules
- checks resource gating through `Events.CanAffordJump`
- computes the jump direction
- clears existing downward output
- injects jump force
- updates jump locomotion state

Ground jumps can be influenced by the current surface normal.
Water jumps use `Swim.BreachJumpMultiplier`.

### 6.7 Platform Influence Output

If the navigator is moving with a platform, `Traverse(...)` can include:

- `RotationDelta`
- `PositionDelta`

These deltas are skipped if the navigator has just jumped, because platform velocity transfer is handled separately.

### 6.8 MotorOutput

At the end of `Traverse(...)`, the motor converts its movement state into a `MotorOutput`:

```csharp
MotorOutput output = motor.Traverse(frameRequest);
```

The host then applies:

```csharp
output.VelocityDelta
output.PositionDelta
output.RotationDelta
```

## 7. Phase 2: FinalizeTraversal(...)

`FinalizeTraversal(...)` is the state-reconciliation phase.

By the time it runs, the host should already have:

- applied all position, rotation, and velocity deltas
- updated `FrameCondition` from environment probing

### 7.1 FrameVelocity Refresh

The motor first recomputes `Locomotions.Move.FrameVelocity` from:

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

One subtle but important distinction:

- landing from a jump is not treated the same as landing from a fall
- `OnStopJump` and `OnLandedFall` are separate event paths

### 7.7 HandleSwimState(...)

This stage manages water-state bookkeeping.

It can:

- clear transient locomotion state on first entering water
- toggle `Swim.IsSwimming`
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

Fall classification depends on both traversal medium and movement direction. It is not just "velocity is negative."

### 7.9 HandlePlatformMovement(...)

This final stage caches the navigator's current foot point and rotation relative to the active platform.

Those cached values are used on the next frame to:

- compute platform velocity
- reconstruct local/platform-relative movement
- keep the navigator attached consistently to a moving transform

### 7.10 Unlock

At the end of finalization, `IsFrameLocked` is cleared so the next simulation step can run.

## 8. Locomotion Responsibilities

Each locomotion module owns a specific slice of behavior.

### 8.1 MoveLocomotion

Owns:

- base movement speeds
- ground and air acceleration
- slope speed scaling
- gravity
- terminal velocity
- frame velocity storage

### 8.2 JumpLocomotion

Owns:

- jump height
- extra held-jump height
- cooldown
- jump count
- jump control multiplier
- frame jump direction
- avoid-grounding timer

### 8.3 FallLocomotion

Owns:

- whether the navigator is currently falling
- start and end fall heights
- max fall height
- fall control multiplier

### 8.4 PlatformLocomotion

Owns:

- active platform tracking
- platform transforms
- platform velocity
- movement transfer mode
- platform hold/release state
- cached local attachment points

### 8.5 SlideLocomotion

Owns:

- slope limit
- sliding speed
- sideways control during slides
- speed control during slides
- the current sliding state

### 8.6 SwimLocomotion

Owns:

- buoyancy
- water drag
- swim acceleration and speed
- breach-jump multiplier
- diving and underwater timers
- drowning rules

## 9. Events

`NavMotorEvents` exposes hooks for external systems and gameplay reactions.

Jump and water events:

- `CanAffordJump`
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

These events are not just cosmetic. Some of them affect whether behavior is allowed at all, such as `CanAffordJump`.

## 10. Common Integration Pattern

With a concrete `Navigator`, the usual order is:

```csharp
TrailblazerManager.Simulate();
navigator.Simulate();

// Host world updates medium, surface, ceiling, water, and platform state.
navigator.SetTrekCondition(
    medium: TraversalMedium.Ground,
    surfaceLevel: Fixed64.Zero,
    surfaceCondition: new GroundCondition(),
    updateMotorState: true);

navigator.CommitFrameMotion();
```

With a lower-level host implementation, the equivalent pattern is:

```csharp
MotorOutput output = motor.Traverse(frameRequest);

// Host applies the deltas and refreshes TrekCondition here.

motor.FinalizeTraversal(newPosition, lastPosition, newRotation, refreshedCondition, newFootPosition);
```

The essential rule is:

- `Traverse(...)` consumes the current state
- `FinalizeTraversal(...)` accepts the updated state for next frame

## 11. Common Gotchas

### Calling Traverse(...) twice in one frame

The second call will do nothing because `IsFrameLocked` is still true.

### Forgetting FinalizeTraversal(...)

The motor will keep stale movement and traversal state, and the frame lock will not be cleared.

### Treating FrameCondition as optional

It is not optional. `NavMotor` depends on host-provided `FrameCondition` for medium changes, surfaces, water, ceilings, and platforms.

### Assuming jump, fall, and air are interchangeable

They are related, but distinct:

- `IsInAir` describes the traversal medium
- `Jump.IsJumping` describes an active jump state
- `Fall.IsFalling` describes a classified fall state

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
