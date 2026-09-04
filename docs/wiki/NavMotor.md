# NavMotor

`NavMotor` turns one fixed frame's movement request into deterministic
locomotion and platform displacements, plus a rotation delta. It supplies
gameplay-oriented locomotion for ground movement, jumping, falling, sliding,
water, controlled flight, climbing, and moving platforms.

Most applications should drive it through `Navigator`. Use `NavMotor` directly
only when the host needs the locomotion stack without Navigator's steering,
turning, occupancy, and guide orchestration.

## What the motor owns

`NavMotor` owns:

- current and previous traversal state;
- installed locomotion modules and their tuning;
- deterministic movement output for the current fixed frame;
- jump, fall, swim, flight, climb, slide, and platform lifecycle state;
- locomotion events.

The host still owns:

- collision detection and environment probing;
- the authoritative transform or kinematic body;
- applying returned motion deltas;
- producing the refreshed `TrekCondition` after movement;
- engine-specific physics and animation.

The motor never performs a raycast or reads an engine object implicitly.

## Motion quantities and units

Use consistent world units for distance and the context's fixed `DeltaTime` for
elapsed simulation seconds. These quantities are related, but not interchangeable:

| Quantity | Meaning over one fixed step | Units |
| --- | --- | --- |
| Displacement | `currentPosition - previousPosition` | world units |
| Frame-average velocity | `displacement / DeltaTime` | world units per second |
| Velocity change from constant acceleration | `acceleration * DeltaTime` | world units per second |
| Next velocity under constant acceleration | `previousVelocity + acceleration * DeltaTime` | world units per second |
| Frame acceleration | `(currentVelocity - previousVelocity) / DeltaTime` | world units per second squared |

The shorthand `velocity = acceleration * time` assumes constant acceleration
and an initial velocity of zero. It is not the general velocity update.

`TryTraversal(...)` returns `locomotionDisplacement` for the current fixed step:
the motor has already multiplied its resolved velocity by `DeltaTime`.
`platformDisplacement` is additional platform displacement, and
`platformRotationDelta` is the platform rotation delta. The built-in
Navigator adds both displacement outputs to position during
`CommitFrameMotion()`. Do not multiply either displacement by time again.

These outputs are not physical forces or impulses. A host using mass-based
physics must explicitly convert between its body's quantities and Trailblazer's
motion contract. Inverse mass converts force to acceleration (`a = F / m`) or
impulse to a velocity change (`deltaVelocity = impulse / m`); it must not be
applied to an already computed velocity or displacement. The core motor does
not require a rigid body or an assumed physical mass of one.

### One-step corrections and gradual response

Dividing a velocity change by the timestep does not make the response gradual.
For a positive `DeltaTime`, consider this one-step calculation:

```text
acceleration = (desiredVelocity - currentVelocity) / DeltaTime
nextVelocity = currentVelocity + acceleration * DeltaTime
             = desiredVelocity
```

With ordinary, unclamped integration and no other contributions, this reaches
the target in one step, subject to fixed-point rounding. Canceling velocity with
`-currentVelocity / DeltaTime` is the same calculation with a target of zero;
both expressions compute acceleration, not force. Canceling downward velocity
uses the vertical component of that equation.

A gradual response needs an explicit limit or response rule. The motor's
desired-velocity adjustment limits the magnitude of each velocity change to
`maxAcceleration * DeltaTime`. A target within that limit can still be reached
in one step; larger changes take longer. An acceleration value must not be
passed where an API expects velocity or displacement.

Neither acceleration nor an immediate velocity change is inherently more
deterministic. Lockstep peers need the same authoritative inputs, timestep,
fixed-point calculations, state, and execution order. A gradual response that
lags behind a platform may be undesirable gameplay, but identical lag on every
peer is not itself a desynchronization.

## The two-phase frame contract

Direct motor integration has two phases in the same simulation frame:

1. Call `TryTraversal(...)` with the frame's `TrekRequest`.
2. Apply its locomotion/platform displacements and rotation delta.
3. Refresh contact, medium, surface, ceiling, and platform state.
4. Call `FinalizeTraversal(...)` with the resulting snapshots.

```csharp
if (motor.TryTraversal(
        frameRequest,
        out Vector3d locomotionDisplacement,
        out Vector3d platformDisplacement,
        out FixedQuaternion platformRotationDelta))
{
    ApplyMotion(locomotionDisplacement, platformDisplacement, platformRotationDelta);
    TrekCondition refreshed = ProbeTraversalState();

    motor.FinalizeTraversal(
        newPosition,
        lastPosition,
        newRotation,
        refreshed,
        newFootPosition);
}
```

`ApplyMotion(...)` and `ProbeTraversalState()` are host placeholders. They are
where an engine adapter or simulation body performs its own work.

Pass the actual accepted positions to `FinalizeTraversal(...)`, after the
host has resolved movement and collisions. The motor derives frame velocity
from that displacement, not from an unfulfilled requested destination. See
[Navigator's committed motion](Navigator.md#committed-motion-and-locomotion-state)
for the distinction between observed controller motion and locomotion state.

Calling `TryTraversal(...)` twice in the same frame returns `false` the second
time so motion is not accumulated twice. Leaving a traversal open across a
frame boundary is an error: finalize it in the opening frame or call
`AbortTraversalFrame()` when the host intentionally discards that frame.

With `Navigator`, the equivalent public lifecycle is simpler:

```csharp
context.Simulate();
navigator.Simulate();
navigator.CommitFrameMotion();
```

Navigator owns the motor ordering and feeds its own committed snapshots back
into finalization.

## Setup and locomotion profiles

A direct motor binds to one usable `TrailblazerWorldContext` and starts from one
host-supplied traversal condition:

```csharp
var motor = new NavMotor(context, initialCondition);
```

The default locomotion profile installs:

- `Move`, `Fall`, and `Platform` as required core modules;
- `Jump`, `Slide`, `Water`, `Fly`, and `Climb` as the built-in optional
  modules.

Each motor owns its own module instances. Two Navigators can therefore use
different movement speeds, gravity overrides, jump heights, water behavior, or
installed module sets without global controller state.

Use `SetLocomotionProfile(...)` to replace the complete composition, or
`ConfigureLocomotions(...)` to start from the current handler settings. A
profile cannot change while a traversal frame is open.

Public modules are available through `Handler`, for example:

```csharp
motor.Handler.Move.MaxFastSpeed = new Fixed64(6);
motor.Handler.Jump!.BaseJumpHeight = new Fixed64(2);
motor.Handler.Water!.BuoyancyFactor = Fixed64.One;
```

Optional modules can be absent under a custom profile. Check nullable module
properties before tuning them.

## Movement by traversal state

### Solid

Grounded movement applies acceleration, friction, slope projection, and a small
downward ground-stick bias. Slopes above the configured limit can enter the
slide module when it is installed and enabled.

`GroundCondition.SurfaceNormal` is the exact host-provided world-space support
normal. It is independent of `PlatformSnapshot.Transform`: the normal describes
collision geometry, while the transform describes carrier movement. Use
`Vector3d.Up` for flat ground and `Vector3d.Zero` only when no normal was
sampled. See [moving platforms](#moving-platforms-attachment-and-momentum) for
how attachment differs from departure-velocity transfer.

### Gas

Gas is the physical airborne medium. Gravity and terminal velocity apply unless
controlled flight or climbing owns the frame. Jumping and falling are separate
locomotion states within Gas; they are not alternate traversal media.

### Liquid

Liquid movement combines buoyancy, water drag, swim input, underwater timing,
and optional breach jumping. Entering or leaving water updates water, jump, and
fall lifecycle state explicitly.

### Climbing and mantling

Climb geometry remains host owned. An optional `IClimbAffordanceResolver`
supplies a deterministic `ClimbAffordanceSnapshot` for the frame.

When active mantle validation is enabled and the resolver also implements
`IActiveMantleValidator`, a failed validation cancels the mantle. Successful
validation preserves the already latched target; it does not retarget the move
implicitly.

## Moving platforms: attachment and momentum

Platform motion has two separate jobs: carrying an attached scout and deciding
what velocity it inherits when it leaves.

### Carrying an attached scout

The motor preserves an attachment point and rotation relative to the platform.
It uses the platform's transform snapshots to return `platformDisplacement`
and `platformRotationDelta`, so following the platform does not require
accelerating toward its speed. On a rotating platform, the attachment point
can move even when the platform's center stays still.

`PlatformLocomotion.PlatformVelocity` is the sampled world-space velocity of
that attachment point: its displacement between platform snapshots divided by
`DeltaTime`. It is not necessarily the velocity of the platform's center, and
it is not an acceleration or physical impulse.

The host applies the returned platform displacement once, alongside
`locomotionDisplacement`. Do not also add `PlatformVelocity * DeltaTime` as
another platform displacement; that would count the same carry twice.
Navigator already combines the two displacement outputs during its commit.

Grounded carry requires an enabled platform module and an active snapshot with
`SupportsKinematicMotion`. Jumping, controlled flight, and climbing suppress
this carry. Supply authoritative fixed-frame platform snapshots, not smoothed
render transforms.

### Leaving the platform

`GroundCondition.MotionTransferState` selects the transfer mode. Finalization
refreshes the platform state before applying departure transfer. For
`InitTransfer` or `PermaTransfer` inheritance, keep the eligible launch-platform
snapshot and transfer mode in the refreshed `GroundState` for the departure
frame, even though `Medium` has changed to Gas. Clearing that state or marking
the platform inactive or non-kinematic first selects `None` and skips transfer.
`Navigator.SetAirborne()` preserves the last ground condition by default.

| Mode | Effect |
| --- | --- |
| `None` | Does not add departure velocity. Ordinary grounded platform carry still applies. |
| `InitTransfer` | Adds the sampled platform velocity on a solid-to-gas transition. Subsequent locomotion can change that velocity. |
| `PermaTransfer` | Performs the initial transfer and also adds the captured horizontal contribution to ordinary desired locomotion velocity while that contribution is retained. |
| `PermaLocked` | Allows platform carry beyond grounded movement while an eligible active platform remains. It does not select departure-velocity transfer. |

`PermaLocked` does not override the jump, flight, or climb exclusions above.
`FramePlatformVelocity` holds the captured transfer contribution, not a promise
of unchanging airborne velocity: platform-state refresh clears it, and later
locomotion can alter the scout's velocity. Landing also has transfer accounting
to avoid counting inherited platform motion again.

An abrupt platform stop does not imply that every scout must keep moving. An
attached scout can stop with the platform; a detached scout may retain velocity
already transferred from it. The result also depends on locomotion settings
and the host's contact, collision, and friction rules. Inertia means motion
persists without a change to velocity; acceleration changes that motion. These
are controller rules, not a rigid-body momentum-conservation simulation.

## Host-authored traversal state

`TrekCondition` is the bridge between host physics and deterministic
locomotion. Keep these facts current when they apply:

- exact `TraversalMedium`;
- ground contact, surface normal, friction, and level;
- ceiling level;
- water surface/contact state;
- moving-platform identity and transform;
- climb affordance state supplied through the resolver.

Use `SyncTraversalState(...)` when the host must push a state change before the
next traversal. Navigator helpers such as `SetGroundContact(...)` and
`SetTrekCondition(...)` can route through the same seam when
`updateMotorState` is true.

When calling `SetGroundContact(...)`, pass the support normal separately from
the platform snapshot. Never rotate a platform snapshot to make its `Up` axis
look like a slope normal; doing so corrupts attachment and carry calculations.

## Events

`NavMotorEvents` exposes notifications for:

- jump and water-breach start/stop;
- fall start/stop, landing, and maximum fall height;
- drowning;
- climb, mantle, and slip lifecycle.

Treat these as deterministic state notifications. The host decides what audio,
animation, damage, or gameplay response follows.

## Serialization

Motor and built-in locomotion state participate in Navigator's explicit
Chronicler record. Context bindings and host physics objects are not serialized.
Populate an already initialized Navigator shell after restoring its world and
navigation dependencies.

See [Serialization](Serialization.md) for the complete load order.

## Common mistakes

### Forgetting finalization

An opened traversal belongs to its exact frame. Always finalize after applying
movement and refreshing contact state. Use `AbortTraversalFrame()` only for a
deliberately discarded frame.

### Treating the motor as a physics engine

The motor computes deterministic locomotion output. It does not resolve
collisions, discover surfaces, or own an engine rigid body.

### Treating Gas, jumping, and falling as synonyms

Gas is a traversal medium. Jump, fall, flight, and climb are locomotion states
that may occur while the body is in Gas.

### Assuming every optional module exists

Custom locomotion profiles can omit Jump, Water, Fly, Climb, or Slide behavior.
Check the profile and nullable handler properties before using them.

## Related guides

- [Navigator](Navigator.md)
- [Gravity](Gravity.md)
- [NavSteering](NavSteering.md)
- [NavTurning](NavTurning.md)
- [Serialization](Serialization.md)
