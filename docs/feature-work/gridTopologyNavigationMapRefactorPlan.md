# Grid Topology Navigation Map Refactor Plan

## Purpose

This plan replaces Trailblazer's dense, cubic, rectangular pathing lattice with
a topology-native navigation graph layered on GridForge. The target is complete
support for the GridForge storage/topology matrix:

| Storage | Rectangular prism | Hex prism |
| --- | --- | --- |
| Dense | Supported, including anisotropic cell metrics | Supported, pointy-top and flat-top |
| Sparse | Supported; missing addresses are absent nodes | Supported; axial holes are absent nodes |

The work is intentionally a major-version cutover. Trailblazer does not need to
preserve the current chart constructors, voxel partitions, request hierarchy,
serialized request records, world-wide voxel-size assumptions, or forwarding
facades. Temporary old/new internals may coexist on the feature branch only to
keep phases testable. The completed branch must expose one model and contain no
obsolete wrappers, compatibility readers, aliases, or dual execution paths.

Trailblazer's kinematic controller remains a deterministic world-space system.
AI pathfinding requires at least one mapped GridForge grid; manual locomotion,
motor, turning, and host-driven steering remain usable without one.

## Why This Must Be A Graph Redesign

The current implementation is rectangular at every layer:

- `NavigationChart` owns a dense `[y,x,z]` lattice, scalar `Interval`, and
  Cartesian world-to-index conversion.
- `SolidChartPartition` stores a 26-slot rectangular neighbor array and derives
  clearance in voxel counts.
- A*, flow fields, raw-volume search, endpoint fallback, and reachability each
  interpret `RectangularDirection` independently.
- movement costs are the metric-independent integers `100` and `141`.
- flow sampling floors world X/Y/Z by one global voxel size and bilinearly
  samples a rectangular XZ cell.
- request defaults, search limits, steering thresholds, and formation padding
  all depend on one context-wide `VoxelSize`.

Adding hex branches to those classes would preserve the wrong abstraction.
Dense versus sparse controls which physical cells exist. Rectangular versus hex
controls local coordinates, adjacency, and geometry. Differing grid metrics
control edge length and clearance. These axes must remain separate.

GridForge also refuses to remove a sparse voxel while it owns a partition or
active voxel subscriber. A coordinated Trailblazer API could detach those
objects before asking GridForge to remove the voxel, so this is not a claim that
partition-backed sparse navigation is impossible. It is an avoidable ownership,
memory, locking, and teardown-order dependency: direct GridForge removal fails
until Trailblazer clears its per-voxel state. The clean-break graph design does
not need that coupling, so navigation state moves off `Voxel` instances.

## Agreed Design Decisions

### One map per grid, one composed graph per context

- A `NavigationMap` is the immutable baked navigation metadata for exactly one
  logical GridForge grid configuration, not a world-sized asset.
- A `TrailblazerWorldContext` owns a registry of zero or more maps. At most one
  map may bind to each exact active `VoxelGrid` generation in
  `GridWorld.ActiveGrids`; one context may therefore have many active maps.
- Engines normally configure a `GridWorld`, create its grids, bake one map for
  each grid intended for AI traversal, and register those map assets with the
  context. An active GridForge grid without a map remains valid but is not
  navigable.
- Maps may be registered before their matching grid exists and remain dormant.
  A matching grid generation may likewise exist before its map is registered.
- Each map is the immutable baked baseline for solid, gas, and liquid traversal
  on its grid. A context-owned sparse semantic overlay is the only runtime
  override authority for cells, physical connections, and semantic transitions.
  Both baseline and overlay may reference stable addresses in other maps.
- Installing, replacing, or removing one map is atomic at the
  context's deterministic maintenance boundary and does not rebuild unrelated
  map instances.
- The context composes every bound map into one segmented runtime graph so one
  query can cross grids and topologies. “One graph” means shared search
  semantics and connectivity, not one giant node array or one monolithic map
  collection.
- Duplicate entries within one map and duplicate bindings to one active grid
  are errors. Trailblazer does not retain chart overlap, priority, or
  registration-order resolution.
- AI queries fail clearly when no eligible mapped grid exists. Kinematic
  control remains available.

### Persistent authoring identity versus runtime identity

- Each map has a unique host-owned stable `MapId` plus one normalized GridForge
  configuration descriptor. Authored cells use `MapId + VoxelIndex`; a separate
  layer identifier would duplicate the per-grid map identity.
- Phase 0 adds a public, non-mutating GridForge normalization API that returns
  the normalized `GridConfiguration`, `GridConfigurationKey`, dimensions, and
  topology-local index validation. Trailblazer does not call `ToGridKey()` on
  unsnapped authoring bounds or copy GridForge's internal normalization math.
- The normalized `GridConfigurationKey` captures snapped bounds, topology kind,
  topology metrics, and hex orientation and binds the durable map to a live
  grid. Storage kind is deliberately not part of the map binding identity, so the
  same authored map can bind to dense or sparse realizations of the same
  logical grid.
- Runtime graph nodes use exact `WorldVoxelIndex` values and reject stale world
  or grid generations.
- Runtime tokens, grid slots, and `WorldVoxelIndex` values are never serialized
  or used as authoritative ordering keys.
- The globally unique canonical node key and total order is
  `(ordinal MapId, lexicographic VoxelIndex (x,y,z))`, using one frozen
  ordinal-string comparator. Per-map baked ordinals and compact instance slots
  are lookup handles only; they never decide ties, equality, cache identity, or
  serialization. Dormancy, removal, re-addition, and packed materialization
  therefore cannot change ties. Generated identifiers encode stable
  strings/addresses directly or use an explicitly stable hash; they never use
  runtime `string.GetHashCode()`.
- Dormant maps and authored entries are valid. If the matching grid or a sparse
  physical voxel is absent, its map/entry remains unmaterialized until
  GridForge reports that the required generation/address exists.

### Trailblazer owns navigation state

- The immutable per-grid bake owns topology-local indices, world anchors, and
  authored traversal defaults once. Runtime snapshots add one context-local
  sparse semantic overlay, exact generation identity, GridForge physical-
  presence/blockage masks, and incident composed seams without copying the bake.
- It does not retain `Voxel` references or attach GridForge partitions or
  per-voxel listeners.
- GridForge remains the authority for physical-cell existence, exact runtime
  identity, topology projection, grid lifecycle, and obstacle counts.
- The graph is synchronized from `GridWorld` grid events and exact
  `GridObstacleManager` events.

### One search graph, not four pathfinders

- Storage kind affects map-instance materialization and node lookup only.
- Topology affects native local offsets and edge witnesses only.
- Surveyors consume one topology-neutral edge enumerator and traversal
  evaluator.
- Rectangular and hex behavior is implemented by two concrete internal topology
  kernels selected by each owning map instance. Every local edge enumeration
  dispatches through the current node's instance; a mixed-topology survey changes
  kernels as it crosses instances. Do not publish a general topology adapter API
  and do not duplicate GridForge's internal `IGridTopology`.
- Dense and sparse are not separate A*, flow-field, or volume implementations.

### World-space costs and agent geometry

- All movement, waypoint, flow integration, transition, and route-plan costs
  become non-negative `Fixed64` values.
- Every directed edge uses the one fixed-point `TraversalCost` contract defined
  below, including geometry, edge surcharge, and destination enter cost.
- Negative cell, edge, or transition costs fail map/query admission.
- `Navigator.Size`, `UnitSize`, and mutable `FootPositionAdjust` are replaced by
  an explicit `KinematicBodyShape` containing radius, height, and root-to-foot Y
  offset. It is the authoritative KCC geometry. An immutable
  `NavigationAgentProfile` combines that exact shape with step-up, drop-down,
  arrival tolerance, allowed media, and transition capabilities. A separately
  supplied profile whose shape disagrees with the owning Navigator is rejected
  rather than permitting an unsafe smaller query.
- No agent or request value defaults from cell metrics.

### Correctness before interpolation

- Flow fields initially sample the exact mapped node and its selected edge;
  checked-out guides derive headings from the actual foot position and own any
  portal-leg progress.
- Cubic bilinear flow sampling is deleted. Hex barycentric or rectangular
  anisotropic interpolation may be added later only as separately verified
  optimizations.
- Unconditional Catmull-Rom smoothing is deleted because it can leave a valid
  graph corridor. Path simplification uses a graph-validating navigation ray.

## Non-Goals

- Do not expose GridForge's internal topology interface through Trailblazer.
- Do not make a rectangular array pretend to be axial hex data.
- Do not allocate dense storage proportional to a sparse grid's address-space
  volume.
- Do not infer persistent identity from a recyclable grid slot.
- Do not treat a physical GridForge voxel as automatically navigable.
- Do not treat physical contact alone as permission to traverse.
- Do not make heightmaps topology-aware. They remain independent world-space XZ
  ground-height data.
- Do not preserve old APIs through `Obsolete` members, forwarding overloads,
  legacy serializers, type aliases, or compatibility packages.
- Do not introduce floating-point math, engine APIs, hidden per-frame rebuilds,
  or iteration-order-dependent tie breaks.

## Target Public Model

The exact spelling may be finalized in Phase 0, but the ownership and data flow
are fixed.

```csharp
public readonly struct NavigationCellAddress
{
    public string MapId { get; }
    public VoxelIndex Index { get; }
}

public readonly struct NavigationAreaId
{
    public ushort Value { get; }
}

public readonly struct NavigationCell
{
    public TraversalMedia Media { get; }
    public TraversalCapability RequiredCapabilities { get; }
    public NavigationAreaId Area { get; }
    public Fixed64 EnterCost { get; }
    public Fixed64 RadiusClearance { get; }
    public Fixed64 HeightClearance { get; }
    public NavigationCellFlags Flags { get; }
}

public sealed class NavigationMap
{
    public string MapId { get; }
    public GridConfigurationDescriptor GridBinding { get; }
    public IReadOnlyList<NavigationCellEntry> Cells { get; }
    public IReadOnlyList<NavigationConnection> Connections { get; }
    public IReadOnlyList<TraversalTransitionDefinition> Transitions { get; }
}

public sealed class NavigationMapOverlayDelta
{
    public string MapId { get; }
    public IReadOnlyList<NavigationCellOverlayOperation> Cells { get; }
    public IReadOnlyList<NavigationConnectionOverlayOperation> Connections { get; }
    public IReadOnlyList<TraversalTransitionOverlayOperation> Transitions { get; }
}

public sealed class NavigationOverlayTransaction
{
    public IReadOnlyList<NavigationMapOverlayDelta> Maps { get; }
}

public readonly struct NavigationAgentProfile
{
    public KinematicBodyShape Shape { get; }
    public Fixed64 MaxStepUp { get; }
    public Fixed64 MaxDropDown { get; }
    public Fixed64 ArrivalRadius { get; }
    public TraversalMedia AllowedMedia { get; }
    public TraversalCapability Capabilities { get; }
}

public readonly struct NavigationEndpoint
{
    public Vector3d Position { get; }
    public string? MapId { get; }
    public EndpointResolutionPolicy Resolution { get; }
    public Fixed64 MaxResolutionDistance { get; }
}

public readonly struct PathQuery
{
    public NavigationEndpoint Start { get; }
    public NavigationEndpoint End { get; }
    public NavigationAgentProfile Agent { get; }
    public NavigationAreaPolicyKey AreaPolicy { get; }
    public TraversalIntent Traversal { get; }
    public PathAlgorithm Algorithm { get; }
    public NavigationWorkBudget Budget { get; }
    public bool AllowTransitions { get; }
    public FlowFieldQueryOptions FlowField { get; }
}
```

`KinematicBodyShape` contains the authoritative non-negative radius and positive
height used by both the controller and navigation, plus a non-negative
`RootToFootOffsetY`. `NavigationEndpoint.Position` and graph waypoints are foot
positions: the horizontal footprint is centered there and the occupied vertical
interval is `[footY, footY + height]`. A Navigator converts its host root with
`footY = rootY - RootToFootOffsetY`. Zero radius is allowed for a point body in
local primary-edge tests, but it does not relax cross-grid portal requirements.
A guided Navigator cannot override the shape independently. Shape equality,
query/cache identity, serialization, and post-load validation include all three
values.

`NavigationMap` contains one stable `MapId`, one normalized GridForge configuration
descriptor, and entries sorted by `VoxelIndex`. The core format is sparse for
both GridForge storage kinds: it stores only authored navigation cells.
Builders/importers may provide ergonomic dense rectangular input or explicit
axial hex input, but they all normalize to the same address/value representation.

`NavigationCell.RequiredCapabilities` contains only generic physical traversal
abilities. For example, water may require `Swim`, but Trailblazer never assigns
material-specific hazard semantics to `TraversalMedium.Liquid` or adds concepts
such as heat, poison, or acid immunity to `TraversalCapability`. Host-defined
terrain, material, and hazard meaning uses `NavigationAreaId` and the area-policy
contract below. The complete cell payload is part of traversal evaluation and
cache validity through graph dependency stamps.
Capability admission uses all-of semantics:
`(agent.Capabilities & cell.RequiredCapabilities) ==
cell.RequiredCapabilities`; map/delta/profile validation rejects unknown bits.

### Host-defined navigation areas and committed gameplay behavior

`NavigationAreaId` is a compact, explicitly assigned host value. Zero is the
default area; other values may mean rock, asphalt, snow, lava, sulphuric-acid
gas, sacred ground, or any other application concept. It is navigation metadata,
not a GridForge partition and not a mutable object attached to a voxel. A cell
overlay changes an area's local classification by replacing the complete
`NavigationCell`; it never mutates a global area definition to express a local
gameplay event.

Each `TrailblazerWorldContext` owns a bounded immutable area-policy catalog.
An area policy has a stable `PolicyId + Revision` and one direct-indexed rule per
configured `NavigationAreaId`. The first contract supports only `Allowed` and a
non-negative fixed-point `AdditionalEnterCost`. That is sufficient for an agent
to reject or avoid a host-defined area without introducing game-specific core
flags, while preserving the existing Euclidean lower bound. `NavigationCell.EnterCost`
remains the authored cost shared by all agents; the area rule is query-specific.
The same physical `NavigationAgentProfile` may therefore use different fastest,
safest, stealth, or emergency policies.

Policy registration validates explicit IDs independent of registration order,
copies and freezes rule data, and publishes through the context's deterministic
maintenance boundary. Search resolves `PathQuery.AreaPolicy` before expansion;
the hot path performs a bounds check and indexed value read only. It performs no
string lookup, dictionary lookup, delegate/interface dispatch, allocation, or
host callback. The exact policy identity/revision participates in query and
flow-field cache keys and guide dependencies. A rule replacement uses a new
revision and invalidates dependent results; a cell area change advances the
normal map/overlay dependency stamp.

Pathfinding is speculative, so an area policy may only admit traversal and add
cost. It must never apply damage, consume stamina, play audio, change friction,
unlock a door, or otherwise mutate host state while A*, flow, reachability, or a
navigation ray examines a cell. Phase 8 exposes deterministic committed
cell/area-entry metadata from the controller after movement crosses into the
effective cell. The host consumes that notification to run gameplay behavior.
The committed record includes the stable cell address, area ID, graph/policy
revision, and simulation frame so lockstep consumers can reject stale state or
repath. The controller also exposes its last committed current area as read-only
state before the next fixed step, allowing the host to choose locomotion inputs
for snow, asphalt, water, or other terrain without a search callback. A dropped
ladder remains an overlay-added connection/transition; it is not modeled as a
cell material.

This follows the proven shape used by Recast/Detour, Unity NavMesh, and Unreal:
graph elements carry compact area classifications, query filters provide
admission/cost policy, and links model discontinuous traversal. Trailblazer's
version is fixed-point, immutable, versioned, capacity-bounded, and side-effect
free to meet lockstep and hot-path requirements. Arbitrary per-cell behavior
objects or virtual callbacks are intentionally not part of the first contract.

`NavigationConnection` represents one directed authored physical graph
connection or shortcut and belongs to the map containing its source cell. It
contains a stable map-local ID, a local source index, a full destination
`MapId + VoxelIndex`, entry/exit anchors, portal clearance, any required witness
addresses, a lower-bound certification flag, and non-negative additional cost.
Bidirectional traversal is two directed source-owned records, one in each map;
there is no bidirectional flag or world-level link registry.
Semantic actions such as climbing, jumping, swimming exits, takeoff, or teleporting remain
`TraversalTransitionDefinition` records and are resolved by the transition
planner rather than disguised as ordinary local edges.

Map objects are immutable, but effective navigation state is intentionally
mutable. The public transaction surface is whole-map
`PrepareNavigationMap(map)`, `CommitNavigationMap(prepared, operationSequence,
effectiveFrame)`, `RemoveNavigationMap(mapId, operationSequence,
effectiveFrame)`, plus `ApplyNavigationOverlay(transaction, operationSequence,
effectiveFrame)`. Whole-map replacement is for rebakes, schema/default changes,
and overlay checkpoint compaction; ordinary gameplay uses sparse addressed
overlay deltas. GridForge obstacle events remain the physical blockage path.
There is no world-position or `(x,y,z)` mutation overload.

Each overlay operation has explicit final-state semantics:

- cell `Set` writes a complete `NavigationCell`, `Suppress` removes effective
  traversal even when the bake contains a cell, and `RevertToBake` deletes the
  override/tombstone;
- connection and transition `Upsert` write a complete source-owned definition.
  Reusing a baked local ID intentionally shadows that baked default; `Suppress`
  tombstones the matching baked or overlay ID, and `RevertToBake` deletes the
  override/tombstone and restores the baked definition if one exists;
- a cell `Set` may target any valid in-bounds topology-local `VoxelIndex`, even
  when the sparse bake did not author it. It remains dormant while the matching
  physical sparse voxel/grid generation is absent and materializes when present.

One `NavigationOverlayTransaction` contains one or more per-map deltas sorted by
ordinal `MapId`; each map appears at most once. This is the minimum atomic unit
for a bidirectional cross-map link, whose two source-owned directions live in
different maps. Operations within each delta are canonically sorted and unique
by cell address or source-owned ID. The complete transaction is validated
against one candidate effective state and publishes all-or-nothing; failure in
either direction rolls back every map delta. Duplicate keys/maps reject; queued
transactions coalesce by highest operation sequence per effective key. The same
context sequence/effective-frame, receipt, capacity, deterministic maintenance,
and snapshot-pressure contracts used by map commits apply to overlay transactions.

Replacing a map requires an explicit `OverlayReplacementPolicy`:
`PreserveAndRevalidate` keeps its overlay and rejects the replacement if the
candidate becomes invalid, while `Clear` atomically installs the bake with an
empty overlay. Removing a map removes its overlay; removing only its GridForge
generation keeps both bake and overlay dormant. Hosts may persist/replay the
coalesced overlay value model before restoring guided Navigators; Trailblazer
does not hide overlay state in an independent serializer or registry.

A checkpoint rebake that absorbs effective overlay state is prepared from a
snapshot lease and carries its exact `(MapId, bake identity/version, overlay
high-water operation sequence)` base stamp. A `Clear` commit succeeds only when
the currently published base stamp still matches; any accepted overlay/map
transaction after capture makes the checkpoint receipt reject as `Stale` without
changing the bake or clearing any overlay. The host may capture/retry. This
optimistic rule prevents both losing a post-capture mutation and double-applying
captured state without retaining a prefix-replay subsystem.

Preparation may run off-tick, but produces only an inert, fully validated bake;
worker completion never makes it visible. Map and overlay transactions return a
receipt and enter one context-owned queue ordered by `(effectiveFrame,
operationSequence)`. Sequences are unique and strictly increasing per context,
effective frames are nondecreasing, and invalid/reused/regressing values reject
immediately through the receipt. A commit whose effective-frame boundary has
already begun is late and rejects; a prepared handle never backdates visibility.
The effective frame is the earliest eligible fixed step; canonical budgeted
composition may complete on a later, deterministically derived frame. Candidate
state is the result of folding operations in ascending sequence; implementation
coalescing must be observably equivalent, with the last operation for each
address/ID winning after any earlier `Clear` replacement. A commit adds a new
`MapId` or replaces the existing asset with that same ID. The candidate registry
always contains unique map IDs and rejects two
different map IDs targeting one normalized grid binding. Candidate composition
folds and validates each transaction in sequence. Success updates the unpublished
candidate and leaves its receipt pending until that snapshot publishes as
`Applied`; validation, capacity, or composition failure completes that
transaction as `Rejected` with a precise
status and leaves the candidate exactly as it was before that transaction. Other
valid transactions at the boundary may still apply, but the resulting candidate
publishes once atomically. No publication frame is selected from wall time or
task readiness.

Context settings cap pending transaction count/bytes, retained prepared-bake
bytes, and effective overlay cells/connections/transitions per map and context.
Admission reserves the submitted bytes immediately or rejects the receipt as
`CapacityExceeded`. When coalescing makes an operation observably redundant, its
receipt completes deterministically as `Superseded`; it is not left pending.

## Target Runtime Architecture

```text
TrailblazerWorldContext
  NavigationMapRegistry (persistent paged MapId index + stable bake slots)
    immutable one-grid bakes
      stable baked node slots + adaptive local lookup
      local directed connections + source-owned transitions
    context-local semantic overlays
      persistent cell override/tombstone pages + bounded dynamic slots
      persistent connection/transition override indexes
  NavigationWorldGraph (composed runtime view)
    persistent paged NavigationMapInstance directory
      exact GridForge generation binding
      effective semantic cells/edges + GridForge presence/blockage masks
      implicit native local adjacency
      incident explicit/automatic seam indexes
    compact cross-map seam table
    published version/dependency state
  TraversalEvaluator
  immutable direct-indexed navigation-area policies
  A* / reverse-Dijkstra flow / reachability
  synchronized guide caches and compiled transition indexes
  bounded PathQueryWorkspace pool (one checkout per admitted batch query)
```

### Navigation map instances and node lookup

Each active grid generation that matches an installed map becomes one
`NavigationMapInstance`. The immutable bake owns the static data once:

- `MapId`, normalized `GridConfigurationKey`, topology kind, and metrics;
- cells in stable baked-ordinal order with local `VoxelIndex`, world anchor, and
  authored `NavigationCell`;
- adaptive index-to-baked-ordinal lookup;
- compact local explicit outgoing/incoming edge tables;
- boundary/seam candidate ordinals.

The context overlay stores only semantic differences from the bake: full cell
overrides/tombstones plus source-owned connection/transition overrides. The
runtime instance composes those values with exact world/grid generation identity,
compact GridForge physical-presence/blockage masks, and incident seam indexes.
It derives exact node identity from the instance generation plus local index when
needed; it does not copy unchanged baked anchors, cells, indices, or edge geometry
into a second node array.

Baked ordinals never move during the map asset's lifetime. A cell first created
by an overlay receives a bounded, persistent, non-reused dynamic slot in an
index keyed by `VoxelIndex`; its anchor/prism is derived once from the normalized
GridForge descriptor through the same topology geometry contract as the baker.
Addresses introduced in the same transaction are assigned slots in canonical
index order. Revert/suppress may empty the slot but
does not recycle its handle while the bake/overlay generation or a retired
snapshot can reference it. Canonical identity and ties always use
`(MapId, VoxelIndex)`, never either slot family. A deliberate whole-map
checkpoint with `OverlayReplacementPolicy.Clear` may absorb effective dynamic
cells into a new bake and reset the dynamic-slot budget.

GridForge sparse add/remove toggles the physical mask for an effective baked or
dynamic cell and updates only native degree, incident explicit edges, and
incident seams. A semantic cell Set/Suppress/Revert updates that same local
neighborhood and any generated transitions. Neither path shifts, swap-removes,
re-sorts, or compacts all mapped nodes.

Choose the local lookup representation by authored navigation density and byte
cost, not by GridForge storage kind. Use a direct address-volume ordinal table
only below Phase 0's frozen density/byte threshold; otherwise use a compact
fixed-capacity hash or sorted lookup proven by benchmarks. Dense physical grids
may have sparse navigation maps, while sparse physical grids still use the same
baked stable-slot model. A separate bounded persistent overlay index prevents a
small mutation from rebuilding or changing the bake's lookup representation.
GridForge storage controls presence synchronization, not search semantics.

Native same-grid adjacency remains implicit. The edge enumerator combines:

1. topology-native primary offsets resolved through the instance's node lookup;
2. sorted explicit cross-grid/contact connections;
3. semantic transitions only when the higher-level planner asks for them.

This avoids a 20/26-element object array per node and a duplicated live node
object while giving A*, flow, and reachability exactly the same legal edges.
The enumerator supports outgoing and incoming traversal; reverse flow
integration must not assume every edge is bidirectional.

### Topology kernels

The internal rectangular and hex kernels consume GridForge's public direction
spans and operate on topology-local indices. They do not reproduce world
projection formulas.

The safe built-in graph uses positive-area, face-adjacent primary movement.
Diagonal, elevated-lateral, and three-axis shortcuts are not automatically
admitted from direction offsets. A map builder may emit them as explicit
`NavigationConnection` records only when it also provides the required witness
chain, portal/swept-corridor clearance, and cost certification. This removes
ambiguous edge/corner traversal from the runtime default.

Rectangular surface rules:

- allow four same-layer planar primary directions;
- exclude pure `Above`/`Below` as ordinary walking edges.
- represent planar diagonal, elevated, and three-axis walking shortcuts as
  certified explicit connections. A rectangular three-axis shortcut must
  certify every proper-subset witness or an explicit deterministic legal
  decomposition, not merely the three primary axes.

Hex surface rules:

- allow all six same-layer planar axial directions;
- exclude pure `Above`/`Below` as ordinary walking edges;
- represent above/below lateral steps as certified explicit connections;
- do not invent rectangular corner witnesses for planar hex movement.

Rectangular volume rules:

- permit the six face-adjacent primary directions when media, clearance, and
  blockers allow;
- represent diagonal/multi-axis shortcuts as certified explicit connections
  with all proper-subset witnesses or an explicit deterministic decomposition.

Hex volume rules:

- permit the six planar plus two vertical face-adjacent primary directions when
  media, clearance, and blockers allow;
- represent the twelve vertical diagonals as certified explicit connections
  with their vertical/planar witnesses and swept clearance;
- pure vertical movement is valid.

Both pointy-top and flat-top hex instances use the same axial offset order. Actual
world positions copied from GridForge determine edge vectors and costs.

### Cross-grid seams

Same-grid offsets are never used to infer mixed-grid geometry. Cross-grid seam
candidates may be discovered with `Voxel.GetNeighborsInto(...)` using same- and
mixed-topology scopes, but the current AABB-overlap results are never promoted
to graph edges, including for zero-radius agents.

GridForge's current contact result reports candidate voxels based on footprint
AABB overlap. It does not report contact dimension, normal, portal span, or
usable clearance. That is insufficient to automatically admit a nonzero-radius
agent across rectangular/hex or differing-metric seams.

Before automatic seam admission lands, add the smallest concrete upstream
GridForge contact-manifold result proven by a Phase 0 geometry spike. It must
carry exact participants, true-footprint rather than broad-phase intersection,
touching-versus-volumetric-overlap classification, and sufficient manifold
bounds/endpoints to calculate positive portal width/height. Do not freeze a
`Normal + Width + Height` shape until rectangular/hex partial contacts prove it
is sufficient. This is a concrete query result, not a new public topology
abstraction.

Until that upstream contract exists, every cross-grid route requires an
explicit `NavigationConnection`. Once it exists, automatic admission is limited
to a precisely defined positive-dimensional portal kind; edge-only,
corner-only, AABB-only, and volumetric-overlap contacts remain rejected for all
agent sizes. Same-topology/different-metric seams follow the same rule.

Authored cross-map connections are source-owned directed records. Their global
identity is `(source MapId, local connection ID)`. If the destination map/grid or
any endpoint/witness cell is dormant, the record remains authored but inactive;
it materializes only when every exact runtime dependency exists. Removing or
replacing either map touches only connections indexed as incident to that
`MapId`.

Automatic exact-contact portals are context-generated rather than stored in a
map. The context discovers candidate grid pairs through GridForge's spatial
broad phase, evaluates boundary candidates once from the canonical lower
endpoint address, and stores each exact portal geometry once in the cross-map
seam table. Canonical ownership deduplicates geometry only. The composer tests
each direction independently only for agent-independent structural contact and
hard authored directional flags, then emits a separate compact directed edge
reference into the appropriate outgoing and incoming indexes for every
structurally permitted direction. `TraversalEvaluator` applies the current
profile's step/drop limits, clearance, media/capability, traversal intent,
blockage, destination-enter cost, and extra cost per query. Adding a map/grid
must not compare it with every active grid or every cell. Grid removal visits
only its incident seams.

Exact convex clipping is a composition/materialization cost, never a search
expansion cost. A* and flow read precomputed seam geometry by compact edge
reference.

### GridForge integration boundary

The phrase “true positive-area contact geometry” refers to a narrow-phase result
that GridForge does not currently expose. `Voxel.GetNeighborsInto(...)` and
`FindOverlappingGridsInto(...)` remain useful broad-phase candidate queries, but
their topology AABBs cannot distinguish true rectangular/hex footprints or
classify point, line, face, and volumetric overlap.

Phase 0 adds these concrete GridForge-owned contracts:

- `GridCellPrism`: exact ordered horizontal convex polygon, exact vertical
  interval, and exact cell identity;
- `VoxelContactManifold`: exact participants, source-to-target orientation,
  `Separated`/`Point`/`Edge`/`Face`/`VolumeOverlap` classification, checked
  fixed-point area, and face geometry;
- for a vertical face, exact horizontal contact-segment endpoints and vertical
  overlap interval; for a horizontal face, the exact convex overlap polygon on
  the shared Y plane;
- allocation-free caller-owned bulk contact output/scratch for a candidate grid
  pair;
- the ordered trace buckets and atomic baseline/change-feed contracts described
  elsewhere in this plan.

Only a two-dimensional `Face` manifold with checked area greater than zero is
an automatic portal candidate. Point/edge contact is insufficient, and volume
overlap is not automatically traversable. The agent-shape inset must still leave
a non-empty usable portal.

A GridForge-to-Trailblazer friend relationship is allowed when it keeps a
specialized high-performance integration seam out of GridForge's general public
API. If used, GridForge adds `[InternalsVisibleTo("Trailblazer")]`, but
Trailblazer may reference only a dedicated internal navigation bridge namespace
enforced by an architecture test. It must not reach directly into
`IGridTopology`, concrete topology classes, `TopologyVoxelAabb`, locks, storage
implementations, or unrelated internals.

Generally useful deterministic geometry results—the normalized descriptor,
exact prisms/manifolds, and ordered trace results—should remain public GridForge
value/query contracts. A friend-only bridge is most defensible for the atomic
address-filtered baseline/change-feed and allocation-free bulk seams. Phase 0 may
use friend access for a spike, then freezes the narrow boundary from API-usage
and benchmark evidence. Friend access is not itself a performance optimization;
it is an API/coupling choice and must not substitute for a GridForge method that
acquires its own private state lock correctly.

### Traversal evaluator and clearance

`TraversalEvaluator` is the single authority used by endpoint admission, A*,
flow integration, reachability, navigation rays, and transition endpoints. It
checks:

- node presence and current graph generation;
- cell media and agent capabilities;
- the resolved navigation-area policy's admission and additional enter cost;
- node and portal radius/height clearance;
- GridForge blockage mirrored into graph state;
- topology witness cells for diagonal/vertical-diagonal movement;
- step-up/drop-down limits from actual world Y delta;
- one-way and semantic edge policy;
- non-negative fixed-point movement cost.

Clearance is expressed in world units and supplied by the authored/baked map.
`NavigationCell.RadiusClearance` is the conservative horizontal radius available
around the cell anchor; `HeightClearance` is the inclusive vertical free span
above the anchor. An implicit primary edge is usable only when both endpoint
cell clearances fit the agent and the topology's positive-area shared face fits
the same shape. Explicit connections store their own inclusive portal radius
and height, and their witness cells must also fit. Endpoint clearance never
substitutes for a narrower portal or swept corridor.

Trailblazer must not infer general clearance from `N` voxel hops. Whole-cell
GridForge obstacles disable their mapped nodes. If a dynamic obstacle narrows a
corridor without fully blocking a node or portal, the host must rebake/replace
the affected map; the current GridForge obstacle event
does not contain enough shape information to infer that narrowing safely.

Anchor clearance and portal width do not by themselves certify an arbitrary
string-pull segment. Each topology kernel also consumes GridForge-owned exact
prism geometry and implements a deterministic swept-segment certificate over
the selected cells' navigable union. A traversed portal plane is not treated as
a solid cell boundary: within each corridor prism, only non-portal boundaries
are inset by the body radius, while the portal cross-section is shrunk
tangentially and vertically by the body shape. The complete body vertical
interval must remain in the union's admitted free span. This permits a body to
straddle two valid cells while crossing their shared face, but still rejects
rectangular or hex corner clipping. If the union, interval, or shrunken portal
cannot be proven with fixed-point geometry, the ray rejects the shortcut; it
never falls back to a centerline-only test.

A future GridForge-backed map baker may calculate cell/portal and shortcut
clearance from
topology-aware coverage, but it is tooling layered on this contract, not a
second runtime representation.

### Versions and lifecycle

The context publishes immutable `NavigationGraphSnapshot` objects. A snapshot
references immutable baked maps, immutable composition/seam tables, persistent
semantic overlay pages, and copy-on-write GridForge presence/blockage pages.
Off-tick work may create inert map bakes only. Runtime composition and event
maintenance advance in canonical order under deterministic work counters; a
worker's completion time never selects a publication frame. Host map/overlay
transactions carry an explicit effective frame and operation sequence. At each fixed-step
maintenance boundary the context performs the configured number of deterministic
work units (blocking that simulation step if necessary to finish those units),
carries remaining work forward, and keeps scopes affected by unapplied mandatory
GridForge safety events fail-closed until their candidate is complete. A pending
host map/overlay transaction leaves the prior snapshot active until it applies or rejects.
The context then performs one short atomic snapshot swap. The same inputs,
operation sequences, and budgets therefore expose the same snapshot on the same
simulation frame even when worker delays differ. It never mutates a snapshot
visible to a reader.

The immutable root is a persistent paged directory of map instances, component
records, dependency indexes, and seam-index roots; changing one overlay bit does
not copy an `O(active maps)` array. Overlay updates copy only touched semantic/
physical pages, incident index pages, plus `O(log pages)` directory nodes and
lazily reject cache stamps.
Structural additions can union component records locally. A removal that splits
a component honestly costs `O(Vc + Ec)` over the affected old effective node
component, where `Ec` includes implicit native, explicit, seam, and transition
edges. It is processed under the maintenance budget and remains fail-closed
through carryover; no initial dynamic connectivity structure is justified
without benchmark evidence.

Use three scoped correctness clocks from the first implementation:

- `CompositionVersion` changes when the registered map set, an exact bound grid
  generation, automatic cross-map seam structure, or baked map structure changes;
- each semantic overlay page has a `MapOverlayPageVersion`. Endpoint resolution
  records the pages covering its bounded candidate region, including pages where
  a later Set could materialize a previously unauthored cell. Page identity is a
  deterministic topology-local address tile derived from `VoxelIndex`, so an
  unallocated page still has a stable zero version and no address-volume table;
- the effective composition precomputes weakly connected structural components
  over baked-plus-overlay cells, explicit connections, and transitions while
  ignoring only GridForge physical presence/blockage. Each has a
  `TraversalComponentVersion` incremented when member media/cost/capability,
  physical presence/blockage, or effective edges change. An overlay node/edge/
  transition addition invalidates and merges incident components; removal
  invalidates the old component and budget-splits it before reopening. An
  isolated new cell creates a new component without invalidating unrelated ones.

Search results and guides store a `GraphDependencyStamp`: the composition
version, sorted endpoint-candidate overlay page versions, and sorted
`(component ID, TraversalComponentVersion)` pairs for the resolved traversal
component and every component intersecting either endpoint's bounded candidate
search. A positive path cannot become non-optimal because an unexpanded node in
its effective structural component changes unnoticed. Negative/budget results
cover the whole structurally reachable component, not only expanded instances.
A local obstacle or semantic overlay change therefore invalidates the affected
component/candidate pages but not a disconnected map set. A baked map/grid/
automatic-seam change conservatively changes `CompositionVersion`.

Phase 0 measures component size and invalidation/repath waves. Finer per-instance
or seam/corridor dependencies land before Phase 2 only if the absolute gate
fails and a proof shows additions/removals/cost changes outside the recorded
set cannot alter endpoint selection, reachability, or optimality. “Only nodes
A* expanded” is explicitly not a valid dependency proof.

Immutable request identity always includes both exact endpoints for equality,
diagnostics, and guide ownership, but survey payload keys are algorithm
specific. A* keys include both endpoints. Destination-centric flow payload keys
exclude the origin and include exact destination, complete agent profile,
traversal intent, flow options, and the complete `NavigationWorkBudget` value;
origin is a coverage
requirement checked atomically at checkout. Cached payloads validate their
dependency stamp against the current snapshot. Effective baked, overlay, and
generated transition indexes publish with the graph snapshot, so there is no
independently mutable registry version.

A search acquires an O(1) ref-counted snapshot lease for resolution, expansion,
and result copy. Each concurrent query also checks out its own pooled
`PathQueryWorkspace`; no mutable scratch is shared. A guide acquires a short
snapshot lease for acquisition, sample, waypoint advance, and steering. It
validates its dependency stamp before work and again against the currently
published snapshot before returning. If publication raced the operation and a
dependency changed, it returns `Stale`; the immutable old snapshot still makes
the operation memory-safe. Retired snapshots/pages remain alive only until all
leases return and count against a configured retained-byte budget.

Deterministic parallel execution is exposed as fixed `PathQueryBatch` and
`GuideSampleBatch` inputs. A batch is assigned a context sequence, sorts and
reserves snapshot leases, workspaces, and each query budget's maximum active
result/payload bytes by stable operation ordinal before launching workers. A
caller-owned result buffer may satisfy that reservation explicitly. The batch
returns `CapacityExceeded` to the deterministic suffix that does not fit, so
worker completion order cannot decide which result is retained.
Ad hoc query/guide calls are serialized through the context admission gate;
hosts do not race them from arbitrary threads. Context settings cap concurrent
snapshot/query leases, active workspace bytes, and retained workspace-pool
bytes. They also cap batch items, submitted descriptor bytes, and sorting-scratch
bytes before sorting begins. Pool trimming uses deterministic largest-byte, then
stable-slot order. These aggregate limits are enforced in addition to each
query's work budget.

Each context's guide caches have a dedicated synchronization gate. Lookup,
checkout/reference-count changes, return, LRU mutation, invalidation, detached
active tracking, and partial-flow promotion are atomic under that gate. Result
payloads are immutable while leased. Concurrent same-key misses may compute
outside the gate under snapshot leases. Publication rechecks key and dependency
stamp against the current snapshot under the gate. For exact A* payload keys,
it keeps the first
valid deterministic result and checks that result out for every loser. For flow,
it rechecks the caller's origin coverage and applies the canonical-prefix
promotion rule below; a smaller payload is never handed to a caller it does not
cover. Since survey output is deterministic and cache presence may affect
performance but never query semantics, publication timing cannot change a
route or status. Lock order is snapshot lease before the cache gate. Code
holding the cache gate never acquires a snapshot lease.

The cache is bounded primarily by bytes, not entry count. Configure maximum
reusable bytes, maximum single-payload bytes, and a secondary entry limit; track
cached, leased, detached, retired-snapshot, and discarded-duplicate bytes
separately. Eviction is deterministic byte-weighted LRU. Active leased payloads
remain valid but cannot be promoted into reusable storage when doing so would
exceed the reusable budget; exceeding the separately configured active/retired
retention ceiling returns an explicit capacity status instead of allowing
unbounded memory growth.

Snapshot generations are separately capped. Mandatory GridForge grid, sparse,
and obstacle changes are never skipped and cannot return a status to their
producer. If publishing them would exceed the retired-generation/byte ceiling,
the context closes query/guide admission, coalesces one pending final-state
candidate, waits at the fixed-step barrier for the already-bounded internal
snapshot leases to drain, evicts invalid reusable payloads, and publishes the
safety update before simulation advances. Configuration must reserve enough
memory for the current root, one compact fail-closed root, and one candidate.
If a full candidate still cannot fit, affected map instances remain dormant in
the compact root and are deterministically rebuilt in budgeted chunks. Map
commit/remove capacity failure instead rejects its receipt and retains the old
registry. Guides never retain snapshot leases between calls, so a leaked or
unbounded external lease is not part of the public model.

Lifecycle rules:

- map before grid: keep that map dormant and materialize it when a matching
  configuration appears;
- grid before map: capture the atomic GridForge baseline and materialize
  matching entries from it during map install, then process only later events;
- sparse add: toggle physical presence only when the bake-plus-overlay effective
  state contains that address, then rebuild the changed node's native/contact
  neighborhood;
- sparse remove: clear physical presence and dematerialize incident edges without
  deleting the baked/overlay semantic cell or retaining a stale `Voxel` reference;
- grid remove: retire only the exact-generation map instance and its incident
  seams using the immutable event snapshot; retain the map and overlay dormant;
- grid respawn: build new exact identities from persistent bake/overlay addresses;
- obstacle add/remove/clear: update exact mapped node blockage, ignore foreign
  worlds, and do not treat the subsequent generic `GridChanged` notification as
  a structural rebuild;
- overlay delta while grid absent: update persistent semantic state and defer
  physical materialization until a matching generation/address exists;
- map removal: retire its instance, semantic overlay, and incident dependencies;
  leave the GridForge grid active but non-navigable;
- reset/dispose: detach world/obstacle event handlers and clear maps, snapshots,
  caches, transition bindings, and scratch/index state for that context only.

Phase 0 adds a GridForge change envelope with a monotonic world-owned sequence,
immutable before/after or final-state payload, and a cause ID shared by a
specific event and any generic `GridChanged` notification it induces. Map and
overlay transactions use the equivalent context-owned envelope. At maintenance start,
Trailblazer atomically captures each source's high-water mark and detaches only
the queue prefixes through those marks. Later events are deferred to the next
batch, and reconciliation never queries live state newer than the detached
payloads. The batch reconciles final state instead of replaying incidental
callback order:

GridForge also exposes an atomic navigation baseline captured under the same
state barrier and requested with `(GridConfigurationKey, sorted requested
VoxelIndex span)`. It returns its high-water sequence, the matching exact active
generation if any, and presence/obstacle count only for those requested
addresses; dense presence is implicit. It does not enumerate/copy every physical
voxel in the grid. Context initialization first subscribes to the immutable
change feed, then captures this baseline. It discards an envelope at or below
the baseline high-water mark only when that envelope's configuration, exact
generation, and address are within the represented baseline scope. Unrelated
map/grid/address envelopes remain queued and are processed normally regardless
of sequence. Only a bulk startup baseline that completely represents a scope may
advance that scope's cursor. GridForge may expose subscription and baseline
cursor capture as one atomic operation. This protocol is used for both context
startup, map install, and an overlay delta introducing new addresses; there is no
unsubscribed gap and no scan of unrelated grids or unrequested addresses.

Ingress is bounded before reconciliation. Exact-address changes coalesce to
final state in a fixed-capacity context table keyed by exact world/grid
generation, `VoxelIndex`, and state domain (presence, obstacle, or other frozen
domain); only the latest sequence/cause metadata is retained for pairing and
diagnostics. Repeated changes therefore do not grow the queue. Settings cap
pending entries and bytes. On unique
event overflow the owning map scope is marked `ResnapshotRequired`, its instance
is made fail-closed at the next maintenance boundary, detailed envelopes for
that scope are dropped, and an address-filtered baseline rebuild advances in
stable effective-address chunks from one immutable baseline/cursor. Later events
only update the scope's observed high-water marker. The candidate may reopen the
scope only if the observed high-water still equals its baseline cursor after the
full pass. Otherwise it discards the unpublished candidate, captures a newer
baseline/cursor, and restarts; repeated overflow/churn remains fail-closed rather
than publishing a mixed-time overlay. This bounds retained deltas without losing
an address change that occurs after its earlier chunk was processed.

`MaintenanceWorkBudget` counts consumed envelopes, baseline addresses, overlay
slots, seam candidates, component nodes, implicit native edges, explicit/seam/
transition edges, dependency-index entries, and cache invalidations. Canonically sorted unfinished work carries to the next
frame; affected work remains fail-closed, while unrelated published instances
continue serving. Work completion is based on counters, never elapsed time.

1. map lifecycle and semantic overlay transactions folded by context sequence,
   then coalesced to final state by ordinal `MapId` plus address/source-owned ID;
2. grid-generation removals, which dominate pending voxel changes for that
   generation;
3. grid-generation additions;
4. sparse physical presence coalesced to the final state per exact address;
5. obstacle count coalesced to the final state per surviving exact node;
6. local/contact/overlay edge and generated/effective transition dependency rebuild;
7. update composition/component versions, build the candidate immutable snapshot,
   invalidate dependency-indexed cache entries, and atomically publish once.

The generic `GridChanged` paired by the same cause ID with a recognized exact
obstacle event is not processed again as structure. An unrelated generic change
is never suppressed merely because it touched the same grid in the same frame.
GridForge's wrapping `uint` grid version is diagnostic/reconciliation input, not
the causal sequence or cache identity.

## Query And Search Redesign

### Immutable query intent

Delete the mutable request hierarchy that stores live `Voxel` references. A
public `PathQuery` contains immutable intent. An internal resolved query adds:

- exact start/end `WorldVoxelIndex` values;
- preserved exact origin/destination points;
- the captured immutable snapshot and dependency stamp;
- resolved traversal domain and transition plan.

`MaxPathSearchRange` becomes an explicit `NavigationWorkBudget` and one mutable
internal meter is shared by the entire public request, including every hybrid,
recovery, and staged subsearch. It covers address/lookup probes before candidate
yield, endpoint candidates, expanded nodes, evaluated edges, connection
witness/polyline legs, transition candidates and pairs, staged-leg attempts,
trace intervals, covered-voxel intervals, and simplification rays. A nested leg
never receives a fresh full budget. Every counter has a finite non-negative
limit and a distinct diagnostic; exhaustion returns `BudgetExceeded` with the
exhausted counter. GridForge bulk lookup, coverage, contact, and trace APIs
accept the relevant remaining count plus caller-owned output/scratch and stop
before materializing work that would exceed it. Defaults come from context
settings frozen at initialization, never cell metrics or only the start/end
grids. Strict endpoint lookup remains O(1)/O(log M); bounded nearest lookup never
enumerates sparse address-space volume. Flow prefix construction uses the exact
settle key `(cost, ordinal)` as its cutoff and returns `BudgetExceeded` rather
than exceeding a node/edge limit to complete an equal-cost ring.

Each endpoint independently specifies a policy such as `Strict` or
`NearestNavigable`, an optional `MapId` selector, and a maximum fixed-point
resolution distance. The distance participates in exact query/cache identity so
a sparse-hole endpoint can never snap arbitrarily far.

`TraversalIntent` names the requested starting domain/current medium and target
domain; allowed media on the profile are capabilities, not routing precedence.
For a multi-media cell the explicit intent wins, followed by a documented fixed
`Solid`, `Liquid`, `Gas` precedence only when the query requests automatic
selection. Algorithm-specific options are value objects. In particular,
`FlowFieldQueryOptions` owns non-negative fixed-point extra integration cost;
payload promotion is the fixed cache invariant below, not a caller-selectable
policy.

The public guide service returns an explicit status/result lease model covering
at least `Success`, `NoMap`, `InvalidProfile`, `InvalidStart`, `InvalidEnd`,
`NoPath`, `BudgetExceeded`, `CostOverflow`, `CapacityExceeded`, and `Stale`.
Queries never encode these states through partially initialized mutable objects.

Resolution examines mapped instances rather than letting a single
`GridWorld.TryGetGridAndVoxel(position)` call choose an overlapping grid.
When `NavigationEndpoint.MapId` is non-null, it is an eligibility filter:
discard every candidate from another map before ranking. Candidate ranking
within the eligible set is:

1. fixed-point distance to the exact query point;
2. stable authored `NavigationCellAddress` order.

Start with GridForge's topology-aware covered-voxel query over the endpoint's
bounded world-space search box, then filter exact mapped nodes and rank them by
the rules above. Phase 0 benchmarks this against a simple graph scan and a
map-owned spatial index. Add a second endpoint index only if it materially
improves the intended workload after accounting for memory and sparse mutation
cost. Runtime identities validate candidates but never decide deterministic
ordering. Exact flow sampling in overlapping instances uses the same map and
candidate-ranking contract.

### Weighted A*

Replace `PathHeap<TNode>` integer metadata with fixed-point costs and an
explicit canonical node-key tie break:

1. lowest total estimated cost;
2. lowest heuristic cost;
3. lowest `(ordinal MapId, lexicographic VoxelIndex)` canonical node key.

Heaps, fields, and search metadata use compact `(instance ordinal, baked node
ordinal)` handles that resolve the canonical key for comparison; the compact
ordinals themselves never decide a cross-map tie. Implicit native enumeration is fixed-degree and allocation-
free; explicit outgoing/incoming indexes are precompiled. Query workspaces choose
between pooled compact hash metadata and lazily cleared generation-stamped
ordinal pages from Phase 0 measurements. A short search must not rent/clear
arrays sized to every node in the world.

Every algorithm consumes one directed function:

```text
TraversalCost(source, edge, target) =
    Distance(source anchor, portal entry)
  + Distance(portal entry, portal exit)
  + Distance(portal exit, target anchor)
  + edge non-negative surcharge
  + target EnterCost
```

For a native primary edge, entry/exit collapse to the shared face crossing and
the geometric terms equal the certified physical route. For reverse integration
over `predecessor -> current`, evaluate
`TraversalCost(predecessor, edge, current)`; do not reverse the asymmetric
target enter cost. A*, flow integration, flow direction choice, reachability,
and staged physical segment totals use this exact function once.

Custom connection anchors must lie inside their declared source/destination
cell footprints. An edge is Euclidean-lower-bound certified only when its
computed traversal cost is at least the direct source-anchor-to-target-anchor
distance for every admitted profile. Otherwise the whole reachable local search
uses a zero heuristic. The certified heuristic uses a proven fixed-point floor
of Euclidean distance; if raw-value overflow or lower-bound certification cannot
be proven, it falls back to zero. Cost addition is checked, and overflow returns
`CostOverflow` rather than wrapping or saturating.

Semantic/nonlocal transitions stay in the staged hybrid planner and do not
silently invalidate a local A* heuristic.

Remove public `HeuristicMethod`, straight/diagonal constants, and duplicated
surface/volume A* implementations. One search core consumes the graph edge
enumerator plus a traversal-domain selector.

### Flow fields

Flow integration becomes weighted reverse Dijkstra over incoming edges:

```text
integration[goal] = 0
candidate = integration[current]
          + TraversalCost(predecessor, edge, current)
```

The selected edge at a node is the outgoing legal edge that minimizes
`TraversalCost(node, edge, target) + integration[target]`, with canonical edge
order as the tie break. The shared field stores only a compact
`SelectedEdgeRef` per settled node, not a direction, object, vector array, or
copied polyline. Native/explicit/seam portal geometry exists once in the bake or
composition seam table and is resolved by edge reference under the snapshot
lease.

Each checked-out flow guide owns a cursor identified by `(current canonical node
key, selected edge locator, leg ordinal)`. Sampling accepts the agent's
actual foot position, resolves its exact current node, and aims at the first
not-yet-reached point of `portal entry -> portal exit -> target anchor`,
skipping coincident points. A node/selected-edge mismatch discards the old leg
ordinal and deterministically rebases the cursor from the actual position.
Signed portal/corridor progress also rebases backward when an agent retreats or
re-enters; proximity alone never advances a leg. A leg ordinal is never carried
onto another edge. Before returning a normalized heading, sampling certifies the
actual-position-to-target segment against the selected edge corridor using the
navigation-ray swept-body rules. The guide advances its local cursor only after
deterministic directed reach/crossing tests; cached field data remains immutable.
This handles native zero-thickness portals, off-center starts, off-axis partial
portals, and multi-leg explicit connections without steering outside the
certified corridor. If the actual position cannot reach the next leg safely,
sampling returns `LocalRecoveryRequired`; it never substitutes an anchor-based
parallel vector. NavSteering then requests a direct local guide/repath against
the same dependency snapshot or reports the failure if no recovery exists.

Every guide sample has a finite `GuideSampleWorkBudget`, supplied per call or as
one shared mutable meter for a `GuideSampleBatch`. It counts current-node lookup
probes, cursor-leg scans/rebases, portal/prism checks, trace intervals, and local
recovery attempts. Sampling uses caller-owned scratch and returns
`BudgetExceeded` without advancing the cursor when any counter would overflow.
A batch never resets a shared meter per guide, and sample-budget exhaustion does
not mutate the immutable field or cache state.

Keep destination-centric caching. A reusable partial field is valid only if it
contains the new origin node. All fields for one payload key are canonical
prefixes of the same reverse-Dijkstra settle order `(integration cost, stable
canonical node key)`. Extra coverage is a non-negative fixed-point integration
cost beyond the requested origin; the survey settles every queued node through
that threshold, including the complete equal-cost ordinal tie group. Each
result records its last settled order key/count and whether the reachable
component is complete. Consequently two valid partial results for one key are
nested, never incomparable; a non-prefix mismatch is an internal invariant
failure, not an alternate cache policy.

A closed field is not resumed. On an uncovered-origin miss, recompute the
required prefix from the destination under the same payload key. Publication
under the cache gate first rechecks whether another field now covers the origin.
If so, use it and discard the duplicate. Otherwise atomically promote the
longer canonical prefix. The smaller result becomes a detached tracked result
until all active leases return; the newly covering caller checks out the larger
result. A budget failure or complete component that excludes the origin returns
the corresponding status and leaves a valid smaller cached prefix in place.
Repeated far requests, simultaneous and concurrent different-origin near/far
requests, equal-threshold ties, active smaller leases, and budget/no-path
failure are pinned by tests.
Delete `FlowFieldSamplingGrid` and rectangular direction-index lookup. Exact
node/selected-edge sampling with guide-local portal progression is the required
first implementation for every matrix member.

### Reachability

Rebuild reachability over the same edge enumerator and evaluator used by A*.
Its payload validates the graph dependency stamp and complete agent profile. Do
not retain a second copy of rectangular diagonal legality. If profiling shows
no value after the shared A* core lands, remove reachability preflight instead
of preserving it for historical reasons.

### Navigation ray and path simplification

GridForge's tracer is topology-aware but intentionally omits absent sparse
voxels. Trailblazer must not accept line of sight merely because every returned
physical voxel is navigable.

The navigation ray consumes ordered trace interval buckets, not a flat voxel
sequence. Each upstream step carries exact grid/runtime identity, topology-local
address, physical-or-missing state, true-footprint intersection, `tEnter`,
`tExit`, and a simultaneous interval/group identity for overlap/boundary ties.

The navigation ray must:

- resolve start and end graph nodes;
- prove that the trace reaches the end node;
- find a deterministic graph-connected chain between successive interval
  buckets without requiring arbitrary peer cells inside one tied bucket to be
  mutually adjacent;
- prove continuous parametric coverage from start to end;
- validate media, blockers, witnesses, and explicit seams;
- certify the complete swept body against the selected rectangular/hex prism
  union: inset non-portal boundaries and shrink traversed portals tangentially
  and vertically;
- reject a skipped sparse address or unselected overlapping map.

The current `GridTracer.TraceLineInto` does not expose these interval/group
semantics or exact convex prism geometry, so Phase 0 adds a narrow upstream
ordered trace-step result plus the fixed-point footprint/vertical-bound data
needed for inset tests. Do not reimplement GridForge hex projection inside
Trailblazer.

Use this same ray for direct-path checks and waypoint string pulling. Curved
presentation may be added later only if every generated segment passes the
navigation ray. String pulling consumes `NavigationWorkBudget` counters and
returns the valid less-simplified prefix/path when its ray/interval budget is
exhausted; it never performs unbounded quadratic LOS attempts.

## Volume And Transition Model

Effective volume truth is the immutable per-grid bake plus its context-local
semantic overlay, masked by GridForge physical presence/blockage. Delete runtime
`VolumeMediumRules`; predicates are opaque, globally invalidate caches, and can
change without an addressable deterministic event. Hosts may still classify
their gameplay state however they choose, but they publish the explicit final
`NavigationCell` values through an overlay delta. Mining a cell, filling it with
water/lava, draining it, or reverting it to baked defaults therefore touches the
addressed cells and incident graph state rather than replacing the map.

Cell media, required capabilities, enter cost, clearance, and flags are one
effective payload. A delta may materialize traversal at an in-bounds address the
bake omitted. GridForge presence remains independent: an effective semantic cell
on an absent sparse voxel is dormant, not deleted, and reappears when that exact
physical address exists. Hosts cannot mutate media through a second rules
registry, so snapshot publication remains the single cache/guide staleness clock.

Surface and volume searches share nodes and search infrastructure but select
different native edge rules through the traversal domain. Hybrid planning
continues to stage local surface/volume segments around semantic transitions.
Generated swim/climb/etc. transitions compile from the effective baked-plus-
overlay cells and actual graph contacts. A cell delta refreshes only generated
transitions incident to the changed address and publishes their indexes in the
same snapshot transaction.

Persistent transition anchors become:

```text
MapId
VoxelIndex
optional exact point override
TraversalMedium
```

They bind to fresh exact-generation graph nodes at runtime. Generated
transitions compile from map addresses and actual graph contacts, not Cartesian
chart increments. Automatic IDs derive from source/destination map IDs, endpoint
indices, medium/type, and directionality; inserting an earlier map entry does
not rename an unchanged transition. Authored semantic transitions require an
explicit stable map-local ID. Global identity is `(source MapId, local
transition ID)`, so local IDs may repeat in different source maps. Any
authored/authored, generated/generated, or authored/generated collision within
the same source-map scope fails the graph build transaction. Transition and
total route costs become `Fixed64`.

There is no independently mutable transition registry. Baked definitions provide
defaults; runtime `Upsert`/`Suppress`/`RevertToBake` transition overlay operations
are the public mutation path. Dropping a ladder upserts one or more source-owned
climb transitions (and any required physical connection); destroying it
suppresses/reverts those IDs. A stable overlay ID may shadow its baked default;
duplicate overlay keys and overlay/generated collisions reject in the same
source-map scope. The changed outgoing/incoming pages,
generated incident transitions, affected components, dependency stamps, and
cache invalidations publish atomically with the graph snapshot, so hybrid guides
need no second staleness clock.

Delete `TrailblazerTransitionService.Register/Unregister`, the current global
manual/generated registry ownership model, and its mutable query facade. Replace
it with `ApplyNavigationOverlay` on the context pathing service plus read-only
effective transition diagnostics under a graph lease. Whole-map replacement
remains available for rebaking/checkpointing, not moment-to-moment transition
mutation.

## Kinematic Controller Boundary

Keep topology out of `NavMotor`, `NavTurning`, locomotion implementations, and
world-space movement integration.

Path-facing changes are intentional:

- `ISteer` replaces scalar `Size` with `KinematicBodyShape BodyShape`; `Radius`
  may remain only as a read-only derived convenience returning
  `BodyShape.Radius` for avoidance/spatial checks;
- every `Navigator.Setup` and `Navigator.Activate` entry point requires a valid
  `KinematicBodyShape` instead of accepting an optional scalar size or silently
  deriving one from grid metrics;
- `Navigator` owns that authoritative `KinematicBodyShape`; its guided
  `NavigationAgentProfile` uses that exact shape, and all path-facing positions
  use the shape's foot reference rather than the host root;
- `NavSteering` uses profile arrival radius and explicit waypoint tolerances;
- line-of-sight calls the graph navigation ray;
- movement-group spatial bucket size and formation padding are explicit
  world-unit settings;
- guide waypoints carry world-space anchors and optional portal entry/exit
  metadata;
- direct/manual motor operation works without a map;
- guided request creation fails clearly when no map or compatible profile is
  available.

The root-to-foot offset is immutable after Navigator construction, serialized
as part of the shape, and checked during populate-existing-instance load. The
old independently mutable `FootPositionAdjust` surface is deleted.

Heightmap grounding remains separate. It may adjust world Y for kinematic
grounding but does not define map connectivity.

## Clean-Break Deletion Manifest

Delete or replace these types and surfaces before the feature branch is ready:

| Delete / remove | Replacement |
| --- | --- |
| `NavigationChart`, `NavigationChartCell`, `NavigationChartCellUpdate` | immutable one-grid `NavigationMap` bakes, `NavigationCell`, and addressed `NavigationMapOverlayDelta` transactions |
| `NavigationChartRegistration`, `ResolvedChartVoxelState`, chart overlap/priority state | context map registry plus composed immutable graph snapshots |
| interval-based `TraversalAuthoringMap`, `TraversalBuildResult`, and old token build results | topology-local map builders/importers |
| `ChartOwnerUtility`, chart grid-bridge requests, and chart diagnostics/extensions | map version dependencies, graph lifecycle events, and graph diagnostics |
| `SolidChartPartition`, `VolumeChartPartition` | immutable baked defaults plus compact semantic overlay and GridForge presence/blockage pages |
| per-node 26-slot neighbor arrays | implicit native adjacency plus compact explicit edges |
| `TrailblazerGridCompatibility` | per-map binding/materialization admission and the narrow GridForge integration seam |
| `TrailblazerWorldContext.VoxelSize` | per-grid metrics and explicit world-unit settings |
| `Navigator.Size`, `ISteer.Size`, scalar/optional `size` parameters on every `Navigator.Setup`/`Activate`, and `UnitSize` | required `KinematicBodyShape`, derived `Radius`, and `NavigationAgentProfile` |
| `MaxPathSearchRange` | deterministic multi-counter `NavigationWorkBudget` |
| `HeuristicMethod`, `StraightCost`, `DiagonalCost` | internal certified Euclidean or zero heuristic |
| `DiagonalTraversalLegs` | topology-kernel witnesses |
| `AlternativeVoxelFinder`, `SolidVoxelFinder`, `VolumeVoxelFinder`, and old endpoint policies | bounded map-node endpoint resolution and navigation rays |
| `FlowFieldSamplingGrid` and cubic interpolation | exact-node selected-edge sampling and guide-local portal progression |
| duplicated `VolumeSurveyor` traversal logic | shared graph search core |
| runtime `VolumeMediumRules` | explicit baked/overlay `NavigationCell` media and required capabilities |
| `TrailblazerVolumeRulesService` and `VolumeMediumRulesState` | addressed `ApplyNavigationOverlay` cell transactions |
| mutable `AStarPathRequest`, `FlowFieldPathRequest`, `VolumePathRequest`, `HybridPathRequest` public hierarchy | immutable `PathQuery` plus internal resolved query |
| static ambient `PathManager` pathing facade | instance coordinator behind `TrailblazerPathingService` |
| current chart/request methods on `TrailblazerPathingService` and `PathRequestContextResolver` | per-grid map registry/query methods and snapshot leases |
| `PathRequestCacheKey` old field model | immutable query/payload keys with dependency stamps and profile identity |
| `ChartsUtilized` and chart-name cache dependencies | sorted graph dependency stamps |
| chart fields in generated-transition and guided-volume types | stable map addresses and traversal intent |
| `TrailblazerTransitionService`, public Register/Unregister/query methods, `TraversalTransitionRegistry`, and `TraversalTransitionRegistryState` | transition overlay operations plus immutable effective graph indexes/read-only diagnostics |
| `PathManager.EnterState` and thread-static test helpers | explicit context/service fixtures |
| old request serialized shapes across Navigator, NavSteering, and guided-volume records | new schema; old saves rejected |

The migration guide documents replacements but does not ship executable
compatibility code.

## Implementation Phases

Every authoritative cutover follows the same order inside its phase: port all
direct production consumers, switch the provider/service authority, delete the
superseded provider and data carriers, then run a clean Release compile and the
full suite. A type that still has a later-phase consumer is retained as an
internal branch-only carrier until that consumer moves; it is never replaced by
a public forwarding facade or deleted while live code still references it. The
phase exit cannot pass with both authorities reachable or with the solution
temporarily uncompilable.

### Phase 0 - Freeze Contracts And Upstream Seams

**Goal:** remove ambiguity before writing the new graph.

Tasks:

- Add dense-rectangular characterization tests for routes, tie breaks, dynamic
  blockers, transitions, flow reuse, guide invalidation, and controller
  waypoint following.
- Record a public API snapshot and an explicit deletion checklist.
- Finalize `NavigationCellAddress`, map composition, agent profile, edge cost,
  surface/volume movement, native portal-clearance semantics, endpoint/query
  options, guide statuses, semantic overlay operations/replacement policy, and
  stable ordering contracts.
- Add the public GridForge normalized-configuration descriptor and index
  validation used by dormant per-grid maps. Active-grid baking may copy its
  already-normalized descriptor; offline baking uses the same GridForge API.
- Define primary-face geometry/clearance for rectangular and hex grids in one
  GridForge-owned public result or prove a complete fixed-point formula from
  public metrics; Trailblazer must not guess native portal dimensions.
- Add GridForge-owned exact fixed-point rectangular/hex prism footprint and
  vertical-bound data sufficient to certify a radius/height swept segment;
  prove inset corner cases for both orientations before freezing the result.
- Prototype the exact contact-manifold result in GridForge and prove face/edge/corner
  classification for rectangular/rectangular, hex/hex, and rectangular/hex
  contacts with equal and differing metrics.
- Implement/benchmark allocation-free broad-phase-plus-exact bulk contact
  construction and prove A*/flow only read precomputed compact seam references.
- Prototype ordered trace interval buckets with true-footprint intersection,
  missing sparse addresses, overlap groups, and parametric coverage.
- Add the GridForge monotonic change sequence, immutable event state, and shared
  cause ID needed to detach deterministic maintenance prefixes and pair exact
  events with their generic notifications.
- Add an atomic GridForge navigation baseline snapshot requested with a
  configuration key and sorted requested `VoxelIndex` span. It contains the same
  high-water sequence, the exact active generation, and presence/obstacle state
  only for those addresses; one-map installation must not enumerate newer live
  state, unrelated grids, or the physical grid outside the requested span. The
  same contract initializes overlay addresses absent from the bake.
- Freeze the subscribe-before-baseline protocol, or one atomic
  subscribe-plus-baseline-cursor equivalent, so context startup and map install
  cannot miss a mutation between capturing state and attaching the change feed.
- Decide public versus friend-only status per GridForge seam. If
  `InternalsVisibleTo("Trailblazer")` is retained, route every internal call
  through the dedicated GridForge navigation bridge and add the architecture
  allowlist test before any production Trailblazer code consumes it.
- Freeze safe primary-edge rules; elevated, diagonal, and multi-axis shortcuts
  require explicit certified connections and exhaustive witness tests.
- Freeze immutable snapshot publication/leases, dependency-stamp invalidation,
  event final-state reconciliation, cost overflow, heuristic lower bounds, and
  active-guide stale contracts.
- Freeze inert off-tick preparation, host-supplied effective-frame/sequence
  map/overlay commits, deterministic maintenance counters/carryover, fail-closed
  pressure handling, and applied/rejected/superseded receipts. Background
  completion must never choose visible simulation state.
- Freeze the context-cache gate, immutable leased-result, same-key publication,
  atomic return/promotion, and lock-order contracts for concurrent readers.
- Freeze maximum snapshot generations, pending-event entries/bytes, concurrent
  query leases, active/retained workspace bytes, and the minimum memory reserve
  for current, fail-closed, and candidate roots.
- Freeze per-map/context dynamic cell, connection, transition, overlay page,
  submitted-delta, and non-reused slot budgets plus the explicit rebake/
  checkpoint recovery when one is exhausted.
- Benchmark global invalidation/RW locking against immutable snapshots with
  structural-component dependencies under streamed-grid churn; freeze the absolute
  writer-wait and repath-wave budgets before Phase 2.
- Benchmark endpoint covered-voxel queries, graph scans, and a candidate spatial
  index before selecting any additional index.
- Decide the maximum dynamic-obstacle behavior supported by exact voxel events:
  whole-node blockage is required; partial portal narrowing requires rebaking/
  replacing the affected map unless GridForge exposes obstacle shape data.
- Capture Release benchmarks for current dense rectangular A*, flow, endpoint
  resolution, LOS, build time, memory, and warmed allocations.

Exit criteria:

- cross-grid contacts cannot promote corner/AABB overlap into an accidental
  walkable edge;
- automatic cross-grid traversal remains disabled until exact positive-area
  contact geometry exists;
- contact construction uses spatial/AABB broad phase plus exact convex
  narrow-phase once per composition change, not per search edge evaluation;
- sparse line of sight has an exact hole-detection strategy;
- dormant maps use a public normalized descriptor and deterministic address
  comparator;
- implicit primary and explicit shortcut portal-clearance contracts are exact;
- snapshot publication cannot corrupt a graph read or guide operation;
- an arbitrary navigation-ray segment has a complete swept-body certificate,
  not merely endpoint and centerline clearance;
- a frozen event prefix can be reconciled without observing later live state;
- grid-before-map initialization is a baseline-plus-later-events operation with
  no gap or double application;
- all public map/overlay operations, removals, replacement policy, and host
  persistence/replay responsibilities are agreed;
- baseline behavior and performance evidence are checked in.

### Phase 1 - New Authoring And Query Contracts

**Goal:** land the only public model that will survive the branch.

The accepted navigation-area policy addition is a Phase 1 contract amendment
and lands before Phase 2 starts. It does not ship later as a compatibility
overload or a second behavior system.

Create focused types under new `Pathing/Map`, `Pathing/Traversal`, and
`Pathing/Query` folders:

- `NavigationCellAddress`
- `NavigationCell`
- `NavigationAreaId`, immutable area rules, policy identity/revision, and the
  context registration contract
- `NavigationCellEntry`
- `NavigationConnection`
- `TraversalTransitionDefinition`
- `NavigationMap` and `NavigationMapBuilder`
- `NavigationOverlayTransaction`, per-map `NavigationMapOverlayDelta`, their
  cell/connection/transition operations, and `OverlayReplacementPolicy`
- prepared map/overlay commit/remove operations, deterministic receipts, and statuses
- rectangular dense and explicit axial hex importers
- `KinematicBodyShape` and `NavigationAgentProfile`
- `NavigationWorkBudget`
- `GuideSampleWorkBudget`
- `NavigationEndpoint`, `TraversalIntent`, and algorithm option value types
- immutable `PathQuery` and endpoint/algorithm enums

Tasks:

- Normalize every one-grid map's entries, source-owned connections, and
  transitions into stable order.
- Normalize overlay operations by addressed cell or source-owned ID; define
  Set/Suppress/RevertToBake and Upsert/Suppress/RevertToBake value semantics.
- Reject duplicate addresses, invalid local indices, negative costs,
  duplicate map IDs inside one candidate batch, different map IDs targeting one
  normalized grid binding, invalid body/profile dimensions, invalid clearance,
  and unknown topology. Setting an existing map ID is the explicit replacement
  operation, not a duplicate-registration error.
- Require every cell area ID and query policy key to resolve through the
  context's bounded immutable catalog. Reject duplicate policy identities with
  different content, unknown/out-of-capacity area IDs, negative additional
  costs, stale revisions, and registration-order-dependent policy layout.
- Reject duplicate map-local connection/transition IDs, dangling local source
  endpoints/witnesses, and entry/exit anchors outside an available referenced
  cell's complete 3D prism. A cross-map destination may be absent for streaming
  and remains dormant; when that target map is present, the candidate
  composition transaction validates every destination/witness or rejects the
  map/overlay transaction. Validate each activatable physical connection's witness
  chain, portal, and swept corridor; a lower-bound declaration is accepted only
  when its fixed-point geometry and canonical cost prove it.
- Retain dormant maps and sparse entries.
- Permit validated overlay cell Sets at in-bounds addresses absent from the bake;
  require complete media/capability/cost/clearance/flag payloads and reject
  out-of-grid addresses or capacity overflow transactionally.
- Port token legend parsing as an importer that emits the new addressed format;
  do not preserve the old constructor surface.
- Add value-equality, insertion-order/materialization-order invariance, and
  validation tests for durable map content. Trailblazer defines the canonical
  value model but does not add a Chronicler map-asset transport; hosts own map
  asset persistence.
- Pin same-`MapId` coalescing by operation sequence, explicit effective-frame
  visibility, ascending-sequence map/overlay folding, all-or-nothing composition,
  and preservation of the old registry/overlay after rejected validation or
  capacity commits.
- Cap pending operation count/prepared-bake bytes and batch item/descriptor/sort
  scratch bytes; pin `Superseded` receipts for coalesced losing operations.

Exit criteria:

- dense rectangular and axial hex sources produce the same canonical map model;
- storage kind is absent from authored cell identity;
- one map contains exactly one grid descriptor and no layer/container type;
- no new public API mentions chart intervals or runtime grid slots.

### Phase 2 - Context-Owned Graph And Lifecycle

**Goal:** compose per-grid maps without touching GridForge voxels.

#### Living implementation tracker

| Checkpoint | Scope | Status |
| --- | --- | --- |
| 2A | Freeze the navigation-area contract amendment, context limits, and exact value/cache identity | Complete |
| 2B | Context-owned immutable graph root, map registry, instance identity, and map-before/grid-before lifecycle | Complete |
| 2C | Stable baked/dynamic slots plus semantic and physical copy-on-write state | Complete |
| 2D | Deterministic event ingestion, maintenance carryover, snapshot publication/leases, and pressure handling | Complete |
| 2E | Workspace/cache synchronization, diagnostics, lifecycle matrix, and performance gates | In progress: implementation and canonical evidence complete; two provisional pinned-contention latency cells remain over budget |
| 2F | Full Release/ReleaseLean/local-stack verification and external review | Complete; Phase 2 exit remains held by 2E |

Update this table only from fresh build/test/benchmark evidence. A checkpoint
may be marked complete while later checkpoints remain in progress, but Phase 2
does not exit until every task and exit criterion below is satisfied.

Current evidence: Trailblazer passes 1,290 Release and 1,261 ReleaseLean tests;
GridForge passes 599 tests in each configuration; both libraries build both
target frameworks with zero warnings and errors; and the final adversarial code
review reports no P0/P1 finding. Every structural, capacity, allocation, and
publication performance gate passes. The archived pinned-contention capture
retains two misses without changing its thresholds: concurrency-2 query p99 is
26.705 us against 25 us (all five over-budget samples occurred in one of three
launches), and concurrency-1 writer p50 is 41.15 us against 40 us. See
`phase2/canonicalPerformanceResults.md` for the full tables and stress results.

Tasks:

- Add the context map registry, immutable `NavigationWorldGraph` snapshots,
  `NavigationMapInstance`, stable baked slots, adaptive local lookup,
  a persistent paged MapId registry plus instance/component/index roots,
  persistent semantic override/tombstone pages, bounded non-reused dynamic
  cell slots, copy-on-write GridForge presence/blockage pages, incident seam
  indexes, dependency stamps, retired-byte tracking, and a bounded
  `PathQueryWorkspace` pool with exclusive checkout per admitted query.
- Add the bounded context-owned navigation-area catalog and immutable policy
  snapshots. Publish policy revisions at the same deterministic maintenance
  boundary as graph state, and include their exact identity in cache/guide
  dependencies without copying rule tables per query.
- Move pathing coordination out of the thread-static `PathManager` ambient and
  behind `TrailblazerPathingService`.
- Materialize map-before-grid and grid-before-map scenarios.
- Subscribe once per context to GridForge's ordered `GridWorld.OnChangeCommitted`
  final-state feed before capturing any initialization baseline. Do not also
  consume the legacy active-grid events, static exact-obstacle feeds, voxel
  subscriptions, or partitions.
- Initialize one map instance from its address-filtered atomic GridForge
  baseline and discard only matching scope/address envelopes represented through
  its high-water mark; unrelated envelopes are never pruned by sequence alone.
- Implement exact-generation grid removal/respawn and sparse add/remove.
- Implement inert off-tick bake preparation, deterministic effective-frame
  map/overlay commits, budgeted/fail-closed runtime composition, short atomic
  snapshot publication, snapshot leases, and final-state event coalescing order.
  Phase 2 structural carryover uses a canonical MapId-ordered cursor over the
  changed map record, its explicit connection/transition work, reverse
  dependencies, and the prior affected weak-component members/incident edges.
  The published affected component remains dormant until the final candidate
  publishes atomically; unrelated components continue serving. Topology-native
  edge, seam-candidate, and cache-invalidation meters remain zero here because
  their producers are owned by Phases 3 and 4.
- Implement transactional cell Set/Suppress/RevertToBake, including an address
  absent from the bake, mutation while its grid/voxel is absent, explicit overlay
  preservation/clear on rebake, and checkpoint compaction.
- Implement bounded ingress coalescing, scope resnapshot on overflow,
  `MaintenanceWorkBudget`, bounded snapshot-generation pressure handling, and
  deterministic carryover.
- Implement deterministic query-batch admission plus concurrent/active/retained
  workspace byte ceilings and pool trimming.
- Implement the context-cache gate and prove concurrent readers use the stated
  snapshot-lease-before-cache-gate order.
- Implement high-water queue detachment from the single committed final-state
  feed; do not read past the frozen event prefix. Reconcile each map baseline
  only with matching exact-generation/address events after that baseline's
  high-water sequence.
- Mirror blocked state without retaining `Voxel` references.
- Add graph diagnostics that enumerate map address, runtime identity, topology,
  baked/default versus effective cell state, overlay source, media/capabilities,
  blockage, component/composition/page versions, dynamic-slot use, and retained bytes.
- Prove an otherwise-empty mapped sparse voxel can still be removed by
  GridForge; Trailblazer must own no partition or voxel subscription.

An early Phase 2 checkpoint may temporarily rebuild one affected instance for a
sparse structural change. It may not pass Phase 2: before exit, use stable baked
and dynamic slots and narrow physical or semantic cell work to touched pages
plus the changed node's local/contact neighborhood.

Exit criteria:

- all four storage/topology cells materialize correct nodes;
- bake memory scales with authored nodes/edges and runtime memory is compact
  sparse semantic/physical overlay state rather than a second copy of static node data;
- local lookup representation follows measured navigation density/bytes rather
  than physical storage kind;
- sparse GridForge and semantic cell mutation never reorders/compacts baked or
  existing dynamic slots and is proportional to touched addresses/pages plus
  local/incident degree;
- slot reuse cannot alias old graph state;
- one context's events cannot mutate another context's graph.

### Phase 3 - Native Edges And Traversal Evaluation

**Goal:** make graph semantics topology-correct before adding search.

Tasks:

- Implement rectangular and hex topology kernels from GridForge's public
  direction spans.
- Implement surface and volume edge rules and witness validation.
- Implement implicit same-grid native edge enumeration in both directions.
- Materialize sorted explicit connections and only exact positive-area
  cross-grid portals. Without upstream contact geometry, require explicit
  connections for every cross-grid seam.
- Compile connection Upsert/Suppress/RevertToBake overlays into persistent
  source-owned outgoing/incoming pages. Validate changed endpoints, witnesses,
  corridor geometry, IDs, and component merge/split atomically without rebuilding
  unrelated baked connection tables.
- Implement the shared `TraversalEvaluator` for media, profile, clearance,
  blockers, steps/drops, witnesses, directionality, navigation-area admission,
  and checked authored plus policy costs.
- Resolve area policy once per admitted query and prove the zero/default and
  custom-area expansion paths use direct indexed reads with zero allocations
  and no virtual/host callback.
- Enforce node, native-face, explicit-portal, witness, and swept-shortcut
  clearance in this phase, before any search uses the graph.
- Pin duplicate suppression and this complete canonical edge order: target
  canonical `(MapId, VoxelIndex)` key; edge kind; topology direction ordinal for native edges or
  ordinal connection ID for explicit edges; then entry and exit anchor raw
  components. Explicit connection IDs are unique within a map. This comparator,
  not cost or runtime insertion order, resolves parallel-edge ties.

Exit criteria:

- rectangular primary surface/volume degrees and certified shortcuts are correct;
- hex primary surface/volume degrees and certified shortcuts are correct for
  pointy and flat orientation;
- sparse holes remove edges naturally;
- one-to-many cross-grid seams retain every valid contact deterministically;
- every search consumer can use the same edge result without topology casts.

### Phase 4 - Endpoint Resolution And Weighted Surface A*

**Goal:** replace the first complete request/search path.

Tasks:

- Build strict and bounded nearest-navigable endpoint resolution over mapped
  instances using the selected Phase 0 query strategy.
- Prove an explicit endpoint `MapId` filters candidates before distance
  ranking, including an overlapping-grid case where the selected cell is
  farther than a cell on another map.
- Add immutable resolved queries with exact endpoints, snapshot leases, and
  captured dependency stamps.
- Convert heap metadata, A* metadata, waypoint costs, survey results, and guide
  costs to `Fixed64`.
- Implement stable three-part heap ordering.
- Implement internal certified Euclidean/zero heuristic selection.
- Use the generic graph edge enumerator/search core and cut the surface domain
  to it. Phase 7 activates the already-designed volume domain after media and
  hybrid semantics are ported.
- Split immutable request identity from algorithm payload keys. Port A* payload
  keys with both endpoints; port destination-centric flow keys without origin,
  with origin coverage checked at checkout. Both include their complete
  exact addressed endpoints where algorithmically applicable, profile,
  traversal, budget, and options identity; payload reuse is validated by the
  captured `GraphDependencyStamp`, not a singular map/version field.
- Validate dependency stamps on guide acquisition, waypoint advance, and every
  steering tick; stale signaling is part of the first guide cutover.
- Add A*-versus-zero-heuristic-Dijkstra property tests across the matrix.
- Port every direct A* caller in `PathGuideFactory`,
  `NavigatorPathRequestFactory`, steering/repath code,
  `GuidedVolumeExitPlanner`, and `HybridRoutePlanner` to `PathQuery`. The latter
  two retain their higher-level orchestration until Phase 7, but their surface
  legs no longer call the old provider. Cut direct A* and guide service
  authority only after an `rg` inventory confirms that every direct production
  caller compiles against the new service.
- Delete the superseded direct A* provider/data path, then run a clean Release
  build and full suite before Phase 4 exits. Shared branch-only carriers needed
  by not-yet-ported flow/volume modes remain internal until their own cut phase.

Exit criteria:

- dense rectangular characterization remains intentionally equivalent where
  the new contract preserves behavior;
- anisotropic and hex path costs reflect actual world geometry;
- no surveyor references GridForge storage kind or rectangular directions;
- negative or overflowing costs fail safely.
- different body/profile clearances select only legal routes;
- same-context mutation cannot race a search or leave a guide live on old state.

### Phase 5 - Weighted Flow Fields And Shared Reachability

**Goal:** provide topology-neutral group guidance and fast-fail behavior.

Tasks:

- Implement reverse Dijkstra over incoming graph edges.
- Store exact-node selected edges with deterministic ties; derive headings from
  the sampled foot position through guide-local certified portal progression.
- Preserve destination-centric cache sharing, partial-field coverage checks,
  recompute-and-promote behavior, detached active leases, and invalidation.
- Replace reachability traversal with the shared edge/evaluator path or remove
  it if measured benefit no longer justifies the cache.
- Delete cubic sampling grids and rectangular direction lookups.
- Add A*/flow cost agreement tests for weighted routes.
- Extend dependency-stamp validation to every flow acquisition/sample and cut
  flow and retained reachability authority to the new graph.
- Port every flow request/guide consumer, delete the old flow provider and
  rectangular sampling carriers, then run a clean Release build and full suite.

Exit criteria:

- exact selected-edge flow sampling works for every matrix member and sparse
  hole, including off-center starts and multi-leg portal progression;
- directed edges integrate correctly in reverse;
- warm flow sampling allocates zero;
- active results become stale immediately after one of their dependencies
  changes; local mutations in disconnected structural components do not stale
  them.

### Phase 6 - Navigation Rays And Simplification

**Goal:** close the geometry-sensitive correctness paths.

Tasks:

- Implement ordered, sparse-hole-safe navigation rays.
- Route direct-path checks, endpoint trace fallback, and string pulling through
  the same evaluator.
- Delete Catmull-Rom path smoothing from path correctness code.
- Add explicit portal entry/exit waypoints where cell centers are not the true
  seam anchors.
- Validate the complete swept body through the selected prism union: inset
  non-portal boundaries and shrink each portal tangentially/vertically;
  node-anchor clearance is necessary but never sufficient for a shortcut.

Exit criteria:

- every simplified consecutive waypoint pair passes the navigation ray;
- sparse gaps, blocked witnesses, and unselected overlapping maps fail LOS;
- nonzero-radius rectangular and hex rays that clip a prism corner fail even
  when their endpoint anchors pass clearance;
- positive-radius rays cross a straight two-cell rectangular portal, a planar
  hex portal in each orientation, and a vertical volume portal; a portal plane
  is not incorrectly inset as a solid boundary;
- mixed-topology portal routes preserve explicit anchors and clearance;
- no topology projection formula exists in Trailblazer.

### Phase 7 - Volume, Transitions, And Hybrid Routing

**Goal:** port the remaining AI traversal domains to the composed graph truth.

Tasks:

- Delete runtime volume predicate rules and port host examples to immutable map
  defaults plus explicit addressed cell overlay deltas.
- Bind persistent transition addresses to exact runtime graph nodes.
- Compile generated transitions from effective baked-plus-overlay cells and graph
  contacts; a cell delta refreshes only transitions incident to changed addresses.
- Compile transition Upsert/Suppress/RevertToBake operations into persistent
  source-owned outgoing/incoming pages in the candidate graph snapshot. A same-
  ID overlay intentionally shadows its baked source-owned default; reject
  duplicate overlay keys and every overlay/generated ID collision before
  publication. Support a dropped/removed ladder without whole-map replacement.
- Convert transition, staged-plan, fallback, and total route costs to `Fixed64`.
- Update transition indexes and caches to exact runtime identities and sorted
  graph dependency stamps.
- Exercise every source/destination topology/storage combination through
  surface-to-volume and semantic transitions.
- Port `GuidedVolumeExitPlanner`, hybrid fallback/preplan factories, transition
  refresh, and every remaining volume/transition consumer before switching
  authority. Delete each superseded provider and carrier only after its direct
  consumers compile.
- Run a clean Release build and full suite after the volume/transition/hybrid
  deletion boundary.

Exit criteria:

- gas/liquid paths, transitions, and hybrid routes work across the full matrix;
- mined/opened cells, water/lava media changes, and ladder add/remove publish as
  bounded overlay transactions and invalidate only affected candidate pages/
  structural components;
- grid respawn rebinds durable transition addresses without preserving stale
  runtime identities;
- effective baked-plus-overlay cell media is the only ordinary traversal truth;
- transition indexes share graph snapshot publication/dependencies; no
  independent mutable registry or second guide-staleness clock remains.

### Phase 8 - Navigator, Kinematic Boundary, And Serialization

**Goal:** remove the remaining grid-size coupling above pathfinding.

Tasks:

- Replace Navigator's scalar `Size` with authoritative `KinematicBodyShape` and
  make guided navigators derive a matching `NavigationAgentProfile` plus
  immutable `PathQuery` intent.
- Replace `ISteer.Size` with `BodyShape`, make any retained `Radius` a direct
  derived value, and replace every optional scalar-size `Setup`/`Activate`
  parameter with a required validated shape.
- Replace root/foot conversions with the immutable shape convention and delete
  the mutable `FootPositionAdjust` surface.
- Replace steering closing/arrival thresholds and movement-group bucket padding
  with explicit world-unit settings.
- Port guide factories, guided volume exits, repath checks, and LOS calls.
- Keep motor, turning, locomotion, and heightmap behavior topology-neutral.
- Emit deterministic committed cell/area-entry metadata only after controller
  movement commits, and expose the last committed area as read-only controller
  state for the next fixed step. The host owns damage, audio, friction, status
  effects, and every other material-specific side effect; speculative navigation
  never runs them.
- Replace the complete old serialized authority: `PathRequestRecord`, Navigator
  path mode/endpoint/heuristic/flow fields, NavSteering last-unit/request fields,
  and guided-volume chart/heuristic/range fields.
- Add an explicit new schema discriminator whose missing/old value fails in
  both JSON and MemoryPack transports. Runtime snapshots, dependency stamps,
  and leases remain
  rebuild-only.
- Continue populate-existing-instance loading: hosts register every per-grid map
  referenced by restored navigators, then restore/replay their coalesced semantic
  overlays, before loading guided Navigators. Overlay state is explicit host-
  persisted gameplay state; graph snapshots and runtime handles remain rebuild-only.
- Reject old serialized request records with a clear schema/version error. Do
  not add a compatibility reader.
- Delete any shared old request carriers retained for earlier phased consumers,
  then require a clean Release build and full suite before the phase exits.

Exit criteria:

- manual kinematic control works without a map;
- guided control works identically from world-space waypoints on rectangular
  and hex routes;
- restored navigators rebuild query/runtime bindings from preregistered maps and
  restored overlays;
- no navigation/controller code reads a context-wide voxel size.
- KCC body/profile geometry cannot silently diverge after construction or load;
- Navigator and steering use only the new query/guide surface, and their
  superseded runtime paths are deleted.

### Phase 9 - Hard Cutover, Documentation, And Release Gates

**Goal:** finish the breaking refactor rather than shipping two systems.

Tasks:

- Verify the per-phase deletions and remove only residual dead tests/helpers;
  Phase 9 is not the first authoritative cutover.
- Invert the current fail-fast hex/sparse/anisotropic/conflicting-metric tests
  into positive feature tests.
- Replace rectangular-only diagnostics and benchmark fixture names.
- Rewrite README and wiki pages for maps, authoring, pathing, guides,
  transitions, volume traversal, Navigator, NavSteering, and serialization.
- Add a major-version migration guide that lists removals and new equivalents
  without executable shims.
- Update `docs/feature-work/feature-work-overview.md` and archive this plan only
  after release verification.
- Run full package, local-stack, multi-target, deterministic-repeat,
  allocation, and benchmark gates.

Exit criteria:

- `rg` confirms no legacy type/API remains;
- public docs contain one model;
- all required matrix, lifecycle, determinism, serialization, and controller
  tests pass in Release;
- performance gates below are satisfied or deviations are explicitly reviewed
  with measurements.

## Required Test Matrix

Build one reusable scenario fixture with these dimensions:

| Dimension | Required values |
| --- | --- |
| Storage | dense, sparse |
| Rectangular metrics | cubic, anisotropic X/Y/Z |
| Hex metrics | pointy, flat, differing radius/layer height |
| Density | 1%, 10%, 50%, 100% where meaningful |
| Active maps | 0, 1, 16, 128 |
| Layout | one grid, conjoined, overlapping, mixed topology |
| Lookup | direct ordinal table, compact sparse lookup |
| Mutation | obstacle, sparse add/remove, grid remove/respawn, cell overlay, connection/transition overlay, one-map replacement/checkpoint |
| Traversal | solid, gas, liquid, directed semantic transition |
| Area policy | default, allowed with surcharge, denied, runtime revision |

The reusable suites run every domain over the core 2x2
storage/topology matrix. Metric, orientation, density, layout, mutation, and
traversal dimensions use a documented pairwise set, plus focused full-
combination cases for mixed seams, sparse mutation, and grid respawn. This keeps
the gate finite without omitting any axis.

- map install/replace/remove, overlay apply/revert/checkpoint, and dormant entry materialization;
- strict and nearest endpoint resolution;
- surface and volume A*;
- weighted flow field build/reuse/sample;
- reachability if retained;
- direct navigation ray and string pulling;
- transitions and hybrid routes;
- cache hit/miss/invalidation and active-guide staleness;
- Navigator waypoint following and manual KCC-without-map behavior;
- serialization restore after map registration.

Critical focused regressions:

- Each configured GridForge grid can own an independent baked map; the context
  composes them into one cross-grid route without constructing a world-map asset.
- A mapped grid can be streamed/rebaked independently while unrelated baked
  maps and runtime overlay pages are neither rebuilt nor copied; cache/guide
  invalidation remains conservative until the Phase 0 streaming gate proves a
  narrower structural dependency is required and safe.
- A 4x4x4 map with two baked walkable cells can mine/open previously unauthored
  in-bounds cells through one sparse delta; native adjacency and a route appear
  without rebaking or copying the map.
- A cell can atomically change solid -> water -> lava-policy -> suppressed ->
  RevertToBake with full media/capability/cost/clearance semantics. Only incident
  edges/generated transitions and affected candidate pages/components invalidate.
- A ladder Upsert creates source-owned climb transitions/required connections;
  Suppress/Revert removes them. Active guides stale atomically, unrelated
  components remain reusable, and no map replacement or secondary registry occurs.
- A semantic overlay applied while the grid or sparse voxel is absent persists
  dormant and materializes with the exact state when the matching generation/
  address appears.
- `PreserveAndRevalidate` retains valid overlay state across a rebake and rejects
  an incompatible candidate without changing the old snapshot; `Clear` installs
  the new bake and resets overlay/dynamic-slot state atomically.
- Duplicate overlay addresses/IDs, overlay/generated ID collisions, invalid
  witnesses/anchors, out-of-grid indices, and overlay capacity overflow reject
  the complete transaction without partial pages or version changes. A same-ID
  overlay/baked pair is the intentional override path, not a collision.
- One atomic multi-map transaction can add/remove both directed halves of a
  cross-map connection; failure in either map rolls back both halves.
- Water requiring `Swim` exercises all-of capability admission. Rock/asphalt/
  snow/lava/acid-gas area IDs exercise default, surcharge, denied, and revised
  query policies without adding material-specific capabilities. Only committed
  controller entry emits host-consumable area metadata; speculative searches
  produce no gameplay side effects.
- A checkpoint captured at overlay sequence N rejects `Stale` if a later delta
  publishes before its Clear commit; concurrent permutations never lose or
  double-apply either mutation.
- Removing a mined articulation cell from a large single-map component charges
  every visited node and implicit/explicit/seam/transition edge, remains fail-
  closed across budget carryover, and reopens only after the split publishes.
- An active GridForge grid with no map remains usable by KCC/spatial systems and
  contributes no AI nodes.
- One candidate registry cannot contain duplicate `MapId` values or two map IDs
  targeting one normalized grid binding; committing a prepared map with the same
  ID is replacement. Map-before-grid and grid-before-map bind by persistent
  descriptor, never grid slot.
- Dense and sparse grids with identical physical/map entries yield identical
  logical routes and costs.
- A sparse hole cannot be crossed by A*, flow, reachability, LOS, or smoothing.
- A mapped sparse voxel remains removable because Trailblazer attaches no
  partition/subscriber.
- Two endpoints with ample cell clearance cannot admit an implicit/explicit
  edge whose shared face, portal, witness, or swept corridor is narrower than
  the body profile.
- A nonzero-radius swept segment that clips a rectangular or hex prism corner
  is rejected even when its endpoints and centerline cells are navigable.
- Certified rectangular shortcuts cannot cut any missing or blocked proper-
  subset witness; the default graph emits no implicit diagonal shortcut.
- Hex exposes stable six-planar/eight-primary native semantics in both
  orientations; certified vertical-diagonal shortcuts cover the remaining
  twelve direction candidates explicitly.
- Anisotropic and mixed-metric edge costs use copied world anchors.
- Cross-grid candidate discovery retains deterministic zero/one/many results
  without promoting any AABB-only result.
- An explicit endpoint map cannot be displaced by a closer candidate from an
  overlapping map.
- Rectangular/hex and same-topology mixed-metric seams require an explicit
  connection until exact positive-area contact geometry certifies them.
- Grid slot reuse never aliases graph, cache, guide, or transition state.
- Installing a map over an existing sparse grid observes one atomic baseline;
  a voxel/obstacle mutation after its high-water mark is applied exactly once in
  the next detached event prefix.
- A mutation concurrent with context startup is represented either by the
  baseline or by the subscribed event prefix, never missed or applied twice.
- Installing map B cannot discard a pre-baseline map A mutation merely because
  A's unrelated event sequence is below B's baseline high-water mark.
- A tiny authored map installed over a very large dense or sparse physical grid
  requests and processes only its sorted authored addresses; a later overlay
  adding one address queries only that requested address.
- Identical prepared map operations with deliberately varied worker delays
  publish or reject on the same effective frame with identical dependency state.
- Same-frame map/overlay operations fold in ascending sequence with per-key last
  writer semantics; rejected validation/capacity commits leave the old map,
  overlay, and snapshot intact and complete the receipt identically on every peer.
- Duplicate/regressing operation sequences and commits submitted after their
  effective-frame boundary reject without entering the maintenance queue.
- Different authored/grid insertion orders produce identical canonical graph,
  route, flow direction, and transition order.
- Equal-cost cross-map searches use `(ordinal MapId, lexicographic VoxelIndex)`
  ties and remain identical across map registration, removal, and re-add
  permutations; instance slots and per-map baked ordinals do not affect output.
- Parallel equal-cost edges to the same target select the same connection and
  flow direction by canonical edge order regardless of insertion order.
- Flow sampling derives the next leg from the actual foot position for native
  zero-thickness portals, partial rectangular/hex seams, and off-axis explicit
  connections. Off-anchor corner starts either follow a certified approach or
  return `LocalRecoveryRequired`; no cached parallel vector escapes the
  corridor.
- A flow guide crosses a multi-leg explicit connection into a native edge,
  rebases its cursor to the new node/edge, and deterministically rewinds on
  backtrack/re-entry without carrying the old leg ordinal forward.
- A* cost equals Dijkstra cost on bounded generated maps.
- Every heuristic is consistent for every emitted certified edge.
- Flow reverse traversal respects one-way edges.
- One stored automatic seam geometry emits independently evaluated directed
  structural references. Two profiles with different step/drop, clearance, and
  media capabilities evaluate the same snapshot differently; asymmetric hard
  direction flags remain visible to reverse flow through the matching incoming
  index.
- A* and flow agree with unequal enter costs, one-way connections, custom portal
  anchors, and near-overflow raw values.
- Concurrent same-key cache misses publish one semantically identical result;
  checkout/return counts, detached flow promotion, invalidation, and disposal
  remain atomic, with no snapshot/cache lock-order inversion.
- Concurrent different-origin flow requests share the origin-free payload key,
  atomically recheck origin coverage, and promote the longer canonical Dijkstra
  prefix; neither caller receives an uncovered field.
- Flow queries differing in any semantic `NavigationWorkBudget` counter do not
  alias a payload unless equivalent logical debiting is proven; a cache hit
  cannot turn `BudgetExceeded` into success or vice versa.
- Rectangular -> hex -> rectangular routes work in A* and reverse flow, proving
  kernel dispatch follows the owning map instance.
- Tied trace intervals, overlapping alternatives, one-to-many seams, and sparse
  missing addresses yield insertion-order-independent navigation-ray results.
- An unrelated active grid with no registered map is ignored rather than globally
  rejected.
- Active guides and cached no-path results invalidate when a recorded dependency
  changes; overlays in disconnected structural components remain reusable.
- Guide acquisition, sampling, waypoint advancement, and steering return stale
  when a captured dependency changes; no active guide observes partial
  composition/overlay publication.
- Same-context snapshot reads and publication obey the selected lease contract,
  while different contexts progress independently.
- Concurrent endpoint resolutions use exclusive workspaces and cannot corrupt
  candidate or search scratch.
- Deterministic query batches admit the same ordinal prefix under concurrent
  query/workspace/result-payload byte pressure, even with reversed worker
  completion, reject oversize item/descriptor/sort-scratch input before sorting,
  and trim retained workspaces in stable order.
- Maintenance detaches a fixed high-water prefix: later events wait for the next
  batch, and only a generic event with the same immutable cause ID is suppressed.
- A pinned bounded search plus repeated obstacle publications closes admission,
  drains the old generation, and publishes mandatory safety state without
  skipping an event or exceeding the snapshot-generation/byte ceiling.
- Event floods beyond the ingress ceiling fail-close only affected scopes,
  remain byte bounded, and converge through deterministic address-filtered
  resnapshot chunks while unrelated maps continue serving.
- If an early-chunk address mutates while a later resnapshot chunk is processed,
  the cursor mismatch discards the candidate and the next complete pass includes
  that mutation exactly once before the scope reopens.
- One request exhausts one shared work meter across endpoint producers,
  GridForge trace/coverage output, transition candidate pairs, staged legs,
  connection legs, and every nested hybrid/recovery search.
- Guide samples and sample batches exhaust finite node/cursor/portal/prism/trace/
  recovery counters without advancing a cursor or mutating cached field state.
- Root-to-foot offset participates in path positions, body clearance, profile
  equality, cache identity, serialization, and post-load mismatch rejection.
- Duplicate map-local connection/transition IDs, dangling local addresses, and
  out-of-prism anchors fail map construction. Cross-map references remain
  dormant while the target map is absent and fail the candidate composition
  deterministically if the installed target lacks a referenced cell/witness.
- `NoMap`, invalid profile/endpoint, no-path, work budget, cost overflow,
  retention capacity, and stale service statuses are distinct and deterministic.
- Debug/Release runs on supported executable test TFMs produce identical route
  goldens. Both library TFMs pass compile, package, and API-surface verification;
  `netstandard2.1` is not treated as an executable runtime.

Property-style tests should generate bounded rectangular/hex maps, shuffle
entry and registration order, compare A* to Dijkstra, and compare incremental
sparse mutation results to a full graph rebuild.

## Benchmark Plan And Performance Gates

Performance is a release criterion, not deferred cleanup. Phase 0 records the
existing dense-rectangular baseline and freezes absolute frame/maintenance,
memory, allocation, writer-wait, and repath-wave budgets before implementation.
Report p50/p95/p99 and worst observed values where contention or spikes matter;
median-only evidence is insufficient. Separate topology and physical storage,
and always record complexity counters alongside time.

The last [checked-in benchmark reference](done/benchmarkPerformanceFinalPlan.md)
measured `RawSurvey_OpenPlane64` at 9.628 ms and 2.13 MB allocated, while
`WarmGuide_OpenPlane128` was 147.3 ns and 0 B. These are evidence that cold flow
memory/build cost and warm cache behavior need separate gates, not permanent
targets; Phase 0 reruns them on the final baseline machine/toolchain before
freezing budgets.

### Structural performance invariants

- A bake stores static cell/anchor/edge data once. Runtime instances contain
  compact semantic override/tombstone pages, physical masks, and incident seams,
  not a second full node graph.
- One map set/replace/remove is `O(Mi + Ei + incident seam candidates)` for that
  map plus any structural-component split it causes. With the persistent AVL
  membership directory selected in Phase 2, deleting a bridge cell/edge costs
  `O((Vc log M) + Ec)` over the affected old effective node component,
  including implicit native edges, but never scans unrelated components. Split
  benchmarks report copied persistent nodes/bytes so a measured dynamic-
  connectivity replacement can be justified later instead of assumed.
- Sparse presence/blockage mutation is `O(native degree + incident explicit
  edges + incident seams + log directory pages)` and never copies an active-map
  root, recomputes structural components, or shifts/re-sorts baked node slots.
- A semantic cell/connection/transition delta copies only touched overlay/index
  pages and updates incident degree. Additions merge incident components locally;
  a removal that splits connectivity has the already-stated honest `O(Vc + Ec)`
  budgeted/fail-closed cost over the affected old effective node component.
- Adding a grid/map uses GridForge's spatial broad phase and boundary candidates;
  it never compares every cell or blindly scans every active grid.
- Exact convex contact clipping runs at composition/mutation boundaries, never
  inside A*/flow neighbor expansion.
- Implicit native edges allocate no objects and store no per-node edge arrays.
- A flow field stores one compact cost and selected-edge reference per settled
  node; portal/corridor geometry is not copied into the field.
- A cell stores one compact area ID. An admitted query resolves one immutable
  direct-indexed policy table; node expansion performs no behavior-object,
  dictionary, string, delegate, or allocation work.
- Query work is bounded by deterministic counters. Short queries do not clear
  metadata proportional to total world size.
- Inert map baking may run away from the simulation tick. Runtime preparation
  and publication advance only through deterministic work counters and explicit
  effective-frame/operation order; wall-clock completion has no semantic input.
- Pending event bytes, active snapshot generations, concurrent workspace bytes,
  retained workspace bytes, and maintenance work per frame all have hard bounds.

### Required benchmark workloads

1. Per-grid bake construction at 1K, 100K, and 1M authored cells: time, peak
   temporary bytes, retained bytes per cell/edge, and lookup representation.
2. Runtime instance materialization for dense/sparse physical grids at
   1/10/50/100% presence: time and overlay bytes per authored/live cell.
3. Active-map scaling at 1/16/128 maps with equal total nodes and increasing
   total nodes: persistent-registry set/remove pages copied, endpoint, search,
   publication, and idle retained memory.
4. One small/large map set, replacement, removal, dormancy, and respawn: prove
   unrelated bake/instance pages are neither visited nor copied.
5. Sparse churn: one event, 1K repeats on one address, 1K/100K unique addresses,
   producer-faster-than-maintenance backlog, and add/remove oscillation; record
   queue bytes, dirty scopes, resnapshot work, touched nodes/edges/seams, and
   publication cost.
6. Semantic overlay churn: the 4x4x4 mining case, one/1K/100K previously
   unauthored cell Sets, solid/water/lava/revert cycles, dormant-grid replay,
   dynamic-slot exhaustion, and bake checkpoint/overlay Clear. Record pages
   copied, incident work, component merges/splits, retained bytes, invalidated
   cache bytes, and repath waves versus whole-map replacement.
7. Ladder/bridge churn: connection/transition Upsert/Suppress/Revert at
   one/1K/100K operations, including cross-component merge and bridge removal;
   record index pages, component work, publication p95/p99, and unrelated reuse.
8. Automatic/authored seam build/remove over zero/one/one-to-many contacts,
   mixed topology/metrics, boundary size, and 2/16/128 active grids.
9. Direct and nearest endpoint resolution by overlap count and map density:
   record candidate count, lookup operations, time, and temporary bytes.
10. Native/explicit outgoing and incoming enumeration: evaluated edges,
    nanoseconds per edge, branch behavior where available, and allocations.
    Include the default-area fast path and custom allowed/surcharge/denied
    policy paths at 1/16/128 configured areas, plus policy-revision publication
    and guide/cache invalidation. All warmed expansion variants allocate zero.
11. A* at 100, 1K, 10K, and 100K expanded nodes: p50/p95/p99, relaxed edges,
   heap operations, transient bytes per expanded node, and result bytes.
12. Weighted flow: cold build, canonical-prefix promotion, concurrent near/far
    misses, cache hit, checkout/return, exact selected-edge sampling, and
    retained/transient bytes per settled node.
13. Cache pressure with differently sized results: byte eviction, single-payload
    rejection, detached/leased/retired bytes, duplicate discard, and recovery.
14. Navigation rays/string pulling: short/medium/long traces, sparse holes,
    mixed seams, worst waypoint counts, ray attempts, and total intervals.
15. Obstacle mutation and dependency invalidation with 100/500/5,000 agents:
    affected versus unaffected guides, discarded cache bytes, and repath count.
16. Snapshot publication/concurrency at 1/2/4/8 query threads: publication time,
    cache-gate wait, active/retained workspace bytes, retired generations/bytes,
    pending prepared-bake bytes, batch descriptor/sort scratch,
    reserved/actual result-payload bytes, reversed worker completion, query
    p50/p95/p99, admission closure, and maintenance delay.
17. One giant structural component: overlay bit flip at 1/16/128+ maps, a mined
    articulation-cell removal inside a 1M-node single map, cross-map bridge seam
    deletion/component split, persistent-root pages copied, visited nodes/all
    edge kinds, fail-closed carryover, and cache stamp rejection work.
18. Transition/hybrid planning across topology seams and multi-map streaming;
    record lookup probes, candidate pairs, staged/subsearch attempts, and shared
    budget exhaustion.
19. Multi-agent flow sampling at 100/500/5,000 agents crossing native and
    explicit portals; measure cursor/trace/prism work-budget exhaustion and
    caller-owned scratch.
20. Allocation tests split into search scratch, cache-hit guide
    checkout/sample/return, uncached result construction, caller-owned result
    buffers, composition preparation, and snapshot publication.

### Gates

- Phase 0 records the existing dense-rectangular reference; provisional
  equivalent A*/flow gate is no more than 15% p50 regression, but final release
  also requires the frozen p95/p99 and absolute frame budgets.
- Bake memory is `O(authored cells + authored edges)`; runtime semantic/physical
  overlay memory is measured separately and may not duplicate unchanged baked
  anchors/cells/local edge geometry.
- Lookup selection obeys the frozen navigation-density/byte threshold; it never
  allocates address-volume tables for a sparse authored map merely because the
  physical GridForge grid is dense.
- Per-map replacement and address-filtered baseline cost are independent of
  unrelated maps and physical cells outside the requested address span. Baseline
  work is `O(requested addresses)`; seam candidate cost follows spatial/boundary
  candidates, not `active maps x cells`.
- Persistent registry set/remove copies only the `MapId` index path and touched
  bake slots; it does not copy or reindex every registered map.
- One physical sparse mutation touches only the changed stable slot, persistent
  directory path, and local/incident degree; it performs no active-map root copy
  or component rebuild.
- One semantic delta costs `O(touched addresses/IDs + copied persistent paths +
  incident degree)` before any required affected-component split. It never copies
  the bake/map root or scans unrelated components; articulation/bridge removal is
  charged as `O(Vc + Ec)` to the maintenance/component-split budget, including
  implicit native, explicit, seam, and transition edges.
- Overlay cells/edges/transitions, non-reused dynamic slots, pending delta bytes,
  copied/retired pages, and per-frame maintenance have hard caps. Exhaustion
  returns `CapacityExceeded` without partial publication; checkpoint rebaking is
  explicit host work, never a hidden hot-path fallback.
- Cache-hit guide checkout/sample/return, flow sampling, bounded LOS with
  caller-owned scratch, native edge enumeration, and guide advancement allocate
  zero after warmup.
- Fresh search scratch allocates zero after pool warmup. Variable result data has
  an explicit byte budget unless caller-owned buffers are used.
- Flow reports retained and transient bytes per settled node and stores no
  per-node object/polyline/`Vector3d[]`. Byte-weighted cache and retired-snapshot
  ceilings are enforced under leased-result pressure.
- One `NavigationWorkBudget` bounds lookup/address probes, endpoint candidates,
  expansions, evaluated edges, connection/witness legs, transition
  candidates/pairs, staged/nested searches, trace/coverage intervals, and
  simplification rays across every public query path.
- Local mutation in a disconnected structural component does not stale/repath
  an agent whose dependency stamp excludes that component. Structural
  composition invalidation must satisfy the
  frozen streaming repath-wave/cache-discard budget or be dependency-scoped
  further before Phase 2 exits.
- Snapshot publication and cache synchronization meet the frozen p95/p99
  contention/writer-delay budget at 1/2/4/8 query threads.
- Query batches, workspace pools, event ingress, maintenance carryover, active
  result/payload reservations, generations, and retired memory stay within
  frozen aggregate byte/count ceilings under overload; mandatory GridForge
  safety changes converge without admitting navigation through stale affected
  scopes.
- Pending prepared bakes and batch descriptors/sorting scratch have explicit
  count/byte ceilings; guide samples have finite per-call/batch work counters.
- No request rescans all active grids to recover a representative metric, and
  deterministic sorting occurs at bake/composition boundaries, not per neighbor.

When a gate fails, capture a profile/trace and optimize the measured bottleneck.
Prefer removing copies/scans, improving data layout, or narrowing invalidation
before adding topology-specific search paths or speculative caches. Record the
before/after result with the same workload and rerun correctness/determinism
tests after every accepted optimization.

## Documentation Impact

At minimum rewrite or replace:

- `README.md`
- `docs/wiki/Overview.md`
- `docs/wiki/NavigationCharts.md`
- `docs/wiki/ChartAuthoring.md`
- `docs/wiki/PathManager.md`
- `docs/wiki/Pathing.md`
- `docs/wiki/PathGuides.md`
- `docs/wiki/Transitions.md`
- `docs/wiki/VolumeTraversal.md`
- `docs/wiki/NavSteering.md`
- `docs/wiki/Navigator.md`
- `docs/wiki/Serialization.md`

The migration guide must state plainly:

- maps are addressed by stable map ID and topology-local voxel index, with each
  one-grid map bound through a normalized GridForge configuration descriptor;
- maps are immutable baselines; runtime mining/media/connection/transition
  changes use deterministic addressed semantic overlay transactions, which hosts
  persist/replay before restoring guided Navigators;
- world-position, scalar interval, and `[y,x,z]` chart APIs are removed;
- old path request types/options and serialized request records are
  incompatible;
- agent geometry and budgets are explicit;
- hosts register every referenced per-grid map before restoring guided navigators;
- no runtime compatibility layer is provided.

## Final Mechanical Gates

Before declaring the refactor complete, automated searches over
`src/Trailblazer` must confirm (focused topology-kernel files are the only named
exceptions, and tests/docs do not count as runtime compatibility):

- no `TrailblazerGridCompatibility`;
- no pathing/navigation use of `TrailblazerWorldContext.VoxelSize`;
- no `Navigator.Size`, `ISteer.Size`, scalar-size `Navigator.Setup`/`Activate`
  parameter, mutable `FootPositionAdjust`, or request `UnitSize`;
- no public `NavigationChart` types;
- no `NavigationMapGridLayer`, navigation `LayerId`, or context-wide authored
  world-map container; one `NavigationMap` binds one grid descriptor;
- no `SolidChartPartition` or `VolumeChartPartition`;
- no `RectangularDirection` outside the rectangular topology kernel and its
  focused tests;
- no GridForge storage-kind branch inside a surveyor;
- no integer straight/diagonal path cost constants;
- no bare grid-slot cache identity where exact generation or configuration
  identity is required;
- no old serializer field names or fallback reader;
- no obsolete/forwarding API added for this migration.
- no `TraversalBuildResult`, `ChartOwnerUtility`, chart bridge request,
  chart diagnostic extension, old voxel finder/endpoint policy, volume-rules
  service/state, or thread-static `PathManager.EnterState` residue;
- no old chart/request member on `TrailblazerPathingService`, generated
  transition records, guided-volume handoffs, Navigator, or NavSteering; the new
  pathing service intentionally retains only the map/overlay transaction surface.
- no `TrailblazerTransitionService`, public transition Register/Unregister
  surface, `TraversalTransitionRegistry`, registry state, or registry-version
  cache/guide identity; dynamic transitions exist only as graph-published overlay
  operations.
- no runtime copy of baked anchors/cells/local edge geometry, per-node implicit
  edge object/array, or flow-field polyline/vector array;
- if GridForge friends Trailblazer, an architecture test rejects every internal
  GridForge reference outside the dedicated navigation bridge namespace and
  explicitly forbids direct `IGridTopology`, concrete topology, storage, lock,
  or `TopologyVoxelAabb` use.

## Risks And Mitigations

| Risk | Mitigation |
| --- | --- |
| AABB overlap becomes an invalid cross-topology portal. | Require upstream exact positive-area contact geometry or an explicit connection for every agent size. |
| Sparse tracing skips a void and reports false LOS. | Require continuous ordered interval coverage and a legal graph chain between successive buckets. |
| A centerline ray fits but the body clips a prism corner. | Certify against the fixed-point navigable prism union, insetting non-portal boundaries and shrinking portal cross-sections; reject when proof is unavailable. |
| Four separate implementations drift. | One graph/evaluator; topology kernels only for native offsets/witnesses; storage only for lookup. |
| Trailblazer-owned voxel partitions force a pre-removal protocol and add per-cell state. | Keep baked/runtime navigation state external so ordinary otherwise-empty sparse removal needs no Trailblazer cleanup call. |
| Mixed metrics break cost/heuristic admissibility. | Use actual fixed-point anchors, non-negative costs, and certified Euclidean or zero heuristic. |
| Dependency tracking grows too complex. | Start with composition plus conservative structural-component versions; refine only with a correctness proof and failed streaming gate. |
| A guide races snapshot publication. | Operate under an immutable snapshot lease, validate dependencies before/after, and retain retired pages until leases return. |
| Event inclusion depends on callback timing. | Detach immutable prefixes at source high-water marks and pair exact/generic events only by cause ID. |
| Per-grid streaming causes rebuild, cache, or repath waves. | Localize bake/overlay/seam ownership, publish immutable snapshots, dependency-index caches/guides, and enforce p95/p99/repath-byte gates. |
| Mining, media changes, or temporary ladders require a whole-map rebake. | Keep the map as an immutable default and publish sparse addressed cell/connection/transition overlay transactions into persistent snapshot pages. |
| Long-running unique overlay churn exhausts non-reused dynamic slots. | Enforce explicit per-map/context caps and expose whole-map checkpoint rebaking with atomic overlay Clear; never compact handles behind active snapshots. |
| Exact contact geometry is too expensive in search. | Use AABB/spatial broad phase, clip only boundary candidates during composition, cache one seam geometry, and read compact refs in expansion. |
| Friend assembly access spreads across GridForge internals. | Restrict it to a dedicated navigation bridge namespace and architecture test; keep generally useful immutable geometry contracts public. |
| Flow behavior feels less smooth after deleting interpolation. | Ship exact topology-correct sampling first; profile and design topology-specific smoothing separately. |
| KCC becomes entangled with topology. | Keep graph output in world-space anchors and place all topology code below the guide boundary. |
| A long branch ships both old and new systems. | Cut authority and delete each superseded slice in Phases 4, 5, 7, and 8; Phase 9 verifies residue. |

## Definition Of Done

This work is complete only when:

- each eligible GridForge grid has an independently bakeable/streamable
  `NavigationMap`, while one context snapshot composes cross-grid routing;
- dense/sparse rectangular and hex prism maps pass the same behavioral
  contracts;
- anisotropic, mixed-metric, pointy, and flat configurations are supported;
- sparse add/remove and grid generation replacement are exact and deterministic;
- previously unauthored in-bounds cells, water/lava policy changes, and temporary
  ladders/connections mutate through bounded deterministic overlays without
  whole-map replacement;
- A*, flow fields, volume traversal, transitions, hybrid planning, endpoints,
  reachability, and navigation rays consume the shared graph semantics;
- all simulation costs and geometry are fixed-point and topology-correct;
- guided Navigator behavior works across the matrix while manual KCC remains
  map-independent;
- caches and active guides cannot outlive graph state;
- baked static data is stored once, runtime semantic/physical overlays are
  compact, sparse/gameplay churn is local, cache/retired memory is byte-bounded,
  and frozen p95/p99/frame/repath performance gates pass;
- the old chart/partition/request/voxel-size architecture is deleted rather
  than hidden behind adapters;
- docs and serialization describe only the new major-version contract;
- full Release verification, determinism checks, allocation gates, and measured
  benchmarks pass against the intended GridForge/FixedMathSharp/SwiftCollections
  stack.
