# Trailblazer Gravity Model

This document explains how vertical motion is currently handled in Trailblazer's navigation motor.

The source of truth for the behavior described here is the code in:

- `src/Trailblazer/Navigation/Motor/NavMotor.cs`
- `src/Trailblazer/Navigation/Motor/Locomotions/MoveLocomotion.cs`
- `src/Trailblazer/Navigation/Motor/Locomotions/JumpLocomotion.cs`
- `src/Trailblazer/Navigation/Motor/Locomotions/FallLocomotion.cs`
- `src/Trailblazer/Navigation/Motor/Locomotions/SwimLocomotion.cs`

## 1. Where Gravity Lives

Gravity is configured on `MoveLocomotion`, not on `TrailblazerManager`.

That is intentional. Each `Navigator` owns a `NavMotor`, and each `NavMotor` owns its own `LocomotionHandler`, including its own `MoveLocomotion` instance. Keeping gravity on `MoveLocomotion` allows different navigators to run under different gravity values at the same time depending on where they are, what they are standing in, or what rules their local simulation needs.

Relevant defaults:

- `MoveLocomotion.DefaultGravityForce = 9.8`
- `MoveLocomotion.DefaultTerminalVelocity = 53`
- `JumpLocomotion.DefaultBaseJumpHeight = 1`
- `JumpLocomotion.DefaultExtraJumpHeight = 2`
- `SwimLocomotion.DefaultBouyancyFactor = 1`
- `SwimLocomotion.DefaultWaterDragFactor = 0.0625`

`TrailblazerManager` provides the fixed simulation timing through:

- `TrailblazerManager.DeltaTime`
- `TrailblazerManager.InvDeltaTime`
- `TrailblazerManager.TotalTime`

Those values are what make gravity and jumping deterministic from frame to frame.

## 2. Frame Order

Vertical motion is not handled in isolation. It is part of the `NavMotor.TryTraversal(...)` pipeline:

1. Cache the current frame request.
2. Start `_forceOutput` from `Handler.Move.FrameVelocity`.
3. Compute movement intent and horizontal acceleration.
4. Apply environmental forces through `ApplyEnvironmentalForces()`.
5. Apply jump force through `ApplyJumpForce()`.
6. Apply platform movement if needed.
7. Convert the final frame output into a velocity delta.

That ordering matters:

- gravity and buoyancy are applied before jump impulse
- jump can clear existing downward vertical output before injecting upward force
- grounded and airborne state are determined from the current `TransitState`, not inferred ad hoc from velocity alone

## 3. Grounded Behavior

When the navigator is grounded, Trailblazer does not simply "turn gravity off."

The current code does this:

```csharp
_forceOutput.y = FixedMath.Min(Fixed64.Zero, _forceOutput.y) - gravityStep;
```

Implications:

- downward vertical output is preserved if already negative
- a small downward bias is still applied every grounded frame
- upward vertical output is suppressed while grounded

This acts more like a ground-stick force than a textbook "normal force cancels gravity" explanation.

Why it exists:

- it helps keep the navigator pressed against the ground
- it reduces hover-like behavior when stepping across uneven surfaces
- it keeps vertical state transitions stable in a deterministic motor

So if you are looking for the mental model, use this one:

- grounded units are still biased downward each frame
- the environment and surface state keep them grounded
- the motor is intentionally sticky rather than physically pure

## 4. Airborne Behavior

When `CurrentState.Medium == TraversalMedium.Air`, the motor applies gravity directly to the vertical component.

Current flow:

```csharp
_forceOutput.y = Handler.Move.FrameVelocity.y - gravityStep;
```

Then the motor clamps against terminal fall speed:

```csharp
Fixed64 terminalFallSpeed = Handler.Move.FrameVelocity.y
    + (_forceOutput.y * TrailblazerManager.DeltaTime);

if (terminalFallSpeed < -Handler.Move.TerminalVelocity)
    _forceOutput.y = -Handler.Move.TerminalVelocity - Handler.Move.FrameVelocity.y;
```

What this means in practice:

- air state is where full gravity application happens
- fall speed is bounded by `MoveLocomotion.TerminalVelocity`
- once falling fast enough, further downward acceleration is capped

This is also where jump-hold behavior can partially offset gravity for a short time; that is covered below.

## 5. Water Behavior

Water does not use the same vertical rule as ground or air.

When `CurrentState.Medium == TraversalMedium.Water`, Trailblazer applies buoyancy relative to gravity:

```csharp
_forceOutput.y += gravityStep * (Handler.Swim.BuoyancyFactor - Fixed64.One);
```

Interpretation of `BuoyancyFactor`:

- `1`: neutral relative to gravity, no additional vertical push from buoyancy
- `> 1`: net upward acceleration
- `< 1`: net downward acceleration

Important distinction:

- vertical buoyancy is handled in `ApplyEnvironmentalForces()`
- swim drag is handled in the desired-velocity path
- optional vertical swimming input is handled in `GetDesiredVelocity()`

So water motion is a combination of:

- buoyancy
- drag
- explicit swim input
- optional breach jumping

## 6. Jumping and Gravity

Jumping is not just "set velocity upward once."

### 6.1 Initial Jump Speed

The current jump-speed formula is:

```csharp
GetVerticalJumpSpeed() = sqrt(2 * BaseJumpHeight * GravityForce)
```

That is the base vertical speed used for:

- normal jumps
- slope-influenced jumps
- water breach jumps, after multiplying by `BreachJumpMultiplier`

### 6.2 Jump Direction

On land, the jump direction is not always straight up.

Trailblazer computes it by interpolating between:

- `Vector3d.Up`
- `CurrentState.SurfaceNormal`

using:

- `JumpLocomotion.PerpendicularJumpAmount`
- `JumpLocomotion.SteepPerpendicularJumpAmount`

This lets jumps lean away from slopes instead of always being perfectly vertical.

### 6.3 Held Jump

While a jump is active and the jump input is still held, Trailblazer partially counteracts gravity for a limited window:

```csharp
if (TrailblazerManager.TotalTime <= extraJumpLimit)
    _forceOutput += Handler.Jump.FrameJumpDirection * gravityStep;
```

This is the system that produces variable jump height.

Current behavior:

- pressing jump injects the initial jump force
- holding jump can extend the upward phase for a short period
- releasing jump early allows gravity to take over sooner

### 6.4 Clearing Existing Downward Motion

Before jump force is added, the motor clears existing downward vertical output:

```csharp
_forceOutput.y = FixedMath.Max(Fixed64.Zero, _forceOutput.y);
_forceOutput += jumpForce;
```

This prevents a jump from being reduced by leftover downward momentum in the same frame.

## 7. Ceiling Interaction

Ceiling handling is finalized after traversal.

If the motor detects upward velocity and the navigator has crossed `CurrentState.CeilingLevel`, it:

- zeroes the upward `FrameVelocity.y`
- clears `Jump.IsJumping`
- clears `Jump.IsHoldingJump`

This stops the current ascent immediately and ends the held-jump extension.

## 8. Falling State

Falling is tracked separately from raw gravity application.

`FallLocomotion` is responsible for:

- whether the navigator is currently falling
- where the fall started
- where the fall ended
- the total fall height

`NavMotor.HandleFallState(...)` uses both traversal state and vertical output to decide when a fall begins.

The main rules are:

- entering water clears fall state
- landing ends the fall and records `FallEnd`
- falling can start when the unit is in air or sliding too steeply and vertical output is downward
- downhill ground travel should not be treated as a fall

Related events:

- `OnStartFall`
- `OnStopFall`
- `OnLandedFall`
- `OnMaxFallHeightReached`

This means gravity affects both motion and state transitions, but those concerns are intentionally split:

- `ApplyEnvironmentalForces()` changes vertical movement
- `HandleFallState(...)` classifies the current movement as a fall

## 9. Platform Interaction

Moving platforms affect vertical motion indirectly.

The platform system can:

- inject inherited platform velocity when leaving a platform
- subtract platform velocity on landing to avoid double-applying motion
- move the navigator through platform transforms while grounded or locked

This matters for gravity because vertical motion after stepping off a moving platform is not purely "gravity from rest." The inherited platform motion becomes part of the airborne state before gravity continues to act on it.

## 10. Practical Tuning Knobs

If you want to change how gravity feels, these are the main fields to tune:

### Heavier or lighter gravity

- `MoveLocomotion.GravityForce`

Higher values:

- increase downward acceleration
- require more jump speed for the same jump height
- make falls reach terminal velocity sooner

### Faster or slower maximum falling

- `MoveLocomotion.TerminalVelocity`

### Shorter or taller jumps

- `JumpLocomotion.BaseJumpHeight`
- `JumpLocomotion.ExtraJumpHeight`

### More or less mid-air control

- `JumpLocomotion.JumpControlMultiplier`
- `FallLocomotion.FallControlMultiplier`

### More floating or more sinking in water

- `SwimLocomotion.BuoyancyFactor`
- `SwimLocomotion.WaterDragFactor`
- `SwimLocomotion.BreachJumpMultiplier`

## 11. Common Misconceptions

### "Gravity is configured on TrailblazerManager"

It is not. The fixed timestep is on `TrailblazerManager`; the gravity magnitude is on `MoveLocomotion`.

This is by design rather than omission. Gravity is per-motor configuration so separate navigators can carry different gravity values concurrently instead of sharing one global gravity constant.

### "Grounded means gravity is canceled"

Not exactly. Grounded frames still receive a downward bias. The current implementation is a grounded-stick model, not a pure equal-and-opposite-force simulation.

### "Water just disables gravity"

No. Water uses buoyancy relative to gravity, plus swim drag and optional swim input.

### "Jump height is only determined by one impulse"

Not in the current motor. Jump height is affected by:

- base jump speed
- held-jump gravity cancellation window
- jump direction relative to the surface normal
- water breach multipliers

## 12. Summary

Trailblazer's vertical model is deterministic and gameplay-oriented:

- ground applies a persistent downward stick force
- air applies gravity with a terminal velocity cap
- water applies buoyancy relative to gravity
- jump injects upward motion and can temporarily offset gravity while held
- fall state, landing, ceilings, and water transitions are handled as explicit locomotion states

If you need the execution order for the full motor, read `NAVMOTOR.MD` alongside this file.
