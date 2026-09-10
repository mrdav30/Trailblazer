# Pathing

Trailblazer uses one immutable `PathQuery` for both A* and flow-field search.
The query is complete caller intent: no algorithm branch infers a medium,
geometry, policy, or unbounded work limit later.

## Query Contract

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

## Endpoints

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

## Agent Profile

`NavigationAgentProfile` is immutable and includes:

- `KinematicBodyShape` radius, body height, and root-to-foot offset;
- maximum surface step up and drop down;
- arrival radius;
- allowed media;
- traversal capabilities such as Jump, Climb, Swim, Fly, and Teleport.

Grid metrics never substitute for agent geometry. A map cell or transition can
require capabilities, but possession of a capability does not invent an action;
the map must still author the corresponding edge or rule.

## Work Budget

`NavigationWorkBudget` bounds lookup probes, endpoint candidates, expanded
nodes, evaluated edges, connection legs, transition candidates and pairs, trace
intervals, covered-volume intervals, and simplification rays. Zero disables a
category.

Budget exhaustion returns `BudgetExceeded`; fixed retained-capacity exhaustion
returns `CapacityExceeded`. Neither silently expands storage or falls back to
an unbounded algorithm.

## A* Versus Flow

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

## Spreading acquisition across fixed frames

`RequestGuide` and `RequestFlowField` are synchronous: they finish the complete
request before returning. Finite query budgets prevent endless work, but do not
make a large cold search cheap enough for a fixed frame. Use them during loading
or other host-owned work where that latency is acceptable.

For frame-spread acquisition, submit the same `PathQuery` with
`context.Guides.Schedule(query, out request)`. Acceptance returns `Pending` and
a disposable `NavigationGuideRequest`; a capacity or invalid-profile rejection
returns no request. The context advances one acquisition phase per `Simulate()`
after graph maintenance and before simulation hooks. Never loop over extra
`Simulate()` calls merely to drain the queue in a real game frame.

This integration fragment assumes an existing context and valid A* `query`.
Keep the request alive between fixed frames:

```csharp
NavigationGuideStatus admission = context.Guides.Schedule(
    query, out NavigationGuideRequest? request);
if (admission != NavigationGuideStatus.Pending)
    Console.WriteLine($"Request not queued: {admission}");
```

Call the following helper once per host fixed frame while that request is
owned; stop calling it when it returns `true`. In a host with several requests,
call `context.Simulate()` once centrally
and poll each request afterwards, rather than calling this helper per request:

```csharp
static bool AdvanceAStarRequest(
    TrailblazerWorldContext context, NavigationGuideRequest request)
{
    context.Simulate();
    if (request.Status == NavigationGuideStatus.Pending)
        return false;

    if (request.TryTakeGuide(out NavigationGuideLease? result))
    {
        using NavigationGuideLease guide = result!.Value;
        Console.WriteLine($"Route: {guide.StepCount} steps, cost {guide.TotalCost}");
        // A real host may transfer this lease to its guidance owner instead.
    }
    else
    {
        Console.WriteLine($"No transferable route: {request.Status}");
    }
    request.Dispose();
    return true;
}
```

For Flow, use a Flow query and `request.TryTakeFlowField(out field)`. Taking a
result revalidates it and transfers its lease exactly once. A wrong-family,
pending, failed or repeated take returns `false`; inspect `Status` for the
acquisition outcome. Successful transfer preserves `Success`. Dispose the
transferred lease normally. Disposing a pending request cancels it; disposing
any other request releases its unclaimed result without changing its completed
outcome. `Status` is a stored outcome, not a continuously running validation:
the take operation can change a previously successful result to `Stale`.

### Ordering, capacity and latency

The scheduler is one FIFO with one active acquisition across A* and Flow.
Queued entries retain query intent only; they observe the graph when activated.
Submit and cancel in deterministic host order. A request submitted from a
simulation hook first advances on a later fixed frame. A large request delays
later requests; there are no priority overrides, automatic retries or worker
threads.

`NavigationSchedulingSettings` separates per-frame continuation slices from
`PathQuery.Budget`, which remains the total-query limit. Configure it through
the `scheduling` argument of `TrailblazerWorldContextSettings`. Retained request
count and logical descriptor bytes include queued, active and completed-but-
unclaimed requests. Take or dispose completed requests promptly: leaving them
unclaimed can exhaust capacity even when no search is running.

An active scheduled request holds the existing algorithm gate and graph lease.
Synchronous acquisition of that algorithm can therefore return
`CapacityExceeded`. Graph snapshot retention limits still apply. The scheduler
does not bypass these ceilings to make progress.

Scheduling is explicit at the guide-service boundary. It does not automatically
convert Navigator-owned acquisition or direct-heading checks into queued work.
The headless benchmark uses input-driven Navigators, not 100 simultaneous cold
guided-controller starts; measure that host workload separately if you need it.

Scheduled acquisition fails `Stale` if the raw GridWorld change sequence changes
from activation through result transfer, even for an unrelated change. Exact
published dependency validation also remains in place. Decide in the host
whether and when to submit fresh intent; the scheduler never silently restarts.
Context or pathing reset cancels pending requests and releases unclaimed
results. Request handles and continuations are runtime-only, not save data.

### What a slice does not guarantee

Slice counts are deterministic work allowances, not a millisecond deadline.
Some operations remain atomic: endpoint/simplification rays, allocation,
dependency checks and payload publication. Reused Flow search records are
cleared incrementally against the node slice; explicit reset/teardown can still
drain retained work synchronously. The finite
query/capacity limits still apply, but they can cost more than an ordinary
search slice. Measure full frames, including activation and result transfer,
with representative endpoints, body shapes and maps. A smaller slice reduces
ordinary per-frame work while increasing request latency in simulated frames.

Prepare large shared fields during loading or submit them well ahead of use.
Conservative slices can make a very large cold field take seconds or minutes
of simulated time to become ready, especially behind earlier FIFO work. Measure
both the containing-frame cost and request readiness in your host. Smaller
slices leave more time for ongoing simulation but make results take longer;
counted work limits are not elapsed-time guarantees.

## Traversal Families Inside The Graph

- Solid native movement uses topology contacts plus surface step/drop rules.
- Solid explicit connections can carry certified witnesses or guide points.
- Gas/Liquid native face movement uses volume-centered body anchors.
- Gas/Liquid shortcuts use GridForge's complete topology direction sets and
  swept-prism-union validation.
- Explicit definitions and procedural rules create semantic action edges.

The public query does not select one of these families. The exact start medium,
target mask, agent, policy, and map publication determine which edges are legal.

## Status And Staleness

Guide acquisition can report:

- `Success`;
- `Unsupported` for an algorithm/method mismatch;
- `NoMap`, `InvalidProfile`, `InvalidStart`, or `InvalidEnd`;
- `NoPath`;
- `BudgetExceeded`, `CostOverflow`, or `CapacityExceeded`;
- `Stale` when publication invalidates the proof being consumed.

Scheduled requests additionally use `Pending` while queued or advancing and
`Cancelled` when disposed or reset before completion.

Leases revalidate their exact dependencies. Dispose every successful lease.
After a relevant map, overlay, policy, seam, or GridForge publication, reacquire
instead of continuing a stale result.

## Determinism Notes

- Costs use `Fixed64`.
- Candidate and tie order is canonical.
- A transition-enabled A* query currently uses a zero heuristic.
- Volume movement costs use exact centered-anchor distance with conservative
  ceiling; rectangular and hex shortcuts do not use hand-written topology math.
- Cached payloads are immutable; cursor and pending-action state belong to one
  lease acquisition.

## Related guides

- [Navigation maps](NavigationMaps.md)
- [Volume traversal](VolumeTraversal.md)
- [Transitions](Transitions.md)
- [Path guides](PathGuides.md)
- [Map publication](MapPublication.md)
