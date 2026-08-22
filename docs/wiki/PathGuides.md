# Path Guides Reference

Trailblazer exposes two lease types over immutable graph payloads:

- `NavigationGuideLease` for ordered A* movement/action steps;
- `NavigationFlowFieldLease` for destination-centric Flow sampling.

The cached payload is immutable. Cursor, current medium, and pending-action state
belong to one lease acquisition and are never shared between consumers.

## 1. Acquisition

This C# fragment assumes algorithm-matching A* and Flow queries:

~~~csharp
NavigationGuideStatus aStarStatus = context.Guides.RequestGuide(
    aStarQuery,
    out NavigationGuideLease? aStar);

NavigationGuideStatus flowStatus = context.Guides.RequestFlowField(
    flowQuery,
    out NavigationFlowFieldLease? flow);
~~~

The query algorithm must match the method. On success the nullable result has a
value and must be disposed. On failure no lease is returned.

## 2. A* Steps

`NavigationGuideLease` exposes:

- dependency-validated `Status`;
- `CurrentStepIndex`;
- immutable `StepCount` and `TotalCost`;
- `TryGetCurrentStep(out NavigationGuideStep)`;
- `TryAdvanceStep()`;
- `CompletePendingTransition(...)`.

`NavigationGuideStep` reports exact address, position, and medium. Ordinary
steps have `HasTransition == false`. Move to the reported position and then
advance, except at `CurrentStepIndex == StepCount - 1`: that final ordinary step
is the arrival target and remains current, so the consumer finishes instead of
calling `TryAdvanceStep()` in a loop.

An action step has `HasTransition == true`. Its position is the source action
position. `TryAdvanceStep()` cannot cross it; call
`CompletePendingTransition(step.Transition)` only after the host performs that
action. If completion returns transient `CapacityExceeded`, retry completion on
a later frame without performing the action again.

The following complete helper demonstrates the cursor boundary. The caller
keeps `actionExecuted` with its host action state between fixed frames:

~~~csharp
using System;
using Trailblazer.Pathing;

public static class GuideConsumer
{
    public static NavigationGuideStatus ConsumeCurrentAStarStep(
        NavigationGuideLease guide,
        ref bool actionExecuted,
        Action<NavigationGuideStep> moveToward,
        Action<NavigationTransitionInstruction> executeAction,
        Action arrive)
    {
        NavigationGuideStatus status =
            guide.TryGetCurrentStep(out NavigationGuideStep step);
        if (status != NavigationGuideStatus.Success)
            return status;

        if (step.HasTransition)
        {
            if (!actionExecuted)
            {
                executeAction(step.Transition);
                actionExecuted = true;
            }

            status = guide.CompletePendingTransition(step.Transition);
            if (status == NavigationGuideStatus.Success)
                actionExecuted = false;
            return status;
        }

        moveToward(step);
        if (guide.CurrentStepIndex == guide.StepCount - 1)
        {
            arrive();
            return NavigationGuideStatus.Success;
        }

        return guide.TryAdvanceStep();
    }
}
~~~

Treat `Stale` as a reacquisition boundary. Treat `CapacityExceeded` as a retry
boundary. Other non-success statuses follow the host's failure policy.

## 3. Flow Samples

`NavigationFlowFieldLease.TrySample(...)` takes the agent's actual foot
position plus a finite `GuideSampleWorkBudget`. This C# fragment assumes an
active Flow lease:

~~~csharp
NavigationGuideStatus status = field.TrySample(
    actualFoot,
    sampleBudget,
    out NavigationFlowSample sample);
~~~

An ordinary sample reports heading, target, and exact medium. A transition
sample reports zero heading, the source action target, and the exact
instruction. Completion uses
`field.CompletePendingTransition(sample.Transition)`.

Flow owns a guide-local source/medium cursor. It can rejoin ordinary selected
edges within its budget, but it never skips or samples through an action
barrier.

## 4. Transition Barrier

Both lease types follow the same contract:

1. approach the instruction's exact source position in its source medium;
2. hold the lease at that action;
3. execute host-owned animation/physics/gameplay;
4. complete the exact instruction;
5. update the host's position/medium to the completed destination;
6. continue the same lease.

The completion stamp is private. Stable public action identity is still exposed
through kind, owner/ID, type, addresses, media, positions, and locomotion hints.

## 5. Status And Retry

`NavigationGuideStatus` distinguishes semantic failure, transient bounded work,
and stale proof. In particular:

- `NoPath` is a completed graph proof;
- `BudgetExceeded` means the supplied work limit was insufficient;
- `CapacityExceeded` may be transient, such as graph-lease pressure;
- `Stale` means a relevant publication invalidated the payload/instruction.

A controller should retain a held action through transient capacity pressure but
release/reacquire on stale publication. A direct consumer decides its own retry
policy.

## 6. Lifetime

Leases hold payload-cache ownership. Always use `using` or call `Dispose()`.
Disposal is generation checked, so a copied stale struct cannot release a later
acquisition.

Do not cache `NavigationGuideStep`, `NavigationFlowSample`, or a transition
instruction as a replacement for the lease. Their completion authority is only
valid while the producing lease/acquisition remains current.

## 7. Dependency Invalidation

Payloads record only the map pages, structural components, rules, area policy,
and raw-world evidence they actually used, including blocked alternatives.
Unrelated publication can preserve a payload; a relevant cell, ladder, seam, or
rule change stales it.

Status and sampling revalidate dependencies before exposing guidance and again
around resumable work where publication could race.

## 8. Internal Direct-Path Simplification

Trailblazer uses one internal navigation-ray work path for endpoint proof, A*
route simplification, Flow rejoin, and controller direct heading. It is not a
public guide type: its correctness depends on graph/store leases, exact policy,
medium, bounded workspaces/meters, dependency ownership, endpoint allowances,
and consumer-specific chain constraints. Public callers should use a query or
guide so those invariants remain hidden.

## 9. Related References

- [Pathing](Pathing.md)
- [Transitions](Transitions.md)
- [NavSteering](NavSteering.md)
- [Navigator](Navigator.md)
