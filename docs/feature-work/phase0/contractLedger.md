# Phase 0 Contract Ledger

This ledger freezes the decisions that Phase 1 and later implementation must
follow. The detailed rationale and algorithms remain in
`../gridTopologyNavigationMapRefactorPlan.md`; this file is the short mechanical
review gate. Trailblazer is making a clean break: no legacy wrapper, alias,
serializer, or second runtime authority survives the cutover.

## Identity And Composition

- One `TrailblazerWorldContext` binds one `GridWorld` and may own many maps.
- A `NavigationMap` binds exactly one normalized GridForge configuration. At
  most one map in a context targets that configuration, whether its physical
  grid is active or dormant.
- `MapId` uses non-empty ordinal string identity. `NavigationCellAddress` is
  `(MapId, VoxelIndex)` and orders by ordinal `MapId`, then GridForge's stable
  X/Q, Y/layer, Z/R comparator.
- Runtime `GridIndex`, `WorldVoxelIndex`, generation tokens, baked ordinals, and
  overlay slots are never persistent authored identity.
- The immutable map stores only authored cells, sorted and unique by
  `VoxelIndex`, for dense and sparse GridForge storage alike.
- Baked slots remain stable for one bake. Dynamic overlay slots are stable,
  bounded, and never reused until a whole-map checkpoint replacement clears
  the overlay generation.

## Authoring And Mutation

- Whole-map prepare/commit/remove is for installation, rebake, and checkpoint
  replacement. Ordinary gameplay mutation is an addressed overlay transaction.
- A transaction may update multiple maps and publishes all-or-nothing. Maps are
  sorted by ordinal ID; cells and source-owned IDs are sorted and unique.
- Cell operations are complete-payload `Set`, `Suppress`, and `RevertToBake`.
  Connection and transition operations are complete-definition `Upsert`,
  `Suppress`, and `RevertToBake`.
- Overlay cells may target any in-bounds normalized address, including one
  absent from the bake or from sparse physical storage. Semantic state remains
  dormant until the exact physical generation/address exists.
- Map replacement explicitly selects `PreserveAndRevalidate` or `Clear`.
  Removal clears that map's overlay. A checkpoint `Clear` commit requires the
  captured bake/overlay high-water stamp or rejects as `Stale`.
- Hosts own persistence and replay of map assets and coalesced overlay values.
  Trailblazer does not retain an independent mutable rules registry.

## Cells, Agents, And Traversal

- `NavigationCell` is the single effective semantic payload: media, required
  capabilities, enter cost, radius/height clearance, and flags.
- Capability admission uses all-of semantics. Unknown flags/media/capability
  bits, negative costs/clearance, or overflowing cost composition reject.
- `KinematicBodyShape` owns non-negative radius, positive height, and
  non-negative root-to-foot Y offset. Controller and pathfinding use the same
  shape. Endpoints and waypoints are foot positions.
- Surface and volume queries share nodes, costs, search infrastructure, and
  explicit transitions. The traversal domain selects native edge rules; media
  never silently chooses gameplay intent.
- Native edge cost is exact fixed-point geometric travel plus non-negative
  source-owned additions and destination enter cost. Explicit corridor cost is
  the certified anchor/portal polyline plus additions. Unrepresentable
  accumulation returns `CostOverflow`; it never saturates into a usable route.
- A* uses Euclidean distance only when every reachable edge certifies that
  lower bound. Otherwise its admissible heuristic is zero. A transition or
  authored shortcut cannot silently invalidate the proof.
- Native rectangular surface edges are four planar faces. Native hex surface
  edges are six planar axial faces. Rectangular volume uses six primary faces;
  hex volume uses six planar plus two vertical primary faces.
- Diagonal, elevated-lateral, jump, ladder, teleport, and other shortcuts are
  explicit certified directed connections or transitions. Witnesses and swept
  clearance are mandatory; no offset-only shortcut is inferred.
- Connections are source-map-owned and directed. Bidirectional behavior is two
  definitions. Transitions are overlay-owned semantic actions and publish with
  the graph snapshot; there is no separately mutable transition registry.

## Geometry And Clearance

- GridForge owns normalization, storage-neutral address validation, exact cell
  prisms, and exact contact manifolds. Trailblazer does not reproduce hex or
  rectangular projection math.
- Map admission fails fast when normalized fixed-point metrics cannot produce
  an exact symmetric cell prism (for example, a raw unit that cannot be
  bisected without rounding). Trailblazer never repairs such geometry locally.
- Automatic same-grid traversal requires a topology-native positive-area
  primary face. Edge/corner contact is never sufficient.
- Automatic cross-grid traversal requires a checked, positive-area GridForge
  face manifold and a non-empty agent-shape inset. Point, edge, AABB-only, and
  volume-overlap contacts reject for every agent radius, including zero.
- Cross-grid contacts are built only on composition change through broad phase
  plus exact convex narrow phase, stored once under canonical endpoint order,
  and read as compact seam references during A*/flow expansion.
- A navigation ray consumes ordered, tied parametric trace buckets with exact
  footprint intervals and explicit missing sparse addresses. It must prove
  continuous coverage and a graph-connected choice through each overlap group.
- Radius and height certify the full swept prism union, including portal inset;
  endpoint or centerline-only clearance is insufficient.

## Queries, Results, And Ordering

- `PathQuery` is immutable intent. Resolution adds exact runtime identities and
  one immutable snapshot lease; public requests never retain `Voxel` objects.
- Each endpoint independently selects strict or bounded-nearest resolution,
  optional `MapId`, and fixed-point maximum distance. All values participate in
  query/cache identity.
- Stable comparison order is total cost, then canonical cell address. Iteration
  order, hash-bucket order, worker completion, and runtime slots never break a
  tie.
- Every query shares one mutable multi-counter `NavigationWorkBudget` across
  endpoint resolution, A*/flow, hybrid stages, recovery, transitions, traces,
  witnesses, and simplification. Nested work never receives a fresh budget.
- Public terminal statuses distinguish success, invalid request/profile,
  missing/dormant map or endpoint, no path, work-budget exhaustion, cost
  overflow, capacity pressure, local recovery, and stale dependencies.
- Flow payload identity is destination/profile/options/budget/dependencies and
  excludes origin. Checkout revalidates origin coverage. Partial fields are
  canonical reverse-Dijkstra prefixes and promotion retains smaller leased
  payloads detached until their references return.
- Flow sampling steers from the actual point toward the first non-coincident
  certified point on the selected leg (`source anchor -> portal entry -> portal
  exit -> target anchor`). Native shared-face entry/exit coincidence therefore
  cannot produce a zero direction.
- Guide payloads are immutable. Sampling keeps cursor/progress per guide,
  validates dependencies before and after work, and never advances on budget,
  stale, or local-recovery failure.

## Publication, Concurrency, And Time

- Preparation may run off-tick only to produce inert validated data. A host
  supplies unique increasing operation sequence and nondecreasing effective
  frame; worker completion and wall time never choose visibility.
- All state becomes visible by one immutable root publication at a deterministic
  fixed-step boundary. Candidates fold operations in sequence order and report
  `Applied`, `Rejected`, or `Superseded` receipts.
- A query owns one snapshot lease and one exclusive workspace. Guides take only
  short leases per operation; no public guide retains a lease between calls.
- Ad-hoc calls pass through the context admission gate. Parallel work uses
  deterministically admitted batches with resources reserved by stable input
  ordinal before workers launch.
- Lock order is snapshot lease, then context cache gate. Code holding a cache
  gate never acquires a snapshot lease. Cache lookup/refcount/LRU/invalidation/
  detached tracking/promotion and return are atomic.
- Mandatory GridForge safety changes cannot be skipped. Under retired-root
  pressure the context closes admission, coalesces one final-state candidate,
  drains its bounded leases at the fixed-step barrier, and publishes a compact
  fail-closed root before simulation advances.
- Results and guides store sorted structural dependency stamps, not global
  mutable versions alone. Publication invalidates by affected structural
  component/map/page; an operation validates before and after work and returns
  `Stale` if a relevant dependency changed.

## GridForge Synchronization

- Context startup, map install, and new overlay addresses use the atomic
  subscribe-plus-addressed-baseline contract. The baseline is keyed by the
  normalized configuration and sorted requested addresses and carries the
  exact generation and change-sequence high-water mark.
- Committed GridForge envelopes are immutable final state and carry monotonic
  world-local sequence/cause identity. A recognized exact obstacle event and
  its generic event are one cause, never two structural mutations.
- Maintenance detaches a high-water prefix, coalesces exact address/domain state,
  and never rereads newer live state. Grid remove dominates its generation's
  pending cell events.
- Unique event overflow marks only the affected map scope for addressed
  resnapshot, keeps it fail-closed, and rebuilds it in deterministic chunks.
  Later events either advance the finished cursor safely or force a retry.
- Exact GridForge obstacle events support whole-node blockage. Trailblazer does
  not infer portal narrowing from an obstacle count; partial obstruction
  requires obstacle-shape geometry upstream or a map/connection replacement.

## Capacity And Performance Gates

- Context settings are immutable after initialization and place finite limits
  on maps, cells, source-owned connections/transitions, overlay pages, dynamic
  slots, submitted operations/bytes, pending event entries/bytes, active and
  retired snapshot generations/bytes, concurrent query leases, active and
  retained workspace bytes, cache bytes/entries, single payload bytes, batch
  items, and maintenance work counters.
- Configuration must reserve the current root, one compact fail-closed root,
  and one candidate root. Values that cannot satisfy that minimum reject at
  context creation rather than failing nondeterministically during a tick.
- Exhausted dynamic slots require an explicit checkpoint rebake. Submitted
  mutation or map capacity rejects its receipt. Query/cache/workspace pressure
  returns `CapacityExceeded`; work-counter exhaustion returns `BudgetExceeded`.
- Hot-path geometry/search APIs use caller-owned output and scratch. No
  per-edge world scan, per-node active-grid scan, LINQ, wall-clock budget, or
  managed allocation is permitted after warmup.
- Numeric defaults and absolute timing gates live in `performanceDecisions.md`
  and are accepted only with the recorded Phase 0 benchmark evidence; they are
  not inferred from cell size or topology.

## Mechanical Phase Gates

- Phase 1 API names and signatures must satisfy this ledger and update the
  public API fingerprint intentionally.
- Production Trailblazer may consume only public GridForge seams unless a
  dedicated navigation bridge and architecture allowlist test are added in the
  same change. Phase 0 currently requires no friend-only call.
- Later phases remove each item in `legacyApiDeletionChecklist.md`; no item may
  be checked while any production reference or compatibility facade remains.
- Release and ReleaseLean suites, deterministic behavior characterizations,
  warmed-allocation checks, and the relevant benchmark gate must pass at each
  publication/search cutover.
