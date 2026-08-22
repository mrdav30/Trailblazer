# Trailblazer Overview

Trailblazer turns GridForge world geometry plus host-authored navigation
semantics into deterministic A* and flow-field guidance. Rendering, physics,
animation, terrain classification, and action execution remain host owned.

## Runtime Ownership

Each `TrailblazerWorldContext` owns one active `GridWorld` binding, three public
context-local services, and its deterministic clock/lifecycle state:

- `Pathing` admits maps, removals, overlays, and area policies and maintains the
  immutable navigation graph;
- `Guides` acquires A* and Flow leases;
- `Heightmaps` stores deterministic optional ground-height layers;
- Navigators bind directly to the context while their registration coordination
  remains internal.

There is no ambient pathing manager. Hosts create or attach a context, publish
world navigation truth into that context, simulate it once per fixed frame, and
dispose it when every lease and controller is released.

## Navigation Truth

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

## Medium-State Graph

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

## Query And Cost Model

`PathQuery` is the only public A*/Flow request. It contains start/end endpoints,
agent profile, area-policy key, exact start medium, target-media mask, algorithm,
finite work budget, transition permission, and optional Flow settings.

Both algorithms evaluate the same canonical edges and fixed-point costs. A
transition contributes certified source approach, authored `ActionCost`,
certified destination exit, and destination enter costs. The semantic gap
between its two action positions has no movement-distance charge.

## Guides And Actions

A* returns `NavigationGuideLease` with `NavigationGuideStep` values. Flow returns
`NavigationFlowFieldLease` with `NavigationFlowSample` values. Each value reports
its exact medium. A transition value also carries a lease-specific
`NavigationTransitionInstruction`.

The host approaches the source position, performs the action, and calls
`CompletePendingTransition(...)` with that exact instruction. Transient capacity
pressure preserves the held action; affected publication makes it stale. No
guide performs gameplay or physics implicitly.

## Publication Lifecycle

Hosts admit four operation families through `context.Pathing`:

- `NavigationMapCommitOperation`;
- `NavigationMapRemoveOperation`;
- `NavigationOverlayCommitOperation`;
- `NavigationAreaPolicyCommitOperation`.

Admission only enters bounded storage. `context.Simulate()` advances graph
maintenance and publishes eligible work atomically at a fixed-step boundary.
Receipts expose the terminal result and actual publication frame.

Overlay transactions change addressed cells, connections, and explicit
transitions. Map replacement changes immutable bake/default/rule truth.
GridForge committed changes enter the same graph maintenance authority.

## Navigator Lifecycle

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

## Serialization Lifecycle

Trailblazer uses explicit Chronicler records and populate-existing-instance
loads. Hosts restore GridForge grids, maps, area policies, and overlays before
populating guided Navigators. Navigator records keep durable intent but not
graph payloads, guide cursors, dependencies, pending instructions, or committed
cell metadata. Fresh guidance is acquired only on a later simulation frame.

## Continue Reading

- [Navigation maps](NavigationMaps.md)
- [Map authoring](MapAuthoring.md)
- [Map publication](MapPublication.md)
- [Pathing](Pathing.md)
- [Volume traversal](VolumeTraversal.md)
- [Transitions](Transitions.md)
- [Path guides](PathGuides.md)
- [Navigator](Navigator.md)
- [Serialization](Serialization.md)
- [Migration](Migration.md)
