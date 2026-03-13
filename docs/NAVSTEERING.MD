# NavSteering Reference

This document is the detailed reference for Trailblazer's deterministic steering and guide-following layer.

If you only need the high-level architecture, read `OVERVIEW.md`.
If you need the movement-execution side, read `NAVMOTOR.MD`.

The code referenced here lives primarily in:

- `src/Trailblazer/Navigation/Steering/NavSteering.cs`
- `src/Trailblazer/Navigation/Steering/Support/*`
- `src/Trailblazer/Pathing/*`

## 1. What NavSteering Is

`NavSteering` is the layer that decides where a navigator wants to move this frame.

It is responsible for:

- accepting path requests
- deciding whether a direct line-of-sight move is enough
- requesting and following A* or flow-field guides
- blending target following with avoidance and group behavior
- detecting stuck movement and triggering repath attempts
- deciding when movement should stop or count as arrival

It is not responsible for:

- path computation internals
- movement execution, gravity, jumps, or surface locomotion
- turning interpolation
- environment probing

Those responsibilities belong to the pathing layer, `NavMotor`, `NavTurning`, and the host navigator.

## 2. Core Design Model

The important design choice is that `NavSteering` is a heading generator, not a mover.

Its main loop is:

1. Accept an `IPathRequest`.
2. Validate whether the request still makes sense from the current origin.
3. Decide between direct travel and guide-following.
4. Produce a `TargetDirection`.
5. Blend in avoidance and group steering.
6. Stop, arrive, repath, or continue.

`NavSteering` does not apply motion directly. It returns a desired movement vector, and `Navigator` passes that to `NavTurning` and `NavMotor`.

This split matters because it keeps:

- pathfinding logic out of the motor
- locomotion logic out of steering
- guide caching separate from runtime movement

## 3. Public Surface

The main entry points are:

- `CreateNew(Fixed64 radius)`
- `OnInitialize(Fixed64 radius)`
- `ApplyPathRequest(IPathRequest pathRequest, int groupId = -1)`
- `GetHeading(ISteer navigator)`
- `PauseAutoStop()`
- `SetTrailGuide(IGuide guide)`
- `AddToMovementGroup(int groupId)`
- `LeaveMovementGroup()`
- `Arrive()`
- `StopMove()`
- `IsDestinationInSight(...)`
- `ComputeCombinedSteering(...)`
- `ShouldAdvanceToNextWaypoint()`

Important public state includes:

- `CanPathfind`
- `Destination`
- `PathRecheckCooldownFrames`
- `TargetDirection`
- `LastTargetDirection`
- `CurrentRequest`
- `TrailGuide`
- `ShouldMove`
- `IsStuck`
- `HasLineOfSightPath`
- `DistanceToTarget`
- `HasTrailGuide`
- `IsAtDestination`
- `CanMove`
- `StoppedFrameCount`
- `CanAutoStop`
- `StopMultiplier`
- `GroupFactor`
- `AvoidFactor`
- `BehaviorWeights`
- `BrakingPower`
- `MovementGroupID`
- `IsInGroup`
- `Events`

## 4. Host Contract

`NavSteering` operates against `ISteer`. The host must supply:

- `Position`
- `Velocity`
- `Speed`
- `Acceleration`
- `StuckThresholdSpeed`
- `Size`
- `Radius`
- voxel-occupant identity via `IVoxelOccupant`, including `GlobalId`

In normal `Navigator` usage, the steering integration looks like this:

1. `ApplyGuidedTrekRequest(...)` builds an internal `IPathRequest` from the target position plus navigator-owned defaults, then forwards it to `Steering.ApplyPathRequest(...)`.
2. Each simulation tick, `Navigator.Simulate()` calls `Steering.GetHeading(this)`.
3. The resulting heading is passed to `NavTurning` and `NavMotor`.

If you use `NavSteering` directly, the essential rule is:

- `ApplyPathRequest(...)` sets the steering session up
- `GetHeading(...)` is the per-frame update

## 5. State Ownership

`NavSteering` owns the runtime state for a movement session.

That includes:

- the active destination
- the current request object
- the current guide, if one exists
- the line-of-sight versus guided-path mode
- stuck and repath counters
- short-term auto-stop cooldown
- deceleration and closing-distance state

Two internal concepts are especially important:

### 5.1 `_currentRequest`

This is the active `IPathRequest` for the current movement session. It is reused and updated as the navigator moves.

### 5.2 `_trailGuide`

This is the active `IGuide` returned by `PathGuideFactory`.

It may be:

- `null`, for direct travel or no active movement
- an `AStarGuide`
- a `FlowFieldGuide`

Guide lifetime is owned by `NavSteering` while the movement session is active.

## 6. Request Lifecycle

### 6.1 ApplyPathRequest(...)

This is the start of a steering session.

It:

- validates that the request has usable endpoints
- clears previous stuck state
- clears line-of-sight state
- records the exact destination point
- stores the request and last known unit size
- marks movement as active
- schedules a path validation pass on the next `GetHeading(...)`
- optionally assigns a movement group id for shared group steering
- routes shared movement-group bookkeeping through an internal coordinator so hosts still only drive `NavSteering`

If the request is invalid or lacks endpoints, `ApplyPathRequest(...)` calls `Arrive()` immediately.

Important detail:

- `destination` can be an exact point inside a voxel
- it is not required to be the voxel's `WorldPosition`
- group membership is keyed by both `groupId` and the exact requested destination

### 6.2 OnInitialize(...)

`OnInitialize(Fixed64 radius)` resets steering state for a new navigator session.

It also computes `_closingDistance` from:

- the agent radius
- `GlobalGridManager.VoxelSize`

That value is what later controls waypoint advancement and arrival tolerance.

## 7. Per-Frame Update: GetHeading(...)

`GetHeading(ISteer navigator)` is the central steering update.

### 7.1 Early Exits

The method returns `Vector3d.Zero` if:

- `CanMove` is false
- the movement session is not active
- the agent has already arrived
- the request or path becomes invalid

### 7.2 Path Validation

If movement is active, `GetHeading(...)` starts by making sure the current path context is still valid.

If `CanPathfind` is true, it calls `ValidateMovementPath(navigator.Position)`.

If that validation fails:

- `OnInvalidPath` fires
- `Arrive()` is called
- the returned heading is zero

### 7.3 Periodic LOS Recheck

Steering does not recompute line-of-sight every frame.

It uses `_pathCheckCooldown` and `PathRecheckCooldownFrames` to periodically re-evaluate:

- whether the destination is directly reachable
- whether the current guide may be unnecessary

This keeps straight-line checks cheaper in the common case.

### 7.4 Target Direction Resolution

Once the request is valid:

1. `LastTargetDirection` is preserved.
2. the effective `Destination` is resolved from the current movement-group state.
3. `FindTargetDirection(...)` resolves the new target direction.
4. `ComputeCombinedSteering(...)` adds group and avoidance influence.

The resulting value becomes the heading returned to the caller.

### 7.5 Arrival and Stop Checks

If the session is in direct-travel mode with no active guide, `GetHeading(...)` may call `Arrive()` when:

- the agent is within the closing distance threshold
- or there is effectively no movement input and the agent is not stuck

This logic is intentionally separate from guide-based movement, where waypoint or field state still matters.

### 7.6 Stuck Checks

`CheckStuckStatus(...)` runs after target resolution.

If it returns `false`, steering:

- fires `OnIsStuck`
- calls `Arrive()`
- returns zero movement

### 7.7 Waypoint Advancement and Deceleration

If the target direction is non-zero:

- `IWaypointGuide.AdvanceWaypoint()` may be called when `ShouldAdvanceToNextWaypoint()` returns true
- `SetDeceleration(...)` can scale the heading magnitude down when approaching the target through a guide

This is how `NavSteering` slows motion before the final stop instead of always driving at full heading magnitude.

## 8. Path Validation and Guide Resolution

### 8.1 ValidateMovementPath(...)

This method is the bridge between steering and pathing.

It performs the following work:

1. skip if no request pass is scheduled
2. detect unit-size changes
3. update the request origin with `TrySetOrigin(...)`
4. fail if endpoints are no longer valid
5. short-circuit if there is zero displacement
6. check direct line-of-sight via `IsDestinationInSight(...)`
7. request a guide through `PathGuideFactory.RequestGuide(...)` if needed

This means `NavSteering` does not blindly follow a cached guide forever. It revalidates from the current position.

### 8.2 Direct Travel

If `PathManager.NeedsPath(...)` returns `false`, steering enters direct-travel mode:

- `HasLineOfSightPath = true`
- no guide is required
- `FindTargetDirection(...)` aims directly at `Destination`

This is the cheapest successful pathing outcome.

### 8.3 Guided Travel

If direct travel is not valid, steering requests a guide:

- `AStarGuide` for waypoint-driven motion
- `FlowFieldGuide` for field sampling

Guide creation and reuse are delegated to `PathGuideFactory`.

### 8.4 Guide Loss or Invalidity

If guide resolution fails or the guide disappears, steering treats the path as invalid and stops or repaths rather than continuing with stale state.

`SetTrailGuide(null)` is tolerated, but the next heading pass will collapse to a zero target unless a new valid path is produced.

## 9. Target Resolution

### 9.1 FindTargetDirection(...)

This method determines the raw movement direction before avoidance/group blending.

It resolves in this order:

- direct vector toward `Destination` if `HasLineOfSightPath`
- waypoint direction if the guide implements `IWaypointGuide`
- generic guide direction through `IGuide.TryGetMovementDirection(...)`

It then normalizes the result and writes the distance into `_distanceToTarget`.

If no direction can be found, it returns zero.

### 9.2 ShouldAdvanceToNextWaypoint()

Waypoint advancement uses both:

- proximity to the current waypoint
- directional flip between `TargetDirection` and `LastTargetDirection`

It also advances if the remaining distance is very small relative to voxel size.

This keeps waypoint following from stalling at close range.

### 9.3 SetDeceleration(...)

Guide-based movement can scale down `TargetDirection` as the agent approaches the remaining target distance.

This uses:

- current acceleration if available
- otherwise `BrakingPower`

This is not a full physical braking model. It is steering-side magnitude scaling to help the motor slow down naturally.

## 10. Stuck Detection and Repathing

`CheckStuckStatus(...)` uses:

- the agent's current speed
- the host-supplied `StuckThresholdSpeed`
- an internal frame counter

to decide whether movement is failing.

The flow is:

1. ignore stuck logic while auto-stop is paused
2. accumulate stuck frames while speed stays below threshold
3. attempt fallback or repath before declaring a hard stuck state
4. eventually fire `OnIsStuck` and fail the session

Repath behavior includes:

- clearing `HasLineOfSightPath`
- leaving the movement group if grouped
- trying guide-specific fallback through `TryGetFallbackDirection(...)`
- otherwise scheduling a new path request for the next frame
- disposing the current guide instead of returning a bad path to the pool

This is one of the most important correctness paths in the class because it crosses:

- runtime movement
- guide lifetime
- path caching

## 11. Arrival vs Stop

`Arrive()` and `StopMove()` are related but not identical.

### 11.1 StopMove()

`StopMove()`:

- halts active movement
- resets stop-related counters
- clears line-of-sight and scheduled path requests
- leaves any active movement group
- fires `OnStopMove`

It does not mark the destination as reached.

### 11.2 Arrive()

`Arrive()`:

- calls `StopMove()`
- returns the active guide to `PathGuideFactory`
- clears the current request
- clears distance and direction state
- marks `IsAtDestination = true`
- fires `OnArrive`

Use this distinction when documenting or testing behavior:

- stopped is not the same thing as arrived

## 12. Line-of-Sight and Reachability

`IsDestinationInSight(...)` is a small but important wrapper around:

- `PathManager.NeedsPath(...)`

It answers:

- can the agent move directly to the destination with the current unit size?

It does not answer:

- whether a global route exists through the map
- whether the best path is direct

This is why steering still needs guide resolution when LOS fails.

## 13. Group and Avoidance Steering

`NavSteering` applies group behavior in two places:

- movement-group target shaping through `groupId`
- same-group local steering through `ComputeCombinedSteering(...)`

`ComputeCombinedSteering(...)` blends:

- separation
- alignment
- cohesion
- nearest-obstacle avoidance

### 13.1 Movement-Group Target Shaping

When multiple steering sessions share the same non-negative `groupId` and exact requested destination:

- the group center is computed from recently updated members
- if the group stays compact, each member preserves its relative formation offset from that center
- if the group spreads too far or is already close enough to the shared destination that the formation would collapse, steering falls back to the shared destination for each member
- that fallback uses a looser stop tolerance to reduce end-of-path crowding

This behavior is host-transparent. Shared movement-group membership is tracked by an internal coordinator, but hosts do not drive a separate movement-group manager.

### 13.2 Scan Radius

The steering scan radius is derived from:

- `GroupFactor`
- `AvoidFactor`
- agent radius

It queries nearby voxel occupants through `GridScanManager.ScanRadius(...)`.

### 13.3 Group Behavior

When `IsInGroup` is true, only nearby steering agents in the same movement group session contribute:

- separation force away from crowding
- alignment force from neighbor velocity
- cohesion force toward the local center of mass

Those contributions are weighted through `BehaviorWeights`.

Avoidance still considers any nearby occupant, including agents outside the movement group.

### 13.4 Avoidance

Avoidance tracks the nearest obstacle candidate inside the avoid radius and generates a perpendicular dodge direction.

The current implementation:

- picks a left/right dodge based on the velocity-direction dot product
- scales avoidance by proximity
- multiplies by `BehaviorWeights.Avoidance`

### 13.5 Current Caveats

There are still known rough edges here:

- group sessions are inferred from active steering state, so `StopMove()` or `Arrive()` removes the member from the group immediately
- path sharing still comes from the normal guide/cache system; `FlowFieldPathRequest` remains the best fit when many grouped units share one destination

Treat this area as active infrastructure rather than final flocking design.

## 14. Events

`NavSteeringEvents` exposes the main steering lifecycle hooks:

- `OnMoveRequestApplied`
- `OnStartTraversal`
- `OnInvalidPath`
- `OnIsStuck`
- `OnArrive`
- `OnStopMove`

These events are useful for:

- animation state
- AI state transitions
- debugging path failures
- telemetry around stuck or arrival behavior

## 15. Common Integration Pattern

With a concrete `Navigator`, the usual flow is:

```csharp
navigator.GuidedPathMode = GuidedPathMode.AStar;
navigator.ApplyGuidedTrekRequest(destination, rate: TrekRate.Moderate);

TrailblazerManager.Simulate();
navigator.Simulate();
navigator.CommitFrameMotion();
```

Under the hood:

1. `Navigator.ApplyGuidedTrekRequest(...)` creates a built-in path request from the target position and navigator defaults, then forwards it to `NavSteering`.
2. `Navigator.Simulate()` calls `Steering.GetHeading(this)`.
3. The returned heading is passed to turning and motor systems.

If you use `NavSteering` without `Navigator`, the minimum pattern is:

```csharp
var steer = new NavSteering(radius);
steer.ApplyPathRequest(request, destination);

Vector3d heading = steer.GetHeading(agent);
```

The essential rule is:

- `ApplyPathRequest(...)` starts the steering session
- `GetHeading(...)` is the frame update

## 16. Common Gotchas

### Mutating the active request externally

`CurrentRequest` is a live object reference. If code mutates it externally, steering sees the mutation directly. This can make tests look like repathing happened even when only the request object changed in place.

There is already a test note in `NavSteering.Tests` calling this out.

### Forgetting that direct travel skips guide allocation

`HasLineOfSightPath = true` means there may be no active guide at all. Do not assume every movement session owns a `TrailGuide`.

### Treating StopMove() as arrival

`StopMove()` halts movement. It does not mark the destination as reached.

### Expecting movement groups to be fully implemented

They are steering-owned behavior. Hosts only pass `groupId` through the steering API; an internal coordinator tracks shared membership while agents with the same `groupId` and exact destination preserve formation offsets when compact, then fall back to the shared destination when the group spreads out or reaches the end of the move.

### Reusing a `groupId` for different destinations

That is safe, but those sessions do not interact. Grouped steering only blends members that share both the `groupId` and the exact requested destination.

### Assuming avoidance is full collision resolution

It is not. It is a lightweight steering influence based on nearby occupants, not a substitute for motor-side collision and world constraints.

### Forgetting that CanPathfind can be disabled

If `CanPathfind` is false, steering will not run path validation logic. This is useful for non-pathing units such as flying actors, but it also means invalid path assumptions will not be corrected automatically.

## 17. Testing References

Current coverage around steering behavior is concentrated in:

- `tests/Trailblazer.Tests/Navigation/Steering/NavSteering.Tests.cs`
- `tests/Trailblazer.Tests/Navigation/Steering/Support/MockSteerAgent.cs`
- `tests/Trailblazer.Tests/Support/PathTestFactory.cs`
- `tests/Trailblazer.Tests/Support/PathingFixture.cs`

Those tests currently cover:

- initialization
- request application
- line-of-sight shortcut behavior
- arrival
- stuck detection
- invalid-path stop behavior
- flow-field guide usage
- combined steering
- same-group steering filtering
- formation-offset preservation
- group fallback to the shared destination
- waypoint advancement
- stop versus arrive behavior
- auto-stop cooldown
- large-unit requests
- request mutation edge cases
- guide cleanup on arrival

If you change request validation, arrival thresholds, stuck logic, guide lifetime, or avoidance/group behavior, update those tests in the same pass.
