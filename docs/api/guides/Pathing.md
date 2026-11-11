# Pathing

Trailblazer uses one immutable `PathQuery` for both spatial A* and flow-field search.
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

| Field                   | Meaning                                                                       |
| ----------------------- | ----------------------------------------------------------------------------- |
| `Start`, `End`          | World-space foot endpoints, optional map filters, and resolution policy       |
| `Agent`                 | Body shape, step/drop limits, arrival radius, allowed media, and capabilities |
| `AreaPolicy`            | Exact published policy ID and revision                                        |
| `Traversal.StartMedium` | Exact current Solid, Gas, or Liquid state                                     |
| `Traversal.TargetMedia` | Nonempty requested target-medium mask                                         |
| `Algorithm`             | A* or Flow                                                                    |
| `Budget`                | Finite deterministic work limits                                              |
| `AllowTransitions`      | Whether semantic actions may be considered                                    |
| `FlowField`             | Flow-only integration options                                                 |

`TargetMedia` must be a subset of `Agent.AllowedMedia`. `Unknown` is a runtime
sentinel and is invalid query intent.

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

### Strict top-down placement

Use `context.Pathing.ResolveTopDownEndpoint` to check a circle at an exact
world X/Z center before placing a top-down actor or choosing a destination.
Pass the published map ID, radius, area policy, traversal medium and
capabilities, and a `NavigationWorkBudget`. The map fixes the selected layer
and world Y embedding. Compatible published layers at the same embedding
can supply neighboring footprint coverage.

The center must belong to a cell in the named map. Every required cell under
the circle must be physically present and unblocked, admit the medium and
capabilities, and satisfy the area policy and radius clearance. Exact tangency
is allowed. Success returns the first admissible durable address in canonical
order for the current publication. `InvalidStart` means placement failed.
`Stale` means the policy or captured generations need to be refreshed.
Placement uses the existing lookup, endpoint candidate, and covered-voxel
limits. Every materialized top-down layer at the same embedding counts
toward the layer workspace and generation-validation work. The result is
a snapshot address for the exact requested center.

### Top-down leg clearance

Use `context.Pathing.ValidateTopDownLeg` to check the whole swept circle
between the centers of two ordinary neighboring cells. Pass the durable cell
addresses and the same radius, area policy, medium, and capabilities used for
placement. Both addresses must use their maps' selected layers at the same
world Y embedding. Rectangular legs follow a cardinal neighbor. Hex legs
follow an immediate hex neighbor. A leg can cross a map boundary where the
neighbor footprints match the source lattice exactly.

Every required cell under the sweep must pass the current physical and
semantic checks. Exact tangency is allowed. `Success` certifies the leg
against the current publication. `NoPath` reports unsupported adjacency or
blocked coverage. `InvalidStart` and `InvalidEnd` identify invalid addresses.
An evaluated leg uses one edge allowance plus lookup and covered-voxel work.

### Immediate top-down routes

Use `context.Pathing.FindTopDownRoute` with two durable addresses on compatible
published top-down layers at a shared embedding, a circle radius, an area
policy, a traversal medium and capabilities, an algorithm, and a work budget.
Supply a span of `NavigationCellAddress` values to receive the route. `Success` writes the
ordered addresses, including both endpoints, and returns their count and the
exact fixed-point cost. A failed query leaves the span untouched and returns
zero count and cost. `CapacityExceeded` can indicate insufficient output space
or unavailable query resources.

Both A* and Flow test stationary clearance at the endpoint centers and swept
clearance between neighbors. A map boundary requires matching neighbor
footprints on the same planar lattice. Each leg charges the outward-rounded
in-plane length plus the destination cell and area entry costs. The result is an
immediate snapshot of the current graph. Run a fresh query to account for
subsequent publication changes.

### Retained top-down guides

Use `context.Guides.RequestTopDownGuide` with the same route inputs to lease
the selected center sequence. `TryGetCurrentStep` gives the current address
and its fixed-point world X/Z center. The cursor starts at the first center.
`TryAdvanceStep` moves it by one and stays at the destination once reached.
The host owns movement and decides when to advance. Each request owns an
independent cursor, and copies of a lease share that cursor and disposal.

Both algorithms' center sequences share the A* route-guide lease, single-payload,
and active-payload byte limits. A Flow request retains the route selected for
its start address. Dispose successful leases to release capacity. A graph
publication or raw GridWorld change makes the route stale. Read and advance
operations validate the snapshot before exposing a center or moving the cursor.

Pass `allowTransitions: true` when requesting a retained guide to admit
authored same-medium top-down actions. Both endpoints must fit the circle.
The action charges its authored cost and the destination entry costs.
`TryGetPendingTransition` reads the exact lease-issued action with its
source and destination centers in world X/Z. At that step, ordinary
`TryAdvanceStep` returns `Pending`. Execute the action through the host and
call `CompletePendingTransition` with the same token after the host accepts
the destination pose. Disposed, replaced, and previously completed tokens
return `Stale`.

### Immediate side-view guides

Call `context.Guides.RequestSideViewGuide` with two authored support addresses,
a SideView profile that permits Solid, an area policy, a finite work budget,
and either A* or Flow. The endpoints resolve to the canonical upright capsule
centers above the two [support midpoints](NavigationMaps.md#side-view-support).
Compatible maps at the same embedding can supply support and headroom. Flow
evaluates each predecessor's forward leg so step-up and drop limits apply in
the requested direction.

Each successful `NavigationSideViewGuideLease` starts at the first capsule
center. Use `TryGetCurrentStep` to read it and `TryAdvanceStep` after accepting
that waypoint. Follow each waypoint in order. A step-up retains the lift before
the crossing. A drop retains the crossing before the descent. The bend's
address identifies the lower support owner and the capsule can be airborne at
that point. Your host owns the actual motion. Advancing at the final waypoint
leaves the cursor there.

The cost sums the proved stage lengths rounded up to the fixed-point unit and
the forward destination's cell and area entry costs. Headroom adds no entry
cost. The route describes one publication and raw world snapshot. A change to
either makes the lease stale. Dispose each successful lease to return its
shared route capacity.

Use [scheduled acquisition](#scheduled-side-view-routes) to spread search
and publication across fixed frames.

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
returns `CapacityExceeded`. Neither silently expands storage or falls back to an
unbounded algorithm.

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
returns no request. `Simulate()` advances acquisition within shared frame
allowances after graph maintenance and before simulation hooks. The host
advances the context once per fixed frame.

This integration fragment assumes an existing context and valid A* `query`. Keep
the request alive between fixed frames:

```csharp
NavigationGuideStatus admission = context.Guides.Schedule(
    query, out NavigationGuideRequest? request);
if (admission != NavigationGuideStatus.Pending)
    Console.WriteLine($"Request not queued: {admission}");
```

After the host calls `context.Simulate()`, poll each request it owns. This
helper returns `true` when the host can stop polling and drop its request
reference.

```csharp
static bool TryFinishAStarRequest(NavigationGuideRequest request)
{
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

### Scheduled top-down routes

Use `context.Guides.ScheduleTopDownGuide` with the same center addresses,
profile inputs, policy, and total work budget as an immediate top-down request.
`Pending` returns a `NavigationTopDownGuideRequest`. Poll its status after each
context frame and use `TryTakeTopDownGuide` to transfer a successful lease once.
Dispose requests that no longer have a consumer.

### Scheduled side-view routes

For side-view routes, use `context.Guides.ScheduleSideViewGuide` with the same
support addresses, capsule profile, policy, and total budget as the immediate
request. Poll the `NavigationSideViewGuideRequest` and transfer its result with
`TryTakeSideViewGuide`. Its retained waypoints and host motion contract match
the immediate guide.

### Planar scheduling rules

Planar acquisition uses the same FIFO and frame allowances as spatial A* and
Flow. It counts endpoint proofs, search, reconstruction, and payload copying
against those allowances. An atomic circle or capsule proof can overrun the
frame's lookup allowance within the query's total work budget. For side-view
publication, counting and emitting each support takes one node and one lookup
step per pass. Emission writes the support's optional bend and anchor together.

Contiguous planar A* requests with identical route intent share a build.
Side-view sharing requires an identical capsule profile.
Planar Flow cohorts share destination intent across origins. The retained
result covers its selected origin, so other origins re-admit and build in
order when the available result cannot serve them. Followers admit their own
endpoints and each lease owns its cursor. A graph publication or raw world
change invalidates an active result before transfer.

### Ordering, capacity and latency

The scheduler is one FIFO with one active single-query continuation across A*
and Flow. It freezes a bounded contiguous cohort of compatible requests before
beginning the oldest build. Formation has a fixed submission horizon; arrivals
cannot keep extending it across frames. An unrelated pending query ends the
cohort. Queued entries retain query intent only; each admission observes its
acquired graph. Submit and cancel in deterministic host order. A request
submitted from a simulation hook first advances on a later fixed frame. A large
request delays unrelated requests; there are no priority overrides, automatic
retries or worker threads.

A* sharing requires the exact admitted request. Flow sharing requires the
complete destination payload key, including resolved destination, profile,
media, policy, budget and options; each origin is admitted independently. A
covered follower can finish before an earlier uncovered member of the same
cohort. Same-pass successes are published in submission order; uncovered members
build oldest-first before the next unrelated cohort. Coverage includes
`ExtraIntegrationCost`, not merely presence of an origin node. A follower
coverage miss does not start a search during fan-out, and no member enlarges the
original build's extent or total budget.

Every successful member has an independent lease, cursor, medium and pending
action. Ordinary A*/Flow cache hits and pinned followers certify their
independently admitted endpoints against the immutable payload's exact
dependency versions. Certification uses the query's lookup budget (and the
shared frame allowance when scheduled). Certified hits retain no additional
proof and preserve the warmed zero-allocation path. Extra endpoint dependencies
require a lease-local proof, checked on publication, transfer and use;
invalidating that proof does not invalidate an otherwise-current source payload.
The scheduler's temporary payload pin consumes ordinary active lease/byte
capacity, including for detached or nonreusable payloads; endpoint proof storage
is reserved before capture and charged under the same active-byte ceiling. A
source-slot transfer preserves its member's proof or replaces and releases the
old proof charge. Capturing a different replacement can temporarily require
capacity for both old and new proofs, returning `CapacityExceeded` when that
bounded storage cannot fit. If an extra lease slot cannot fit, the oldest
eligible member takes the original source slot and pinned fan-out ends.
Remaining members use ordinary bounded acquisition/capacity outcomes. Cancelling
one member does not cancel an already-started build while a frozen member is
still interested; cancelling the last interested member releases it.

### Shared frame allowances

`NavigationSchedulingSettings` separates shared per-frame counted-work
allowances from `PathQuery.Budget`, which remains the total-query limit.
Configure it through the `scheduling` argument of
`TrailblazerWorldContextSettings`. Retained request count and logical descriptor
bytes include queued, active and completed-but- unclaimed requests. Take or
dispose completed requests promptly: leaving them unclaimed can exhaust capacity
even when no search is running.

Lookup, endpoint-candidate, node, edge and connection credits are shared across
all phases, requests and algorithms in a frame; they are never refilled by a
phase change or cache hit. Node credits include reused Flow-record clearing,
reconstruction, replay and payload copying, not just search expansions. Several
requests can complete in one frame when the remaining allowances permit.

Tune these allowances for both response latency and containing-frame cost, not
just successful completion. Each request still needs endpoint admission, so a
small endpoint-candidate allowance can delay a large burst even when guides are
already cached. More retained request slots do not add processing credit. The
defaults are conservative work limits, not a promise that a crowd becomes ready
within a particular number of frames.

`OperationStepLimit` (default 64) separately caps FIFO inspection/activation and
cohort formation, follower admission/checkout, search and publication
operations, including cache-hit completion. Scanning retains its place across
frames; completed, unclaimed tickets cannot force an endless prefix rescan. A
pending operation with no counted or phase progress yields until the next frame.

An active scheduled request holds the existing algorithm gate and graph lease.
Synchronous acquisition of that algorithm can therefore return
`CapacityExceeded`. Graph snapshot retention limits still apply. The scheduler
does not bypass these ceilings to make progress.

### Navigator adoption and validation

Navigator steering defaults to `NavigationAcquisitionMode.Immediate`. Opt in
with
`navigator.Steering.ConfigureAcquisitionMode(NavigationAcquisitionMode.Scheduled)`.
Both A* and Flow then queue mandatory initial/recovery acquisition, including
the initial direct-heading check; public `Guides.Schedule` still returns graph
leases. `IsAwaitingGuidance` reports waiting intent and `LastAcquisitionStatus`
reports the latest attempted acquisition/adoption outcome. Waiting supplies zero
new guided heading, but hosts must continue motor/contact and
`CommitFrameMotion` processing. Changing policy cancels pending acquisition and
defers restart until a later context frame; it preserves already usable
guidance. The mode is durable save data, while tickets, samples, validation
turns and queue position are not.

Adoption uses current-foot validation: direct results are reproved and Flow
results are sampled once for that heading call. Moved-foot A* results require a
bounded, addressed connector to the lease's exact ordinary index-zero anchor
(which may differ from the submitted start position). Every connector heading
reproves the actual foot-to-anchor segment, using the ordinary query budget for
Solid, Gas or Liquid and one fair validation turn. No turn means zero new
heading and no cursor, arrival or stuck progress; repeated same-frame calls do
not repeat this work. Transient capacity, stale and local-recovery outcomes
wait/retry without synchronous fallback. An unplanned actual-medium change stops
scheduled intent and requires an explicit new request; authored transition
completion remains the authority for planned medium changes.

The connector owns a separate bounded dependency stamp and raw-world epoch; it
never changes a shared route payload or skips a semantic action. Relevant local
proof invalidation yields zero heading before bounded reproof/recovery.
Unrelated semantic pages remain reusable. Proof arrays are replaced on each
successful connector heading, so these headings are not allocation-free. Old
proof storage is dropped before reserving replacement storage; failure supplies
no heading. `MaxAStarActivePayloadBytes` includes every lease-local proof and
metadata in addition to active route payload and endpoint-proof bytes. Proof
size is bounded by the immediate ray's page/component capacities and the
active-byte ceiling. `TotalCost` remains the immutable route cost, excluding the
connector's traversal cost; positive-cost passable connectors are allowed, not a
new optimal-route claim. The connector lasts until index zero advances or the
guide/session is released. Changing acquisition mode preserves this proof
obligation. Neither proof nor turn ownership is serialized. See
[NavSteering](NavSteering.md#scheduled-acquisition-and-moving-origins).

The same `OperationStepLimit` covers scheduler work and granted validation
turns. A bounded ownership-interest ring rotates in registration order; it
stores no jobs or query continuations. With both queue work and validation
interests, at most half the operation limit rounded up is granted to validation;
at limit one, queue and validation alternate. With no queue work, validation may
use the full limit. Unused turns (for example a paused host) expire that frame
without refund, so diagnostics count granted turns, not necessarily executed
rays/samples. New interests wait until a subsequent context frame. The ring
capacity is derived from retained-request plus active A*/Flow payload-lease
capacities, allowing later adopters and recurring owned-guide validators to
coexist without a second queue. Normal periodic LOS checks remain governed by
their existing cadence/phase policy. Grants and acquisition attempts use the
context's long frame count and clock lifetime. A grant from an earlier frame or
before a context reset cannot be consumed, even when a reset reaches the same
numeric frame again. See the recovery benchmark harness for explicit
acquisition-mode and work-limit controls; counted limits alone are not timing
claims.

### Stale requests and reset

Scheduled acquisition fails `Stale` if the raw GridWorld change sequence changes
from cohort activation through result transfer, including followers admitted
later, even for an unrelated change. Exact published dependency validation also
remains in place. Decide in the host whether and when to submit fresh intent;
the scheduler never silently restarts. Context or pathing reset cancels pending
requests and releases unclaimed results. Request handles and continuations are
runtime-only, not save data.

A fresh scheduled Flow query can evict an unused dependency-stale cached
incumbent and continue as a cache miss within the same admission and budget.
This narrow case requires the admitted graph and request raw epoch still be
current and the old payload's raw stamp still match. It does not retry a stale
checked-out proof or publication failure. Immediate and existing batch
acquisition keep their existing stale-incumbent behavior.

### What a slice does not guarantee

Slice counts are deterministic work allowances, not a millisecond deadline. Some
operations remain atomic: traversal evaluations, endpoint/simplification rays,
allocation, dependency checks and payload publication. Reused Flow search
records are cleared incrementally against the node slice; explicit
reset/teardown can still drain retained work synchronously. The finite
query/capacity limits still apply, but they can cost more than an ordinary
search slice. Measure full frames, including activation and result transfer,
with representative endpoints, body shapes and maps. A smaller slice reduces
ordinary per-frame work while increasing request latency in simulated frames.

An atomic traversal or ray may exceed its remaining frame lookup credit. Its
actual query-meter lookup usage saturates that credit at zero before another
evaluation can begin; total query budgets remain authoritative. The operation
ceiling bounds repetition, not the duration of any individual operation.

Prepare large shared fields during loading or submit them well ahead of use.
Conservative slices can make a very large cold field take seconds or minutes of
simulated time to become ready, especially behind earlier FIFO work. Measure
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
