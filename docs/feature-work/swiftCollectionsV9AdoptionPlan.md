# SwiftCollections v9 Adoption Plan

## Purpose And Status

**Status: planned; source audit complete, implementation and measurements not
started.** Evaluate `SwiftDenseTicketMap<T>`, `SwiftTicketStore<T>` and native
fixed-point 2D queries at real Trailblazer owners. Adopt only where the complete
consumer workload improves while its existing contracts remain intact. Retaining
an existing representation is a valid outcome; this is not an ECS conversion.

This plan is the owning design and phase-evidence record. It adds no runtime,
test, benchmark, project, dependency-pin or tracker changes. No test-suite,
coverage or performance benchmark result is claimed. The independently checked
disposable public-API probe below used existing local runtime assemblies, with
the relevant HeightmapSurface/FixedMathSharp source unchanged; it supplies one
correctness counterexample and no latency/allocation claim. The source audit used Trailblazer
`c71929aa9682a4f28d698b6d743745a393f409a2` and SwiftCollections
`7c2fa696f10d88194f5a895aaafd7debd8e27574`; refresh affected ownership and
contracts before implementation. Core already references SwiftCollections and
its FixedMathSharp companion v9.0.0 in both package families. Source availability
establishes API shape, not measured adoption benefit or package validation.

## Existing Ownership And Decisions

The audit follows [the architecture guide](../api/guides/Overview.md),
[map publication](../api/guides/MapPublication.md),
[pathing](../api/guides/Pathing.md),
[serialization](../api/guides/Serialization.md), and
[Gravitas integration](../api/guides/Gravitas.md). The completed
[scheduling](done/navigationSchedulingPlan.md),
[recovery](done/navigationRecoveryPlan.md),
[heightmap](done/heightmapImplementationPlan.md),
[2D navigation](done/twoDimensionalNavigationPlan.md), and
[bounded physics query](done/physicsQueryBudgetPlan.md) plans remain the owners
of their existing behavior and evidence. This plan does not reopen their
accepted performance limits or create a second scheduler, graph or physics index.

| Production owner and callers | Current representation and required behavior | Adoption decision |
| --- | --- | --- |
| `Pathing/Search/Guide/TrailblazerGuideService.cs`, `NavigationGuideScheduler.cs`, `.Validation.cs`, `Search/Planar/NavigationGuideScheduler.Planar.cs`; called by `Guides.Schedule`, context `Simulate`, and immediate/scheduled Navigator acquisition | Public reference-type requests, intrusive `Previous`/`Next` FIFO, `_scan`, `_active`, fixed cohort array and continuation cursors. A second linked queue grants fair connector-validation turns. Submission/freeze sequences, retained completed results and descriptor capacities are observable. | Retain. Dense iteration cannot replace FIFO/cohort/validation order. Request reference identity, exactly-once transfer and disposal remain public contracts. Adding a dense lookup beside the linked owners has no demonstrated consumer and duplicates state. |
| `Search/AStar/Payloads/NavigationAStarPayloadCache.cs`, `Search/Flow/Payloads/NavigationFlowFieldPayloadCache.cs`; guide acquire/sample/return and cohort source sharing | Bounded open-address tables, cache-entry references, LRU index links, reservations, free lease slots and generation-checked pooled leases. Cached, active, detached and reserved bytes have separate limits. | Retain. These are semantic cache and ownership systems, not generic integer maps. Swift tickets use a different generation width/lifetime and cannot silently replace the current `long`/`ulong` guards or reference identity. Any later simplification must preserve pin/transfer/eviction and byte-accounting behavior together. |
| `Search/AStar/NavigationAStarWorkspace.cs`, `NavigationAStarNodeTable`; `Search/Flow/NavigationFlowFieldWorkspace.cs`; admission gates and planar route work | Fixed-capacity arrays, epoch-stamped node tables, active-slot arrays, heaps, path/settled slots and indexed postprocessing. These owners already retain contiguous work storage and avoid full table clearing. | Retain. Do not label every array sparse or replace dense buffers merely for API uniformity. A moved dense record would require repairing heap references, parent/path links, active/settled slots, work cursors and partial results within the same counted budget. |
| `Navigation/MovementGroups/MovementGroupState.cs`, `MovementGroupCoordinatorState.cs`, `MovementGroupSession.cs`; `NavSteering.Requests.cs`, `.Simulation.cs`, `.Serialization.cs` and planar `Navigator2D` movement groups | `SwiftSlotMap<MovementGroupMember>` per group; persistent session `GroupIndex`; group IDs and occupant `Guid` dictionary keys; two member scans calculate center/radius/spread and persistent formation offsets. | Conditional dense candidate after the first owner passes. Dense live scans may help heavily churned groups with holes; no benefit is established. Keep group IDs, occupant IDs and member reference objects. Do not turn arbitrary group IDs into sparse slots. Preserve iteration-sensitive arithmetic and replay before considering replacement. |
| `Runtime/TrailblazerWorldContext.cs`, `TrailblazerLifecycleHooks`, `Navigation/TrailblazerNavigationService.cs` | Context/world lifetime, ordered hook lists, internal coordination, independent planar group owners keyed by map, monotonic Navigator `Guid` allocation. No central packed agent registry exists. | Retain. Hosts drive controllers; introducing a world agent/ECS registry would duplicate ownership. Hook order is explicit. Context reset invalidates clock-lifetime/prepared work before reuse. |
| `Heightmaps/HeightmapWorldState.cs`, `TrailblazerHeightmapService.cs`, immutable `HeightmapSurface` and `HeightmapLayerRegistration`; direct host sampling and protected `Navigator.TryApplyHeightmapGrounding` | String-keyed registrations, preferred-name fast path, fallback full scan. Surface is an immutable compressed XZ lattice; registration stores half-open contact-Y eligibility, priority and registration order. | First coherent candidate: conservatively prune fallback XZ candidates using a native fixed-point BVH. Retain authoritative registry, registration objects, preferred lookup and exact winner calculation. Measure the full opted-in grounded-controller frame before accepting. |
| `Pathing/Graph/NavigationWorldGraph.cs`, `Storage/PersistentVoxelIndexMap.cs`, persistent string/configuration maps, `Surface/NavigationSurfaceComponentIndex.cs`, explicit connections and automatic seams | Immutable publication snapshots, topology/cell/medium identities, copy-on-write pages, ordinal cursors and revision-local dependencies. GridForge tracing supplies covered-body geometry. | Retain. These indexes encode exact topology and publication state. A mutable XY broad phase is not an immutable map directory or connectivity proof. No blanket map/endpoint index replacement is planned. |
| `Navigation/Navigator/Occupancy/NavigatorOccupancyTracker.cs`, `Navigation/Steering/NavSteering.cs`; calls into GridForge occupants/neighborhood traversal | GridForge owns occupancy tickets, cells and topology. Steering collects nearby `ISteer` values into existing caller-owned scratch. | Retain. Do not create a second agent/occupancy index or infer a Swift ticket from a voxel, `Guid`, slot or body ID. A different broad phase would still need exact 3D/layer, duplicate, order and neighbor filtering. |
| `Trailblazer.Gravitas/GravitasNavigator3D.cs`, `Planar/GravitasNavigator2D.cs`, `SupportQueryPolicy` | Controllers retain body/collider references, bounded native support/posture work and prepared-frame state. Gravitas owns collision distribution and query indexes. | Retain adapter storage and physics indexes. Verify all-prepare / one-resolve / all-commit and collision-accepted poses through existing adapter scenarios. The planar adapter has its own support semantics; 3D heightmap grounding is not implicitly enabled there. |

All paths above are under `src/Trailblazer` except explicitly named adapter
paths. This table is a source-grounded candidate ledger, not a performance result.

## Provider Contracts To Preserve

Provider source of truth is the SwiftCollections repository. The v9.0.0 API
references are [SwiftDenseTicketMap](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/src/SwiftCollections/Collection/DenseTicketMap/SwiftDenseTicketMap.cs),
[SwiftTicketStore](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/src/SwiftCollections/Collection/TicketStore/SwiftTicketStore.cs),
[dense state](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/src/SwiftCollections/Serialization/State/SwiftDenseTicketMapState.cs),
[store state](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/src/SwiftCollections/Serialization/State/SwiftTicketStoreState.cs),
[fixed-point BVH2D](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/src/SwiftCollections.FixedMathSharp/Query/BVH/SwiftFixedBVH2D.cs),
[inherited BVH query operations](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/src/SwiftCollections/Query/BVH/SwiftBVH.cs),
[fixed-point bounds](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/src/SwiftCollections.FixedMathSharp/Query/Bounds/FixedBoundVolume2D.cs),
[spatial hash2D](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/src/SwiftCollections.FixedMathSharp/Query/SpatialHash/SwiftFixedSpatialHash2D.cs),
and [quadtree](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/src/SwiftCollections.FixedMathSharp/Query/Quadtree/SwiftFixedQuadtree.cs).
Recheck the [collection-selection](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/docs/api/guides/collections.md)
and [spatial-query](https://github.com/mrdav30/SwiftCollections/blob/v9.0.0/docs/api/guides/spatial-queries.md)
guides and current owner serialization partials when implementing; do not copy
their algorithms into Trailblazer.

- `SwiftDenseTicketMap<T>` issues `SwiftTicket` values with fresh generation one,
  rejects default, retires exhausted slots and removes by swap-back. Surviving
  tickets remain valid; dense positions, enumeration, spans and references do
  not. A ticket contains no issuer/world/timeline identity. `Values`, `GetRef`
  and `GetTicketAt` may only be borrowed within a structural-mutation boundary.
  `Clear` preserves issuance history; constructing a fresh owner is not equivalent
  while old tickets may escape. State includes dense order, slot generations and
  free order, rather than only live values; capacities are reconstructed.
- `SwiftTicketStore<T>` attaches to externally issued tickets. It accepts
  generation zero and rejects every attachment at an occupied index, including a
  different generation. Local membership cannot prove issuer liveness; the owner
  must validate and detach before issuer removal/reuse. Dense order in two stores
  is unrelated: joins require the ticket from `GetTicketAt`, issuer validation
  where needed, and `TryGet` on the other store. Budget these lookups and missing
  components. Sparse bytes follow highest index, not live count. Reattachment also
  does not give delayed component-instance work a fresh identity.
- **Retain decision for TicketStore:** this audit found no production
  `SwiftTicketMap`/`SwiftDenseTicketMap` issuer in Trailblazer. Existing group
  indices, request sequences, graph ordinals, occupant tickets and Gravitas IDs
  are not a demonstrated interchangeable issuer. Do not synthesize generation
  zero or create an issuer solely to justify stores. A future genuinely separate
  owner and optional payload can reopen this decision with attach/teardown,
  issuer-scoped routing, rollback and bounded sparse-index evidence.
- `SwiftFixedBVH2D<TKey>`, `SwiftFixedSpatialHash2D<TKey>` and
  `SwiftFixedQuadtree<TKey>` use `FixedBoundVolume2D` in native XY coordinates.
  Bounds use inclusive intersection, including zero-size point rectangles.
  They copy bounds; equal-key insertion updates the existing entry. They append
  to caller-owned results, have mutation-history-dependent traversal order and
  require external synchronization without callback reentry. BVH node-array
  borrows/indices are not stable identities. In released v9.0.0, ordinary BVH
  `Query` can grow scratch; inherited
  `TryQuery(bounds, SwiftList<TKey>, Span<int>)` requires a plain list with spare
  capacity and traversal length each at least leaf `Count`.
  Insufficient storage returns false without partial output. Never interpret
  incomplete collection as no hit. Constructor/`EnsureCapacity` capacity counts
  node slots, not leaves: N leaves require `2N-1` live nodes.

The 2026-11-25 review also checked SwiftCollections source at
`99ee3e042e976e394bf45d6376fd0e0bccc400b6`. That revision reserves owner-held BVH
traversal storage during construction/node growth; ordinary `Query` no longer
lazily grows it. Select and cost the API against the actual dependency graph:
compare `Query` with reserved result capacity against `TryQuery`, including any
caller-owned traversal array retained alongside the provider's internal array.
Do not assume this source behavior is present in the released v9.0.0 packages.

Native XY means the two coordinates supplied to the index. For heightmaps map
`Vector3d(X,Y,Z)` to `Vector2d(X,Z)` explicitly, for query and both bounds.
Do not reinterpret world Y as heightmap depth. Top-down, side-view and spatial
navigation have separate GridForge planar bindings and embeddings. A 2D rectangle
is a conservative candidate filter only: it cannot establish stacked-layer
membership, cell presence, hex/prism adjacency, clearance, supported posture,
3D/mixed collision, nearest admissible endpoint, medium or a dependency proof.

## First Implementation Slice: Heightmap Fallback Candidates

The first eligible change lives in the existing heightmap state/service. A
`SwiftFixedBVH2D<HeightmapLayerRegistration>` is only eligible after proving a
conservative envelope for the existing sampler's full accepted XZ domain;
`Surface.MinBounds`/`MaxBounds` alone are insufficient. The same immutable
registration references are indexed, avoiding a per-hit name
join or second identity owner. Keep `LayersByName` for unique-name admission,
`TryGetRegistration`, `IsRegistered`, unregister and the preferred-layer fast
path. Allocate a plain reusable candidate `SwiftList<HeightmapLayerRegistration>`
at registration/growth boundaries. If the selected API is `TryQuery`, also reserve
an integer traversal buffer; both its length and spare result capacity must cover
the complete registered count, not the expected number of hits. Cost provider
node capacity and internal traversal storage separately.

The production entry path remains `context.Heightmaps.TrySampleGround`; an
opted-in concrete Navigator calls protected `TryApplyHeightmapGrounding` from
its actual traversal probe. The base Navigator does not call it automatically.
The existing `HeightmapTestNavigator` exposes that protected behavior for tests;
use the same production path in a complete host workload instead of timing an
unconnected index. Do not enable heightmaps on an adapter merely to manufacture
a consumer.

**Demonstrated veto against tight footprint pruning.** The independent audit
exercised the public surface and BVH APIs with
`HeightmapSurface.FromHeights("point", new Fixed64[,] { { 0 } }, Vector3d.Zero,
(Fixed64)4, new HeightmapCompression(Fixed64.Zero, Fixed64.One))`. At world
Y/Z zero, set world X using `Fixed64.FromRaw(r)`. Sampling succeeds for raw
`r = -2, -1, 0, 1, 2` and fails at `-3` and `3`; an index rectangle from
`MinBounds` to `MaxBounds` contains only raw X zero. Division rounds the local
coordinate before the sampler checks its range; the half-way values round to
even zero. This is an adoption counterexample, not a claim that the current
sampler is defective. Do not tighten its behavior as an optimization side effect.

First profile baseline fallback frequency and containing-frame cost. Only if that
opportunity justifies further work, and before implementing any pruning, derive
or reuse a conservative inverse-quantization envelope in the owning arithmetic
layer, checking actual fixed-point subtraction, multiplication, division rounding
and saturation over the supported
domain. Test raw-unit neighbors around both ends and interval half-way values,
including translated, extreme-coordinate, one-row/column and one-sample
surfaces. `Fixed64.FromRaw(1)` denotes one Q32.32 unit; the named `Epsilon`
tolerance is not a substitute. Ambiguous/unrepresentable cases must conservatively
reach the exact sampler through an accounted fallback, or retain the whole scan.
Do not add copied wide arithmetic downstream or a speculative general helper.
If proving and maintaining the envelope costs more than the measured opportunity,
stop this candidate before timing an inequivalent implementation.

For each fallback query clear only caller-owned candidate contents, collect the
point rectangle at `(worldPosition.X, worldPosition.Z)`, then run the existing
exact `TrySampleRegistration` and `IsBetterCandidate` logic. Keep:

1. preferred-name sampling first, returning immediately when valid even if a
   different layer would otherwise win;
2. contact Y in `[MinSelectionY, MaxSelectionY)`, exact compressed interpolation
   and the sampler's rounded-coordinate acceptance domain, including raw-unit
   boundary neighbors and 1x1/1xN/Nx1 surfaces;
3. smaller absolute contact-to-ground distance, then higher priority, then
   smaller registration order. Layer name is lookup identity, not an additional
   ordering tie;
4. the original `HeightmapLayerRegistration` object identity returned by public
   lookup, and the same successful/failure sample values;
5. unregister/re-register order, per-context isolation, inactive/disposed errors,
   reset/dispose reference release and the existing single-owner access model.

Clear candidate references after every fallback sample, including exceptional
exits, normally in `finally`; retain array capacity. Use ordinary `Clear()`
consistently, not `FastClear()`, which retains references. If references may
already be hidden beyond `Count`, use `Clear(true)`. Test fallback sampling,
unregistering a candidate, then only preferred-layer hits or no further sampling:
the removed registration/surface must be released without another fallback query.
Reset/dispose must also release references. Integer traversal slots retain no
surface references and need no comparable cleanup.

No nearest-neighbor shortcut may replace the full reduction over eligible hits.
Traversal order must not choose the winner. An overlapping but Y-ineligible
rectangle encountered first must not hide a valid different floor. Large
overlapping stacks can produce all registrations and may offer no benefit.

Registration-order uniqueness needs an explicit boundary check. The current
`NextRegistrationOrder` is an unchecked `int`; after full counter wrap, distinct
live registrations can share an order. Equal distance, priority and order then
make the baseline winner depend on dictionary traversal. Emulate counter reuse
through internal test state and compare the selected layer against the scan;
preserve baseline fallback behavior for ambiguous ties, or separately decide an
exhaustion contract. Do not silently add a layer-name tie-breaker or change counter
width. This is a source-derived adoption risk, not a reproduced BVH defect. Where
the reduction keys uniquely select a winner, consume unsorted hits directly;
sorting to mimic dictionary traversal adds no selection value.

Registration and unregister must keep registry/index/scratch publication
coherent. Stage capacity and candidate construction before publishing new
membership; preserve the baseline public failure mutation behavior and clean up
any partially prepared index entry. Audit provider mutation/exception behavior
before selecting in-place update versus rebuilding a candidate index. Count the
real mutation cost and transient retained peak. Do not assume `EnsureCapacity`
alone makes BVH insertion allocation-free. Avoid a permanent shadow registry,
versioned cache or an unconditional per-frame rebuild. Preserve existing
Chronicler/ChronicleGuard owners rather than adding lifecycle frameworks.

Measure BVH against the existing scan first. Spatial hash is a later alternative
only if measured layer sizes/churn justify its cell registration and broad-range
cost. Quadtree requires an explicit fixed root and wholly contained bounds; do
not add a new world-bounds contract just for a benchmark. Keep the scan if the
representative case cannot justify the index and scratch retained cost. A
measured, simple small-count cutoff is allowed only with one authority, complete
tests and both branches costed; do not introduce it speculatively.

## Conditional Dense Movement-Group Slice

After reviewing the first slice, independently measure common small groups and
large churned groups. The candidate is replacing only the member slot owner with
`SwiftDenseTicketMap<MovementGroupMember>`, storing its actual ticket in the
internal session and retaining that group's issuer reference/lifetime when
validating it. Keep public `int` group IDs and Navigator/occupant `Guid` values.
`MovementGroupMember` remains a reference object unless separate evidence
justifies a payload change. No optional stores or component join are needed.

Before changing it, enumerate every `GroupIndex` read/reset and planar/global
coordinator binding, including `Prewarm`, owner changes, leave/group-switch,
profile replacement, stop, serialization population, context reset and disposal.
Old sessions must not attach to a numerically matching ticket from a replaced
group or another map/context. Detach first, clear aliases and validate issuer
identity across callbacks. Never retain a dense index/ref/span through member
removal, capacity growth, host callback, group replacement or reset.

Current member iteration follows slot order. Swap-back changes reduction order
and can change bounded fixed-point sum overflow/failure behavior, not just an
enumerator cosmetic. Prove center, radius, offsets, spread and destination/mode
against baseline under non-tail removal, stale members, different destinations,
extreme valid values and the same ordered host updates. Keep one-frame freshness
and map isolation. Preserve snapshot continuation: group slot/ticket aliases are
runtime state and must be reacquired; durable group ID/mode/formation records
must retain their existing schema and populate-existing-instance behavior.
Heightmap registrations and indexes likewise remain context runtime state;
do not serialize new collection `State` envelopes merely because the provider
supports them. Rebuild from the host's existing restored surface registrations.

If equivalent behavior requires an ordered view, cost construction, removal
repair and sorting on the containing frame before acceptance. Do not silently
add a permanent second membership owner or reorder steering, acquisition,
collision/solver submission or commit loops. Retain `SwiftSlotMap` if exact order
cannot be kept simply, dense capacity/history costs exceed the benefit, or any
observed gain disappears with repair included.

## Verification And Measurement Contract

Use existing context-first fixtures and recording/hash facilities. Extend
existing scenarios rather than creating a second scheduler, replay system or
benchmark launcher. Baseline and candidate must execute identical inputs,
dependencies, settings, registration/update order and host phase order.

| Boundary | Existing evidence to extend | Required oracle and adverse controls |
| --- | --- | --- |
| Heightmap sampling/grounding | `tests/Trailblazer.Tests/Heightmaps/TrailblazerHeightmapService.Tests.cs`, `HeightmapSurface.Tests.cs`, `Navigation/Navigator/NavigatorHeightmapGrounding.Tests.cs` | Hand-authored expected sample coordinates/heights/layer names; preferred valid/invalid, exact half-open Y endpoints, rounded XZ edges/raw-unit neighbors, negative/extreme coordinates, degenerate footprints, stacked floors and every tie level. Include the interval-four point counterexample above. Check accepted root/contact, LastPosition, snap limits, Solid-only behavior, occupancy transfer and failed projection; graph destination must stay on the exact authored floor. |
| Member identity and formation | `Navigation/MovementGroups/MovementGroupCoordinator.Tests.cs`, `Navigation/Steering/NavSteering.Tests.cs`, planar Navigator tests | Remove non-tail then reuse, old-session/foreign-owner rejection, stale/different-destination members, owner/group/map changes, retained member objects, capacity growth, reset and callback reentry/teardown. Exact destinations, modes, headings and all group metrics; no lost/duplicated members. |
| Guidance and publication controls | `Pathing/Graph/NavigationGuideSchedulingTests.cs`, `TrailblazerGuideServiceTests.cs`, `NavigationFlowFieldGuideTests.cs`, TopDown/SideView scheduling/guide tests | Same FIFO/cohort membership, oldest build, fairness turns, completion statuses/frame, work counters, independent cursors, pending/cancelled/unclaimed capacity, stale lease proofs and reset drainage. Hold an old lease across intersecting publication/removal and unrelated controls; do not weaken cache proofs for a storage change. |
| Durable state and physical continuation | `NavigatorSerialization.Tests.cs`, planar serialization tests, `Runtime/TrailblazerWorldContextLifecycle.Tests.cs`; adapter `GravitasNavigator3D.PlatformReplay.Tests.cs`, `.RestoreLifecycle.Tests.cs`, planar lifecycle/accepted-motion tests | JSON and standard MemoryPack populate existing shells transactionally, then continue identical fixed frames and hashes with reconstructed runtime ownership. Malformed state leaves the shell unchanged. Prepare all / resolve once / commit all, clipped/off-segment accepted poses, stale prepared frames, body lifetime, support incompleteness and profile replacement remain exact in both dimensions. |

Index scratch must never escape the sample call or be overwritten by nested
sampling/registration callbacks. The chosen API has no host result callback;
use the existing non-reentrant ownership boundary and do not expose callback
adapters that create one. If a changed path invokes host code, test mutation,
reset/disposal and thrown callbacks at that actual boundary. Borrow tests must
distinguish stable payload object references from invalidated container refs,
spans and indices; do not require unsupported retained storage to stay valid.

Start performance work by reproducing representative complete grounded-Navigator
frames, using the existing fixtures and benchmark lifecycle. Extend
`NavigationGuidedFrameBenchmarks` or the closest existing host scenario to
actually opt in to heightmap grounding. Existing guided-frame rows do not prove
heightmap sampling cost merely because their agents move. Reuse
`NavigationGuideServiceBenchmarks`, `NavigationScheduledFrameBenchmarks`,
`NavigationRecoveryBenchmarks` and `NavigationPlanarFrameBenchmarks` as unchanged
acquisition/recovery/native-physics controls where relevant; the last already
includes real Gravitas complete frames.

The measured envelope includes registration/setup, cold acquisition, publication
and scheduled work in `context.Simulate`, controller simulation/grounding,
motion acceptance and commit, `LateSimulate` where used, unregister/removal and
teardown. Record these episodes separately and compare full containing frames;
an isolated lookup/iteration microbenchmark is attribution only. Keep host
ordering and action/physics phases unchanged. Do not extrapolate the historical
Flow/500 results into a new budget claim or reopen its deferred 2 ms goal.

Measure small ordinary registries (including 0/1), many disjoint layers, fully
overlapping stacks, preferred hits, preferred failures followed by fallback,
empty candidate sets and candidates rejected by exact sampling, boundary-crossing
agents, negative/far coordinates and deterministic register/unregister churn. Use the
same agent populations as the existing relevant frame scenarios. For optional
dense groups include no churn, sparse high-water slots after churn, frequent
non-tail removal, small groups, large groups, multiple groups/maps and teardown.

Use representative hit/miss proportions and value-consuming mixed lookups where
lookup costs matter, including normal tiered/PGO execution. The provider's
`SC-Benchmark-009` record in SwiftCollections
`docs/feature-work/benchmark-signal-hardening-backlog.md`, reviewed 2026-11-25,
closed with no runtime change after miss controls exposed regressions in tested
lookup optimizations. All-hit gains alone cannot select a collection or establish
a Trailblazer speedup.

Record time per full frame/episode, frame P50/P95/P99/max, readiness frames and
cumulative acquisition work; measure cold and warmed allocation separately.
Record allocated bytes, collections, peak and settled retained bytes, capacity
growth counts and long-session high-water behavior. The baseline already owns
registration/surface/member objects: count incremental node arrays, key maps,
free stacks, candidate and traversal arrays, registry references, ticket
metadata/free/retired slots, and any ordering/join scratch without double-counting
shared payload objects. Measure actual runtime layout; do not invent a bytes per
entry constant. For store experiments, include sparse capacity and each ticket
join; for ordered dense iteration include every repair/copy/sort. Allocation zero
alone does not establish bounded retained memory or timing.

Freeze source/dependency identities, configurations, fixture values and commands
in phase evidence. Freeze the common benchmark host and record hashes of the
benchmark assembly, manifests/configuration and runtime dependencies; vary only
the intended runtime payload between comparison arms. Unchanged benchmark source
alone does not establish identical benchmark binaries. Verify the actual loaded
files, including generated child payloads. Use balanced baseline/candidate runs
without competing builds/profilers; verify expected cases, successful child exits
and complete allocation samples. Preserve adverse and
inconclusive results. Accept only with exact behavioral/replay equivalence,
preserved warm allocation gates and a reproducible containing-frame benefit or
an explicitly accepted measured tradeoff. Unclear signal, common-case regression,
excess retained memory, changed ordering/status or lost lifecycle proof means
retain/simplify before expansion. Thresholds and target-hardware budget must be
recorded from the reproduced baseline before candidate tuning; no gain is claimed
by this plan.

## Implementation Phases

### Phase 0 — Reproduce And Choose

- Refresh the owner ledger and provider signatures; preserve worktree changes.
- Reproduce a full production-path grounded-controller scene and baseline
  semantic/retained-byte/allocation/frame observations using existing machinery.
- Profile fallback scanning versus preferred hits and all other containing-frame
  work. Define representative sizes and acceptance thresholds before optimizing.
- If scans are immaterial or index ownership costs dominate, retain current
  storage and close that candidate with reopening criteria before investing in
  envelope arithmetic.
- Only for a justified candidate, prove a conservative sampler envelope or retain
  scanning. The demonstrated rounded-coordinate counterexample remains a
  correctness gate before any pruning implementation or equivalence claim.
- Select traversal storage for the actual provider version, account for candidate
  reference cleanup and registration-order reuse, then record the heightmap
  go/no-go. Do not force a replacement to finish a phase.

### Phase 1 — Implement And Prove One Owner

- Implement only the selected heightmap candidate; keep exact sampling/winner
  logic and current public identity/lifecycle. Add focused behavioral and
  failure-boundary coverage beside existing tests.
- Run the discriminating stacked-floor/boundary and full-controller oracle,
  allocation/retained-memory gates and matched containing-frame measurements.
- Obtain independent review at the first working production slice, including
  ordering, mutation publication, memory, source growth and deletion opportunities.
  Resolve findings before expansion; record retain/revert if the gate fails.

### Phase 2 — Decide Dense Members Without Forcing Stores

- Measure the actual movement-group callers and churn shapes. Only implement the
  dense member owner if the predicted benefit survives session/issuer/order
  requirements. Otherwise record the existing slot owner as retained.
- If implemented, prove the complete global and planar lifecycle, continuation,
  retained-borrow and reentrancy cases, then measure complete acquisition/movement/
  commit/teardown episodes with repair costs included. Review independently.
- Keep TicketStore and all retained owners out of scope unless a genuine new
  production owner and measured need establish a coherent separate slice.

### Phase 3 — Verify And Close

- Build core and adapter for `netstandard2.1` and `net10.0` in `Release` and
  `ReleaseLean`; execute relevant focused tests and then both complete suites on
  the supported Windows/Linux matrix. Standard includes JSON/MemoryPack;
  Lean includes JSON and the existing transport exclusions.
- Collect fresh independent reports with each test project's
  `coverlet.runsettings`; run `.assets/scripts/assert-full-coverage.ps1` over
  both assemblies. Require exactly 100% reported line, branch and executable
  method coverage for `Trailblazer` and `Trailblazer.Gravitas` in both variants.
  Remove proven unreachable branches; no exclusions or rounded-percentage waiver.
- Recheck complete child-run evidence, replay/serialization continuation,
  allocations, retained capacity and containing-frame measurements after final
  source changes. Record exact results, limits and every retain/no-go decision
  here; review final connected code and update affected evergreen docs/XML.
- Existing package mode remains normal. If implementation requires coordinated
  upstream changes, use documented `UseLocalLsfStack=true` validation with global
  `DisableTransitiveProjectReferences=true`, provider-to-consumer order and
  verified child provenance; record those exact revisions. Only then remind the
  owner to update affected CI build/test dependency pins after upstream lands.
  No provider change or pin edit is presumed by this plan.

Completion is implementation and verification of accepted slices, with explicit
retained decisions for the rest. Release/distribution readiness is not a phase
or gate. Keep concise evidence in this tracked plan, without local ignored
artifact links or standalone phase reports. Leave all work unstaged/uncommitted.
