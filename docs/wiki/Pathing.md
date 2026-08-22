# Pathing Reference

Trailblazer uses one immutable `PathQuery` for both A* and flow-field search.
The query is complete caller intent: no algorithm branch infers a medium,
geometry, policy, or unbounded work limit later.

## 1. Query Contract

This C# fragment assumes the endpoints, profile, policy key, and finite budget
have already been created:

```csharp
var query = new PathQuery(
    new NavigationEndpoint(startFoot, mapId),
    new NavigationEndpoint(destinationFoot, mapId),
    profile,
    areaPolicyKey,
    new TraversalIntent(
        TraversalMedium.Liquid,
        TraversalMedia.Liquid | TraversalMedia.Gas),
    PathAlgorithm.AStar,
    budget,
    allowTransitions: true);
```

The fields are:

| Field | Meaning |
| --- | --- |
| `Start`, `End` | World-space foot endpoints, optional map filters, and resolution policy |
| `Agent` | Body shape, step/drop limits, arrival radius, allowed media, and capabilities |
| `AreaPolicy` | Exact published policy ID and revision |
| `Traversal.StartMedium` | Exact current Solid, Gas, or Liquid state |
| `Traversal.TargetMedia` | Nonempty requested target-medium mask |
| `Algorithm` | A* or Flow |
| `Budget` | Finite deterministic work limits |
| `AllowTransitions` | Whether semantic actions may be considered |
| `FlowField` | Flow-only integration options |

`TargetMedia` must be a subset of `Agent.AllowedMedia`. `Unknown` is a
runtime sentinel and is invalid query intent.

## 2. Endpoints

`NavigationEndpoint` supports:

- `EndpointResolutionPolicy.Strict` for the requested point only;
- `EndpointResolutionPolicy.NearestNavigable` with an explicit maximum
  resolution distance;
- an optional stable map ID filter.

Endpoint resolution evaluates physical presence, effective cell data, exact
medium, body clearance, capabilities, area policy, and bounded ray evidence.
Resolution does not make an impassable endpoint legal.

When transitions are enabled, the winning target address may admit every
qualifying medium in `TargetMedia`. When disabled, only the start medium is
eligible; if the target mask excludes it, the valid query returns `NoPath`.

## 3. Agent Profile

`NavigationAgentProfile` is immutable and includes:

- `KinematicBodyShape` radius, body height, and root-to-foot offset;
- maximum surface step up and drop down;
- arrival radius;
- allowed media;
- traversal capabilities such as Jump, Climb, Swim, Fly, and Teleport.

Grid metrics never substitute for agent geometry. A map cell or transition can
require capabilities, but possession of a capability does not invent an action;
the map must still author the corresponding edge or rule.

## 4. Work Budget

`NavigationWorkBudget` bounds lookup probes, endpoint candidates, expanded
nodes, evaluated edges, connection legs, transition candidates and pairs, trace
intervals, covered-volume intervals, and simplification rays. Zero disables a
category.

Budget exhaustion returns `BudgetExceeded`; fixed retained-capacity exhaustion
returns `CapacityExceeded`. Neither silently expands storage or falls back to
an unbounded algorithm.

## 5. A* Versus Flow

Choose A* when one agent needs an ordered route. This C# fragment assumes a
valid A* query:

```csharp
NavigationGuideStatus status = context.Guides.RequestGuide(
    query,
    out NavigationGuideLease? guide);
```

Choose Flow when many agents share one destination and should sample selected
edges from their own current positions. This fragment assumes a valid Flow
query:

```csharp
NavigationGuideStatus status = context.Guides.RequestFlowField(
    query,
    out NavigationFlowFieldLease? field);
```

The query's `Algorithm` must match the request method. Both algorithms use the
same medium-state graph, edge evaluator, costs, dependencies, and transition
completion semantics.

## 6. Traversal Families Inside The Graph

- Solid native movement uses topology contacts plus surface step/drop rules.
- Solid explicit connections can carry certified witnesses or guide points.
- Gas/Liquid native face movement uses volume-centered body anchors.
- Gas/Liquid shortcuts use GridForge's complete topology direction sets and
  swept-prism-union validation.
- Explicit definitions and procedural rules create semantic action edges.

The public query does not select one of these families. The exact start medium,
target mask, agent, policy, and map publication determine which edges are legal.

## 7. Status And Staleness

Guide acquisition can report:

- `Success`;
- `Unsupported` for an algorithm/method mismatch;
- `NoMap`, `InvalidProfile`, `InvalidStart`, or `InvalidEnd`;
- `NoPath`;
- `BudgetExceeded`, `CostOverflow`, or `CapacityExceeded`;
- `Stale` when publication invalidates the proof being consumed.

Leases revalidate their exact dependencies. Dispose every successful lease.
After a relevant map, overlay, policy, seam, or GridForge publication, reacquire
instead of continuing a stale result.

## 8. Determinism Notes

- Costs use `Fixed64`.
- Candidate and tie order is canonical.
- A transition-enabled A* query currently uses a zero heuristic.
- Volume movement costs use exact centered-anchor distance with conservative
  ceiling; rectangular and hex shortcuts do not use hand-written topology math.
- Cached payloads are immutable; cursor and pending-action state belong to one
  lease acquisition.

## 9. Related References

- [Navigation maps](NavigationMaps.md)
- [Volume traversal](VolumeTraversal.md)
- [Transitions](Transitions.md)
- [Path guides](PathGuides.md)
- [Map publication](MapPublication.md)
