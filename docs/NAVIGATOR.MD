# Navigator Reference

This document is the detailed reference for Trailblazer's `Navigator` class.

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
- providing a hook for host-specific traversal probing

It is not responsible for:

- raw pathfinding internals
- steering heuristics
- turning interpolation details
- locomotion rule execution
- grid or chart registration

Those responsibilities belong to the respective subsystem classes and the pathing layer.

## 2. Core Design Model

`Navigator` is a coordinator, not a subsystem implementation.

It combines three lower-level controllers:

- `NavSteering`
- `NavTurning`
- `NavMotor`

and drives them through a two-step lifecycle:

1. `Simulate()` for the fixed-step control pass
2. `CommitFrameMotion()` for applying deltas, probing the environment, and finalizing state

`Navigator` is intended to be subclassed.

Concrete navigator types provide host-specific traversal probing by implementing `CheckTrekCondition()`.

## 3. Interface Role

`Navigator` implements `INavigate`, which is now a thin runtime-state interface built on:

- `ISteer`
- `LastPosition`
- `Rotation`
- `Forward`

That keeps the steering-facing contract small while still letting turning or host code read the current frame state from the concrete `Navigator`.

## 4. Public Surface

The main entry points are:

- `Setup(...)`
- `Initialize(TrekCondition condition)`
- `BindAnimationHandler(...)`
- `UnbindAnimationHandler()`
- `PrewarmMovementGroup()`
- `ReplaceTrekCondition(TrekCondition state)`
- `Reset()`
- `ApplyInputTrekRequest(...)`
- `ApplyGuidedTrekRequest(...)`
- `ToggleGuidedJump(bool status)`
- `ToggleGuidedFlight(bool status)`
- `SetGuidedTrekRate(TrekRate rate)`
- `Simulate()`
- `CommitFrameMotion()`
- `SetGroundContact(...)`
- `SetAirborne(...)`
- `SetWaterContact(...)`
- `SetTrekCondition(...)`
- `CheckTrekCondition()`
- `AddPositionDelta(...)`
- `ApplyRotationDelta(...)`
- `AddVelocityDelta(...)`
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
- `Size`
- `Radius`
- `FootPositionAdjust`
- `GuidedPathMode`
- `GuidedAllowUnwalkable`
- `GuidedAStarHeuristic`
- `GuidedAStarMaxClimbHeight`
- `GuidedFlowFieldExtraFloodRange`
- `GlobalId`
- `OccupantGroupId`
- `OccupyingIndexMap`
- `IsLockedOn`
- `AnimDampTime`

Protected frame-local state includes:

- `_frameCondition`
- `_frameRequest`
- `_positionDelta`
- `_rotationDelta`
- `_velocityDelta`

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

Subsystems do not mutate committed transform state through a shared host interface. They return or accumulate deltas, and `Navigator` applies those deltas in `CommitFrameMotion()`.

### 5.3 Traversal State

`Navigator` owns:

- `_frameCondition`
- `_frameRequest`
- `StuckThresholdSpeed`
- `IsGuideded`

`_frameCondition` is the current traversal snapshot the motor will finalize against.
`_frameRequest` is semi-transient travel state.

In practice that means:

- `Origin`, `FootPosition`, and `Rotation` are refreshed every `Simulate()`
- `Direction` is overwritten by steering while guided, or preserved from manual input for the current frame
- `Rate` and `IsRequestingFlight` can persist across guided frames
- `IsRequestingJump` is frame-scoped and is cleared after commit

`IsGuideded` is explicit navigator state.
It is turned on by `ApplyGuidedTrekRequest(...)` and turned off by `ApplyInputTrekRequest(...)` or `Reset()`.

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

### 5.6 Host Bindings

`Navigator` can also bind optional host integrations such as animation handlers.

Those bindings are not treated as navigator-owned simulation state.

## 6. Lifecycle

### 6.1 Setup(...)

`Setup(...)` initializes the base transform state for a navigator instance.

It:

- assigns `GlobalId`
- uses a host-provided `globalId` when one is supplied
- otherwise allocates a deterministic default id from navigator setup order
- sets `Position` and `LastPosition`
- sets `Rotation`
- derives `Forward`
- sets starting `Velocity`
- sets `Size`
- marks `_isSet = true`

It does not create the steering, turning, or motor controllers yet.

If your broader simulation stack already owns stable agent ids, pass that id into `Setup(...)` so Trailblazer uses the same identity for occupancy and movement-group coordination.

### 6.2 Initialize(...)

`Initialize(TrekCondition condition)` completes activation.

It:

- stores the initial `_frameCondition`
- creates `NavSteering` using `Radius`
- creates `NavMotor` using `_frameCondition`
- seeds motor velocity from the navigator's starting `Velocity`
- creates `NavTurning` using `Radius`
- performs initial voxel occupancy registration
- marks `_isInitialized = true`

After `Setup(...)` and `Initialize(...)`, `IsActive` becomes true.

### 6.3 Reset()

`Reset()` clears the current traversal session and occupancy state.

It:

- resets `_frameCondition`
- resets `_frameRequest`
- sets `IsGuideded = false`
- removes the navigator from occupied voxels
- clears `OccupyingIndexMap`
- marks the navigator inactive

After `Reset()`, the instance must go through `Setup(...)` and `Initialize(...)` again before reuse.

## 7. Input and Travel Requests

`Navigator` supports two high-level movement modes.

### 7.1 Manual Input

`ApplyInputTrekRequest(...)` writes directly into `_frameRequest`:

- `Direction`
- `Rate`
- `IsRequestingJump`
- `IsRequestingFlight`

and marks the navigator as not guided.

Manual input is frame-local. After `CommitFrameMotion()`, a non-guided navigator fully resets `_frameRequest`, so manual callers should reissue input each fixed step.

### 7.2 Guided Travel

`ApplyGuidedTrekRequest(...)` switches the navigator into guided mode.

It:

- sets `IsGuideded = true`
- clears the current manual direction
- stores guided request state in `_frameRequest`
- builds a concrete `IPathRequest` from the target position, the navigator's current state, and guided-path defaults
- optionally overrides the navigator's default `GuidedPathMode` for a single request
- optionally accepts a shared `groupId` for grouped movement
- forwards the request to `Steering.ApplyPathRequest(...)`

While guided movement remains active:

- `Rate` persists until changed or replaced by a different request
- `IsRequestingFlight` persists until changed or replaced by a different request
- `IsRequestingJump` is still frame-scoped unless the host reissues it

When the selected path mode is `GuidedPathMode.Aerial`, guided travel also requests controlled flight automatically. Steering may then travel directly through open volume or acquire a volume guide if blockers force a detour.

When the selected path mode is `GuidedPathMode.Swim`, guided travel does not toggle a swim state. Swim remains medium-driven through `TrekCondition.Medium == TraversalMedium.Water`. The request simply uses the raw-volume pathing stack with the host's configured water-voxel rule.

Passing the same non-negative `groupId` to multiple navigators lets `NavSteering` preserve relative formation offsets while the group stays compact.

The built-in request factory currently supports:

- `GuidedPathMode.AStar`
- `GuidedPathMode.FlowField`
- `GuidedPathMode.Aerial`
- `GuidedPathMode.Swim`

If a project needs a different request shape, override `TryCreateGuidedPathRequest(...)`.

### 7.3 Other Request Helpers

`Navigator` also exposes:

- `ToggleGuidedJump(bool status)`
- `ToggleGuidedFlight(bool status)`
- `SetGuidedTrekRate(TrekRate rate)`

These let the host modify the frame request for a guided movement session without rebuilding the whole movement session. While guided movement is active, the `ToggleGuidedFlight(bool status)` and `SetGuidedTrekRate(TrekRate rate)` values persist across subsequent simulation ticks. Note that `ToggleGuidedJump(...)` only updates the current frame request, and is not intended for toggling jump outside of a guided movement session.

## 8. Simulation Lifecycle

The `Navigator` lifecycle is split across `Simulate()` and `CommitFrameMotion()`.

### 8.1 Simulate()

`Simulate()` is the fixed-step controller pass.

It first verifies that the navigator is active. If not, it throws.

Then it:

1. writes transient request state through `_frameRequest.SetTransientState(...)`
2. asks steering for a heading if the navigator is guided
3. buffers turning toward the current movement direction
4. runs `Motor.TryTraversal(...)`
5. accumulates any returned deltas
6. runs `Turning.TrySimulateTurn(...)`
7. if a rotation was returned, stores it in `Rotation`
8. optionally updates animation parameters

Important detail:

- guided movement overwrites `_frameRequest.Direction` from `Steering.GetHeading(this)`
- non-guided movement preserves whatever manual direction was last written into `_frameRequest`

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
10. calls `Motor.FinalizeTraversal(...)`
11. resets `_frameRequest`

That last step is conditional:

- non-guided movement calls `_frameRequest.Reset()`
- guided movement calls `_frameRequest.ResetTransient()`

So guided sessions preserve their semi-persistent request values between frames, while manual input does not.

This is where the navigator becomes internally consistent for the next simulation frame.

### 8.3 Why the Split Exists

The two-step structure matters because:

- subsystem controllers can accumulate intent first
- the host can update traversal state from the world in between
- the motor gets finalized against the post-movement environment state

This is one of the most important architectural rules in Trailblazer's navigation stack.

## 9. Traversal-State Ownership

### 9.1 _frameCondition

`_frameCondition` is the navigator's current traversal description:

- medium
- surface level
- ground state
- ceiling level

This state is consumed by `NavMotor`.

### 9.2 SetGroundContact(...), SetAirborne(...), and SetWaterContact(...)

These are the preferred high-level helpers for concrete navigator integrations.

Use them when host code has already determined:

- whether the navigator is grounded, airborne, or in water
- the current sampled platform snapshot, if any
- the current surface level
- the current ceiling level

`SetGroundContact(...)` is especially important for moving-platform integration because it makes the host provide a sampled `PlatformSnapshot` rather than treating Trailblazer as if it owned the platform object itself.

If the sampled `PlatformSnapshot` is marked inert, Trailblazer still uses it as surface data for things like surface orientation and friction, but it skips kinematic platform carry, attachment, and movement-transfer behavior.

### 9.3 SetTrekCondition(...)

`SetTrekCondition(...)` is the low-level partial-update helper for `_frameCondition`.

It:

- updates only the values you pass
- leaves unspecified values unchanged
- optionally pushes the updated condition immediately into the motor

Use `updateMotorState: true` when the motor needs to see the new traversal state before the next `CommitFrameMotion()`.
Prefer `SetGroundContact(...)`, `SetAirborne(...)`, or `SetWaterContact(...)` when the host is writing a fresh environment contact result.

### 9.4 CheckTrekCondition()

`CheckTrekCondition()` is abstract.

Concrete navigator types implement it to:

- probe the world
- determine ground, air, or water medium
- update surface normals and sampled platform data
- set ceiling constraints
- write the result into `_frameCondition`

### 9.5 ReplaceTrekCondition(...)

`ReplaceTrekCondition(...)` swaps `_frameCondition` wholesale.

This is useful when a host already has a complete traversal snapshot and wants to replace the current one in a single call.

## 10. Delta APIs

The delta methods are the write-path used by the motor and host integration.

### 10.1 AddPositionDelta(...)

Adds to `_positionDelta` and also shifts `LastPosition` so externally applied position deltas do not distort the velocity calculation.

### 10.2 ApplyRotationDelta(...)

Accumulates rotational changes into `_rotationDelta`.

### 10.3 AddVelocityDelta(...)

Adds to `_velocityDelta`.

Current implementation detail:

- it assumes a mass of `1`

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

This is important because grid occupancy is global or static infrastructure.

## 12. Animation Integration

If a host binds an animation handler through `BindAnimationHandler(...)`, `Simulate()` calls:

- `NavAnimationUpdater.UpdateAnimationParameters(...)`

The animation update uses:

- current movement direction
- lock-on state
- sprinting state
- damping time

Animation integration is optional. If no handler is bound, the simulation still runs normally.

## 13. Utility Methods and Extension Points

### 13.1 GetFootPosition()

Returns:

- `Position + Vector3d.Down * FootPositionAdjust`

This is used by platform and ground-contact logic.

### 13.2 GenerateGUID()

`GenerateGUID()` is virtual.

By default it delegates to Trailblazer's deterministic navigator-id allocator.

That still gives a subclass a way to control identity generation if needed for testing, host integration, or stricter determinism workflows.

### 13.3 TryCreateGuidedPathRequest(...)

This protected factory hook lets subclasses build custom request types without replacing steering.

## 14. Common Integration Pattern

A typical concrete navigator flow looks like this:

```csharp
var navigator = new MyNavigator();

navigator.Setup(
    position: new Vector3d(0, 0, 0),
    rotation: FixedQuaternion.Identity,
    velocity: Vector3d.Zero,
    size: Fixed64.One);

navigator.Initialize(new TrekCondition
{
    Medium = TraversalMedium.Ground,
    SurfaceLevel = Fixed64.Zero,
    GroundState = new GroundCondition()
});

navigator.GuidedPathMode = GuidedPathMode.AStar;
navigator.ApplyGuidedTrekRequest(new Vector3d(5, 0, 5), rate: TrekRate.Moderate);

TrailblazerManager.Simulate();
navigator.Simulate();
navigator.CommitFrameMotion();
```

If several navigators should move as one group, pass the same optional `groupId` to each `ApplyGuidedTrekRequest(...)` call.

If grouped navigators are restored through Chronicler and you want formation behavior available on the very next frame, call `PrewarmMovementGroup()` once per loaded navigator before the next `TrailblazerManager.Simulate()` step. If you skip it, grouped steering still re-joins lazily during the next steering update.

## 15. Common Gotchas

### Calling Simulate() or CommitFrameMotion() before activation

Both methods throw until the navigator has gone through `Setup(...)` and `Initialize(...)`.

### Treating Navigator as self-sufficient

It is not. Concrete navigators still need to refresh traversal state between `Simulate()` and `CommitFrameMotion()` through `CheckTrekCondition()`.

### Forgetting to update traversal state between simulation phases

If `_frameCondition` is stale, `NavMotor.FinalizeTraversal(...)` will finalize against incorrect medium, surface, or ceiling data.

### Forgetting that occupancy is part of behavior

Voxel occupancy is not cosmetic. It feeds scan-based steering and avoidance.

### Assuming Reset() fully disposes subsystem objects

`Reset()` deactivates the navigator and clears state, but it is still a reuse-oriented reset. Re-run `Setup(...)` and `Initialize(...)` before using the instance again.

## 16. Testing Notes

Current direct coverage for navigator orchestration is still lighter than steering, turning, and motor coverage.

The main direct support files are:

- `tests/Trailblazer.Tests/Navigation/Navigator/Support/TestNavigator.cs`
- `tests/Trailblazer.Tests/Navigation/Navigator/Navigator.Tests.cs`

If `Navigator` behavior changes, especially around lifecycle, frame ordering, or traversal-state updates, this area benefits from refreshed direct tests.
