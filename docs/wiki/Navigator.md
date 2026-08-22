# Navigator Reference

`Navigator` is the simulation-facing coordinator for steering, turning, motor,
locomotion, traversal state, occupancy, and guided semantic actions. It remains
abstract so hosts can supply their own traversal-state checks and integrations.

## 1. Setup

A Navigator binds to one `TrailblazerWorldContext` and requires one exact
`NavigationAgentProfile`.

~~~csharp
navigator.Setup(context, startRootPosition, profile);
navigator.Initialize(new TrekCondition
{
    Medium = TraversalMedium.Solid
});
~~~

The profile's `KinematicBodyShape` is authoritative. `FootPosition` is derived
from root position and `RootToFootOffsetY`; guided endpoints use foot space.
The configured profile cannot be replaced after setup, and the radius used by
Navigator-owned turning cannot be independently mutated.

`Reset()` releases guidance, pending actions, movement-group ownership, and
occupancy before the object can be rebound/reinitialized.

## 2. Guided Travel

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

## 3. Fixed-Step Lifecycle

The normal order is:

1. update host contacts/current traversal state;
2. call `Simulate()`;
3. let the motor/locomotion stack accumulate deterministic deltas;
4. call `CommitFrameMotion()`;
5. publish the resulting state for the next lockstep frame.

`Simulate()` asks `NavSteering` for ordinary heading or one transition
instruction. `CommitFrameMotion()` applies accumulated position, velocity, and
rotation deltas; it does not execute semantic actions.

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

## 4. Pending Transition

`PendingTransition` is the single surfaced action owned by Navigator. While it
has a value:

- movement direction is zero;
- authored locomotion hints are applied to the frame request;
- ordinary guide sampling is blocked;
- the producing lease remains at the source action.

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

## 5. Failure Semantics

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

## 6. Locomotion Hints

`TraversalTransitionLocomotionHints` are authored on definitions/rules and are
copied into the instruction.

- `RequestClimb` sets climb intent while pending.
- `PreserveClimbAfterCompletion` keeps climb intent after successful
  completion.

Hints do not perform the action. They only bridge the semantic instruction into
the built-in locomotion request flags.

## 7. Manual Input

`ApplyInputTrekRequest(...)` cancels guided state and applies one ordinary frame
request. Manual movement can work without a navigation map. Guided map truth and
semantic actions are relevant only to `ApplyGuidedTrekRequest(...)`.

## 8. Traversal State

Hosts report state through methods such as:

- `SetGroundContact(...)`;
- `SetAirborne(...)`;
- `SetWaterContact(...)`;
- `SetTrekCondition(...)`;
- `ReplaceTrekCondition(...)`.

The concrete Navigator's `CheckTrekCondition()` remains the host integration
point. Keep physical state publication deterministic and consistent with any
completed instruction.

## 9. Heightmaps And Occupancy

Heightmap grounding is opt-in and affects kinematic Y projection, not graph
connectivity. Occupancy is registered against the bound GridWorld and rebuilt on
load/reset as documented by the host lifecycle.

Movement-group formation padding is the single world-unit
`TrailblazerWorldContextSettings.MovementGroupPadding` value. It is independent
of rectangular or hex grid metrics.

## 10. Serialization

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

## 11. Related References

- [NavSteering](NavSteering.md)
- [Pathing](Pathing.md)
- [Path guides](PathGuides.md)
- [Transitions](Transitions.md)
