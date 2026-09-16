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
- `PathRecheckCooldownFrames` and `PathRecheckPhase`;
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

A direct check can stop once a complete, current obstruction proof rules out
the route. It need not inspect the remaining segment after that proof, but a
clear result always requires the full route check. This does not skip freshness
checks on an active guide: keeping guidance still requires that guide to be
independently current.

At the start of travel, a blocked or non-neutral direct route falls back to
ordinary guide acquisition in the same call. During a periodic check, a blocked
direct route does not discard an independently valid guide. If the agent was
already travelling directly, it leaves direct mode and retries acquisition on
the next heading call instead.

Observed stale data is different: steering releases guidance, yields zero
heading, and prepares a retry rather than acquiring against that stale result
in the same call. Internal ray capacity and dependency details are not part of
the public steering contract.

`CanPathfind = false` disables graph-guide acquisition; it does not disable
direct line-of-sight checks. Direct-only steering still applies the query's
profile, policy, medium, budget, and current published graph. Periodic phasing
is not a way to bypass that validation.

## Periodic Line-Of-Sight Rechecks

`ConfigurePathRechecks(int intervalFrames, int? phase = null)` replaces the
complete periodic-recheck configuration atomically. `PathRecheckCooldownFrames`
and `PathRecheckPhase` are read-only observations. The default interval is 16
frames.

A null `phase` disables fixed-frame alignment and retains the established
completed-heading countdown: context-only frames and heading calls that return
before completion do not consume it. Nonpositive intervals also retain their
existing unphased behavior; they are supported compatibility values, not
recommended tuning.

A non-null `phase` enables alignment to the bound context's fixed-frame clock.
The interval must then be positive and the phase must satisfy
`0 <= phase < intervalFrames`; phase zero is valid. After a periodic check, the
next slot is always strictly 1 through `intervalFrames` context frames later.
If steering cannot update at a due slot, it refreshes once at the next eligible
update and realigns. There is no background work or catch-up loop, and repeated
heading calls in one context frame do not repeat periodic work.

This host fragment assumes an initialized Navigator with a configured, non-null
steering controller and a stable, nonnegative host ordinal:

~~~csharp
const int pathRecheckIntervalFrames = 16;
int pathRecheckPhase = stableHostOrdinal % pathRecheckIntervalFrames;

navigator.Steering!.ConfigurePathRechecks(
    pathRecheckIntervalFrames,
    pathRecheckPhase);
~~~

Derive the ordinal from stable host identity or canonical simulation order.
Do not use runtime hashes, randomness, object construction order, or assign a
new phase every frame. Keep the normal fixed-step order described in the
[Navigator guide](Navigator.md#fixed-step-lifecycle).

Initial admission and required recovery keep their existing boundary and do not
wait for a configured phase. Replacing the configuration retains work that is
already due; applying identical values is a no-op. Stopping or starting a
session discards phased timing debt while retaining the configuration. At 32
Hz, the default interval is one half-second between periodic opportunities; it
is the existing cadence, not a reduction from every-frame checks.

Phasing affects only periodic discovery that a direct route has opened or
closed. It does not defer guide freshness, held-action validation,
collision/occupancy work, or guide-acquisition scheduling. Stable phases can
spread ordinary periodic checks, but simultaneous request admission, resume,
publication, or profile changes can still create bursts. Balanced check counts
are not a universal frame-time ceiling.

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

Group and avoidance forces shape direction, not movement throttle. A nonzero
combined result is normalized back to a heading. If local shaping exactly
cancels the selected path direction, steering retains that path direction so a
stable formation cannot erase valid guidance indefinitely. Destination
deceleration remains responsible for reducing the requested movement magnitude;
the host still decides which motion is physically accepted.

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
