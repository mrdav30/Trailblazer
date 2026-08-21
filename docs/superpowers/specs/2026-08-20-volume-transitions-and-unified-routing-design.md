# Phase 7 Volume, Transitions, And Unified Routing Design

**Date:** 2026-08-20  
**Status:** Approved architecture; implementation pending  
**Primary tracker:** `docs/feature-work/gridTopologyNavigationMapRefactorPlan.md`

## Decision

Phase 7 replaces the experimental staged Hybrid and legacy Volume route
families with one graph-backed medium-state search used by both A* and Flow.
Physical cells remain one immutable addressed graph node, while bounded search
state is keyed by `(NavigationNodeRef, TraversalMedium)`. Native movement and
volume shortcuts retain the medium; explicit or rule-generated semantic
transitions may retain or change it and always produce an action instruction.

Volume means free-form deterministic three-dimensional travel through Gas or
Liquid matter. It is not surface traversal with a different heuristic, and it
does not depend on terrain charts or Solid partitions. Terrain remains an
optional host concern expressed through effective cell data, area policy, or a
later host adapter.

The phase keeps `PathQuery` as the sole A*/Flow request, uses the existing graph
payload caches and dependency clocks, exposes actionable transition-aware guide
results, requires explicit consumer completion of transition actions, and then
deletes the superseded Hybrid/Volume providers without aliases or forwarding
overloads.

## Goals

- Route Solid, Gas, and Liquid through one immutable graph publication.
- Preserve Euclidean-like legacy Volume route quality for rectangular grids and
  provide equivalent first-class pointy/flat hex behavior.
- Make map state-of-matter defaults explicit, immutable authoring truth.
- Support object-anchored and rule-generated transitions without per-cell edge
  bloat.
- Give A* and Flow the same medium, transition, cost, dependency, and stale
  semantics.
- Surface transition actions without assuming an engine, animation, physics
  system, or locomotion implementation.
- Delete staged Hybrid and legacy Volume code once every live consumer moves.

## Non-Goals

- Arbitrary body pitch/roll or rigid-body configuration-space search.
- Curved paths, presentation splines, or engine collision response.
- Automatic gameplay actions inferred solely from touching media.
- Plasma support in Phase 7. The design does not couple Volume to terrain, so a
  later medium addition remains possible, but it must be introduced explicitly
  across enums, authoring, serialization, policies, tests, and documentation.
- A second transition registry, hybrid component hierarchy, route cache, or
  staleness clock.
- Materializing every rectangular/hex shortcut or generated transition as a
  retained per-node graph edge.

## Authority Boundaries

1. **FixedMathSharp.Geometry owns deterministic fixed-point geometry.** Exact
   segment, capsule, convex, intersection, distance, and rounding predicates
   live upstream. Its strict swept-upright-cylinder/convex-prism boolean keeps
   planar and vertical overlap on one exact parameter domain, so boundary-only
   contact cannot be promoted by composing separately rounded intervals.
   Trailblazer never adds local cross/dot/multiword/epsilon geometry.
2. **GridForge owns physical topology and issued geometry.** Cell prisms,
   topology direction sets, cell centers, contact geometry, sparse presence,
   swept-body coverage, local prism-union predicates, and translation remain
   GridForge facts.
3. **Trailblazer owns navigation semantics.** It composes effective cells,
   media, capabilities, area policy, transitions, costs, budgets, canonical
   search order, payloads, guides, and controller orchestration.
4. **The host owns terrain and action execution.** Terrain may author cells,
   areas, costs, or policies. Transition instructions describe actions; the host
   performs them and explicitly reports completion.

No Trailblazer runtime file may contain rectangular/hex neighbor formulas,
floating-point movement math, a second prism-union solver, or BCL/LINQ hot-path
collections when the upstream stack already supplies the required authority.

## Effective Cell Authoring And State-Of-Matter Defaults

`NavigationMap` gains one optional complete default `NavigationCell`. Absence
means no implicit navigation cell. The exact effective precedence is:

1. overlay cell;
2. explicit baked cell;
3. map default cell;
4. no navigation cell.

Each winner replaces the complete cell payload. Media, capabilities, area,
cost, clearance, and flags never merge field-by-field. The default covers every
physically present GridForge cell whose index lies inside the map's normalized
`GridBinding`; sparse or absent addresses are never populated implicitly. A
cell-overlay `Suppress` is a tombstone over every lower layer. `RevertToBake`
falls through explicit baked cell, map default, then no cell.

Replacing the same durable map/binding with a Gas default versus a Liquid
default is an ordinary semantic publication. Affected pages/components change,
old payloads and leases become stale, and new queries resolve the new medium.

`NavigationCell.Media` remains a flag set because one physical cell may support
multiple traversal states. A medium-state exists only when the effective cell,
agent allowed media, required capabilities, area policy, and profile-specific
clearance all admit it.

## Query Intent

`PathQuery` remains the only public graph request. Delete `TraversalDomain` and
replace the current prototype `TraversalIntent` with:

- `TraversalMedium StartMedium`;
- nonempty `TraversalMedia TargetMedia`;
- the existing `bool AllowTransitions` on `PathQuery`.

Every query requires an exact Solid/Gas/Liquid start. `Unknown` remains only as
a runtime-state sentinel and is rejected by query construction/admission. This
avoids implicit medium selection and a second multi-medium start probe.

`TargetMedia` must contain only known flags and be a subset of
`NavigationAgentProfile.AllowedMedia`. Endpoint resolution filters start
candidates by exact `StartMedium` before ranking. With transitions disabled,
return `NoPath` immediately when `TargetMedia` excludes `StartMedium`; otherwise
set `effectiveTargetMedia = StartMedium`, qualify/rank target candidates only
by that mask, admit only
`(destination, StartMedium)`, and use that one anchor. With transitions enabled,
set `effectiveTargetMedia = TargetMedia`, qualify/rank targets by at least one
admissible medium in that mask, then admit
every qualifying requested medium-state at the winning address; A* uses zero
heuristic and Flow seeds each at zero integration cost. Exact ties use stable
address order followed by medium ordinal.

`AllowTransitions == false` excludes every semantic transition edge, including
same-medium Jump/Climb actions. A
valid query whose target flags exclude the resolved start medium naturally
returns `NoPath`. No `VolumePathRequest`, `HybridPathRequest`, route-plan
wrapper, or compatibility factory survives.

`Navigator` does not silently rewrite caller intent from its current frame
medium. A guided request whose exact `StartMedium` differs from the
host-restored current medium fails before guide acquisition; callers construct
the query for the actual state. This is an intentional clean break from the
legacy volume-first fallback and prevents a controller-only hidden priority or
query synthesis path.

Legacy raw-volume knobs are retired explicitly:

- `UnitSize` becomes the agent profile's horizontal radius/body height while
  GridForge metrics remain the movement geometry authority;
- `AllowUnwalkableEndpoints` is removed; callers choose `Strict` or
  `NearestNavigable` endpoint resolution and the resolved medium-state must be
  admissible;
- `MaxPathSearchRange` is removed in favor of finite `NavigationWorkBudget`
  capacities;
- caller-selected volume heuristics are removed; A* owns the admissible
  heuristic stated by this design.

## Medium-State Graph

The immutable physical graph keeps one node per effective addressed
`NavigationCell`. Search workspaces and payload facts key state by
`(NavigationNodeRef, TraversalMedium)` without eagerly allocating three state
records per physical node.

- Native positive-face edges retain medium.
- Certified volume shortcuts retain medium.
- Semantic transition edges may retain or change medium and always require an
  action instruction.
- Surface Solid edges use existing surface anchors, portals, step, and drop
  evaluation.
- Gas/Liquid edges use volume anchors and free-flight evaluation.

Same-medium positive-face components are structural over-approximations built
only from effective medium presence and positive-face contact:

- rectangular: six faces;
- pointy/flat hex prisms: six planar plus two vertical faces.

Components are medium-specific because the same physical cell can support
different media. They are not keyed by profile, capabilities, area policy, or
clearance: different components safely prove `NoPath` when transitions are
disabled, while membership in one component never proves a route. Certified
shortcuts never create connectivity. Directed transitions connect structural
components only during search. Transition-enabled queries do not build or
consult a second hybrid-component cache.

## Volume Anchors And Free-Flight Evaluation

Trailblazer retains bottom-center foot position as its common public body
reference. GridForge resolves a profile-specific volume anchor by placing the
upright body center at the topology-issued cell volume center and deriving the
foot from the body height. Anchor placement is a degenerate swept-body coverage
query, so a body wider/taller than one cell may be supported by multiple
positive-overlap prisms across adjacent maps/grids. A cell is unavailable when
that complete placement cannot be certified in the effective local medium
volume.

Gas/Liquid movement:

- ignores `MaxStepUp`, `MaxDropDown`, floor following, and surface portal
  semantics;
- checks every required support cell for physical presence, selected medium,
  agent capabilities, area permission, and clearance;
- validates one-prism-fitting positive-face movement with GridForge's existing
  directed portal/body predicates and falls back to the same covered-prism
  union authority for larger profiles;
- charges exact Fixed64 direct anchor distance rounded conservatively upward,
  plus destination cell/area enter cost exactly once;
- uses downward-rounded Euclidean anchor distance as the A* heuristic.

Free-form means unrestricted deterministic 3D translation of the conservative
upright shape. Pitch, roll, animation pose, and engine collision response stay
outside pathfinding.

## Topology-Native Volume Connectivity

Face-only routing is not acceptable: A* simplification cannot restore a
corridor the search did not choose, and Flow would retain a Manhattan-biased
integration field. Unchecked corner edges are also unacceptable.

The structural face graph is augmented during query evaluation with bounded
procedural shortcuts from GridForge's deterministic complete direction sets:

- rectangular: 26 total directions, of which 20 are non-face shortcuts;
- hex prism: 20 total directions, of which 12 are vertical-planar shortcuts.

Required local support closure:

- rectangular two-axis shortcut: all four subset cells;
- rectangular three-axis shortcut: all eight subset cells;
- hex vertical-planar shortcut: source, planar peer, vertical peer, target.

The fixed closure is the minimum corner-cut certificate, not a body-size limit.
GridForge also enumerates every physical cell whose prism interior has positive
overlap with the swept upright body into caller-bounded scratch. Exact tangency
does not claim the neighboring closed prism; deterministic half-open/boundary
ownership preserves just-fit equality. Every closure and swept-coverage cell
is semantic and dependency evidence. A blocked, absent, wrong-medium,
forbidden, insufficient-clearance, over-budget, or over-capacity witness
rejects only the shortcut or returns the exact terminal budget/capacity status;
face-connected search remains available when the rejection is merely
impassable. Witness-only cells are not path-center entries, so their enter
costs are not charged; the destination cell/area enter cost remains the single
semantic enter charge.

The old `100/141` constants are deleted. They approximate `sqrt(2)` only for
unit isotropic two-axis motion, incorrectly charge three-axis motion as `141`
instead of approximately `173`, and ignore actual GridForge metrics.

## Upstream Prism-Union Predicate

Support-cell membership alone cannot certify a positive-radius body at a
simultaneous edge/corner crossing. The Phase 6 one-incoming/one-outgoing portal
ray and ordered corridor API prove a face chain, not the direct sweep through
the complete local cell union.

GridForge therefore gains one allocation-free, caller-bounded operation that
enumerates every prism with positive interior overlap against the swept upright
body and validates the direct segment through their union. Inputs are the
physical world, exact source/target cells, direct foot segment, horizontal
radius, body height, and caller-owned result/scratch spans. Coverage may cross
adjacent grids/maps when they issue exact congruent prisms in one aligned
topology lattice rather than stopping at one binding. Heterogeneous or
misaligned partial-prism CSG is deliberately unavailable and fails closed. The
report distinguishes invalid geometry, result capacity, and work-budget
exhaustion. Result roles separate cells selected for required physical coverage
from missing exact-prism OR alternatives retained only for affected dependency
invalidation. Trailblazer applies media, policy, and clearance only to required
coverage cells, but records dependencies for both roles; the topology closure
must be present in the required set. GridForge composes FixedMathSharp.Geometry;
Trailblazer does not implement a fallback union solver.

Per-prism positive overlap delegates to the single public
`FixedConvexPrismRelations.IntersectsSweptUprightCylinderStrict` relation. It
answers whether strict planar footprint overlap and strict vertical overlap
exist at the same continuous path parameter. Its public result is boolean; the
exact rational parameter bounds, open half-plane clipping, and wide quadratic
comparisons stay internal to FixedMathSharp. Neither GridForge nor Trailblazer
combines rounded planar and vertical intervals, and no public rational interval,
compiled certificate, or general CSG API is introduced.

The union operation is authoritative for volume anchor placement (a degenerate
sweep), non-face shortcuts, and positive-face movement when a profile does not
fit one prism. For one-prism-fitting positive-face movement, reuse
`TryCreateNavigationPortal`, `TryGetNavigationPortalTraversalParameters`, and
`IsNavigationBodySegmentValid` as the proven fast path without surface
step/drop semantics. Phase 6 volume ray legs delegate to this same face
authority rather than running a second long-ray union solver. Until the union
operation accepts a positive-radius shortcut, that shortcut is unavailable and
face movement remains correct.

Candidate directions remain bounded to 20/12, while swept-coverage count is
profile/binding-bounded through the existing ray-related query limits and
A*/Flow `NavigationRayWorkspace` capacity. Extend that workspace only with the
typed covered-cell buffer GridForge requires; do not add a volume-shortcut
workspace, capacity family, or pool. Do not retain a compiled union certificate, maxima, cache,
or public descriptor without benchmark evidence. If the Phase 7 shortcut
benchmark identifies the stateless operation as hot, a later internal
normalized-binding/direction template may be considered. The existing
conservative same-wall dual-opening handoff remains fail-closed unless a real
Phase 7 route proves the need for the already-ledgered exact interval extension.

## Transition Sources

Transition immutability is snapshot-local, not permanent.

### Anchored definitions

World objects such as ladders, doors, elevators, authored jump links, and
teleporters publish exact directed `TraversalTransitionDefinition` values as
baked data or bounded overlay operations. A dropped ladder generally publishes
one definition per usable direction. Moving it replaces the definitions;
removing it suppresses them. Stable object identity supplies stable transition
IDs. Definitions carry complete nonnegative `ActionCost` plus the compact
locomotion hints needed by the built-in controller; same-medium Jump/Climb is
as valid as a medium-changing action.

### Generation rules

Reusable public map-authoring `TraversalTransitionRule` values describe
environment-wide state changes without per-cell authoring. Each rule contains:

- stable rule ID;
- transition type;
- source and destination media;
- `SameCell` or `PositiveFaceContact` scope;
- required capabilities;
- nonnegative Fixed64 action cost;
- compact locomotion hints required by the built-in controller.

Rules do not contain engine callbacks, terrain predicates, profile instances,
or arbitrary delegates. The immutable graph snapshot retains one canonically
sorted bounded rule array. A*/Flow scan it procedurally—forward from source
state and reverse from destination state—so a water surface does not retain one
edge object per cell. Add a bucket index only if the required benchmarks prove
the bounded scan dominant.

Rule action points are derived, not separately authored. `SameCell` uses the
two resolved medium anchors. `PositiveFaceContact` uses GridForge's directed
profile-resolved contact/portal anchors and fails when the profile cannot fit.
An explicit definition's optional point overrides must belong to their declared
endpoint prisms at publication; evaluation certifies medium-specific
anchor-to-action segments and records their dependencies.

The exact responsibility split is:

- effective cell media describes matter;
- the agent profile describes allowed media and abilities;
- the transition rule describes which state-changing action the environment
  permits.

Mere media contact never invents an action. One authored Liquid-to-Gas Takeoff
rule can nevertheless serve every eligible water-surface contact for an agent
with `Swim | Fly`, while a non-flying agent cannot use it. Cells supporting
both media may use a `SameCell` rule.

Instruction identity is a tagged tuple: definition or rule kind, stable owner
ID, and exact source/destination medium-states. Explicit and procedural IDs
therefore cannot collide across kinds.

## Transition Publication And Dependencies

Explicit transition definitions, transition rules, effective cells, and
transition overlays compile as one candidate graph snapshot.

- Resolve durable endpoint addresses to exact physical nodes and verify that
  both endpoint cells support the declared media.
- Explicit definitions populate source outgoing and destination incoming pages.
- Procedural rules remain one canonically sorted bounded array for forward and
  reverse enumeration.
- Canonical edge tie order is destination address, destination medium,
  transition type, identity kind, then owner ID.
- A same-ID overlay `Upsert` shadows its source-owned baked definition;
  `Suppress` removes it; `RevertToBake` restores it.
- Duplicate IDs within the baked/overlay definition owner and duplicate IDs
  within the rule table reject the candidate transaction while leaving the
  current graph unchanged. Cross-kind IDs remain distinct by construction.
- Malformed baked definitions/rules reject authoring/publication.
- A later cell overlay that removes an endpoint or declared medium makes the
  affected transition inactive in the candidate graph instead of blocking the
  environmental edit. Revert restores it.

Outgoing/incoming transition pages, rule tables, effective cell pages,
medium-specific components, and touched GridForge generations use the ordinary
graph dependency stamp. A map default change, flood, ladder edit, rule change,
or affected policy publication stales cached A*/Flow results through that one
clock.

## Unified Edge Evaluation And Costs

One canonical internal edge dispatcher gives A* and Flow the same semantics.
It delegates to the existing surface evaluator, stateless volume face/shortcut
evaluation, or transition evaluation. Only an edge kind that actually yields
across a budget boundary retains specialized work state; every pending edge
record is not inflated to the largest case.

Transition definitions and rules own one nonnegative `ActionCost`; rename the
pre-release `AdditionalCost` rather than retaining both. Transition edge cost
is:

1. movement from the current medium anchor to the source action point;
2. `ActionCost`;
3. movement from the destination action point to the destination medium anchor;
4. destination cell enter cost;
5. destination area-policy enter cost.

Never infer a straight-line travel cost between the two action points: a
teleporter may connect distant positions with a small authored action cost.
A* uses the medium-appropriate Euclidean floor heuristic only when transitions
are disabled and uses zero when transitions are enabled. Add a stronger
transition-aware lower bound only after measurement proves it worthwhile.

Capabilities and media are checked before relaxation. Costs use `Fixed64` with
checked addition and return `CostOverflow` rather than narrowing. A*/Flow use
identical edge sets, costs, canonical ordinals, and dependency recording.

## A* And Flow Payloads

Existing graph admission, workspaces, caches, leases, reservations, generation
validation, and dependency publication remain the owners. They are extended,
not wrapped.

A* records predecessors for medium-state nodes and reconstructs movement steps
plus rare transition instructions. Ordinary guide entries remain compact;
transition payload is retained only for actual transition steps. Guide
simplification may remove only same-medium movement subsequences certified by
the volume-aware navigation ray. It never skips a semantic transition.

Flow reverse integration seeds every resolved target medium-state and follows
incoming native/shortcut/transition authority. Each source medium-state stores
the exact selected action. Sampling returns movement guidance or a transition
instruction. Same-lease local rejoin remains constrained to the selected
medium/edge and cannot skip a transition.

Exact reusable `NoPath` results retain all dependencies that influenced the
negative proof, including blocked transition rules and shortcut witnesses.

## Public Guidance And Explicit Completion

Delete address/position-only and heading-only legacy result overloads.

`NavigationGuideLease.TryGetCurrentStep` returns a `NavigationGuideStep` with
the selected address, exact position, medium, and either ordinary movement or a
`NavigationTransitionInstruction`.

`NavigationFlowFieldLease.TrySample` returns a `NavigationFlowSample` with the
selected medium, ordinary heading/target facts, or a transition instruction.
Transition samples expose zero ordinary movement heading once the exact source
action anchor is reached.

`NavigationTransitionInstruction` exposes only consumer-required facts:

- stable explicit/rule identity;
- transition type;
- exact source/destination addresses;
- source/destination media;
- resolved source/destination world positions;
- compact `TraversalTransitionLocomotionHints` needed by the built-in
  controller, including request-climb and preserve-climb-after-completion.

It does not expose graph nodes, route stages, terrain, delegates, capabilities,
or costs already consumed by planning. The same value carries a private opaque
completion stamp composed from the lease's existing acquisition generation and
the current step/sample ordinal. It is not serialized or exposed as a second
token API. Exact completion compares it; the lease's ordinary graph/dependency
validation remains the sole publication-staleness authority.

Pending instruction and current-medium state are per acquired A*/Flow lease,
never mutable cached-payload state. A lease initializes at `StartMedium`, changes
medium only after exact completion, and blocks ordinary sample/advance while
one instruction is pending. Cached payloads remain immutable and reusable.

Both lease types accept explicit completion of the exact currently pending
instruction. Ordinary advancement cannot cross a pending transition. A
mismatched, copied-from-an-old-generation, or no-longer-current instruction
fails closed. On completion the same guide advances to the destination
medium-state; it never assumes that proximity means an engine-specific action
succeeded.

The unified lease owns cursor/action truth. `Navigator` retains only the exact
pending instruction needed by its public surface and exposes
`CompletePendingTransition(in instruction)` as the sole advancement path. It
publishes zero ordinary steering while waiting and deletes
`NavigatorGuidedTraversalState`'s automatic volume-exit handoff activation.
Built-in and custom transition types use the same handshake; host
locomotion/animation decides how to perform the action. The built-in controller
applies the explicit locomotion hints instead of inferring them from transition
type. Failure is handled by cancelling/retrying after the host changes
capabilities, policy, cell, or transition availability rather than by a second
failure protocol.

## Serialization Boundary

Serialize exact `PathQuery` intent using start medium, target-media flags, and
transition permission. Delete `TraversalDomain`, `PathRequestRecord`'s
`UnitSize`/endpoint-permissiveness/search-range/heuristic/guide/waypoint state,
Navigator guided-volume mode/handoff fields, hybrid discriminators, and every
inactive wire mode once all readers migrate. Reject missing, old, unknown, or
malformed media/rule/action shapes transactionally in JSON and MemoryPack
without mutating the existing shell.

Graph leases, guide cursors, and an in-flight host action are runtime-owned and
are not reconstructed from serialized graph objects. A standalone `PathQuery`
record round-trips exact intent. A serialized Navigator session instead retains
durable destination/profile/policy/algorithm/budget/target-media intent, clears
the pending action, and rebuilds a fresh start endpoint plus `StartMedium` from
the host-restored current position/medium. Invalid restored state fails before
guide acquisition; serialized and host start media are never merged silently.
Trailblazer does not claim to serialize an engine's animation or physics action.

## Status, Budget, And Capacity Semantics

- Unsupported/invalid query shapes fail before admission mutation.
- Missing map/effective cells retain `NoMap`, `InvalidStart`, or `InvalidEnd`
  according to the existing boundary.
- Semantic/geometry rejection of one edge is impassable and search continues.
- Fixed-point overflow is `CostOverflow`.
- Workspace, dependency, payload, or graph lease exhaustion is
  `CapacityExceeded`.
- Caller budget exhaustion is `BudgetExceeded` with exact debits.
- Publication/generation/dependency/world changes are sticky `Stale`.
- A transition instruction mismatch fails closed without cursor mutation.

Volume shortcut candidate limits are topology-bounded: at most 20 rectangular
or 12 hex non-face candidates per expanded state. Rule enumeration is bounded
by the immutable configured rule count. No unmetered fallback search, recursive
ray, or per-candidate allocation is permitted.

## Legacy Deletion Boundary

After the unified graph consumers are green, delete—not adapt—the superseded
families and their tests/benchmarks/docs/API entries:

- `VolumePathRequest`, `VolumeSurveyor`, `VolumeSurveyResult`, `VolumeGuide`,
  `VolumeVoxelFinder`, volume-specific path heap/waypoint/heuristic carriers,
  and legacy volume caches/pools;
- `HybridPathRequest`, `HybridRoutePlanner`, `HybridRoutePlan`,
  `HybridRouteStep`, `HybridRouteGuide`, `GuidedVolumeExitPlanner`,
  `GuidedVolumeExitHandoff`, staged fallback/preplan factories, and their
  independent ownership state;
- mutable transition registries/indexes replaced by graph snapshot pages/rules;
- old `TraversalDomain`, legacy `TraversalIntent` fields, old guide-result
  overloads, forwarding constructors/factories, dormant serialized
  discriminators, and compatibility aliases.

Deletion occurs only after direct consumers compile against the replacement.
Final exact-token residue scans cover source, tests, benchmarks, docs, public
API snapshots, and serialization records.

This is one atomic Phase 7 owner boundary. Before deletion, migrate
`NavSteering` sampling/request state, `Navigator` guided-volume fields and
automatic handoff activation, `GuideSampleBatch`, all benchmark consumers, and
both serialization transports. Phase 8 does not retain a later volume/hybrid
porting lane.

The public runtime volume-predicate service is not translated automatically.
Hosts materialize prior predicate results before publication as a complete map
default, explicit `NavigationCell` entries, or addressed overlay deltas. The
old requirement that a voxel first own a Solid/Volume chart partition is
deleted; physical GridForge coverage plus effective immutable cell data is the
new authority.

## Public Navigation-Ray Decision

After the same ray is proven against both surface and volume consumers, Phase 7
performs the ledgered pre-release review. Promote one clean public query/result
API only if it expresses a generally useful graph-connected body-segment proof
without exposing internal meters/workspaces/chain constraints. Otherwise record
an explicit decision to retain the specialized kernel internally. Do not add a
public forwarding facade merely to satisfy the ledger.

## Verification Strategy

All feature behavior is developed RED-first. Focused gates precede broad gates;
.NET processes remain serial where the local upstream package stack shares
assets.

Minimum scenario families:

1. **Defaults and publication:** absent/default/explicit/overlay precedence;
   sparse physical coverage; Gas-to-Liquid replacement; exact affected versus
   unaffected invalidation.
2. **Volume geometry:** rectangular two-axis/three-axis and anisotropic costs;
   pointy/flat hex planar and vertical-diagonal movement; dense/sparse storage;
   just-fit/one-raw-fail radius and height; blocked closure fallback.
3. **A*/Flow parity:** identical medium-state edges/costs, open-volume direct
   headings, selected transitions, multiple target media, cache hits, NoPath,
   and same-lease rejoin.
4. **Transition rules:** same-cell and face-contact generation, canonical
   forward/reverse enumeration, capability/policy rejection, cost applied once,
   stable identity, overlay/default invalidation.
5. **Ladder simulation:** no initial route; drop a ladder from a cliff into
   liquid through overlay definitions; obtain and hold a climb instruction;
   explicitly complete it; move/remove the ladder; prove stale/unavailable and
   no resource leak.
6. **Duck simulation:** one Liquid-to-Gas Takeoff rule; a `Swim | Fly` duck
   takes off from multiple eligible water-surface contacts without per-cell
   authoring; a non-flying otherwise-equivalent agent cannot; instruction
   completion continues the same guide.
7. **Concurrency/publication:** object/rule/cell mutation before and after
   linearization; copied lease ABA/double dispose; blocked negative dependency
   proof; no partial cache publication.
8. **Serialization/API deletion:** exact round trips for new query intent;
   malformed values preserve existing shell state; removed public types and
   wire modes are absent.

Run cross-process/configuration determinism with canonical digests over
rectangular, both hex orientations, volume shortcuts, transition rules, ladder,
duck, Flow, A*, mutation, and serialization-visible state.

Benchmarks cover open/obstructed rectangular 2D/3D diagonals, hex vertical
diagonals, A*/Flow, generated transition rules, explicit transition guidance,
and mixed surface-volume routes. Report settled states, base edges, shortcut
candidates, swept-coverage/prism-union checks, transition candidates, dependency merges,
guide steps, elapsed distribution, and allocations. Cache-hit sampling must
perform zero shortcut/rule construction and warm guide sampling must allocate
zero; immutable A* payload construction may report its intentional allocation.

## Acceptance Criteria

- Gas/Liquid routing is graph-backed, topology-agnostic, and terrain-optional.
- Rectangular 26 and hex 20 movement quality is preserved with exact Fixed64
  geometry and no corner cutting.
- Surface, volume, and transition routing share one A*/Flow semantic authority.
- Map defaults and flooding publish deterministic affected-only invalidation.
- Ladder and duck scenarios pass through real public authoring/guide APIs.
- Transition actions remain held until exact explicit completion.
- No legacy Volume/Hybrid provider, cache, registry, overload, serialized mode,
  or API snapshot entry remains.
- No duplicate FixedMathSharp/GridForge geometry or topology formulas exist in
  Trailblazer.
- Focused, full Release/ReleaseLean, multi-TFM source, upstream stack,
  allocation, determinism, benchmark semantic-preflight, package, docs, and
  independent correctness/minimality review gates pass.
