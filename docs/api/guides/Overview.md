# Technical Overview

Trailblazer turns GridForge world geometry plus host-authored navigation
semantics into deterministic A* and flow-field guidance. Rendering, physics,
animation, terrain classification, and action execution remain host owned.

The short version is: GridForge says what physically exists, the host says what
that space means, and Trailblazer turns those two truths into guidance that can
be replayed exactly.

## One world, one context

Each `TrailblazerWorldContext` owns one active `GridWorld` binding, three public
context-local services, and its deterministic clock/lifecycle state:

- `Pathing` admits maps, removals, overlays, and area policies and maintains the
  immutable navigation graph;
- `Guides` acquires A* and Flow leases synchronously or through a bounded,
  deterministic frame-spread queue;
- `Heightmaps` stores deterministic optional ground-height layers;
- Navigators bind directly to the context while their registration coordination
  remains internal.

There is no ambient pathing manager. Hosts create or attach a context, publish
world navigation truth into that context, simulate it once per fixed frame, and
dispose it when every lease and controller is released.

## Fixed frames and long-lived time

`FrameCount` is a nonnegative `long`. `ElapsedTime` is a
`Chronicler.Timing.ChronicleTimestamp`: whole seconds plus an exact binary
fraction, measured from this context's origin. Publication, steering and motor
transactions use that same timeline. `DeltaTime` and `InvDeltaTime` remain
`Fixed64` values for ordinary motion integration.

Subtract timestamps to get a `ChronicleDuration`; do not first convert both
absolute times to Fixed64. Only convert a duration through
`FixedMathSharp.Chronicler.FixedChronicleTime` when a bounded calculation needs
it. Its checked and `Try` conversions make out-of-range intervals explicit.

This C# fragment assumes an active context:

```csharp
Chronicler.Timing.ChronicleTimestamp started = context.ElapsedTime;
context.Simulate();
Chronicler.Timing.ChronicleDuration elapsed = context.ElapsedTime - started;
long completeSteps = context.GetFrameCountForDuration(Fixed64.One);
```

`GetFrameCountForDuration` counts complete steps using the **currently
represented step size**. It rejects negative durations; it is not a conversion
to a historical frame index. Changing the rate affects future steps without
recomputing elapsed history. The actual accumulated step, not
`FrameCount / FrameRate`, defines time.

The clock never wraps or saturates. At frame or timestamp exhaustion,
`Simulate()` throws before publication, scheduled work or hooks advance.
`Reset()` restarts this context at zero, preserves its configured rate, and
invalidates outstanding clock-lifetime state. Reset/reinitialize live
controllers as part of the host's reset transaction; do not commit old prepared
motion in the new lifetime, even if the numeric frame is the same. Reset is not
rollback.

## What gets published

One immutable `NavigationMap` binds a stable map ID to one normalized GridForge
configuration. Its bake may contain a complete default cell, explicit cells,
physical connections, explicit transition definitions, and bounded procedural
transition rules.

For a physically present address, the effective cell is selected in this order:

1. overlay cell;
2. explicit baked cell;
3. map default;
4. no navigation cell.

The winner is one complete `NavigationCell`; media, capabilities, area, cost,
clearance, and flags do not merge field by field. GridForge independently owns
physical presence, blockage, prisms, and contacts.

## How cells become search states

Search keys are:

```text
(NavigationCellAddress, TraversalMedium)
```

One physical cell may admit Solid, Gas, Liquid, or more than one of them.
Ordinary movement retains medium. An authored semantic action may retain it
(such as a Jump) or change it (such as Liquid-to-Gas Takeoff).

Solid movement uses foot/surface anchors, portal proof, and step/drop limits.
Gas and Liquid movement uses centered body anchors and exact GridForge swept
coverage. Rectangular and hex-prism topology both use GridForge-issued direction
sets and geometry rather than local neighbor formulas.

## One query and cost model

`PathQuery` describes spatial A*/Flow requests. It contains start/end endpoints,
agent profile, area-policy key, exact start medium, target-media mask,
algorithm, finite work budget, transition permission, and optional Flow
settings.

Both algorithms evaluate the same canonical edges and fixed-point costs. A
transition contributes certified source approach, authored `ActionCost`,
certified destination exit, and destination enter costs. The semantic gap
between its two action positions has no movement-distance charge.

## Guidance never performs gameplay

A* returns `NavigationGuideLease` with `NavigationGuideStep` values. Flow
returns `NavigationFlowFieldLease` with `NavigationFlowSample` values. Each
value reports its exact medium. A transition value also carries a lease-specific
`NavigationTransitionInstruction`.

Top-down requests use durable cell addresses and a circle radius. Immediate
and scheduled A* and Flow can return a retained `NavigationTopDownGuideLease`
with an acquisition-local cursor over the selected centers. Opted-in actions
carry a lease-specific `NavigationTransitionInstruction2D`.

`Navigator2D` also coordinates host-resolved manual and guided side-view
capsule motion.
Its native profile owns geometry and traversal permissions, motor settings own
speed and gravity, and the host owns physical support witnesses and accepted
motion. Side-view gravity and support act in the native X/Z plane. Both modes
share frame validation and accepted-cell occupancy publication. See
[Navigator.md](Navigator.md#side-view-navigator2d).

The host approaches the source position, performs the action, and calls
`CompletePendingTransition(...)` with that exact instruction. Transient capacity
pressure preserves the held action; affected publication makes it stale. No
guide performs gameplay or physics implicitly.

## Changes publish at fixed-step boundaries

Hosts admit four operation families through `context.Pathing`:

- `NavigationMapCommitOperation`;
- `NavigationMapRemoveOperation`;
- `NavigationOverlayCommitOperation`;
- `NavigationAreaPolicyCommitOperation`.

Admission only enters bounded storage. `context.Simulate()` advances graph
maintenance and publishes eligible work atomically at a fixed-step boundary.
Receipts expose the terminal result and actual publication frame.

After maintenance, the context advances scheduled guide acquisition within
shared frame allowances, then invokes host simulation hooks. Queue submission
does not run a search. See
[Pathing](Pathing.md#spreading-acquisition-across-fixed-frames) for request
ownership, cancellation, capacity and the distinction between deterministic work
slices and elapsed-time guarantees.

Overlay transactions change addressed cells, connections, and explicit
transitions. Map replacement changes immutable bake/default/rule truth.
GridForge committed changes enter the same graph maintenance authority.

## How Navigator fits in

`Navigator` owns one exact `NavigationAgentProfile`, current host-reported
`TrekCondition`, one guided session, and at most one surfaced pending action.
The normal frame is:

1. update host contacts and traversal state;
2. call `Navigator.Simulate()`;
3. let steering, turning, motor, and locomotion accumulate deterministic deltas;
4. call `Navigator.CommitFrameMotion()`;
5. consume `LastCommittedCell` or `CommittedCellChanged` after motion commits.

The committed-cell event is not a query callback. It reports the effective cell
entered after motion. A version-only graph refresh updates metadata without
repeating a stable entry event.

For Gravitas-backed physical movement, the optional `Trailblazer.Gravitas`
package supplies `GravitasNavigator2D` and `GravitasNavigator3D`. Both contexts share a host-owned world.
all Navigators prepare before one physics resolution, then all commit accepted
motion. Neither core acquires a dependency on the other. See the
[Gravitas integration guide](Gravitas.md) for the complete host contract.

## Save durable intent, rebuild transient guidance

Trailblazer uses explicit Chronicler records and populate-existing-instance
loads. Hosts restore GridForge grids, maps, area policies, and overlays before
populating guided Navigators. Navigator records keep durable intent but not
graph payloads, guide cursors, dependencies, pending instructions, or committed
cell metadata. Fresh guidance is acquired only on a later simulation frame.

## Continue Reading

- [Getting started](GettingStarted.md)
- [Navigation maps](NavigationMaps.md)
- [Map authoring](MapAuthoring.md)
- [Map publication](MapPublication.md)
- [Pathing](Pathing.md)
- [Volume traversal](VolumeTraversal.md)
- [Transitions](Transitions.md)
- [Path guides](PathGuides.md)
- [Navigator](Navigator.md)
- [Serialization](Serialization.md)
- [Troubleshooting](Troubleshooting.md)
- [Migration guide](https://github.com/mrdav30/Trailblazer/blob/main/docs/MIGRATION.md)
