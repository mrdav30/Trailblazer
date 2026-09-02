# Gravity, Jumping, and Vertical Motion

Trailblazer's vertical model is deterministic and gameplay oriented. Grounded
bodies receive a small downward stick bias, airborne bodies accelerate toward a
terminal fall speed, Liquid bodies use buoyancy, and jumps combine an initial
impulse with an optional held-jump extension.

The host still owns collision detection and the authoritative body. `NavMotor`
computes motion from the exact `TrekCondition` and fixed timestep supplied for
the frame.

## Configure global defaults

`LocomotionForces.GlobalForces` holds the simulation-wide gravity and terminal
velocity used by motors without local overrides:

```csharp
LocomotionForces.GlobalForces.GravityForce = Fixed64.FromDecimal(1.6m);
LocomotionForces.GlobalForces.TerminalVelocity = new Fixed64(20);

// Restore the built-in values.
LocomotionForces.GlobalForces.Reset();
```

The built-in defaults are:

- gravity magnitude: `9.8`;
- terminal fall speed: `53`;
- base jump height: `1`;
- extra held-jump height: `2`;
- neutral buoyancy factor: `1`;
- passive water drag factor: `0.0625`.

Changing a global value affects every motor that is still following the global
on its next fixed frame.

## Override one motor

Each motor can pin its own gravity or terminal velocity:

```csharp
motor.Handler.Forces.GravityForce = new Fixed64(20);
motor.Handler.Forces.TerminalVelocity = new Fixed64(10);

motor.Handler.Forces.ClearGravityForceOverride();
motor.Handler.Forces.ClearTerminalVelocityOverride();
```

An override wins over the global setting until it is cleared. This makes it
possible to model local gravity wells, lightweight actors, or special movement
profiles without changing every Navigator.

## Fixed-step timing

The owning `TrailblazerWorldContext` supplies `DeltaTime`, `InvDeltaTime`, and
`TotalTime`. Gravity, jump extension, cooldowns, and terminal-velocity checks
all use that deterministic clock.

Call `context.Simulate()` exactly once for each authoritative fixed frame.
Do not derive motor time from wall-clock time or a rendering frame.

## Grounded motion

Grounded does not mean gravity is disabled. The motor suppresses upward output
and applies a small downward bias so the body stays attached across uneven
surfaces.

The host's refreshed ground contact is what keeps the next frame classified as
Solid. If ground probing is stale or missing, the motor cannot invent that
contact.

## Airborne motion

While the current medium is Gas, gravity reduces vertical velocity until the
configured terminal fall speed is reached.

Jumping, falling, controlled flight, and climbing are separate locomotion states
that can change this behavior:

- an active jump may temporarily offset gravity while input is held;
- a fall records start, stop, landing, and maximum-height lifecycle;
- controlled flight can compensate for gravity;
- climbing owns its attachment motion while active.

Gas is the traversal medium; those are controller states within it.

## Jump height and held input

Base jump speed is derived from configured gravity and
`JumpLocomotion.BaseJumpHeight`. Before applying the jump, the motor clears
existing downward output so a valid jump is not weakened by leftover descent in
the same frame.

While jump input remains held and the extra-height window is active, the motor
partially offsets gravity along the latched jump direction. Releasing input
earlier lets gravity take over sooner.

The main tuning values are:

- `BaseJumpHeight`;
- `ExtraJumpHeight`;
- `JumpControlMultiplier`;
- `PerpendicularJumpAmount` and `SteepPerpendicularJumpAmount`;
- jump count and cooldown;
- `WaterLocomotion.BreachJumpMultiplier` for water exits.

Ground jumps can lean with the surface normal. Water-breach jumps use the
Liquid-specific multiplier.

## Liquid and buoyancy

Liquid motion combines gravity-relative buoyancy, water drag, swim input, and
optional breach jumping.

Interpret `WaterLocomotion.BuoyancyFactor` as:

- `1`: neutral relative to gravity;
- greater than `1`: upward acceleration;
- less than `1`: downward acceleration.

Water drag is configured separately on `MoveLocomotion`. Buoyancy does not
replace explicit vertical swim input.

## Ceilings, falls, and platforms

During finalization, a ceiling hit stops upward frame velocity and ends the
active jump extension.

Fall classification is separate from raw gravity. Entering water clears a fall;
landing ends it; ordinary downhill movement should not become a fall solely
because vertical position decreased.

Moving platforms can contribute inherited velocity when a body leaves them and
landing correction when it arrives. Airborne vertical motion therefore starts
from the complete prior frame state, not always from rest.

## Serialization

Per-motor gravity and terminal-velocity override state is serialized. A motor
that followed the global before saving continues to follow the current runtime
global after loading; the global value itself is not copied into each Navigator
record.

## Common mistakes

### Looking for gravity on `TrailblazerWorldContext`

The context owns deterministic time. Gravity settings live on
`LocomotionForces.GlobalForces` and each motor's `Handler.Forces`.

### Treating grounded gravity as physically canceled

Trailblazer uses a ground-stick model. Grounded frames still bias motion
downward intentionally.

### Treating Liquid as “gravity off”

Liquid uses gravity-relative buoyancy plus drag and swim input.

### Changing global settings for one actor

Use a per-motor override when only one controller should differ.

## Related guides

- [NavMotor](NavMotor.md)
- [Navigator](Navigator.md)
- [Heightmaps](HeightMaps.md)
- [Serialization](Serialization.md)
