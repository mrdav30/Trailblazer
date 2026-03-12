# Navigator Reference

This document is the detailed reference for Trailblazer's `Navigator` base class.

If you only need the high-level architecture, read `OVERVIEW.md`.
If you need the subsystem references behind `Navigator`, read:

- `NAVSTEERING.MD`
- `NAVTURNING.MD`
- `NAVMOTOR.MD`

The code referenced here lives primarily in:

- `src/Trailblazer/Navigation/Navigator.cs`
- `src/Trailblazer/Navigation/Support/INavigate.cs`
- `src/Trailblazer/Navigation/Animation/*`

## 1. What Navigator Is

`Navigator` is the host-facing orchestration layer for Trailblazer's navigation stack.

It is responsible for:

- holding the navigator's transform and motion state
- composing `NavSteering`, `NavTurning`, and `NavMotor`
- bridging manual input and guided path requests into frame requests
- coordinating the simulation-step and commit-step lifecycle
- exposing traversal state to the motor
- tracking voxel occupancy for scan-based steering systems
- providing an extension point for host-specific traversal probing

It is not responsible for:

- raw pathfinding internals
- steering heuristics
- turning interpolation details
- locomotion rule execution
- grid or chart registration

Those responsibilities belong to the respective subsystem classes and the pathing layer.

## 2. Core Design Model

The important design choice is that `Navigator` is a coordinator, not a subsystem implementation.

It combines three lower-level controllers:

- `NavSteering`
- `NavTurning`
- `NavMotor`

and drives them through a two-step lifecycle:

1. `Simulate()` for the fixed-step control pass
2. `CommitFrameMotion()` for applying deltas, probing the environment, and finalizing state

`Navigator` is also the main point where Trailblazer meets host code. It is abstract because the host must provide `CheckTrekCondition()` to update traversal state from its own world data.

## 3. Interface Role

`Navigator` implements `INavigate`, which is a composition of:

- `ISteer`
- `IMotor`
- `ITurn`

That makes `Navigator` the single object passed to:

- steering as an `ISteer`
- motor as an `IMotor`
- turning as an `ITurn`

This is the main reason `Navigator` exists as an orchestration object instead of a thinner data carrier.

## 4. Public Surface

The main entry points are:

- `Setup(...)`
- `Initialize(TrekCondition condition)`
- `ReplaceTrekCondition(TrekCondition state)`
- `Reset()`
- `ApplyInputTrekRequest(...)`
- `ApplyGuidedTrekRequest(...)`
- `ToggleJumpStatus(bool status)`
- `SetTraversalSpeed(TrekRate rate)`
- `Simulate()`
- `CommitFrameMotion()`
- `SetTrekCondition(...)`
- `CheckTrekCondition()`
- `AddPositionDelta(...)`
- `AddRotationDelta(...)`
- `AddVelocityDelta(...)`
- `ApplyRotation(FixedQuaternion rotation)`
- `GetFootPosition()`
- `SetOccupancy(...)`
- `RemoveOccupancy(...)`

Important public state includes:

- `Position`
- `LastPosition`
- `Rotation`
- `Forward`
- `Velocity`
- `Speed`
- `Acceleration`
- `IsActive`
- `Steering`
- `Turning`
- `Motor`
- `StuckThresholdSpeed`
- `IsGuideded`
- `FrameCondition`
- `FrameRequest`
- `Size`
- `Radius`
- `FootPositionAdjust`
- `GlobalId`
- `OccupantGroupId`
- `OccupyingIndexMap`
- `AnimationHandler`
- `IsLockedOn`
- `AnimDampTime`

## 5. State Ownership

`Navigator` owns several categories of state.

### 5.1 Transform and Motion State

- `Position`
- `LastPosition`
- `Rotation`
- `Forward`
- `Velocity`
- `Speed`
- `Acceleration`

These are the host-visible motion values produced across frames.

### 5.2 Frame Deltas

`Navigator` accumulates frame-local changes through:

- `_positionDelta`
- `_rotationDelta`
- `_velocityDelta`

Subsystems do not mutate `Position` or `Rotation` directly. They push deltas through:

- `AddPositionDelta(...)`
- `AddRotationDelta(...)`
- `AddVelocityDelta(...)`

Those deltas are applied in `CommitFrameMotion()`.

### 5.3 Traversal State

`Navigator` owns:

- `FrameCondition`
- `FrameRequest`
- `StuckThresholdSpeed`
- `IsGuideded`

This is the state the lower-level controllers consume and update around the frame boundary.

### 5.4 Controller Instances

`Navigator` owns and composes:

- `Steering`
- `Turning`
- `Motor`

These are created in `Initialize(...)`.

### 5.5 Occupancy State

`Navigator` also owns:

- `GlobalId`
- `OccupantGroupId`
- `OccupyingIndexMap`

This is what makes the navigator visible to scan-based steering and voxel-occupancy systems.

## 6. Lifecycle

### 6.1 Setup(...)

`Setup(...)` initializes the base transform state for a navigator instance.

It:

- assigns a fresh `GlobalId`
- sets `Position` and `LastPosition`
- sets `Rotation`
- derives `Forward`
- sets starting `Velocity`
- sets `Size`
- marks `_isSet = true`

It does not create the steering, turning, or motor controllers yet.

### 6.2 Initialize(...)

`Initialize(TrekCondition condition)` completes activation.

It:

- stores the initial `FrameCondition`
- creates `NavSteering` using `Radius`
- creates `NavMotor` using `Position` and `FrameCondition`
- seeds motor velocity from the navigator's starting `Velocity`
- creates `NavTurning` using `Radius`
- performs initial voxel occupancy registration
- marks `_isInitialized = true`

After `Setup(...)` and `Initialize(...)`, `IsActive` becomes true.

### 6.3 Reset()

`Reset()` clears the current traversal session and occupancy state.

It:

- resets `FrameCondition`
- resets `FrameRequest`
- removes the navigator from occupied voxels
- clears `OccupyingIndexMap`
- marks the navigator inactive

Important detail:

- after `Reset()`, the instance must go through `Setup(...)` and `Initialize(...)` again before reuse

## 7. Input and Travel Requests

`Navigator` supports two high-level movement modes.

### 7.1 Manual Input

`ApplyInputTrekRequest(...)` writes directly into `FrameRequest`:

- `Direction`
- `Rate`
- `IsRequestingJump`

and sets:

- `IsGuideded = true`

In this mode, `Simulate()` does not ask `NavSteering` for a heading.

### 7.2 Guided Travel

`ApplyGuidedTrekRequest(...)` switches the navigator into guided mode.

It:

- clears the current manual direction
- sets movement rate and jump request
- sets `IsGuideded = false`
- prepares the `IPathRequest` if needed
- optionally accepts a shared `groupId` for grouped movement
- forwards the request to `Steering.ApplyPathRequest(...)`

Passing the same non-negative `groupId` to multiple navigators lets `NavSteering` preserve relative formation offsets while the group stays compact.

### 7.3 Other Request Helpers

`Navigator` also exposes:

- `ToggleJumpStatus(bool status)`
- `SetTraversalSpeed(TrekRate rate)`

These let the host modify the current frame request without rebuilding the whole movement session.

## 8. Simulation Lifecycle

The `Navigator` lifecycle is split across `Simulate()` and `CommitFrameMotion()`.

### 8.1 Simulate()

`Simulate()` is the fixed-step controller pass.

It first verifies that the navigator is active. If not, it throws.

Then it:

1. writes `FrameRequest.Origin = Position`
2. writes `FrameRequest.Rotation = Rotation`
3. asks steering for a heading if the navigator is not manually controlled
4. requests turning toward the current movement direction
5. runs `Motor.Traverse(this)`
6. runs `Turning.SimulateTurn(this)`
7. optionally updates animation parameters

This means `Simulate()` is where the controller stack is executed, but not where final position and velocity are committed.

### 8.2 CommitFrameMotion()

`CommitFrameMotion()` is the delta-application and state-finalization pass.

It first verifies that the navigator is active. If not, it throws.

Then it:

1. copies `Position` into `LastPosition`
2. applies `_positionDelta` and `_velocityDelta`
3. updates voxel occupancy
4. applies `_rotationDelta`
5. recomputes `Forward`
6. calls `CheckTrekCondition()`
7. recomputes `Velocity`, `Speed`, and `Acceleration`
8. updates `StuckThresholdSpeed`
9. clears frame deltas
10. calls `Motor.FinalizeTraversal(this)`
11. resets `FrameRequest`

This is where the navigator becomes internally consistent for the next simulation frame.

### 8.3 Why the Split Exists

The two-step structure matters because:

- subsystem controllers can accumulate intent first
- the host can update traversal state from the world in between
- the motor gets finalized against the post-movement environment state

This is one of the most important architectural rules in Trailblazer's navigation stack.

## 9. Traversal-State Ownership

### 9.1 FrameCondition

`FrameCondition` is the navigator's current traversal description:

- medium
- surface level
- ground state
- ceiling level

This state is consumed by `NavMotor`.

### 9.2 SetTrekCondition(...)

`SetTrekCondition(...)` is the public helper for updating `FrameCondition`.

It:

- updates only the values you pass
- leaves unspecified values unchanged
- optionally pushes the updated condition immediately into the motor

Use `updateMotorState: true` when the motor needs to see the new traversal state before the next `CommitFrameMotion()`.

### 9.3 CheckTrekCondition()

`CheckTrekCondition()` is abstract.

This is the host-specific extension point where a concrete navigator should:

- probe the world
- determine ground, air, or water medium
- update surface normals and platform data
- set ceiling constraints
- write the result into `FrameCondition`

This method is the main reason `Navigator` remains abstract.

## 10. Delta and Rotation APIs

The delta methods are the write-path used by motor and turning systems.

### 10.1 AddPositionDelta(...)

Adds to `_positionDelta` and also shifts `LastPosition` so externally applied position deltas do not distort the velocity calculation.

### 10.2 AddRotationDelta(...)

Accumulates rotational changes into `_rotationDelta`.

### 10.3 AddVelocityDelta(...)

Adds to `_velocityDelta`.

Current implementation detail:

- it assumes a mass of `1`

### 10.4 ApplyRotation(...)

This is the immediate rotation sink used by `NavTurning`.

Unlike delta-based methods, this directly overwrites `Rotation`.

## 11. Occupancy Management

`Navigator` participates in voxel occupancy so other systems can find it.

### 11.1 CheckVoxelOccupancy(...)

This internal helper:

- resolves the current voxel from `Position`
- adds the navigator to the current voxel if needed
- compares against the last voxel
- removes old occupancy when the navigator crosses voxel boundaries

This is what makes nearby-occupant scans work for `NavSteering.ComputeCombinedSteering(...)`.

### 11.2 SetOccupancy(...) and RemoveOccupancy(...)

These are called by the grid occupancy system to keep `OccupyingIndexMap` in sync.

### 11.3 Reset Cleanup

`Reset()` explicitly removes the navigator from all tracked occupied voxels before deactivating it.

This is important because grid occupancy is global/static infrastructure.

## 12. Animation Integration

If `AnimationHandler` is assigned, `Simulate()` calls:

- `NavAnimationUpdater.UpdateAnimationParameters(...)`

The animation update uses:

- current movement direction
- lock-on state
- sprinting state
- damping time

Animation integration is optional. If `AnimationHandler` is null, the simulation still runs normally.

## 13. Utility Methods and Extension Points

### 13.1 GetFootPosition()

Returns:

- `Position + Vector3d.Down * FootPositionAdjust`

This is used by platform and ground-contact logic.

### 13.2 GenerateGUID()

`GenerateGUID()` is virtual.

That gives a subclass a way to control identity generation if needed for testing or determinism-related workflows.

### 13.3 ReplaceTrekCondition(...)

This is a simple state replacement helper for swapping `FrameCondition` wholesale.

## 14. Common Integration Pattern

A typical concrete navigator flow looks like this:

```csharp
var navigator = new MyNavigator();

navigator.Setup(
    position: new Vector3d(0, 0, 0),
    rotation: FixedQuaternion.Identity,
    velocity: Vector3d.Zero,
    size: Fixed64.One);

navigator.Initialize(new TrekCondition(
    medium: TraversalMedium.Ground,
    surfaceLevel: Fixed64.Zero,
    surfaceCondition: GroundCondition.CreateEmpty()));

var request = AStarPathRequest.Create(navigator.Position, new Vector3d(5, 0, 5), navigator.Size);
navigator.ApplyGuidedTrekRequest(request, new Vector3d(5, 0, 5), rate: TrekRate.Moderate);

TrailblazerManager.Simulate();
navigator.Simulate();

// Host probes world state here.
navigator.SetTrekCondition(
    medium: TraversalMedium.Ground,
    surfaceLevel: Fixed64.Zero,
    surfaceCondition: GroundCondition.CreateEmpty(),
    updateMotorState: true);

navigator.CommitFrameMotion();
```

If several navigators should move as one group, pass the same optional `groupId` to each `ApplyGuidedTrekRequest(...)` call.

The essential rule is:

- `Simulate()` runs the controller stack
- `CommitFrameMotion()` applies and finalizes the frame

## 15. Common Gotchas

### Calling Simulate() or CommitFrameMotion() before activation

Both methods throw until the navigator has gone through `Setup(...)` and `Initialize(...)`.

### Treating Navigator as self-sufficient

It is not. A concrete subclass must still implement `CheckTrekCondition()` using host-world data.

### Forgetting to update traversal state between simulation phases

If `FrameCondition` is stale, `NavMotor.FinalizeTraversal(...)` will finalize against incorrect medium, surface, or ceiling data.

### Forgetting that occupancy is part of behavior

Voxel occupancy is not cosmetic. It feeds scan-based steering and avoidance.

### Assuming Reset() fully disposes subsystem objects

`Reset()` deactivates the navigator and clears state, but it is still a reuse-oriented reset. Re-run `Setup(...)` and `Initialize(...)` before using the instance again.

## 16. Testing Notes

Current direct test coverage for `Navigator` is weak.

The files under:

- `tests/Trailblazer.Tests/Navigation/Navigator/Navigator.Tests.cs`
- `tests/Trailblazer.Tests/Navigation/Navigator/Support/TestNavigator.cs`

are currently commented out and appear stale relative to the current API.

That means the best live behavioral coverage currently comes indirectly through:

- steering tests
- turning tests
- motor/locomotion tests

If `Navigator` behavior changes, especially around lifecycle, frame ordering, or traversal-state updates, this area would benefit from refreshed direct tests.
