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

## Upstream Swept-Body Primitive

`GridCellGeometry.IsNavigationBodyAnchorValid` proves one body anchor and a
selected portal opening. A navigation ray additionally needs to prove the body
over the complete segment portion inside each selected prism. If the current
GridForge surface cannot express that proof, Phase 6 adds the smallest reusable
GridForge primitive that:

- accepts one exact `GridCellPrism`, the bounded foot segment portion, body
  radius and height, and at most the selected incoming/outgoing
  `GridNavigationPortal` certificates;
- validates the swept horizontal disk/capsule against every non-selected prism
  boundary;
- permits crossing only through the exact selected portal openings after
  resolving the active body profile;
- validates vertical body bounds throughout the segment, including horizontal
  portals;
- fails closed on invalid certificates, ambiguity, or fixed-point overflow;
- delegates general capsule, segment, and convex predicates to
  `FixedMathSharp.Geometry`;
- allocates zero and exposes no Trailblazer type.

The primitive is tested independently for rectangular and both hex
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

The result exposes only the facts needed by its internal consumer: status,
selected start/end addresses, exact consumed counters, and the selected
interval/edge chain held in caller-owned scratch while the work is active. It
does not retain a graph snapshot or become a cache by itself.

## Ordered Ray Evaluation

For a segment from `start` to `end`, the work performs these steps:

1. Call GridForge `GridTracer.TraceIntervalsInto` with caller-owned
   `SwiftList<GridTraceInterval>` and `GridTraceIntervalScratch`.
   `MaxCoveredVoxelIntervals` bounds address candidates and
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
5. Traverse interval/tie groups in their canonical upstream order. Within a
   tied group, use stable address order. Carry reachable candidates forward by
   enumerating canonical graph edges, not by assuming tied peers are mutually
   adjacent.
6. Apply the same `TraversalEvaluator` used by A*. Native, automatic-seam, and
   explicit edges must resolve to the exact selected edge and its compiled
   portal/corridor certificates.
7. Prove monotonically continuous parameter coverage from zero through one.
   Every handoff is supported by the selected graph edge and body-valid portal;
   an interior sparse hole, blocked witness, or unselected overlap fails.
8. Validate the bounded segment portion in each selected prism with the
   GridForge swept-body primitive. The portal plane is exempt only through the
   selected certificate; all other walls remain solid.
9. Revalidate graph dependencies/current publication after the final geometric
   result before returning `Success`.

The chain algorithm is iterative. Workspace arrays are fixed-capacity and
generation-stamped or explicitly reset. No recursion, LINQ, per-ray collection,
or dependence on hash-table iteration order is permitted.

## Endpoint Trace Fallback

Strict endpoint resolution remains exact. For `NearestNavigable`, candidates
retain the existing fixed-distance/address ranking but must also be reachable
from the requested point through the navigation ray before they may win.

The endpoint policy may authorize only one uncovered prefix adjacent to the
unresolved requested point (or suffix for an unresolved destination). After the
first selected physical graph interval, coverage must remain continuous through
the candidate foot anchor. This permits an intentional bounded snap out of a
sparse or unmapped endpoint without allowing an interior sparse gap. Strict
resolution and ordinary LOS never receive that allowance.

Candidate ray work shares the query's one `NavigationWorkMeter`. A budget or
capacity failure remains a query failure; a geometrically blocked candidate is
skipped and the next canonical candidate is considered.

## A* Guide-Point Expansion

Search cost and predecessor selection remain unchanged. Each winning node record
adds only a compact canonical predecessor-edge ordinal. During reconstruction,
the edge is re-resolved from the parent in canonical enumeration order and its
compiled geometry expands the raw route:

- node foot anchors remain graph-addressed guide points;
- native and automatic seams add active-profile source/target portal anchors
  when those differ from the surrounding node anchors;
- explicit connections add entry anchor, each active-profile portal source and
  target anchor, and exit anchor in semantic order;
- consecutive duplicate positions are removed deterministically;
- every raw consecutive leg is certified by its compiled edge/corridor
  authority before simplification begins.

The immutable A* payload changes from an address-only node array to a bounded
array of internal guide points containing the stable associated address and
exact fixed-point position. The existing public `NavigationGuideLease` remains
the sole A* guide API and keeps its current address-plus-position waypoint
surface. Payload/cache byte accounting and reservation preflight use the guide
point capacity, not the searched-node count alone. `TotalCost` remains the
original graph route cost; geometric string pulling never rewrites route cost.

## Bounded Canonical Simplification

Simplification runs after raw guide-point expansion and before dependency-stamp
capture/payload publication.

- From the current committed point, candidates are attempted in deterministic
  farthest-to-nearest route order.
- A successful navigation ray commits the farthest proven candidate.
- A blocked ray tries the next candidate while budget remains.
- Each attempt consumes exactly one `MaxSimplificationRays` debit plus its trace,
  coverage, edge, connection, and dependency work.
- When no simplification ray remains, the untouched raw suffix is appended.
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
  check. A successful ray releases the guide and steers directly.
- `Blocked`, `BudgetExceeded`, or temporary ray-workspace contention means
  "direct travel not proven" and falls back to/retains normal guidance; it does
  not fail the navigation request.
- `Stale` retries on a later frame through the existing graph retry path.
- Combined/group steering never converts a terminal zero-heading arrival or a
  retry-neutral zero into movement.

This is orchestration reuse, not a second LOS system.

## Flow Local Rejoin

When ordinary Flow sampling returns `LocalRecoveryRequired`, the guide searches
only a bounded local set of nodes already covered by its current immutable Flow
payload. Candidate ranking is deterministic by fixed-point distance and stable
address, with current selected-edge candidates preferred when eligible.

For each candidate, the guide runs the same navigation ray from the actual foot
to the candidate's certified anchor/selected-edge entry. A successful ray
returns the ray heading for that frame while retaining the original Flow lease
and cache identity. The Flow cursor is committed/rebased only after the actual
foot reaches a payload-covered node; until then each sample revalidates the
corridor and current graph. No Flow field is rebuilt and no A* query is created.

If no candidate is proven within the guide sample budget, the guide returns the
existing retry-neutral status without cursor mutation. Dependency publication
between ray validation and heading commit returns sticky `Stale`. Disposal and
copied-lease generation behavior remain exactly once.

The following Phase 5 bridge residue is deleted in this phase:

- `_flowRecoveryGuideLease`;
- `TryGetFlowRecoveryHeading`;
- its sole `ponytail:` source comment;
- every recovery-A* acquisition, lifecycle, serialization assumption, and test.

## Workspace, Budgets, And Allocation

`NavigationQueryLimits` receives explicit capacities for ray intervals,
candidates/chain state, and expanded A* guide points. There is one explicit
constructor shape; no compatibility or forwarding overload is added.

- Each exclusive A* workspace owns one ray workspace.
- Each pooled Flow guide shell owns one fixed-capacity ray workspace; its bytes
  are included in pool/cache accounting and the pool remains bounded by the
  active lease ceiling.
- The context guide/pathing service owns one locked reusable direct-ray
  workspace initially. A pool is added only if the Phase 6 contention benchmark
  proves serialization is a bottleneck.
- All `GridTraceIntervalScratch` and `SwiftList` capacities are fully reserved
  from settings before warm execution. Admission rejects budgets that exceed
  the configured workspace rather than allowing a hidden list growth.
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
  mapped/unmapped grids, and tied interval groups;
- interior sparse holes, blocked witnesses, wrong/unselected overlaps, stale
  grid generations, and publication interleavings;
- radius-zero and positive-radius straight crossings;
- rectangular and both hex corner clips whose endpoint anchors are valid;
- planar and vertical portals, partial openings, height/radius equality and
  one-raw-unit failures, forward/reverse traversal;
- nearest endpoint allowed prefix versus forbidden interior gap;
- raw A* portal expansion, deterministic simplification, zero-ray unchanged
  raw route, partial budget preserving a valid suffix, cache byte accounting,
  and mutation invalidation;
- NavSteering initial direct path, cooldown recheck, blocked/budget fallback,
  arrival-before-combined-steering, and serialization state;
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
5. remove any Phase 6 vertical-portal test-only activation that Phase 7 replaces
   with a real runtime consumer.

