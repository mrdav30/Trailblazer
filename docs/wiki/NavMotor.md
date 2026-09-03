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

Despite its name, `TryTraversal(...)` returns `velocityDelta` as the locomotion
displacement for the current fixed step: the motor has already multiplied its
resolved velocity by `DeltaTime`. `positionDelta` is additional platform
displacement, and `rotationDelta` is the platform rotation delta. The built-in
Navigator adds both displacement outputs to position during
`CommitFrameMotion()`. Do not multiply either displacement by time again.

These outputs are not physical forces or impulses. A host using mass-based
physics must explicitly convert between its body's quantities and Trailblazer's
motion contract. Inverse mass converts force to acceleration (`a = F / m`) or
impulse to a velocity change (`deltaVelocity = impulse / m`); it must not be
applied to an already computed velocity or displacement. The core motor does
not require a rigid body or an assumed physical mass of one.

## The two-phase frame contract

Direct motor integration has two phases in the same simulation frame:

1. Call `TryTraversal(...)` with the frame's `TrekRequest`.
2. Apply its locomotion/platform displacements and rotation delta.
3. Refresh contact, medium, surface, ceiling, and platform state.
4. Call `FinalizeTraversal(...)` with the resulting snapshots.

```csharp
if (motor.TryTraversal(
        frameRequest,
        out Vector3d velocityDelta,
        out Vector3d positionDelta,
        out FixedQuaternion rotationDelta))
{
    ApplyMotion(velocityDelta, positionDelta, rotationDelta);
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

`GroundCondition` also carries moving-platform identity and transform data.
The motor uses it to transfer velocity, preserve attachment, and avoid applying
platform motion twice when landing or leaving.

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
