# Phase 6 Navigation Rays And Simplification Design

**Date:** 2026-08-16  
**Status:** Approved architecture; implementation pending  
**Primary tracker:** `docs/feature-work/gridTopologyNavigationMapRefactorPlan.md`

## Decision

Phase 6 adds one internal, bounded, deterministic navigation-ray kernel and
routes graph surface direct travel, nearest-endpoint trace validation, A* path
simplification, and Flow local rejoin through it. A* payload construction emits
one canonical simplified guide route. Exhausting the optional simplification
budget preserves a valid less-simplified route rather than failing a successful
search.

The ray remains internal during Phase 6. Phase 7 must wire the same kernel into
volume and transition traversal, delete the remaining raw-volume line-of-sight
provider, and decide whether the now-proven surface-plus-volume contract should
be promoted to a public query API before release.

## Why This Shape

The repository currently has three geometry-sensitive behaviors that should be
one behavior:

- legacy surface line-of-sight uses `PathManager.NeedsPath` and a flat voxel
  trace;
- graph A* returns node foot anchors without certified string pulling or the
  required portal anchors;
- Flow sampling falls back to a destination-bound A* guide when local sampling
  returns `LocalRecoveryRequired`.

A runtime-only ray would repeat work for every guide and agent. Retaining full
corridor certificates in every A* payload would enlarge the cache and duplicate
GridForge geometry. The selected design instead shares one internal evaluator,
performs bounded simplification once while building the cacheable A* payload,
and reuses the same evaluator for direct travel and Flow rejoin.

## Scope

### Phase 6 owns

- ordered surface navigation rays over rectangular, pointy-hex, flat-hex,
  sparse, overlapping, seam, and explicit-connection layouts;
- exact swept-body validation for `NavigationAgentProfile.Shape`;
- nearest-endpoint trace validation;
- portal-aware raw A* guide-point expansion and bounded canonical string
  pulling;
- graph `PathQuery` direct-path checks before guide acquisition and on the
  existing steering recheck cadence;
- navigation-ray-certified Flow rejoin while retaining the same Flow lease;
- deletion of the temporary Flow-to-A* recovery bridge;
- deletion of superseded surface LOS APIs and their documentation;
- fixed workspace, cache-byte, allocation, determinism, and benchmark evidence.

### Phase 6 does not own

- volume graph edge activation or transition/hybrid graph cutover;
- runtime volume navigation-ray wiring;
- a public navigation-ray API;
- curved smoothing or presentation splines;
- a second topology projection, portal compiler, or collision-math library in
  Trailblazer;
- rebuilding a Flow field to recover a locally displaced agent.

## Authority Boundaries

The implementation must preserve these ownership lines:

1. **FixedMathSharp.Geometry owns fixed-point geometry predicates.** Segment,
   capsule, convex, prism, projection, distance, and intersection math comes
   from `FixedMathSharp.Geometry`. If a required general geometric predicate is
   absent, add it there with independent tests rather than coding it in
   Trailblazer or GridForge.
2. **GridForge owns grid topology and topology-issued geometry.** Ordered trace
   intervals, exact rectangular/hex prisms, physical sparse presence, portal
   certificates, topology coordinate conversion, and selected-prism body
   validation remain GridForge responsibilities.
3. **Trailblazer owns navigation semantics.** It resolves trace intervals to
   immutable graph nodes, selects one deterministic graph-connected chain,
   applies `TraversalEvaluator`, consumes navigation budgets, records graph
   dependencies, simplifies guide points, and orchestrates steering/rejoin.

Trailblazer must contain no rectangular/hex projection formula, local portal
reconstruction, epsilon comparison, floating-point fallback, or duplicate
capsule/convex relation.

## Upstream Swept-Body Authority

`GridCellGeometry.IsNavigationBodyAnchorValid` proves one body anchor and a
selected portal opening. A navigation ray additionally needs to prove the body
over the complete segment portion inside each selected prism and through the
selected source/target prism union. Phase 6 extends that existing GridForge
authority into one shared segment-capable core; the anchor API becomes the
coincident-endpoint case and existing corridor consumers delegate to the same
core. GridForge must not retain parallel point and sweep clearance
implementations. The shared authority:

- accepts one exact `GridCellPrism`, the bounded foot segment portion, body
  radius and height, and at most the selected incoming/outgoing
  `GridNavigationPortal` certificates;
- validates the swept horizontal disk/capsule against every non-selected prism
  boundary;
- permits traversal only through the exact selected portal opening after
  resolving the active body profile. Vertical portals return a directed
  source/target parameter enclosure around the exact crossing; horizontal
  portals return ordered source/target parameters while their exact resolved
  profile anchors remain the geometric split authority;
- validates vertical body bounds throughout in-prism legs and validates a
  horizontal transition against the certified two-prism union rather than
  requiring the body to remain inside either prism alone;
- fails closed on invalid certificates, ambiguity, or fixed-point overflow;
- delegates general capsule, segment, and convex predicates to
  `FixedMathSharp.Geometry`;
- allocates zero and exposes no Trailblazer type.

One segment does not switch vertical authority between two same-wall portal
openings. Each selected portal must cover its complete possible planar-overlap
interval; otherwise GridForge fails closed and the caller retains an
intermediate anchor. Phase 7 owns the decision to add exact inner/outer interval
authority and a fixed A-only/overlap/B-only proof if a real volume or hybrid
route requires that handoff.

The authority is tested independently for rectangular and both hex
orientations, corner clips, partial portals, vertical bounds, equality and
one-raw-unit failures, reverse traversal, and extreme fixed-point inputs before
Trailblazer consumes it.

## Internal Ray Contract

Phase 6 introduces one internal work type and one fixed reusable workspace. The
names may change during implementation, but there is only one behavioral
authority.

The internal status model is finite and explicit:

- `Pending` only for resumable caller work;
- `Success` when a complete graph-connected body corridor is proven;
- `Blocked` when geometry or traversal semantics reject the segment;
- `BudgetExceeded` when a declared counter is exhausted;
- `CostOverflow`, `CapacityExceeded`, and `Stale` for their existing meanings.

The request carries only ray-used semantics: resolved agent profile, area
policy, traversal intent, transition permission, directed endpoints, endpoint
allowance, and an optional fixed chain constraint. It does not retain a complete
`PathQuery`. The constraint is unrestricted, current-source-address-only, or
current source followed by one exact canonical selected edge.

The result exposes only consumer-required facts: status, selected start/end
addresses, exact traversal cost, and whether every selected semantic surcharge
was zero. Counters remain in the caller-owned `NavigationWorkMeter` or
`GuideSampleWorkMeter`; the selected interval/edge chain remains in scratch
while work is active. The result does not retain a graph snapshot or become a
cache by itself.

## Ordered Ray Evaluation

For a segment from `start` to `end`, the work performs these steps:

1. Extend and call GridForge `GridTracer.TraceIntervalsInto` with caller-owned
   `SwiftList<GridTraceInterval>` and `GridTraceIntervalScratch`. Candidate-grid
   discovery receives an explicit limit and stops before appending beyond it;
   the report exposes the exact grid count and a distinct exceeded status. No
   forwarding overload preserves the unbounded call shape. For query work,
   `MaxLookupProbes` charges grid candidates,
   `MaxCoveredVoxelIntervals` bounds address candidates, and
   `MaxTraceIntervals` bounds written exact intervals.
2. Reject incomplete, unrepresentable, or non-continuous address coverage. For
   ordinary direct travel and simplification, require continuous physical
   coverage as well.
3. Resolve every physical interval through its exact configuration/generation
   to a mapped `NavigationCellAddress` and immutable graph node. An overlapping
   but unmapped or disconnected grid never grants passage.
4. Resolve and validate the required start and end graph nodes. The selected
   chain must begin at the expected start and end at the expected destination;
   merely intersecting another passable map is insufficient.
5. Traverse interval/tie groups in their canonical upstream order. A tie group
   is a transitive overlap frontier, not one equal-parameter bucket. Within each
   frontier, repeatedly relax canonical outgoing edges until bounded reachability
   closure is reached; stable address/edge order chooses the canonical
   predecessor but never prevents an opposite-order valid edge from becoming
   reachable. Tied peers are not assumed mutually adjacent.
6. Apply the same `TraversalEvaluator` used by A*. Native, automatic-seam, and
   explicit edges must resolve to the exact selected edge and its compiled
   portal/corridor certificates.
7. Resolve every selected portal contact against the input segment. A vertical
   directed source/target enclosure or horizontal source/target parameter pair must exist,
   occur in semantic order, and lie inside the participating trace intervals.
   An off-line explicit corridor cannot become valid merely because its endpoint
   prisms overlap the same tie frontier.
8. Prove monotonically continuous parameter coverage from zero through one.
   Every handoff is supported by the selected graph edge and body-valid portal;
   an interior sparse hole, blocked witness, or unselected overlap fails.
9. Validate the bounded segment portion in each selected prism with the
   GridForge swept-body primitive. The portal plane is exempt only through the
   selected certificate; all other walls remain solid.
10. Accumulate the exact existing `TraversalCost` for the selected edge chain
    and separately retain whether authored enter costs, area-policy costs, and
    edge surcharges were all zero. Geometric edge/corridor distance is not a
    semantic surcharge.
11. Revalidate graph dependencies/current publication after the final geometric
   result before returning `Success`.

The chain algorithm is iterative. Workspace arrays are fixed-capacity and
generation-stamped or explicitly reset. No recursion, LINQ, per-ray collection,
or dependence on hash-table iteration order is permitted.

## Endpoint Trace Fallback

Strict endpoint resolution remains exact. For `NearestNavigable`, candidates
retain the existing fixed-distance/address ranking but must also be reachable
through a role-aware navigation ray before they may win. Start resolution tests
requested start -> candidate anchor. End resolution tests candidate anchor ->
requested destination so directed edges and portals keep their meaning.

The endpoint policy may authorize only one uncovered prefix adjacent to an
unresolved requested start, before the first selected physical graph interval;
from that interval through the start candidate anchor, coverage must be
continuous. The mirrored destination rule requires continuous coverage from
the destination candidate anchor through the last selected physical graph
interval and permits only the uncovered suffix after that interval. This
permits an intentional bounded snap out of a sparse or unmapped endpoint
without allowing an interior sparse gap. Strict resolution and ordinary LOS
never receive that allowance. The allowance is an endpoint-resolution result
only and can never be returned or reused as ordinary full-segment ray
`Success`.

Candidate ray work shares the query's one `NavigationWorkMeter`. A budget or
capacity failure remains a query failure; a geometrically blocked candidate is
skipped and the next canonical candidate is considered.

## A* Guide-Point Expansion

Search cost and predecessor selection remain unchanged. Each winning node record
adds only a compact canonical predecessor-edge ordinal. During reconstruction,
the edge is re-resolved from the parent in canonical enumeration order and its
compiled geometry expands the raw route. Task 7 stores only each guide point's
stable address and exact position:

- node foot anchors remain graph-addressed guide points;
- native and automatic seams add active-profile source/target portal anchors
  when those differ from the surrounding node anchors;
- explicit connections add entry anchor, each active-profile portal source and
  target anchor, and exit anchor in semantic order;
- consecutive duplicate positions are removed deterministically;
- every edge's complete active-profile route is swept-body certified before
  relaxation, so invalid geometry can lose to another A* route, and the winning
  raw route is revalidated before payload publication;
- every raw consecutive leg is certified by its compiled edge/corridor
  authority before simplification begins. Explicit routes emit entry, compiled
  portal source/target points, and exit; witness cell foot anchors are not
  synthetic corridor waypoints.

The existing `NavigationAStarNodeTable` remains the cumulative node-foot cost
authority. Task 8 alone adds bounded node-to-guide ordinal scratch when
simplification consumes those costs; no per-guide cost array is retained.

The immutable A* payload changes from an address-only node array to a bounded
array of internal guide points containing the stable associated address and
exact fixed-point position. The existing public `NavigationGuideLease` remains
the sole A* guide API and keeps its current address-plus-position waypoint
surface. Payload/cache byte accounting and reservation preflight use the guide
point capacity, not the searched-node count alone. `TotalCost` remains the
original graph route cost; geometric string pulling never rewrites route cost.

## Bounded Canonical Simplification

Simplification runs after raw guide-point expansion and before final
dependency-stamp capture/payload publication. Before optional work begins, the
query reserves the exact remaining lookup/copy work needed to publish the raw
route and its already-discovered dependency stamp. Optional rays can consume
only the unreserved remainder.

- From the current committed node foot anchor, later node foot anchors are
  attempted in deterministic farthest-to-nearest route order. Portal and
  connection points remain in the raw fallback but are never shortcut
  endpoints.
- A successful navigation ray commits the farthest proven candidate only when
  its exact traversal cost is no greater than the exact difference between the
  two node-anchor cumulative costs.
- A blocked ray tries the next candidate while budget remains.
- Each attempt consumes exactly one `MaxSimplificationRays` debit plus its trace,
  coverage, edge, connection, and dependency work.
- When no simplification ray remains, the untouched raw suffix is appended.
- Ray dependencies are first held in temporary fixed scratch. A shortcut is
  committed and its dependencies merged only when the exact union still fits
  both dependency capacity and the reserved final-capture work; otherwise
  optional simplification stops and appends the raw suffix.
- A simplification `BudgetExceeded` therefore does not turn a successful A*
  search into failure. Search, endpoint, overflow, capacity, or stale failures
  retain their existing terminal semantics.

This algorithm is bounded by the declared ray count even though the conceptual
candidate space is quadratic. It never performs an unmetered all-pairs scan.
The same query, graph snapshot, profile, policy, and budget produce byte-for-byte
identical payload guide points.

Ray-touched semantic pages and components are merged into the payload dependency
stamp. Cached simplified routes therefore become stale when any node, edge,
witness, portal, or policy fact used by a shortcut changes.

## Graph Direct Travel

`NavSteering` retains its existing direct-path cadence but graph `PathQuery`
requests no longer bypass it.

- Before acquiring A* or Flow guidance, steering attempts one certified ray
  from the actual foot position to the exact requested destination.
- While guidance is active, the existing cooldown periodically repeats the
  check.
- Direct travel is accepted only when the ray is successful and every authored
  cell enter cost, area-policy additional cost, and edge surcharge on its chain
  is zero. This conservative rule prevents a clear straight segment from
  bypassing an intentionally cheaper weighted route. Geometric traversal cost
  remains allowed. A successful eligible ray releases the guide and steers
  directly.
- At this orchestration boundary, a geometrically successful but
  semantic-cost-ineligible ray is normalized to `Blocked`; `Success` therefore
  always means the returned direct heading is eligible.
- Each steering check is a distinct bounded internal operation with a fresh
  `NavigationWorkMeter` created from the query's immutable
  `NavigationWorkBudget`. It does not consume or refresh the separate public
  guide request's meter.
- Every synchronous non-success (`Blocked`, `BudgetExceeded`, `CostOverflow`,
  or `CapacityExceeded`) means "direct travel not proven" and falls back to or
  retains normal guidance. `Pending` never escapes the synchronous check.
- `Stale` exposes no heading and follows the existing graph retry path.
- Combined/group steering never converts a terminal zero-heading arrival or a
  retry-neutral zero into movement.

This is orchestration reuse, not a second LOS system.

## Flow Local Rejoin

When ordinary Flow sampling returns `LocalRecoveryRequired`, the guide first
uses its existing exact covered-address rebase. If that fails, it ray-tests only
the fixed geometry already named by its current cursor: the current source
anchor followed by the selected edge's compiled portal/target anchors. Each
target is exposed and tested immediately by stable ordinal; no candidate array
is retained. It does not scan or rank the Flow payload and owns no second
candidate table.

For each fixed candidate, the guide runs the same navigation ray from the actual
foot to the certified anchor/selected-edge entry. The source-anchor candidate is
confined to current-source geometry; selected-edge candidates require current
source followed by that exact canonical edge. Any semantic travel before the
already-selected Flow edge must be cost-neutral. A successful ray
returns the ray heading for that frame while retaining the original Flow lease
and cache identity. The Flow cursor is committed/rebased only after the actual
foot reaches a payload-covered node; until then each sample revalidates the
corridor and current graph. No Flow field is rebuilt and no A* query is created.

The failed exact-rebase/rejoin branch transfers the existing local-recovery
debit and consumes exactly one such unit total. Blocked or cost-ineligible
candidates continue to the next fixed candidate and,
when none succeeds, return `LocalRecoveryRequired` without cursor mutation.
Meter exhaustion returns `BudgetExceeded`; `CostOverflow` and
`CapacityExceeded` retain their existing public statuses; dependency
publication between ray validation and heading commit returns sticky `Stale`.
Only `Success` exposes a heading. Disposal and copied-lease generation behavior
remain exactly once.

The following Phase 5 bridge residue is deleted in this phase:

- `_flowRecoveryGuideLease`;
- `TryGetFlowRecoveryHeading`;
- its sole `ponytail:` source comment;
- every recovery-A* acquisition, lifecycle, serialization assumption, and test.

## Workspace, Budgets, And Allocation

`NavigationQueryLimits` receives only three new ceilings: covered ray addresses,
trace intervals, and expanded A* guide points. Existing workspace map capacity
sizes candidate-grid storage; interval capacity also sizes chain state. All
other arrays derive from those values. There is one explicit constructor shape;
no compatibility or forwarding overload is added.

- Each exclusive A* workspace owns one ray workspace.
- The context guide/pathing service owns one locked reusable immediate-ray
  workspace shared by direct checks and exceptional Flow rejoin. Lock
  acquisition blocks; thread scheduling never changes a navigation result. A
  deterministic bounded pool is added only if the Phase 6 contention benchmark
  proves serialization is a bottleneck.
- All `GridTraceIntervalScratch` and `SwiftList` capacities are fully reserved
  from settings before warm execution. Admission rejects budgets that exceed
  the configured workspace rather than allowing a hidden list growth. Upstream
  candidate-grid collection observes the supplied limit before appending.
- Warm direct ray, simplification workspace reuse, Flow rejoin sample, A* guide
  advancement, and disposal allocate zero on the measured thread.

No elapsed-time cutoff is used. Work stops only at deterministic counters.

## Concurrency And Staleness

Ray evaluation operates against one leased immutable graph. It validates that
the lease graph is compatible with the initiating query/payload, then checks
`store.Current` after the last dependency/body decision and before exposing a
heading or publishing a payload.

No GridForge/world lock is held while taking a graph-store, cache, guide, or
steering lock. Caller-owned workspaces are never shared without their existing
owner lock. A stale result never commits a simplification point, endpoint,
guide cursor, or steering heading.

Meter mapping is explicit. Query/endpoint/simplification/direct work charges
candidate grids to lookup probes, covered addresses to covered-voxel intervals,
trace output to trace intervals, graph edges to evaluated edges, and explicit
corridor legs to connection legs. Flow rejoin charges grid/address/node probes
to `GuideSampleWorkMeter` current-node lookup probes, graph edges/connection
legs to cursor-leg scans, selected openings/prisms to portal/prism checks, and
trace output to trace intervals. It receives no fresh hidden budget.

## Public And Legacy API Policy

The navigation ray stays internal in Phase 6 so a surface-only status,
workspace, and endpoint-allowance contract is not frozen prematurely. There is
no temporary public alias.

The superseded surface APIs are deleted, not forwarded:

- both public `PathManager.NeedsPath` overloads;
- internal `TrailblazerPathingService.NeedsPath`;
- public surface `NavSteering.IsDestinationInSight`.

`NavSteering.IsVolumeDestinationInSight` remains explicitly volume-only because
its direct consumers are owned by Phase 7. The Phase 7 ledger requires its
replacement and deletion together with `VolumeVoxelFinder` direct-path
authority. Documentation and API snapshots must make the surviving choice
unambiguous.

## Verification

Implementation is strict TDD. The minimum matrix includes:

- exact short/medium/long ordered traces and one-below interval/candidate/ray
  budgets;
- dense and sparse rectangular, pointy-hex, and flat-hex maps;
- mixed metrics, automatic seams, explicit multi-witness corridors, overlapping
  mapped/unmapped grids, candidate-grid one-below limits, and tied interval
  groups requiring opposite-address-order closure;
- interior sparse holes, blocked witnesses, wrong/unselected overlaps, stale
  grid generations, and publication interleavings;
- radius-zero and positive-radius straight crossings;
- rectangular and both hex corner clips whose endpoint anchors are valid;
- planar and vertical portals, partial openings, height/radius equality and
  one-raw-unit failures, exact on-segment crossing parameters, off-line portal
  rejection, and forward/reverse traversal;
- nearest start/end endpoint allowed prefix/suffix versus forbidden interior
  gap, including asymmetric directed portals;
- raw A* portal expansion, deterministic simplification, zero-ray unchanged
  raw route, partial budget preserving a valid suffix, cache byte accounting,
  and mutation invalidation;
- NavSteering initial direct path, cooldown recheck, blocked/budget fallback,
  all-status degradation, weighted-cost fallback, arrival-before-combined-
  steering, and serialization state;
- Flow local displacement, certified rejoin, no-candidate retry, graph mutation,
  same lease/cache identity, no A* admission, copied lease ABA, and disposal;
- warmed zero-allocation gates for direct ray, A* construction reuse, and Flow
  rejoin;
- Debug/Release/ReleaseLean determinism digests and both Trailblazer target
  frameworks;
- source scans proving no Catmull-Rom, duplicate topology projection, legacy
  surface LOS API, Flow recovery bridge, forwarding overload, LINQ, or floating
  simulation math remains in the Phase 6 slice.

Benchmarks cover short/medium/long rays, sparse failures, mixed seams, worst
guide-point counts, simplification attempts, total trace/candidate intervals,
Flow rejoin, and direct-ray contention. Optimizations follow measurements; they
may not introduce a second geometry or traversal path.

## Living Tracker And Phase 7 Handoff

The primary refactor plan is updated at every Phase 6 slice with RED, GREEN,
review, gate, commit, and residual-debt status. Phase 7 owns these explicit
follow-ups:

1. activate the same navigation ray for volume and transition graph traversal;
2. replace and delete `VolumeVoxelFinder.IsDirectPathClear` and
   `NavSteering.IsVolumeDestinationInSight` only when their graph consumers are
   live;
3. exercise complete volume/hybrid media and traversal policy semantics through
   the ray;
4. after surface and volume behavior are proven, either promote one clean public
   navigation-ray query/result API or record an explicit pre-release decision to
   keep it internal—never add a forwarding alias;
5. retain the reusable upstream vertical-portal primitive tests and add the
   first real runtime volume consumer without a test-only production hook.
