# Trailblazer Overview

Trailblazer turns GridForge world geometry plus host-authored navigation
semantics into deterministic A* and flow-field guidance. It remains independent
of rendering, physics, animation, and game-engine APIs.

## 1. Runtime Model

Each `TrailblazerWorldContext` owns:

- an active `GridWorld`;
- a pathing service that publishes maps, overlays, and area policies;
- an immutable medium-state graph runtime;
- bounded A* and Flow admission/caches;
- a guide service;
- navigation, heightmap, and serialization-facing runtime state.

The host advances the context once per fixed simulation frame. Accepted
operations publish at deterministic frame boundaries and expose terminal
receipts.

## 2. Navigation Truth

One `NavigationMap` describes one normalized GridForge grid under a stable map
ID. Its immutable bake may contain:

- an optional complete default cell;
- explicit cell entries;
- physical connections;
- explicit transition definitions;
- bounded procedural transition rules.

The effective cell at a physically present in-bounds address is selected by:

1. overlay cell;
2. explicit baked cell;
3. map default cell;
4. no navigation cell.

The winner is a complete `NavigationCell`. Its media, capability requirement,
area, enter cost, clearance, and flags do not merge with lower layers.
`Suppress` is a tombstone; `RevertToBake` falls back through the bake and then
the default.

GridForge physical presence remains independent. A semantic cell for an absent
sparse address is dormant until that address physically exists.

## 3. Medium-State Search

The graph retains one physical node per effective addressed cell. Search state
adds one exact medium:

```text
(NavigationCellAddress, TraversalMedium)
```

`NavigationCell.Media` is a flag set, so one physical cell can support multiple
states. Native movement and certified shortcuts retain medium. Semantic
transitions may retain medium (for example, Jump or Climb) or change it (for
example, Liquid to Gas takeoff).

Solid movement uses surface anchors, step/drop limits, portals, and explicit
physical connections. Gas and Liquid movement is free-form three-dimensional
translation using centered body anchors, GridForge topology directions, portals,
and exact swept-prism-union coverage. Volume is state of matter, not terrain.

## 4. One Query

`PathQuery` is the only public A*/Flow request. It contains:

- start and destination `NavigationEndpoint` values;
- one `NavigationAgentProfile`;
- one `NavigationAreaPolicyKey`;
- `TraversalIntent` with exact `StartMedium` and nonempty `TargetMedia`;
- `PathAlgorithm.AStar` or `PathAlgorithm.FlowField`;
- a finite `NavigationWorkBudget`;
- `AllowTransitions`;
- optional Flow-specific integration cost.

The start medium must be exactly Solid, Gas, or Liquid. It is never inferred.
Target media must be a subset of the agent's allowed media.

When transitions are disabled, every semantic action is excluded, including a
same-medium Jump or Climb. A target mask that excludes the start medium then
produces `NoPath`.

## 5. Search And Costs

A* stores one immutable route payload and exposes `NavigationGuideLease`. Flow
stores a destination-centric field and exposes `NavigationFlowFieldLease`.
Both algorithms consume the same graph edges, exact fixed-point costs,
dependencies, transition identities, and publication stamps.

Movement costs use exact world-space fixed-point geometry with conservative
rounding. A transition adds:

- movement from the source anchor to its source action position;
- authored `ActionCost`;
- movement from its destination action position to the destination anchor;
- destination cell and area enter costs once.

There is no distance charge between action positions. This lets a teleporter
span a large gap without pretending the host walked that gap.

## 6. Guides And Actions

An A* guide reports `NavigationGuideStep`; a Flow guide reports
`NavigationFlowSample`. Each reports its exact medium. If `HasTransition` is
false, it is ordinary movement. If true, the result includes a
`NavigationTransitionInstruction` with stable identity, source/destination
addresses, media, resolved positions, type, and locomotion hints.

The instruction is lease-specific. The host must execute the action and pass
that exact value to `CompletePendingTransition(...)`. A copied instruction from
another acquisition, a stale publication, or a second completion does not
advance the lease.

## 7. Dynamic World Changes

Maps and overlays publish through `TrailblazerWorldContext.Pathing`:

- `NavigationMapCommitOperation` installs or replaces a prepared bake;
- `NavigationOverlayCommitOperation` applies one atomic transaction;
- `NavigationAreaPolicyCommitOperation` publishes a policy revision;
- `NavigationMapRemoveOperation` removes a map.

Overlay deltas can set/suppress/revert cells, physical connections, and explicit
transitions. Publication rebuilds only affected graph facts and stales cached
proofs through ordinary dependencies.

Host-owned terrain or matter predicates are not called during search. The host
materializes their current results into `NavigationCell` defaults, explicit
entries, or addressed overlay operations before publication. Terrain remains an
optional authoring input, not the definition of Gas or Liquid.

## 8. Navigator

`Navigator` owns an exact `NavigationAgentProfile`, current `TrekCondition`,
and one surfaced pending transition. `ApplyGuidedTrekRequest(...)` requires the
query's start medium to match the current host-restored frame medium.

During simulation, ordinary guidance becomes a movement request. A transition
produces zero movement guidance, applies authored locomotion hints, and remains
in `PendingTransition` until the host calls
`CompletePendingTransition(...)`. The host then updates its physical state and
continues the same session.

## 9. Serialization

Trailblazer uses explicit Chronicler records and populate-existing-instance
loads. `PathQueryRecord` round-trips complete query intent. Navigator sessions
store durable destination/query intent but not guide payloads, cursors, or a
pending action. Load validates the complete staged record before mutating the
existing shell, rebuilds start position/medium from the restored host state, and
requests fresh guidance on a later frame.

## 10. Ownership Boundaries

- FixedMathSharp owns deterministic fixed-point mathematics.
- GridForge owns grid topology and issued geometry.
- Trailblazer owns navigation semantics, search, budgets, dependencies, and
  guide orchestration.
- The host owns terrain classification and transition execution.

## 11. Next Reading

- [Navigation maps](NavigationCharts.md)
- [Map authoring](ChartAuthoring.md)
- [Pathing](Pathing.md)
- [Volume traversal](VolumeTraversal.md)
- [Transitions](Transitions.md)
- [Path guides](PathGuides.md)
- [Navigator](Navigator.md)
- [Serialization](Serialization.md)
