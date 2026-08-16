# Navigator Reference

This document is the detailed reference for Trailblazer's `Navigator` class.

If you only need the high-level architecture, read `Overview.md`. If you only
need the standalone pathing request and guide layer, read `Pathing.md`. If you
need the subsystem references behind `Navigator`, read:

- `NavSteering.md`
- `NavTurning.md`
- `NavMotor.md`

The code referenced here lives primarily in:

- `src/Trailblazer/Navigation/Navigator/Navigator.cs`
- `src/Trailblazer/Navigation/Navigator/Navigator.HeightmapGrounding.cs`
- `src/Trailblazer/Navigation/Navigator/Navigator.Serialization.cs`
- `src/Trailblazer/Navigation/Navigator/INavigate.cs`
- `src/Trailblazer/Navigation/Navigator/Occupancy/NavigatorOccupancyTracker.cs`
- `src/Trailblazer/Navigation/Navigator/Guidance/*`

`Navigator` is implemented as a partial class split by ownership area:

- `Navigator.cs` contains the host-facing orchestration API, lifecycle flow,
  frame state, guided request construction, and traversal-state helpers.
- `Navigator.HeightmapGrounding.cs` contains the optional protected heightmap
  grounding helper used by concrete navigator implementations.
- `Navigator.Serialization.cs` contains the Chronicler `RecordData(...)`
  implementation.
- `NavigatorOccupancyTracker` owns the shared occupancy bookkeeping that backs
  `GlobalId`, `OccupantGroupId`, and nearby-occupant scans.
- `Guidance/*` contains guided traversal defaults, guided climb intent state,
  request construction, and volume-exit handoff planning.

## 1. What Navigator Is

`Navigator` is the host-facing orchestration layer for Trailblazer's navigation
stack.

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

Those responsibilities belong to the respective subsystem classes and the
pathing layer.

## 2. Core Design Model

`Navigator` is a coordinator, not a subsystem implementation.

It combines three lower-level controllers:

- `NavSteering`
- `NavTurning`
- `NavMotor`

and drives them through a two-step lifecycle:

1. `Simulate()` for the fixed-step control pass
2. `CommitFrameMotion()` for applying deltas, probing the environment, and
   finalizing state

`Navigator` is intended to be subclassed.

Concrete navigator types provide host-specific traversal probing by implementing
`CheckTrekCondition()`.

## 3. Interface Role

`Navigator` implements `INavigate`, which is a thin runtime-state interface
built on:

- `ISteer`
- `LastPosition`
- `Rotation`
- `Forward`

That keeps the steering-facing contract small while still letting turning or
host code read the current frame state from the concrete `Navigator`.

## 4. Public Surface

The main entry points are:

- constructor injection, `BindContext(...)`, `Setup(context, ...)`, or
  `Activate(context, ...)`
- `Setup(...)`
- `Initialize(TrekCondition condition)`
- `PrewarmMovementGroup()`
- `SyncCurrentTrekConditionToMotor()`
- `ReplaceTrekCondition(TrekCondition state)`
- `Reset()`
- `ApplyInputTrekRequest(...)`
- `ApplyGuidedTrekRequest(PathQuery query, ...)` for graph A* or Flow surface
  travel and retained volume-exit handoff planning
- `ToggleGuidedJump(bool status)`
- `ToggleGuidedFlight(bool status)`
- `ToggleGuidedSwim(bool status)`
- `SetGuidedTrekRate(TrekRate rate)`
- `Simulate()`
- `CommitFrameMotion()`
- `NotifyCollision()`
- `SetGroundContact(...)`
- `SetAirborne(...)`
- `SetWaterContact(...)`
- `SetTrekCondition(...)`
- `CheckTrekCondition()`
- `AddPositionDelta(...)`
- `ApplyRotationDelta(...)`
- `AddVelocityDelta(...)`

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
- `NavigationProfile`
- `BodyShape`
- `Radius`
- `FootPosition`
- `GlobalId`
- `OccupantGroupId`
- `IsLockedOn`

`NavigationProfile` is the sole body/profile authority. `BodyShape`, `Radius`,
and `FootPosition` are derived from it. Complete guided pathing intent belongs
to the supplied `PathQuery`.

Protected frame-local state includes:

- `_frameCondition`
- `_frameRequest`
- `_positionDelta`
- `_rotationDelta`
- `_velocityDelta`

## 5. State Ownership

`Navigator` owns several categories of state.

### 5.0 World Context Binding

Each navigator belongs to exactly one `TrailblazerWorldContext` while active.
Bind the context by passing it to the concrete navigator constructor, calling
`BindContext(context)`, or using `Setup(context, ...)` /
`Activate(context, ...)`. Setup without an existing binding throws, so hosts
should make context ownership explicit.

The bound context supplies voxel occupancy, guided request creation, guide
lookup, frame timing, movement-group coordination, and deterministic navigator
id allocation. `Reset()` deregisters from the bound world and clears the
binding; reuse across worlds is allowed only after reset and an explicit rebind.

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

Subsystems do not mutate committed transform state through a shared host
interface. They return or accumulate deltas, and `Navigator` applies those
deltas in `CommitFrameMotion()`.

### 5.3 Traversal State

`Navigator` owns:

- `_frameCondition`
- `_frameRequest`
- `StuckThresholdSpeed`
- `IsGuideded`

`_frameCondition` is the current traversal snapshot the motor will finalize
against. `_frameRequest` is semi-transient travel state.

In practice that means:

- `Origin`, `FootPosition`, and `Rotation` are refreshed every `Simulate()`
- `Direction` is overwritten by steering while guided, or preserved from manual
  input for the current frame
- `Rate`, `IsRequestingFlight`, and `IsRequestingSwim` can persist across guided
  frames
- `IsRequestingJump` is frame-scoped and is cleared after commit

`IsGuideded` is explicit navigator state. It is turned on by
`ApplyGuidedTrekRequest(...)` and turned off by `ApplyInputTrekRequest(...)` or
`Reset()`.

Guided climb intent is internally split into two ownership modes:

- `Explicit`, for host-authored
  `ApplyGuidedTrekRequest(... isRequestingClimb: ...)` and
  `ToggleGuidedClimb(...)`
- `Auto`, for route-derived climb intent that follows steering-owned route
  topology metadata

On bounded handoff activation frames, navigator-owned bootstrap climb intent
still wins over an immediate same-frame `true -> false` clear, but steering can
still promote `false -> true` immediately when the live follow-up route resolves
into a climb-requiring topology.

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

This is what makes the navigator visible to scan-based steering and
voxel-occupancy systems.

### 5.6 Host Bindings

`Navigator` can also bind optional host integrations.

Those bindings are not treated as navigator-owned simulation state.

## 6. Lifecycle

### 6.1 Setup(...)

`Setup(...)` initializes the base transform state for a navigator instance.

It:

- assigns `GlobalId`
- uses a host-provided `globalId` when one is supplied
- otherwise allocates a deterministic default id from the owning context's
  navigator setup order
- sets `Position` and `LastPosition`
- sets `Rotation`
- derives `Forward`
- sets starting `Velocity`
- stores the required exact `NavigationProfile`
- marks `_isSet = true`

It does not create the steering, turning, or motor controllers yet.

If your broader simulation stack already owns stable agent ids, pass that id
into `Setup(...)` so Trailblazer uses the same identity for occupancy and
movement-group coordination.

### 6.2 Initialize(...)

`Initialize(TrekCondition condition)` completes activation.

It:

- stores the initial `_frameCondition`
- creates context-bound `NavSteering` using `Radius`
- creates context-bound `NavMotor` using `_frameCondition`
- seeds motor velocity from the navigator's starting `Velocity`
- creates context-bound `NavTurning` using `Radius`
- performs initial voxel occupancy registration in the owning context's
  `GridWorld`
- marks `_isInitialized = true`

After `Setup(...)` and `Initialize(...)`, `IsActive` becomes true.

### 6.3 Reset()

`Reset()` clears the current traversal session and occupancy state.

It:

- resets `_frameCondition`
- resets `_frameRequest`
- sets `IsGuideded = false`
- removes the navigator from occupied voxels
- clears the bound context
- marks the navigator inactive

After `Reset()`, the instance must be rebound, then go through `Setup(...)` and
`Initialize(...)` again before reuse.

## 7. Input and Travel Requests

`Navigator` supports two high-level movement modes.

### 7.1 Manual Input

`ApplyInputTrekRequest(...)` writes directly into `_frameRequest`:

- `Direction`
- optional `FacingDirection`
- `Rate`
- `IsRequestingJump`
- `IsRequestingFlight`
- `IsRequestingSwim`

and marks the navigator as not guided.

If `FacingDirection` is supplied, `Navigator` keeps using `Direction` for
movement while turning toward `FacingDirection`. This is the intended way to
support strafing, backpedaling, or turn-in-place requests without changing the
motor contract.

If `FacingDirection` is not supplied, manual input still follows the current
lock-on rule:

- when `IsLockedOn` is `true` and the request is not sprinting, manual input
  does not auto-turn toward movement
- sprinting manual input still auto-turns toward movement

Manual input is frame-local. After `CommitFrameMotion()`, a non-guided navigator
fully resets `_frameRequest`, so manual callers should reissue input each fixed
step.

### 7.2 Guided Travel

`ApplyGuidedTrekRequest(...)` switches the navigator into guided mode.

For graph-backed surface travel, pass a complete immutable `PathQuery`. The
navigator requires:

- `query.Agent == NavigationProfile`
- `query.Start.Position == FootPosition`
- surface-to-surface `PathAlgorithm.AStar` or `PathAlgorithm.FlowField`
- `AllowTransitions == false` for A*; only Flow may opt into retained handoff
  planning

It then:

- sets `IsGuideded = true`
- clears the current manual direction
- stores guided request state in `_frameRequest`
- optionally accepts a shared `groupId` for grouped movement
- gives the exact query to steering, which owns and disposes the resulting
  `NavigationGuideLease` or `NavigationFlowFieldLease`

For Flow queries, `query.AllowTransitions` controls whether Navigator may use
the retained hybrid planner above the direct graph guide-service boundary. It
also allows bounded swim-exit style handoffs
from liquid volume into a follow-up chart request when the requested target is
chart-backed outside the active liquid volume, plus bounded aerial landing
handoffs when an authored volume-to-chart landing route beats staying in
gas-volume travel. This keeps the public navigator surface on the existing
guided modes instead of adding a separate hybrid mode.

While guided movement remains active:

- `Rate` persists until changed or replaced by a different request
- `IsRequestingFlight` persists until changed or replaced by a different request
- `IsRequestingSwim` persists until changed or replaced by a different request
- `IsRequestingJump` is still frame-scoped unless the host reissues it
- explicit guided climb intent stays host-owned and sticky
- auto-derived guided climb intent follows the current effective steering route
  topology after `Steering.GetHeading(...)`

When the current `TrekCondition.Medium` is `TraversalMedium.Gas`, guided travel
uses volume-first routing through authored or explicitly configured gas volume.
Steering may travel directly, acquire a volume guide if blockers force a detour
inside that space, or hand off into a chart-backed follow-up route through an
authored landing transition when transition fallback is enabled and the landing
route is preferable.

Guided travel through liquid does not auto-toggle a swim state.
`TrekCondition.Medium` still decides that the request should use liquid-volume
pathing, but active swim control is authored separately through
`IsRequestingSwim` on the frame request via `ApplyGuidedTrekRequest(...)`,
`ApplyInputTrekRequest(...)`, or `ToggleGuidedSwim(bool status)`.

If the requested gas or liquid target sits outside the active volume medium and
reaching it would require an authored exit or landing handoff, when guided
traversal transitions are disabled, the request will fail instead of silently
snapping the volume endpoint onto the nearest handoff voxel.

Gas and liquid guided travel remain volume-first. Transition fallback does not
reinterpret those flows as a new hybrid navigator mode, although liquid can use
a bounded authored exit handoff into a normal chart-backed request and gas can
use a bounded authored landing handoff into chart-backed follow-up travel.

Passing the same non-negative `groupId` to multiple navigators lets
`NavSteering` preserve relative formation offsets while the group stays compact.

When `_frameCondition.Medium` is `TraversalMedium.Gas` or
`TraversalMedium.Liquid`, the navigator uses `VolumePathRequest` first and uses
the supplied graph Flow query only for optional chart-backed follow-up handoffs.

### 7.3 Other Request Helpers

`Navigator` also exposes:

- `ToggleGuidedJump(bool status)`
- `ToggleGuidedFlight(bool status)`
- `ToggleGuidedSwim(bool status)`
- `SetGuidedTrekRate(TrekRate rate)`

These let the host modify the frame request for a guided movement session
without rebuilding the whole movement session. While guided movement is active,
the `ToggleGuidedFlight(bool status)`, `ToggleGuidedSwim(bool status)`, and
`SetGuidedTrekRate(TrekRate rate)` values persist across subsequent simulation
ticks. Note that `ToggleGuidedJump(...)` only updates the current frame request,
and is not intended for toggling jump outside of a guided movement session.

## 8. Simulation Lifecycle

The `Navigator` lifecycle is split across `Simulate()` and
`CommitFrameMotion()`.

### 8.1 Simulate()

`Simulate()` is the fixed-step controller pass.

It first verifies that the navigator is active. If not, it throws.

Then it:

1. writes transient request state through `_frameRequest.SetTransientState(...)`
2. asks steering for a heading if the navigator is guided
3. buffers turning toward the explicit facing direction when one is present,
   otherwise toward the current movement direction unless manual lock-on is
   suppressing auto-turn for the frame
4. runs `Motor.TryTraversal(...)`
5. accumulates any returned deltas
6. runs `Turning.TrySimulateTurn(...)`
7. if a rotation was returned, stores it in `Rotation`

Important detail:

- guided movement overwrites `_frameRequest.Direction` from
  `Steering.GetHeading(this)`
- non-guided movement preserves whatever manual direction was last written into
  `_frameRequest`
- a non-null `FacingDirection` overrides the default "face movement" turn rule
  without changing the movement direction that the motor consumes
- manual lock-on keeps the current facing for non-sprinting input unless the
  host supplies `FacingDirection`

This means `Simulate()` is where the controller stack is executed, but not where
final position and velocity are committed.

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

So guided sessions preserve their semi-persistent request values between frames,
while manual input does not.

This is where the navigator becomes internally consistent for the next
simulation frame.

### 8.3 Why the Split Exists

The two-step structure matters because:

- subsystem controllers can accumulate intent first
- the host can update traversal state from the world in between
- the motor gets finalized against the post-movement environment state

This is one of the most important architectural rules in Trailblazer's
navigation stack.

### 8.4 NotifyCollision()

`NotifyCollision()` is the host-facing collision hook on `Navigator`.

It currently forwards collision notification into `NavTurning` so collision
auto-turn can be evaluated on the next `Simulate()` tick without forcing callers
to reach into `navigator.Turning` directly.

Keeping this hook on `Navigator` leaves room for future collision-driven
responses in other subsystems without changing the host integration point.

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

`SetGroundContact(...)` is especially important for moving-platform integration
because it makes the host provide a sampled `PlatformSnapshot` rather than
treating Trailblazer as if it owned the platform object itself.

If the sampled `PlatformSnapshot` is marked inert, Trailblazer still uses it as
surface data for things like surface orientation and friction, but it skips
kinematic platform carry, attachment, and movement-transfer behavior.

### 9.3 SetTrekCondition(...)

`SetTrekCondition(...)` is the low-level partial-update helper for
`_frameCondition`.

It:

- updates only the values you pass
- leaves unspecified values unchanged
- optionally pushes the updated condition immediately into the motor through the
  explicit pre-traversal sync seam

Use `updateMotorState: true` when the motor needs to see the new traversal state
before the next traversal step. Prefer `SetGroundContact(...)`,
`SetAirborne(...)`, or `SetWaterContact(...)` when the host is writing a fresh
environment contact result.

### 9.4 SyncCurrentTrekConditionToMotor()

`SyncCurrentTrekConditionToMotor()` pushes the current `_frameCondition`
snapshot into `NavMotor` immediately.

Use it when:

- the host has already updated `_frameCondition`
- the motor must consume that new snapshot before the next `Simulate()` or
  `TryTraversal(...)`
- the host wants an explicit lifecycle handoff instead of relying on a boolean
  flag on a setter

This is the named navigator-level seam behind `updateMotorState: true`.

### 9.5 CheckTrekCondition()

`CheckTrekCondition()` is abstract.

Concrete navigator types implement it to:

- probe the world
- determine solid, gas, or liquid medium
- update surface normals and sampled platform data
- set ceiling constraints
- write the result into `_frameCondition`

### 9.6 ReplaceTrekCondition(...)

`ReplaceTrekCondition(...)` swaps `_frameCondition` wholesale.

This is useful when a host already has a complete traversal snapshot and wants
to replace the current one in a single call. If the motor needs to see that
replacement before the next traversal step, either pass `updateMotorState: true`
or call `SyncCurrentTrekConditionToMotor()` immediately after the replace.

## 10. Delta APIs

The delta methods are the write-path used by the motor and host integration.

### 10.1 AddPositionDelta(...)

Adds to `_positionDelta` and also shifts `LastPosition` so externally applied
position deltas do not distort the velocity calculation.

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

This is what makes nearby-occupant scans work for
`NavSteering.ComputeCombinedSteering(...)`.

### 11.2 Reset Cleanup

`Reset()` explicitly removes the navigator from all tracked occupied voxels
before deactivating it.

This is important because grid occupancy is context-owned world infrastructure
that feeds steering behavior.

## 12. Utility Methods and Extension Points

### 12.1 FootPosition

Returns:

- `Position + Vector3d.Down * BodyShape.RootToFootOffsetY`

This is used by platform and ground-contact logic.

### 12.2 GenerateGUID()

`GenerateGUID()` is virtual.

By default it delegates to Trailblazer's deterministic navigator-id allocator.

That still gives a subclass a way to control identity generation if needed for
testing, host integration, or stricter determinism workflows.

## 13. Common Integration Pattern

A typical concrete navigator flow looks like this:

```csharp
var navigator = new MyNavigator(context);
var profile = new NavigationAgentProfile(
    new KinematicBodyShape(Fixed64.Half, Fixed64.One, Fixed64.Zero),
    Fixed64.One,
    Fixed64.One,
    Fixed64.FromFraction(1, 4),
    TraversalMedia.Solid,
    TraversalCapability.None);

navigator.Setup(
    position: new Vector3d(0, 0, 0),
    navigationProfile: profile,
    rotation: FixedQuaternion.Identity,
    velocity: Vector3d.Zero);

navigator.Initialize(new TrekCondition
{
    Medium = TraversalMedium.Solid,
    SurfaceLevel = Fixed64.Zero,
    GroundState = new GroundCondition()
});

var query = new PathQuery(
    new NavigationEndpoint(navigator.FootPosition, "Ground"),
    new NavigationEndpoint(new Vector3d(5, 0, 5), "Ground"),
    profile,
    new NavigationAreaPolicyKey("default", revision: 1),
    new TraversalIntent(TraversalDomain.Surface, TraversalMedium.Solid, TraversalDomain.Surface),
    PathAlgorithm.AStar,
    new NavigationWorkBudget(1024, 64, 4096, 16384, 4096, 0, 0, 0, 0, 0, 0),
    allowTransitions: false);

navigator.ApplyGuidedTrekRequest(query, rate: TrekRate.Moderate);

context.Simulate();
navigator.Simulate();
navigator.CommitFrameMotion();
```

If several navigators should move as one group, pass the same optional `groupId`
to each `ApplyGuidedTrekRequest(...)` call.

If grouped navigators are restored through Chronicler and you want formation
behavior available on the very next frame, bind and initialize each navigator
shell to the correct context, populate it, then call `PrewarmMovementGroup()`
once per loaded navigator before the next `context.Simulate()` step. If you skip
it, grouped steering still re-joins lazily during the next steering update.

## 14. Common Gotchas

### Calling Simulate() or CommitFrameMotion() before activation

Both methods throw until the navigator has gone through `Setup(...)` and
`Initialize(...)`.

### Treating Navigator as self-sufficient

It is not. Concrete navigators still need to refresh traversal state between
`Simulate()` and `CommitFrameMotion()` through `CheckTrekCondition()`.

### Forgetting to update traversal state between simulation phases

If `_frameCondition` is stale, `NavMotor.FinalizeTraversal(...)` will finalize
against incorrect medium, surface, or ceiling data.

### Forgetting that occupancy is part of behavior

Voxel occupancy is not cosmetic. It feeds scan-based steering and avoidance.

### Assuming Reset() fully disposes subsystem objects

`Reset()` deactivates the navigator and clears state, but it is still a
reuse-oriented reset. Re-run `Setup(...)` and `Initialize(...)` before using the
instance again.

## 15. Testing Notes

Current direct coverage for navigator orchestration is still lighter than
steering, turning, and motor coverage.

The main direct support files are:

- `tests/Trailblazer.Tests/Navigation/Navigator/TestDoubles/TestNavigator.cs`
- `tests/Trailblazer.Tests/Navigation/Navigator/Navigator.Tests.cs`

If `Navigator` behavior changes, especially around lifecycle, frame ordering, or
traversal-state updates, this area benefits from refreshed direct tests.
