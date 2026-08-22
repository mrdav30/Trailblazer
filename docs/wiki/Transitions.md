# Traversal Transitions Reference

Transitions are explicit semantic actions selected by the same medium-state graph
used for ordinary movement. They model actions such as climbing a ladder,
jumping a gap, taking off, landing, entering/leaving water, riding a lift, or
teleporting.

Nothing is inferred solely from touching two media. The environment must author
either an exact definition or a bounded rule.

## 1. Explicit Definitions

`TraversalTransitionDefinition` is one directed, source-map-owned action. It
contains:

- stable map-local ID and `TraversalTransitionType`;
- source local index and exact source medium;
- durable destination map/index and exact destination medium;
- optional source/destination point overrides;
- required capabilities;
- nonnegative `ActionCost`;
- authored `TraversalTransitionLocomotionHints`.

Definitions can retain medium or change it. A same-medium Jump or Climb is still
a semantic action and is excluded when a query disables transitions.

A valid definition may be dormant because an endpoint map, physical address, or
required medium is not currently active. Publication does not silently delete
it. When the endpoint later becomes compatible, ordinary graph composition can
activate it; an incompatible replacement rejects transactionally.

## 2. Procedural Rules

`TraversalTransitionRule` describes one reusable bounded action:

- `TraversalTransitionRuleScope.SameCell`; or
- `TraversalTransitionRuleScope.PositiveFaceContact`.

Rules include exact media, type, capability requirement, action cost, locomotion
hints, and a globally unique rule ID. They are stored once in the graph snapshot
and evaluated procedurally in canonical order. Trailblazer does not retain one
generated transition per matching cell.

Publication enforces the public `MaxTransitionRulesPerMap` and
`MaxTransitionRules` operation limits. Rule payload validation remains builder
owned; map/global retained-count admission remains publication owned.

Use a rule only when the action truly applies uniformly. Use explicit
definitions for object-specific anchors, unique costs, scripted devices, or
actions that exist only while one runtime object exists.

## 3. Cost And Action Positions

For an accepted transition, total route cost includes:

1. certified movement from the source medium anchor to the source action point;
2. authored `ActionCost`;
3. certified movement from the destination action point to its medium anchor;
4. destination cell and area enter costs once.

There is no straight-line cost between the two action points. A distant
`Custom` teleporter therefore costs its certified approach/exit legs plus its
authored action cost, not the world-space endpoint gap.

SameCell rules use the two resolved medium anchors. Positive-face rules use
GridForge's directed profile-resolved contact positions. Explicit overrides must
lie inside their declared endpoint prisms.

## 4. Identity And Ordering

Definitions and rules use tagged identities. A public instruction reports:

- `NavigationTransitionIdentityKind.Definition` or `Rule`;
- owner map ID for a definition;
- stable ID;
- exact resolved source/destination addresses and media.

The kind matters because a definition and rule may share the same text ID.
Definitions reject duplicates within their source owner. Rule IDs are globally
unique. Forward A* and reverse Flow recover the same canonical action identity
and ordinal.

## 5. Ladder: Dynamic Explicit Actions

A bidirectional ladder is two directed definitions. Dropping it into the world
publishes both in one overlay transaction. This C# fragment assumes the stable
map ID and endpoint indices are available:

~~~csharp
var down = new TraversalTransitionDefinition(
    "ladder-down",
    TraversalTransitionType.Climb,
    cliffIndex,
    TraversalMedium.Solid,
    new NavigationCellAddress(mapId, waterIndex),
    TraversalMedium.Liquid,
    TraversalCapability.Climb,
    locomotionHints: TraversalTransitionLocomotionHints.RequestClimb);

var up = new TraversalTransitionDefinition(
    "ladder-up",
    TraversalTransitionType.Climb,
    waterIndex,
    TraversalMedium.Liquid,
    new NavigationCellAddress(mapId, cliffIndex),
    TraversalMedium.Solid,
    TraversalCapability.Climb,
    locomotionHints: TraversalTransitionLocomotionHints.RequestClimb);

var delta = new NavigationMapOverlayDelta(
    mapId,
    transitions: new[]
    {
        TraversalTransitionOverlayOperation.Upsert(down),
        TraversalTransitionOverlayOperation.Upsert(up)
    });
~~~

Moving the ladder upserts the same IDs with new endpoints. Removing it uses
`Suppress(id)` or `RevertToBake(id)`. A held instruction from the old
publication becomes `Stale`; it never retargets to the moved ladder.

## 6. Duck: One Rule, Many Water Surfaces

One rule can allow a swimming/flying duck to take off at any qualifying
Liquid-to-Gas face. This C# fragment assumes the map binding and cells are
available:

~~~csharp
var takeoff = new TraversalTransitionRule(
    "duck-takeoff",
    TraversalTransitionType.Takeoff,
    TraversalMedium.Liquid,
    TraversalMedium.Gas,
    TraversalTransitionRuleScope.PositiveFaceContact,
    TraversalCapability.Swim | TraversalCapability.Fly,
    actionCost: Fixed64.Zero,
    locomotionHints: TraversalTransitionLocomotionHints.None);

NavigationMap map = new NavigationMapBuilder(mapId, binding)
    .SetDefaultCell(gasCell)
    .AddCell(firstWater, liquidCell)
    .AddCell(secondWater, liquidCell)
    .AddTransitionRule(takeoff)
    .Build();
~~~

The same rule serves both contacts for A* and Flow. An otherwise identical agent
without Fly capability gets `NoPath`. To replace a rule, publish a higher map
bake with `PreserveAndRevalidate`; rules do not have a separate overlay API or
staleness clock.

## 7. Guide Completion

An A* `NavigationGuideStep` or Flow `NavigationFlowSample` with
`HasTransition == true` includes the exact instruction. The lease remains at
the source action until completion. This fragment assumes `guide` and `step`
come from the same active lease and that the host action has already executed:

~~~csharp
NavigationGuideStatus status =
    guide.CompletePendingTransition(step.Transition);
~~~

Only the exact active acquisition/ordinal is accepted. Mismatch, duplicate
completion, stale publication, or another lease's instruction leaves the cursor
unchanged.

For `Navigator`, read `PendingTransition`, perform the host action, call
`CompletePendingTransition(...)`, then update the host's physical
`TrekCondition`/position to the destination state before continuing.

## 8. Locomotion Hints

Hints are authored data, never inferred globally from transition type:

- `RequestClimb` requests climb while the action is pending;
- `PreserveClimbAfterCompletion` preserves climb intent after success.

The built-in shoreline importer authors both hints only for a Liquid SwimExit
whose Solid destination is a climb surface that also supports Liquid. Ordinary
shore entry/exit remains unhinted.

## 9. Related References

- [Map authoring](MapAuthoring.md)
- [Map publication](MapPublication.md)
- [Path guides](PathGuides.md)
- [Navigator](Navigator.md)
