# Navigator

`Navigator` is the simulation-facing coordinator for steering, turning, motor,
locomotion, traversal state, occupancy, and guided semantic actions. It remains
abstract so hosts can supply their own traversal-state checks and integrations.

## Top-down Navigator2D

`Navigator2D` follows an A* or Flow route for a circle on published top-down
layers at the same embedding. Derive a host shell and implement
`SubmitMotionTarget` and `ReadAcceptedMotion`. Construct it with the matching
host collider center, radius, speed, traversal medium, and a nonempty
world-unique actor ID. The center is native world X/Z and the host owns its
physical world Y. The map's selected layer determines navigation occupancy.

Call `ApplyGuidedTrekRequest` with the destination cell, area policy, algorithm,
and finite work budget. The route starts in the accepted center's cell. The
controller can approach the first canonical center from a clear off-axis
position. Each nonzero guided target requires a swept-circle proof from the
last accepted center. The budget applies separately to acquisition and each
movement proof. An explicit analog move replaces guidance and delegates motion
clearance to the host. Targets carry the current accepted rotation.

Immediate acquisition is the default. Set `ConfigureAcquisitionMode` to
`Scheduled` to use the context queue. A pending controller submits a held
target and still commits the accepted host pose each frame. If the accepted
center moves while queued, the controller checks the connector to the exact
first guide center on the queue's fair validation turn. It repeats that check
while the connector is active, including after a mode change.

Pass a movement group ID to select a formation cell when a compatible group
approaches the same destination. Each adjusted goal must pass clearance and
route acquisition. Groups use the same formation policy as spatial navigation
and keep separate membership by planar destination map. An invalid formation
route retries the common goal. `ConfigurePathRechecks` optionally looks one
ordinary leg ahead on a fixed frame phase from an already accepted center.
The lookahead keeps the selected edge and checks the actual sweep. A clipped
result on that sweep can leave the source waypoint behind. The next waypoint
still requires its own accepted arrival.

For each fixed frame, call `context.Simulate()`, prepare all controllers,
resolve host motion once, commit all controllers, then call
`context.LateSimulate()`. Return a `NavigationMotionResult2D` with the accepted
pose and `NotApplicable` support for the matching `NavigationMotionRequest2D`.
Prepare leaves pose, velocity, and occupancy
intact. Commit derives velocity from the accepted displacement and the
prepared timestep. It consumes the request before publishing cell changes.
A short or blocked result holds the current waypoint. Arrival requires
accepted motion at the exact final center on a current guide.

Opt in to authored same-medium actions with `allowTransitions`. Once the
controller accepts the action's source center, `PendingTransition` exposes
the exact lease stamp. Use `PrepareActionFrameMotion` with that stamp for
each host-controlled action frame. Complete with `CompletePendingTransition`
after the accepted result reaches the exact destination center. The accepted
landing selects the action's destination map, including at coincident layers.
A wrong landing leaves the action held. A stale, cancelled, or completed stamp
cannot submit another action frame or complete it again.

If a publication invalidates the guide between prepare and commit, the valid
host pose still commits and the guide is released. A temporarily unavailable
cell publication preserves the last semantic cell sample and releases any
occupancy ticket the accepted center has exited. A physical cell can keep its
occupancy ticket even when it has no authored navigation semantics. Queue
authoritative changes from host callbacks and cell notifications for the next
ordered command boundary. Dispose the controller to release guidance and occupancy.

## Side-view Navigator2D

For host-resolved side-view movement, bind a `SideView` map with a
`NavigationAgentProfile2D` and `NavigationMotorSettings2D`. The body is an
upright capsule in world X/Z. Radius is positive and axis length is the
non-negative distance between cap centers. The host keeps the physical collider
consistent with this geometry and resolves collisions, steps, and support.
Gravity acts along negative `Vector2d.Forward`. The constructor's explicit
traversal medium defaults to Solid and must be allowed by the profile.
Physical support loss does not change this navigation intent.

Use `NavigationInput2D` to supply horizontal analog movement, a jump input edge,
optional horizontal facing, and host jump permission. Null movement produces
zero voluntary movement without an active guide. Side-view logical facing is
Left or Right while physical rotation stays zero. Prepare keeps the last
accepted pose, velocity, facing, and support intact until commit. A walkable
support normal projects ordinary speed onto the surface tangent. The first jump
requires walkable support. Set `maxJumpCount` above one in `NavigationMotorSettings2D`
to allow additional jumps during accepted ascent. Each jump requires the Jump
capability, positive jump speed, and host permission. Airborne motion integrates
gravity with the fixed step. Accepted ceiling clipping limits subsequent ascent.
A fresh walkable landing resets fall speed and replenishes the jump allowance.

`ReadAcceptedMotion` receives a nullable request and returns the accepted pose
with a support status. Found carries a physical witness, unit normal,
non-negative friction, and optional carrier binding. The controller applies its
slope and upward-departure policy to decide `IsGrounded`. NoSupport is a complete
miss. Unavailable discards the previous sample while still committing motion.
Only Found has a support snapshot. Side-view results cannot use NotApplicable
or nonzero physical rotation. Invalid results reject before pose or occupancy
publication and consume the prepared request.

Call `RefreshSupport()` between frames to seed support after constructing the
host shell. It invokes `ReadAcceptedMotion(null)` and requires the exact current
accepted pose. It submits no target and publishes no movement or occupancy.
Construction invokes no host hooks.

A carrier binding identifies a positive host-stable ID, its positive
registration lifetime, the sampled carrier pose, and the witness in carrier-local
coordinates. Override `TryReadCarrierPose` to resolve that exact registration.
Carry is the difference between transforms of the same local anchor. Equal poses
produce exact zero carry, including on rotated platforms. Failed lookup supplies
no carry. Every accepted commit replaces the previous support sample, so fresh
reattachment starts from its newly sampled carrier pose. Observable velocity
includes accepted carry while the motor keeps carry out of its next integration.

### Following a side-view guide

Call `ApplySideViewGuidedTrekRequest` with the source and destination support
addresses, area policy, search algorithm, and work budget. The accepted
capsule center must initially equal the canonical source anchor and have walkable host
support. The support address is independent of the physical cell occupied by
the center. Use `RefreshSupport()` to seed the host sample between frames.

Immediate acquisition is the default. In Scheduled mode, waiting frames
still run the free motor. If the accepted center moves while waiting, the
controller can adopt the result while grounded and return to the exact first
anchor. The moved origin must be on the finite authored support of that
anchor. Each return target waits for a scheduler validation turn and requires
a fresh capsule sweep to the anchor, plus a sweep to the actual target if
rounding takes it off that line. Both sweeps share the movement work budget.
The guide advances only after the host accepts the exact anchor with live
walkable support. An explicit analog input or an admitted manual jump cancels
the guide. Formation and lookahead apply to top-down guidance.

Guided motion follows the retained capsule sweep one stage at a time, at the
configured move speed. A step-up lifts before crossing. A drop crosses before
descending. The host resolves each target and reports the accepted pose and
support. A clipped result on the prepared segment can continue that stage.
Progress through a bend requires its exact accepted center. A grounded valley
bend and each support anchor also require fresh walkable support. An
off-segment result or missing landing support releases guidance while committing the accepted
pose. A moving carrier likewise releases the static route and applies its
sampled carry through the free motor.

Exact subsegments inherit the retained sweep. Fixed-point slope substeps
outside that line are rounded from the canonical stage within the move-speed
cap. They stay on its open side within two raw units along the minor axis
and require a new capsule sweep under the movement work budget. Each end of
that sweep must contact a footprint owned by one of the route's two support
cells. A rounded step that fails the proof releases guidance and reports the
proof status.

A graph publication or raw world change invalidates the guide before any
cursor advance. Ordinary reacquisition starts at an accepted canonical
support anchor. After population or posture replacement, a fresh support
witness can also identify an anchor for a bounded return connector.

### Replacing a planar profile

Call `Navigator2D.TryReplaceNavigationProfile` between frames, after committing
any prepared target. Side-view replacement preserves the nominal foot using
the radius plus the half-axis rounded down to a raw unit. Top-down replacement
preserves the center. If the center belongs to authored space, including a
closed cell seam, the candidate envelope must fit the published coverage and
applicable support limits. A tangent support from another map must belong to
the traced lattice. The host also checks the complete physical shape before
accepting the change.

Override `TryCommitPhysicalProfileReplacement` to commit the host shape and
center, then invoke the supplied publisher once before notifying host listeners.
On a successful replacement, the controller keeps its destination and retires
the old proofs. Call `RefreshSupport()` at the new side-view pose before the
next prepared frame. A notification failure surfaces after the accepted change.
A recovering side-view trek waits to reacquire if support is temporarily lost
while a scheduled result is pending.

Pass `allowTransitions: true` to use authored Solid-to-Solid actions. The
controller holds at the accepted source anchor and exposes `PendingTransition`.
For each action frame, pass that instruction and the host's upright target to
`PrepareActionFrameMotion`, then resolve and commit through the usual barrier.
The host owns the trajectory. Call `CompletePendingTransition` after the exact
destination center is accepted with fresh walkable support. A displaced or
unsupported landing returns `InvalidEnd` and the host can continue the held
action. A cancelled, stale, or consumed token cannot submit another action.

## Setup

A Navigator binds to one `TrailblazerWorldContext` and requires one exact
`NavigationAgentProfile`. This C# fragment assumes a concrete Navigator shell,
context, position, and profile:

```csharp
navigator.Setup(context, startRootPosition, profile);
navigator.Initialize(new TrekCondition
{
    Medium = TraversalMedium.Solid
});
```

The profile's `KinematicBodyShape` is authoritative. `FootPosition` is derived
from root position and `RootToFootOffsetY`; guided endpoints use foot space. The
profile remains an immutable value. Replace the complete profile through the
Navigator rather than mutating body size, turning radius, or traversal
capabilities independently.

`Reset()` releases guidance, pending actions, movement-group ownership, and
occupancy before the object can be rebound/reinitialized.

## Runtime Profile Replacement

Use `TryReplaceNavigationProfile(...)` for an atomic posture or capability
change such as standing to crouching, compact-ball presentation, or losing
flight. Call it between fixed frames: after committed motion and before the next
`Simulate()`. This fragment assumes `crouchingProfile` is a complete valid
profile and the host has not opened the next frame:

```csharp
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
```

An accepted change preserves the exact foot point, stable Navigator identity,
current physical medium, controller instances, unrelated locomotion state, and
durable destination. Trailblazer updates the root offset, previous root, turning
radius, occupancy, movement-group radius, affected capability state, and active
query profile as one operation. Existing A* or Flow guidance is released and
reacquired from the accepted foot and current medium on demand.

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
replacement is fully committed. Host callback exceptions propagate; they do not
roll back the profile or physical body, and callers must not interpret one as a
rejected change or retry the same replacement. Keep simulation callbacks
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
allowing exact support contact. A presentation change does not add rolling
physics, animation, or another navigation mode.

## Guided Travel

This C# fragment assumes a query whose start/profile/medium match the current
host-restored Navigator state:

```csharp
navigator.ApplyGuidedTrekRequest(
    query,
    rate: TrekRate.Moderate);
```

The query must:

- use the same exact `NavigationAgentProfile`;
- start at the current derived foot position;
- use the host-restored current `TrekCondition.Medium`;
- reference published maps and an exact area-policy revision.

Navigator does not infer or prioritize another start medium. Construct the query
for the physical state the host has actually committed.

`NavigationAgentProfile.ArrivalRadius` is the inclusive final-destination
radius. `NavSteering.WaypointTolerance` is a separate non-negative world-unit
tolerance for ordinary intermediate guide steps. Intermediate steps advance when
they enter that tolerance, or when a heading reversal proves the body passed the
step within the arrival radius. Neither rule can cross a pending transition
action.

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
committed position change divided by the context's fixed `DeltaTime`. `Speed` is
that velocity's magnitude, and `Acceleration` is the change from the previous
velocity divided by the same timestep. These describe the resulting controller
motion, not the requested steering heading or desired speed.

`Motor.Handler.Move.FrameVelocity` serves a different purpose: it is the motor's
working locomotion velocity. Finalization starts it from the accepted
displacement, then can adjust it for platform velocity transfer or a ceiling
hit. It is therefore not an interchangeable alias for `Navigator.Velocity`.

Measuring velocity this way is compatible with lockstep when every peer uses the
same authoritative positions, fixed timestep, and deterministic collision
results. It does not make a nondeterministic physics engine deterministic. Hosts
must report accepted simulation motion rather than an interpolated render
transform or a desired position that collisions prevented.

See [motion quantities and units](NavMotor.md#motion-quantities-and-units)
before adapting motor outputs to another physics model, and
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
requested pose reached its source. Arrival tolerance does not skip this
boundary.

`PendingTransition` is the single surfaced action owned by Navigator. While it
has a value:

- movement direction is zero;
- authored locomotion hints are applied to the frame request;
- ordinary guide sampling is blocked;
- the producing lease remains at the source action.

The following controller-loop fragment names host-owned action/state functions
as placeholders:

```csharp
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
```

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
- `PreserveClimbAfterCompletion` keeps climb intent after successful completion.

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
and any damage or presentation response; see
[NavMotor](NavMotor.md#fall-preserving-controlled-descent) for the exact
fixed-frame admission and velocity rules.

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

When the accepted foot is attached to the current platform pose, an unchanged
platform adds zero displacement to that foot. Trailblazer keeps the exact
accepted point so a fixed-point transform round trip cannot introduce motion.

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
but not a guide payload, cursor, or pending instruction. Load stages and
validates the complete record before mutating the shell, rebuilds the query
start from the restored foot position/current medium, clears transient action
state, silently rebuilds `LastCommittedCell` from the already restored world,
and reacquires guidance only on a later simulation frame. `LastCommittedCell`
and its callback are runtime-only and have no wire field.

Restore GridForge grids, maps, area policies, and overlays before loading or
resuming a guided Navigator. See [Serialization](Serialization.md).

## Related guides

- [NavSteering](NavSteering.md)
- [Pathing](Pathing.md)
- [Path guides](PathGuides.md)
- [Transitions](Transitions.md)
