# Navigator

`Navigator` is the simulation-facing coordinator for steering, turning, motor,
locomotion, traversal state, occupancy, and guided semantic actions. It remains
abstract so hosts can supply their own traversal-state checks and integrations.

## Setup

A Navigator binds to one `TrailblazerWorldContext` and requires one exact
`NavigationAgentProfile`. This C# fragment assumes a concrete Navigator shell,
context, position, and profile:

~~~csharp
navigator.Setup(context, startRootPosition, profile);
navigator.Initialize(new TrekCondition
{
    Medium = TraversalMedium.Solid
});
~~~

The profile's `KinematicBodyShape` is authoritative. `FootPosition` is derived
from root position and `RootToFootOffsetY`; guided endpoints use foot space.
The profile remains an immutable value. Replace the complete profile through
the Navigator rather than mutating body size, turning radius, or traversal
capabilities independently.

`Reset()` releases guidance, pending actions, movement-group ownership, and
occupancy before the object can be rebound/reinitialized.

## Runtime Profile Replacement

Use `TryReplaceNavigationProfile(...)` for an atomic posture or capability
change such as standing to crouching, compact-ball presentation, or losing
flight. Call it between fixed frames: after committed motion and before the
next `Simulate()`. This fragment assumes `crouchingProfile` is a complete valid
profile and the host has not opened the next frame:

~~~csharp
NavigationProfileReplacementStatus status =
    navigator.TryReplaceNavigationProfile(crouchingProfile);

switch (status)
{
    case NavigationProfileReplacementStatus.Applied:
    case NavigationProfileReplacementStatus.Unchanged:
        break;
    default:
        // Keep the previous posture and retry only after the reported
        // lifecycle, navigation, guidance, or physical condition changes.
        break;
}
~~~

An accepted change preserves the exact foot point, stable Navigator identity,
current physical medium, controller instances, unrelated locomotion state, and
durable destination. Trailblazer updates the root offset, previous root,
turning radius, occupancy, movement-group radius, affected capability state,
and active query profile as one operation. Existing A* or Flow guidance is
released and reacquired from the accepted foot and current medium on demand.

Expected rejection is a status, not an exception, and leaves the live profile,
position, occupancy, and guidance unchanged. In particular:

- `UnsafeLifecycle` means the Navigator is inactive, a traversal frame is open,
  or accepted motion still needs to be committed;
- `ActionInProgress` preserves the pending semantic action for the host to
  complete or cancel through the ordinary lifecycle;
- `NavigationClearanceRejected` means the current graph cell or GridForge
  occupancy cannot accept the replacement envelope;
- `GuidanceIncompatible` means the durable destination intent cannot be
  represented with the replacement profile;
- `PhysicalFitRejected` means the host-owned collision shape cannot be changed
  at the foot-preserving candidate root.

`CommittedCellChanged` and capability-loss callbacks run only after the
replacement is fully committed. Host callback exceptions propagate; they do
not roll back the profile or physical body, and callers must not interpret one
as a rejected change or retry the same replacement. Keep simulation callbacks
non-throwing. Trailblazer still attempts the climb-stop notification if a
committed-cell callback fails. A physical adapter must likewise distinguish an
accepted physical transaction from a later notification failure;
`GravitasNavigator3D` publishes the Navigator profile, root, occupancy,
guidance, and capability state after Gravitas commits the physical state but
before Gravitas invokes separation callbacks. It then rethrows any returned
post-commit notification failure with its original stack.

The engine-agnostic base Navigator has no collision body, so a derived physical
adapter must implement the protected synchronous replacement hook. It must
either commit the complete host shape and candidate root or reject without
mutation. `GravitasNavigator3D` supports this transaction for an existing
capsule body with exact unit world scale; it rejects unsupported collider
families, scaled owners, unrepresentable scale, and positive overlap while
allowing exact support contact. A presentation change does not add
rolling physics, animation, or another navigation mode.

## Guided Travel

This C# fragment assumes a query whose start/profile/medium match the current
host-restored Navigator state:

~~~csharp
navigator.ApplyGuidedTrekRequest(
    query,
    rate: TrekRate.Moderate);
~~~

The query must:

- use the same exact `NavigationAgentProfile`;
- start at the current derived foot position;
- use the host-restored current `TrekCondition.Medium`;
- reference published maps and an exact area-policy revision.

Navigator does not infer or prioritize another start medium. Construct the query
for the physical state the host has actually committed.

`NavigationAgentProfile.ArrivalRadius` is the inclusive final-destination
radius. `NavSteering.WaypointTolerance` is a separate non-negative world-unit
tolerance for ordinary intermediate guide steps. Intermediate steps advance
when they enter that tolerance, or when a heading reversal proves the body
passed the step within the arrival radius. Neither rule can cross a pending
transition action.

## Fixed-Step Lifecycle

The normal order is:

1. update host contacts/current traversal state;
2. call `Simulate()`;
3. let the motor/locomotion stack accumulate deterministic deltas;
4. call `CommitFrameMotion()`;
5. publish the resulting state for the next lockstep frame.

`Simulate()` asks `NavSteering` for ordinary heading or one transition
instruction. `CommitFrameMotion()` applies accumulated locomotion/platform
displacements and rotation deltas; it does not execute semantic actions. Both
calls belong to the authoritative fixed-step lifecycle, not the render loop.
Configure optional stable fixed-frame alignment for periodic direct-LOS checks
through the steering controller; see
[periodic line-of-sight rechecks](NavSteering.md#periodic-line-of-sight-rechecks).

Custom integrations can queue locomotion with `AddLocomotionDisplacement(...)`
and additional world-space offsets with `AddPositionDelta(...)`. Both accept
displacement in world units, with any timestep conversion already performed.

After the frame request and motor state are fully finalized,
`CommitFrameMotion()` resolves the root position against the current published
navigation graph. `LastCommittedCell` then exposes the cell address, area,
physical medium, graph version, optional active policy key, and simulation
frame. `CommittedCellChanged` fires only when the stable cell entry
(address/area/medium) changes or becomes absent; graph-version, frame, or policy
refreshes update the property without repeating the entry callback. Setup,
querying, guide sampling, and `Simulate()` never publish this notification.

An unavailable graph generation preserves the previous value for retry. A
definitive position with no physical/effective navigation cell clears it once.
This notification reports committed navigation metadata only; hosts own any
gameplay effects.

## Committed motion and locomotion state

The base `CommitFrameMotion()` implementation derives `Velocity` from the
committed position change divided by the context's fixed `DeltaTime`. `Speed`
is that velocity's magnitude, and `Acceleration` is the change from the previous
velocity divided by the same timestep. These describe the resulting controller
motion, not the requested steering heading or desired speed.

`Motor.Handler.Move.FrameVelocity` serves a different purpose: it is the
motor's working locomotion velocity. Finalization starts it from the accepted
displacement, then can adjust it for platform velocity transfer or a ceiling
hit. It is therefore not an interchangeable alias for `Navigator.Velocity`.

Measuring velocity this way is compatible with lockstep when every peer uses
the same authoritative positions, fixed timestep, and deterministic collision
results. It does not make a nondeterministic physics engine deterministic.
Hosts must report accepted simulation motion rather than an interpolated
render transform or a desired position that collisions prevented.

See [motion quantities and units](NavMotor.md#motion-quantities-and-units) before
adapting motor outputs to another physics model, and
[simulation versus visual smoothing](NavTurning.md#simulation-turning-versus-visual-smoothing)
when presenting fixed-step motion in an engine.

## Pending Transition

Before an action becomes pending, guided movement approaches its exact source
anchor. Navigator bounds the motor's controlled approach displacement when that
frame would cross the anchor, avoiding fixed-step overshoot and oscillation.
Ground movement follows the sampled support tangent; flight, swimming, and
climbing approach in three dimensions. Gravity, jumping, and platform carry stay
independent. This is a movement request, not a snap: the host must still resolve
collisions, and a blocked body does not acquire the action just because the
requested pose reached its source. Arrival tolerance does not skip this boundary.

`PendingTransition` is the single surfaced action owned by Navigator. While it
has a value:

- movement direction is zero;
- authored locomotion hints are applied to the frame request;
- ordinary guide sampling is blocked;
- the producing lease remains at the source action.

The following controller-loop fragment names host-owned action/state functions
as placeholders:

~~~csharp
NavigationTransitionInstruction? pending = navigator.PendingTransition;
if (pending.HasValue)
{
    PerformHostAction(pending.Value);

    NavigationGuideStatus status =
        navigator.CompletePendingTransition(pending.Value);

    if (status == NavigationGuideStatus.Success)
    {
        // Commit host position/contact/medium to the instruction destination
        // before the next simulation step.
    }
}
~~~

Completion is the host assertion that the action happened. Trailblazer advances
the guide and updates durable query start-medium intent from the exact completed
instruction, but it does not rewrite the host's `TrekCondition`, teleport a
body, run animation, or change engine physics.

Only assert completion after the host has accepted the action's outcome. A
blocked ladder or dismount remains pending; requested height alone is not
evidence of a completed climb.

## Failure Semantics

- A mismatched, copied-from-another-lease, duplicate, or stale instruction does
  not advance the cursor.
- A relevant publication makes completion return `Stale`. The old action stays
  visible until the next public simulation/cancellation lifecycle releases it.
- Transient `CapacityExceeded` while validating a held action preserves it and
  zero guidance for retry.
- `StopMove()`, `Arrive()`, a new manual/guided request, load, reset, or
  disposal cancels the surfaced action and releases its guide.

Exact-medium validation occurs when `ApplyGuidedTrekRequest(...)` starts the
session. Navigator does not automatically reread and repair the host medium on
later ordinary repaths: those update start position while retaining the current
query medium. The host must synchronize `TrekCondition` after an action and
construct any later query for its real physical medium.

## Locomotion Hints

`TraversalTransitionLocomotionHints` are authored on definitions/rules and are
copied into the instruction.

- `RequestClimb` sets climb intent while pending.
- `PreserveClimbAfterCompletion` keeps climb intent after successful
  completion.

Hints do not perform the action. They only bridge the semantic instruction into
the built-in locomotion request flags.

## Manual Input

`ApplyInputTrekRequest(...)` cancels guided state and applies one ordinary frame
request. Manual movement can work without a navigation map. Guided map truth and
semantic actions are relevant only to `ApplyGuidedTrekRequest(...)`.

Both input methods accept `isRequestingControlledDescent`. For an already
falling agent whose required `FallLocomotion` is configured, this requests
fall-preserving controlled descent for the current intent without enabling
flight or ascent. Guided callers can update the active intent with
`ToggleGuidedControlledDescent(bool)`. The host still owns the request timing
and any damage or presentation response; see [NavMotor](NavMotor.md#fall-preserving-controlled-descent)
for the exact fixed-frame admission and velocity rules.

## Traversal State

Hosts report state through methods such as:

- `SetGroundContact(...)`;
- `SetAirborne(...)`;
- `SetWaterContact(...)`;
- `SetTrekCondition(...)`;
- `ReplaceTrekCondition(...)`.

The concrete Navigator's `CheckTrekCondition()` remains the host integration
point. Keep physical state publication deterministic and consistent with any
completed instruction.

`SetGroundContact(...)` requires the sampled world-space support normal in
addition to the surface level. Use `Vector3d.Up` for flat ground and
`Vector3d.Zero` only when no normal was sampled. A `PlatformSnapshot` carries
moving-platform identity and transform independently; its `Transform.Up` is not
used as collision geometry.

## Heightmaps And Occupancy

Heightmap grounding is opt-in and affects kinematic Y projection, not graph
connectivity. Occupancy is registered against the bound GridWorld and rebuilt on
load/reset as documented by the host lifecycle.

Movement-group formation padding is the single world-unit
`TrailblazerWorldContextSettings.MovementGroupPadding` value. It is independent
of rectangular or hex grid metrics.

## Serialization

Navigator serialization uses populate-existing-instance semantics. It records
durable query destination/profile/policy/algorithm/budget/target-media intent,
but not a guide payload, cursor, or pending instruction. Load stages and validates
the complete record before mutating the shell, rebuilds the query start from the
restored foot position/current medium, clears transient action state, silently
rebuilds `LastCommittedCell` from the already restored world, and reacquires
guidance only on a later simulation frame. `LastCommittedCell` and its callback
are runtime-only and have no wire field.

Restore GridForge grids, maps, area policies, and overlays before loading or
resuming a guided Navigator. See [Serialization](Serialization.md).

## Related guides

- [NavSteering](NavSteering.md)
- [Pathing](Pathing.md)
- [Path guides](PathGuides.md)
- [Transitions](Transitions.md)
