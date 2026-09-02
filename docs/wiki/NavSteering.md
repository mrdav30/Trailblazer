# NavSteering

`NavSteering` is the Navigator-owned controller that converts one active
`PathQuery` into a deterministic heading or one pending semantic action. It
coordinates direct-path checks, guide acquisition, A*/Flow consumption,
repathing, stop/arrival logic, and movement-group shaping.

Hosts normally start guided travel through
`Navigator.ApplyGuidedTrekRequest(...)`; the steering query-start method is
internal so Navigator can enforce profile and medium ownership.

## Core State

Useful public observations include:

- `CurrentQuery`;
- `Destination`;
- `ShouldMove`;
- `HasLineOfSightPath`;
- `HasNavigationGuidance`;
- `IsAtDestination`;
- `IsStuck`;
- movement-group ID and tuning values;
- `Events`.

An active session owns at most one A* lease or one Flow lease. Direct travel can
remain guide-free while the exact same-medium route is certified.

## Per-Frame Heading

This C# fragment assumes a configured steering instance and `ISteer` host:

~~~csharp
Vector3d heading = steering.GetHeading(
    vessel,
    out NavigationTransitionInstruction? pending);
~~~

The method:

1. validates the active query/guidance and host position;
2. tries a cost-neutral direct route only when the target admits the same
   medium;
3. acquires or validates the configured A*/Flow guide;
4. approaches the selected movement/action target;
5. applies group/avoidance shaping to ordinary movement;
6. returns zero while a semantic action is pending.

The returned nullable instruction is transient output. `NavSteering` does not
own a second public pending-action queue. Navigator copies it into its sole
`PendingTransition` field while the lease remains the cursor/action authority.

## Direct Travel

Direct heading uses Trailblazer's internal graph navigation ray with the query's
exact start medium, profile, policy, and work budget. It is skipped when reaching
the target requires a medium change. A transition-required query therefore
cannot bypass its action through a geometric line-of-sight shortcut.

If direct proof is blocked, stale, or not semantically cost neutral, steering
acquires normal guidance. Internal ray capacity and dependency details are not
part of the public steering contract.

## A* Guidance

For A*, steering reads `NavigationGuideStep` values. It heads toward an ordinary
step's position and advances only after reaching it. For a transition step, it
first approaches the exact source action position; only at that position does it
surface the instruction and hold zero guidance.

This preserves explicit in-cell point overrides. A transition is not published
early merely because its source cell has been reached.

## Flow Guidance

For Flow, steering samples with a finite `GuideSampleWorkBudget`. Ordinary
samples provide selected-edge headings and exact medium. A transition sample
approaches the source action position using the same-medium ray/rejoin authority,
then holds the exact instruction.

Flow never rebinds across a selected transition or treats a zero heading action
as arrival.

## Completion And Cancellation

Completion delegates to the active producing lease. Only the exact current
instruction can advance it.

`StopMove()` and `Arrive()` release active guidance and notify the bound
Navigator so a surfaced pending action is cancelled too. A stale steering object
cannot cancel a later Navigator session because owner binding is identity
checked and removed during reset.

While a pending action is current:

- `CapacityExceeded` from transient graph-lease pressure keeps it held for
  retry;
- `Stale` cancels the old guidance so ordinary repath logic can run;
- mismatch or duplicate completion does not move the cursor.

## Repath And Publication

Steering does not mutate the caller's original intent arbitrarily. An ordinary
repath updates the session start position while retaining the query's existing
`StartMedium`. Only successful completion of the exact pending instruction
changes the session start medium to its destination medium. The host must keep
its physical `TrekCondition` synchronized with that completed action.

Map, overlay, policy, seam, or GridForge changes can stale the active proof.
Relevant changes reacquire; unrelated changes can reuse the cached payload.

## Movement Groups

Movement groups shape ordinary destinations/headings after path selection. They
do not modify medium-state graph connectivity and do not merge semantic action
ownership. `PrewarmMovementGroup(...)` can restore coordinator membership after
loading; otherwise it rebuilds lazily.

## Common Mistakes

- Starting a guided query whose profile differs from the Navigator profile.
- Supplying a start medium that differs from the host's current frame medium.
- Treating zero heading as arrival without checking for a pending transition.
- Calling completion with a reconstructed instruction instead of the exact
  surfaced value.
- Keeping a stale lease/action after an affected publication.
- Forgetting that `StopMove()` is cancellation, while `Arrive()` also emits
  arrival semantics.

## Related guides

- [Pathing](Pathing.md)
- [Path guides](PathGuides.md)
- [Transitions](Transitions.md)
- [Navigator](Navigator.md)
- [Serialization](Serialization.md)
