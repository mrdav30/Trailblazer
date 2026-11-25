# SwiftCollections v9 Adoption Plan

## Purpose And Status

**Status: Closed and archived on 2026-11-25 with retain decisions.** Phase 0 and
Phase 2 profiling are complete. Retain heightmap scanning and movement-group
SlotMap storage; TicketStore has no demonstrated issuer/consumer. No envelope,
index or dense runtime adoption is selected.

This investigation evaluated `SwiftDenseTicketMap<T>`, `SwiftTicketStore<T>` and
native fixed-point 2D queries at real Trailblazer owners. Adoption required the
complete consumer workload to improve while preserving existing contracts.
Retaining the existing representations is the accepted outcome.

This plan is the owning design and phase-evidence record. Phases 0 and 2 add
benchmark coverage and measured allocation signals, with no retained runtime,
project or dependency changes. Measurements and focused checks are recorded below; no full-suite,
coverage, package-readiness or adoption speedup claim is made. The independently checked
disposable public-API probe below used existing local runtime assemblies, with
the relevant HeightmapSurface/FixedMathSharp source unchanged; it supplies one
correctness counterexample and no latency/allocation claim. The source audit used Trailblazer
`c71929aa9682a4f28d698b6d743745a393f409a2` and SwiftCollections
`7c2fa696f10d88194f5a895aaafd7debd8e27574`; refresh affected ownership and
contracts before implementation. Core already references SwiftCollections and
its FixedMathSharp companion v9.0.0 in both package families. Source availability
establishes API shape, not measured adoption benefit or package validation.

## Existing Ownership And Decisions

The audit follows [the architecture guide](../../api/guides/Overview.md),
[map publication](../../api/guides/MapPublication.md),
[pathing](../../api/guides/Pathing.md),
[serialization](../../api/guides/Serialization.md), and
[Gravitas integration](../../api/guides/Gravitas.md). The completed
[scheduling](navigationSchedulingPlan.md),
[recovery](navigationRecoveryPlan.md),
[heightmap](heightmapImplementationPlan.md),
[2D navigation](twoDimensionalNavigationPlan.md), and
[bounded physics query](physicsQueryBudgetPlan.md) plans remain the owners
of their existing behavior and evidence. This plan does not reopen their
accepted performance limits or create a second scheduler, graph or physics index.

| Production owner and callers | Current representation and required behavior | Adoption decision |
| --- | --- | --- |
| `Pathing/Search/Guide/TrailblazerGuideService.cs`, `NavigationGuideScheduler.cs`, `.Validation.cs`, `Search/Planar/NavigationGuideScheduler.Planar.cs`; called by `Guides.Schedule`, context `Simulate`, and immediate/scheduled Navigator acquisition | Public reference-type requests, intrusive `Previous`/`Next` FIFO, `_scan`, `_active`, fixed cohort array and continuation cursors. A second linked queue grants fair connector-validation turns. Submission/freeze sequences, retained completed results and descriptor capacities are observable. | Retain. Dense iteration cannot replace FIFO/cohort/validation order. Request reference identity, exactly-once transfer and disposal remain public contracts. Adding a dense lookup beside the linked owners has no demonstrated consumer and duplicates state. |
| `Search/AStar/Payloads/NavigationAStarPayloadCache.cs`, `Search/Flow/Payloads/NavigationFlowFieldPayloadCache.cs`; guide acquire/sample/return and cohort source sharing | Bounded open-address tables, cache-entry references, LRU index links, reservations, free lease slots and generation-checked pooled leases. Cached, active, detached and reserved bytes have separate limits. | Retain. These are semantic cache and ownership systems, not generic integer maps. Swift tickets use a different generation width/lifetime and cannot silently replace the current `long`/`ulong` guards or reference identity. Any later simplification must preserve pin/transfer/eviction and byte-accounting behavior together. |
| `Search/AStar/NavigationAStarWorkspace.cs`, `NavigationAStarNodeTable`; `Search/Flow/NavigationFlowFieldWorkspace.cs`; admission gates and planar route work | Fixed-capacity arrays, epoch-stamped node tables, active-slot arrays, heaps, path/settled slots and indexed postprocessing. These owners already retain contiguous work storage and avoid full table clearing. | Retain. Do not label every array sparse or replace dense buffers merely for API uniformity. A moved dense record would require repairing heap references, parent/path links, active/settled slots, work cursors and partial results within the same counted budget. |
| `Navigation/MovementGroups/MovementGroupState.cs`, `MovementGroupCoordinatorState.cs`, `MovementGroupSession.cs`; `NavSteering.Requests.cs`, `.Simulation.cs`, `.Serialization.cs` and planar `Navigator2D` movement groups | `SwiftSlotMap<MovementGroupMember>` per group; persistent session `GroupIndex`; group IDs and occupant `Guid` dictionary keys; two member scans calculate center/radius/spread and persistent formation offsets. | Retain after Phase 2. The isolated dense pilot has mixed timing signals, 75% more backing-array element storage, larger sessions and remaining terminal-retirement order work. Keep group IDs, occupant IDs and member reference objects; reopen only for a justified large sparse workload with explicit memory and lifecycle budgets. |
| `Runtime/TrailblazerWorldContext.cs`, `TrailblazerLifecycleHooks`, `Navigation/TrailblazerNavigationService.cs` | Context/world lifetime, ordered hook lists, internal coordination, independent planar group owners keyed by map, monotonic Navigator `Guid` allocation. No central packed agent registry exists. | Retain. Hosts drive controllers; introducing a world agent/ECS registry would duplicate ownership. Hook order is explicit. Context reset invalidates clock-lifetime/prepared work before reuse. |
| `Heightmaps/HeightmapWorldState.cs`, `TrailblazerHeightmapService.cs`, immutable `HeightmapSurface` and `HeightmapLayerRegistration`; direct host sampling and protected `Navigator.TryApplyHeightmapGrounding` | String-keyed registrations, preferred-name fast path, fallback full scan. Surface is an immutable compressed XZ lattice; registration stores half-open contact-Y eligibility, priority and registration order. | Retain after Phase 0. Preferred hits bypass scanning and natural crossings rarely need fallback. Persistent missing coverage or an explicit churn/cold-population budget can reopen conservative XZ pruning; the registry, registration objects and exact winner remain authoritative. |
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

The baseline experiment uses the existing input-driven benchmark host lifecycle
as the smallest complete grounded-controller case: 100/500 Navigators at 32 Hz,
with a published 100x100 map, 32 preparation frames and 64 measured frames per
block. It includes context simulation, input, controller simulation/grounding,
commit and late simulation, without guide acquisition or physics. This is a
lower-bound host workload, not a replacement for guided-frame acceptance.
Separate preferred hits, absent coverage, natural stripe-boundary crossings,
alternating-name registration churn, eligible stacks and Y-rejected layers.
Preserve sticky active-layer selection. Caller-boundary grounding timers run in
a separate attribution pass; uninstrumented containing frames supply the baseline.

Before capturing timings, the provisional pursuit threshold is a repeatable
opportunity of at least 0.1 ms and 5% of the 500-controller containing-frame cost
in a justified recurring workload. Ordinary preferred hits and up to 64 layers
are the starting envelope; miss-every-frame controls are stress cases rather than
an assumed host distribution. These are investigation thresholds, not a product
budget or an accepted regression allowance. A material stress-only cost calls for
an explicit reopening workload, not automatic envelope/index implementation.

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

#### Phase 0 Result — Retain Scanning (2026-11-25)

**Decision:** no-go for heightmap indexing on the demonstrated ordinary workload.
Do not start envelope work. Preferred selection already removes the scan after
acquisition; natural crossings did not establish enough recurring fallback work
to justify an index and its proof, mutation and retained-storage costs. This is
not a claim that indexing cannot help a different host. Reopen for demonstrated
persistent missing coverage, larger registries, frequent cold population, or a
specific churn-frame tail requirement, with a containing-frame budget and actual
fallback distribution. Dense members and TicketStore were not measured here.

**Source and environment.** Trailblazer base
`47cdd5692c551f7ae06802309ebc56847e6fdf0e` plus the reviewable benchmark changes;
runtime source is unchanged. Coordinated source dependencies:

| Provider | Commit |
| --- | --- |
| Chronicler | `0281469ecc40e65a05867f473bc10f030e7f69f8` |
| FixedMathSharp | `8fdf889322f1bb82501c2afec63d6bb2f304ace2` |
| SwiftCollections | `99ee3e042e976e394bf45d6376fd0e0bccc400b6` |
| GridForge | `2162ba65a091d1cb26cfc02d64f64032ec9340d0` |
| Gravitas | `7702d822c94cbeab43f9ebd9676b9a3e3c7cf98a` |

SwiftCollections had existing benchmark/test/tracker edits, with no runtime
edits; those were preserved. Host: Windows 11 Home x64 `10.0.26300`, Intel
i7-9700K (8 logical CPUs), SDK `10.0.302`, runtime `10.0.10`, Release/net10.0,
normal runtime tiering/PGO. Captures ran serially at Below Normal priority with
no affinity/power-plan change or competing builds/tests/profilers.

**Workload and reproduction.** The retained
`NavigationHeightmapFrameBenchmarks` extends the existing input-driven benchmark
host through a derived `BenchmarkNavigator`; runtime files are untouched. The
11 workload definitions and exact construction are in that class and the
[benchmark guide](../../../tests/Trailblazer.Benchmarks/README.md#heightmap-baseline-frames).
The map floor is Y=-0.5, radius is 0.25, height is 1, and root-to-foot offset is
zero. Compressed sample zero decompresses to that floor with step 1. Preferred
surfaces span [-1,99] on XZ; off-route surfaces start at X=1000+200i. Crossings
use 64 adjacent inclusive unit-X stripes starting at -0.5, spanning Z=[-0.5,99.5].
Agents start at X=2+(i%25)+(i%32)/32, Z=2+2*(i/25), and move right with the
unchanged Slow speed limit of 0.1 units/s. Grounding uses
`SurfaceLevelAndPosition`; these flat cases require no root correction. The
churn control alternates the covering surface name every 16 frames, including
unregister/register inside the timed frame. Eligible stacks select `layer-2`
through priority then registration order. Y-rejected layers lie above contact Y.

```powershell
dotnet build tests/Trailblazer.Benchmarks/Trailblazer.Benchmarks.csproj -c Release -f net10.0 -p:UseLocalLsfStack=true -p:DisableTransitiveProjectReferences=true -m:1 -p:BuildInParallel=false
# Copy all top-level output files to a frozen host directory before timing.
dotnet <frozen-host>/Trailblazer.Benchmarks.dll heightmap-capture 1
dotnet <frozen-host>/Trailblazer.Benchmarks.dll heightmap-capture 2
dotnet <frozen-host>/Trailblazer.Benchmarks.dll heightmap-capture 3
dotnet <frozen-host>/Trailblazer.Benchmarks.dll heightmap-capture 4 profile
```

All four processes exited zero. Each produced 22 cases x 4 blocks x 64 frames:
88 sample records / 5,632 timed frames per launch. Baseline totals are **16,896
uninstrumented frames**; the separate attribution pass has **5,632 frames**.
The per-case preflight and at least four warmup blocks spanning at least 300 ms
were excluded. Workload order reverses on even launches. All observation arrays
were complete, all frames had exactly one grounding attempt per controller, and
every frame hash agreed across blocks, launches and attribution mode. Independent
checks cover preferred persistence and fallback winners, positive X motion,
floor/vertical velocity, authored committed cells, real occupancy tickets,
old-cell removal and reset drainage. An initial exploratory capture with no
timed stripe crossings was excluded; the final staggered scene fixes that
fixture omission without changing runtime behavior.

**Frozen binary identities (SHA-256).** Loaded benchmark/core/GridForge/math/
collections paths were emitted by `HEIGHTMAP_RUNTIME`; the copied host's
manifest and configuration were also hashed. The measured identities were:

| File | SHA-256 |
| --- | --- |
| Trailblazer.Benchmarks.dll | `40693EA1195351ACA5F72CA32BFD0394E2F19F9724AB6AD7890FB78869222A7F` |
| Trailblazer.dll | `828071DAA6F65C3B10D25DCD8FD3CC0C3E3B059D2F635CB79CCB74308B3E657A` |
| SwiftCollections.dll | `3E30E23DE91959463FB2DA3F964E7A1477E15D85FFC848E2E21590760A12F083` |
| FixedMathSharp.dll | `4B9BC065906409A277CD2BD632C9C0AB1ACC20B216BB27D0BA78A2819A1FAFBC` |
| GridForge.dll | `B3922A64F316EBC2BFCABC71055EE9D5F782E03D501AEBB18A9C752F37BDA436` |
| Gravitas.dll | `F505EA309474BE6C34E51917E91ED9FAC71B4E94ABD66B201739E51C7A79193C` |
| Chronicler.dll | `74AA809F8F3AC73ECEB5A8F420D4FE3F29ED836FC9F15A8214C8ACB1305B555D` |
| Trailblazer.Benchmarks.deps.json | `69364258056DB7028E9448A61BFB9A717B3B8A5C12FFFE077E50CAF43A44BE91` |
| Trailblazer.Benchmarks.runtimeconfig.json | `C230A317A54DD960BCBEB5F347F52E18DC665A26F7EFDA2159FCED9A5AC7E097` |

**Containing-frame observations.** Values are milliseconds per complete frame,
pooled over three processes (768 observations per population/workload). Percentiles
use linear interpolation at rank `(N-1)*p`; no frames or outliers were removed.
These are observed workstation distributions, not portable latency guarantees.

| Workload | 100 P50 | 500 P50 | 500 P95 | 500 P99 | 500 max | 500 fallback calls |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Disabled | 0.490 | 2.165 | 2.978 | 3.535 | 4.322 | 0% |
| Empty | 0.444 | 2.186 | 2.993 | 3.476 | 5.030 | 100% |
| Preferred1 | 0.423 | 2.386 | 3.644 | 4.842 | 9.068 | 0% |
| Preferred16 | 0.396 | 2.317 | 3.256 | 4.095 | 9.739 | 0% |
| Preferred64 | 0.398 | 2.314 | 3.292 | 3.981 | 6.365 | 0% |
| Missing16 | 0.476 | 2.695 | 3.874 | 4.420 | 5.239 | 100% |
| Missing64 | 0.764 | 4.413 | 7.140 | 10.911 | 18.457 | 100% |
| Crossing64 | 0.391 | 2.436 | 4.016 | 5.214 | 37.335 | 0.45% |
| Churn64 | 0.405 | 2.445 | 4.361 | 5.968 | 17.338 | 6.25% |
| Stacked64 | 0.401 | 2.316 | 3.083 | 4.114 | 70.014 | 0% |
| YRejected64 | 0.466 | 2.677 | 4.461 | 7.307 | 10.842 | 100% |

The 100-controller crossing rate was 0.4375%. In process order 1/2/3, the
500-controller Preferred64 medians were 2.409/2.327/2.202 ms; Missing64 was
5.039/4.196/4.017 ms. First-process drift and large isolated tails remain in the
record; small differences between preferred-layer rows establish neither a
regression nor a registry-size benefit. The 100-controller maximum across all
rows was 13.475 ms. No source attribution for the large tails is claimed.

The separate 500-controller attribution pass measured mean inclusive grounding
time of 0.111 ms for Preferred64, 1.782 ms for Missing64, 0.105 ms for Crossing64,
0.218 ms for Churn64 and 0.136 ms for YRejected64. These timers include call and
selection overhead; they are not exclusive scan time. In Churn64, the four
all-fallback frames per block averaged 1.777 ms of grounding versus 0.114 ms on
preferred frames. Their weighted excess is about **0.104 ms per frame**, roughly
**4%** of that pass's 2.639 ms mean full frame. It does not establish the proposed
0.1 ms **and** 5% recurring opportunity, though its bursts could matter to a host
with an explicit tail budget. Natural crossings offer much less scan work.

Missing64 is a material counterexample to any universal retain-performance claim:
all three processes were substantially slower than preferred hits. A host with
persistent coverage gaps could justify revisiting indexing. Establish that actual
duty cycle and recurring budget first; do not assume all opt-in controllers miss
every frame or present these controls as measured BVH savings.

Registration of 64 existing surfaces took a median 5.2-5.7 microseconds and
allocated 3,584 bytes (56 per registration), excluding surface construction and
initial registry growth. At 500 controllers, median first-frame-after-reset
episodes were 2.078 ms for Preferred1, 4.266 ms for Preferred64 and 5.159 ms for
Stacked64. These use warmed runtime/pools and fresh controller ownership, not
process-cold startup. Reopening for frequent cold population must include those
episodes, as well as future index construction/removal and retained peaks.

**Allocation signal and limits.** No new warm-zero-allocation claim is made.
In the 500-controller Missing64 case, one plain process incurred 48 bytes per
fallback in addition to control-frame allocations; the other two plain processes
and attribution pass did not show that increment. Empty/100 also incurred the
48-byte increment in the third plain process, so this is not a universal
process-wide classification. A disposable direct public-API control, using the same core and
collections DLL hashes, independently measured 4,800,000 bytes per 100,000
fallback misses in each of three rounds, versus zero for preferred hits. The
concrete/interface enumerator path warrants focused isolation before adding a
spatial index; see
[TRB-Benchmark-007](../benchmark-signal-hardening-backlog.md#trb-benchmark-007---heightmap-fallback-allocation-depends-on-executed-codegen).
Both the allocating and nonallocating captures are retained in the numbers above.
Control frames also allocated during cell transfers; that cost is not assigned
to heightmap scanning. Churn adds a 56-byte registration object per mutation.
The six baseline frames with observed GC were retained. Complete retained-heap,
long-session, guided-host and physics measurements were not performed.

**Verification and scope.** Source-mode benchmark builds passed with zero
warnings/errors in Release and ReleaseLean. Existing focused heightmap tests
passed **58/58 Release** and **55/55 ReleaseLean**, using the same source-stack
properties and `--filter FullyQualifiedName~Heightmap`. Independent review found
and verified fixes to the benchmark's stripe oracle and occupancy proof before
the final captures. Runtime growth is zero; the retained addition is one benchmark
class, its existing-launcher route, helper inheritance and documentation.

The initial default package-mode benchmark build failed with 26 `CS0246` errors
because current adapter source uses `PhysicsQueryBudget`/scratch/status/work APIs
absent from the referenced Gravitas 2.0.1 package. Source-mode validation above
does not resolve that package boundary. No dependency pins or provider runtime
were edited. Full solution/OS/coverage gates and candidate equivalence remain
unclaimed; Phase 1 is not selected, and no envelope or BVH implementation exists.

### Phase 1 — Implement And Prove One Owner

**Disposition: not selected.** Phase 0 retained heightmap scanning, so no envelope
or index implementation followed. These implementation steps remain conditional
on the recorded reopening criteria.

- Implement only the selected heightmap candidate; keep exact sampling/winner
  logic and current public identity/lifecycle. Add focused behavioral and
  failure-boundary coverage beside existing tests.
- Run the discriminating stacked-floor/boundary and full-controller oracle,
  allocation/retained-memory gates and matched containing-frame measurements.
- Obtain independent review at the first working production slice, including
  ordering, mutation publication, memory, source growth and deletion opportunities.
  Resolve findings before expansion; record retain/revert if the gate fails.

### Phase 2 — Decide Dense Members Without Forcing Stores

The Phase 2 pilot compares unchanged runtime storage with an isolated dense
prototype through real guided controller frames. The bounded starting cases are
100/500 controllers in eight-member or whole-population groups, 16x/4x sparse
high-water history respectively, and non-tail leave/rejoin on every frame. Keep
the same benchmark executable and dependencies in both arms; replace only the
core DLL. Preserve slot-order reductions through a derived ordered member view,
and include its invalidation/rebuild/sort in mutation frames. A per-group identity
token must not retain the group storage through stale sessions.

Before measurement, the provisional pursuit threshold is a repeatable saving of
at least 0.1 ms and 5% per 500-controller complete frame in a justified workload,
without a confirmed ordinary/churn regression or increased warm allocation.
Account for retained arrays and session growth separately; a speed gain does not
accept a memory tradeoff. Ticket-slot retirement is a remaining equivalence gate:
sorting tickets by their index preserves baseline order only while their numeric
slot allocation histories match. A rejected pilot need not implement additional
order-slot machinery merely to demonstrate its further cost.

- Measure the actual movement-group callers and churn shapes. Only implement the
  dense member owner if the predicted benefit survives session/issuer/order
  requirements. Otherwise record the existing slot owner as retained.
- If implemented, prove the complete global and planar lifecycle, continuation,
  retained-borrow and reentrancy cases, then measure complete acquisition/movement/
  commit/teardown episodes with repair costs included. Review independently.
- Keep TicketStore and all retained owners out of scope unless a genuine new
  production owner and measured need establish a coherent separate slice.

#### Phase 2 Result — Retain SlotMap (2026-11-25)

**Decision:** retain `SwiftSlotMap<MovementGroupMember>`. The bounded dense pilot
has promising large-sparse timing signals, but does not justify adoption across
the demonstrated workloads. Backing-array element storage grows by 75%, sessions
grow from 48 to 64 allocated bytes, population/admission becomes more expensive
in large sparse cases, and some small sparse observations are adverse. No memory
tradeoff is accepted. Unrestricted slot-order equivalence would require further
ticket-retirement handling. Keep the prototype out of runtime source; do not
expand it into a registry, optional store or compatibility framework.

**Source and prototype.** The base commit, five provider revisions, Windows
host, SDK/runtime and source-stack mode are the same as the Phase 0 record.
Existing Phase 0 edits were preserved. All runtime changes were confined to an
isolated source copy of the core project. Its project references were resolved
to the original sibling source projects, preserving their existing additional
properties; packaging was disabled for that experiment. The benchmark executable
was compiled once against the unchanged core, then copied with identical
dependencies/configuration into both arms. Only `Trailblazer.dll` was replaced.

The reproducible prototype recipe changes four owning files:

- `MovementGroupState`: use `SwiftDenseTicketMap<MovementGroupMember>`; allocate
  one unique opaque `object Identity` per group. Keep a derived member-reference
  array, initially empty, growing by doubling from a minimum of eight before
  publishing an addition. Add through the real map, then put its ticket index in
  the member's `OrderIndex`. Clear the live ordered-view references and mark it
  dirty after each structural change. This view is not a second membership
  authority.
- `MovementGroupMember`: add `int OrderIndex`; retain the reference payload and
  all original fields and calculations.
- `MovementGroupSession`: add `SwiftTicket GroupTicket` and `object? GroupIssuer`.
  A `ClearMember` operation clears these and sets `GroupIndex=-1`; call it from
  session reset and coordinator removal. The issuer token has no reference back
  to the group/map, so idle old sessions cannot keep its storage alive.
- `MovementGroupCoordinatorState`: replace integer member lookup/removal with
  ticket lookup/removal, requiring nonnegative `GroupIndex` and reference equality
  with the current group's identity token. Capture the actual ticket, index and
  token on addition. After successful removal invalidate the view. Replace only
  the two member-loop sources with `GetOrderedMembers()`; leave their arithmetic,
  freshness, formation, membership dictionary and destination decisions unchanged.

The view rebuild copies the issuer's current values into its retained array and
sorts only the live prefix by `OrderIndex`, then returns that prefix as a
`ReadOnlySpan<MovementGroupMember>`. It is borrowed only inside the existing
callback-free reductions. The final sort call is:

```csharp
Array.Sort((Array)ordered, 0, members.Count, (System.Collections.IComparer)order);
```

Here `order` is one cached `Comparer<MovementGroupMember>.Create(...)` comparing
the two integer order fields. No per-member ticket join or copied coordinator
algorithm exists in the benchmark. The state is runtime-only; wire schemas and
public identities do not change.

An initial pilot used the generic range-sort overload with that comparer. It
allocated **64 bytes per view rebuild**: at 500 controllers Churn8 allocated
43,624 versus baseline 39,592 bytes per frame. Switching to the non-generic
range-sort overload eliminated the recurring increment in a discriminating
Churn8 smoke (39,592 bytes in every measured frame). Initial allocating-pilot
captures are diagnostic only; the results below use the revised prototype.

**Reproduction and complete observations.** Build the original benchmark and
isolated core with the same Release/net10.0 source properties as Phase 0; the
isolated core additionally uses `-p:GeneratePackageOnBuild=false`. Freeze both
hosts and verify all top-level file hashes before running:

```text
dotnet <baseline-host>/Trailblazer.Benchmarks.dll group-capture 5
dotnet <candidate-host>/Trailblazer.Benchmarks.dll group-capture 6
dotnet <candidate-host>/Trailblazer.Benchmarks.dll group-capture 7
dotnet <baseline-host>/Trailblazer.Benchmarks.dll group-capture 8
```

The [benchmark guide](../../../tests/Trailblazer.Benchmarks/README.md#movement-group-storage-frames)
and `NavigationMovementGroupFrameBenchmarks` preserve exact case inputs. The
production entry is `ApplyGuidedTrekRequest(..., groupId)`, followed by context
simulation, ordered controller simulation/commit and late simulation. Manual
input is never applied: it would stop steering and leave the groups. Zero local
group-force radius isolates storage from local grouping forces while retaining
the production coordinator's formation updates. Sparse history can also affect
formation offsets, so compare arms within a case, not case-to-case scan costs.

All four processes exited zero and each exported **14 cases / 42 records / 672
timed frames**. Total: **2,688 frames**, 1,344 per arm and 96 per population/case
per arm. Every 16-element timing/allocation/GC/hash array was complete. All frame
hashes agreed across all 12 records for each population/case, covering positions,
velocities, resolved destinations and committed cells. Untimed checks also prove
fresh group membership, actual occupancy and nonzero XZ movement per block.
The actual reduction passed the adversarial non-tail-removal control:
X=`[MaxValue,0,MaxValue,-MaxValue]`, remove index 1, expected center zero/count 3.
An early smoke incorrectly required positive X movement on every frame; Flow's
valid initial cardinal Z step exposed that fixture error. Final checks require
movement over the measured block and do not impose a route-direction shortcut.

Each case had replay preflight and at least three warmup blocks spanning at
least 750 ms. Each measured block follows four preparation frames. Case order
is forward/reverse/forward/reverse in the A/B/B/A series. Runs were serial at
Below Normal priority without affinity, power-plan, tiering or dependency
changes, and without competing builds/tests/profilers. They are direct captures,
not BenchmarkDotNet child iterations or exclusive CPU profiles.

| Frozen file | SHA-256 |
| --- | --- |
| Common Trailblazer.Benchmarks.dll | `B4FC465D88099BEB41F207D222B2F6F1908465D81B7657B8A6336C48A80C5D74` |
| Baseline Trailblazer.dll | `828071DAA6F65C3B10D25DCD8FD3CC0C3E3B059D2F635CB79CCB74308B3E657A` |
| Revised candidate Trailblazer.dll | `0CCA4A79DD51220872E3F3FE8A7763F77BEA61A93D4EF81F4C4F0E9CA8198101` |
| Common dependency manifest | `69364258056DB7028E9448A61BFB9A717B3B8A5C12FFFE077E50CAF43A44BE91` |
| Common runtime configuration | `C230A317A54DD960BCBEB5F347F52E18DC665A26F7EFDA2159FCED9A5AC7E097` |

Provider DLLs have the Phase 0 identities. Loaded benchmark/core paths were
printed by each process, and comparison of all frozen files found only the
intended core-DLL difference.

**Full-frame results.** Milliseconds per complete frame; each arm is the balanced
mean of its two process means. Positive changes mean the candidate was slower.
Every observation, including GC and large tails, is included.

| Controllers | Case | SlotMap ms | Dense ms | Change |
| ---: | --- | ---: | ---: | ---: |
| 100 | Ungrouped | 2.983 | 2.587 | -13.28% |
| 100 | Small8 | 1.490 | 1.371 | -8.00% |
| 100 | Large | 1.526 | 1.540 | +0.91% |
| 100 | Sparse8x16 | 1.322 | 1.603 | +21.31% |
| 100 | SparseLargex4 | 1.686 | 1.598 | -5.24% |
| 100 | Churn8 | 3.320 | 3.396 | +2.27% |
| 100 | ChurnLarge | 3.061 | 3.079 | +0.58% |
| 500 | Ungrouped | 7.826 | 7.264 | -7.18% |
| 500 | Small8 | 8.053 | 7.412 | -7.96% |
| 500 | Large | 15.839 | 14.612 | -7.74% |
| 500 | Sparse8x16 | 7.876 | 7.539 | -4.27% |
| 500 | SparseLargex4 | 20.652 | 15.265 | -26.08% |
| 500 | Churn8 | 19.198 | 17.063 | -11.12% |
| 500 | ChurnLarge | 15.599 | 14.991 | -3.90% |

The ungrouped control changes despite doing no member scanning; broadly shared
improvements cannot be attributed solely to dense iteration. Order/process
sensitivity is substantial: the 100-controller ChurnLarge process means are
1.729/4.394 ms baseline and 4.484/1.674 ms candidate. These observations establish
neither a universal speedup nor common-case equivalence. In 100 Sparse8x16 the
process means are 1.311/1.332 versus 1.409/1.798 ms: retain the adverse result.

500 SparseLargex4 remains a useful signal: process means are 18.011/23.292 versus
15.011/15.519 ms, with process medians 17.704/18.622 versus 14.973/14.557 ms.
Its population/admission interval, however, grows from median **44.654 to
132.354 ms**, and allocated bytes in that interval grow from 3,579,512 to
3,661,272. That interval includes padding admission/removal, fresh controller
initialization and requests; it excludes prior controller reset and map/world
publication. Maximum observed frame times across all cases were 57.970 ms
baseline and 56.322 ms candidate. Their source is not attributed.

**Storage and allocation.** Actual array element widths were read using
`Unsafe.SizeOf<T>` for the runtime array types, outside timing. The snapshot
includes SlotMap entries or dense values/slot metadata/dense-slot indices plus
the candidate ordered view. Representative 500-controller totals:

| Case | Baseline array-element bytes | Candidate array-element bytes |
| --- | ---: | ---: |
| Small8 / Churn8 | 8,064 | 14,112 |
| Large / ChurnLarge | 8,192 | 14,336 |
| Sparse8x16 | 128,000 | 224,000 |
| SparseLargex4 | 32,768 | 57,344 |

This is **75% more array-element storage**, not a full retained-heap percentage.
Array headers, group/map scalars, identity tokens, payload and session objects,
and transient growth peaks are not included. Session allocation measured once
before warmup rises from **48 to 64 bytes**, including ungrouped controllers.
Its probe objects remain rooted for the process; they are not repeatedly
allocated/dropped between timed iterations.

Regular warm allocation matches at 28,000 bytes per 500-controller grouped
frame, 39,592 for Churn8 and 28,184 for ChurnLarge. Ungrouped frames allocate
zero. One candidate ChurnLarge frame recorded 31,344 bytes, an extra 3,160,
coinciding with collection mask 7; the cause is unknown and the frame is retained.
Three baseline and two candidate frames observed GC. No complete allocation
equivalence claim is made. The existing 56-byte per-owner membership-record
allocation is recorded separately as
[TRB-Benchmark-008](../benchmark-signal-hardening-backlog.md#trb-benchmark-008---movement-group-updates-allocate-membership-records).

**Verification and remaining boundary.** Both runtime prototype targets
(`netstandard2.1`/`net10.0`) build in Release and ReleaseLean, and benchmark
builds pass in both profiles. Independent source review verified the real caller,
issuer/view ownership, ordered reductions and measurement boundary; it caught
and removed repeated session-probe allocation between captures. Independent
evidence review verified counts, hashes, storage figures and balanced means.
Focused existing movement-group, NavSteering and Navigator2D tests passed
**545/545 Release and 519/519 ReleaseLean in each arm**, using
`FullyQualifiedName~MovementGroup|FullyQualifiedName~NavSteering|FullyQualifiedName~Navigator2D`.
Candidate test hosts replaced only the core DLL; the Release candidate hash
matched the measured DLL. The Phase 0 package-mode limitation remains unresolved.

After capturing, benchmark `GlobalSetup` also gained a guard to initialize the
session-size scalar for standalone BenchmarkDotNet invocation. The direct
capture route already initialized it before setup, so that guard is inactive
there; timed frame methods and runtime DLLs are unchanged. The frozen identities
above describe the actually measured host, not a claim of timings from a rebuild
after this metadata initialization change.

Sorting by ticket index is equivalent to baseline slot order only while their
allocation histories match; terminal generation retirement can diverge from
SlotMap's indefinite integer reuse. Supporting that lifetime would need an
independent legacy-order allocation history or an explicit contract change,
with further metadata/verification costs. Neither is implemented or accepted.
Full restore/reentrancy/lifetime/OS/coverage acceptance and planar performance
were not established. The rejected prototype leaves **zero runtime source
growth** and no provider/dependency changes.

Reopen only for an intended large sparse workload with a defined memory and
population/churn budget, reproducible gains beyond the unchanged-control signal,
and the original order, issuer, serialization and dimensional requirements.
Retaining existing storage is the Phase 2 result, not a claim that dense storage
can never help.

### Phase 3 — Verify And Close

**Disposition: closed for the retain outcome on 2026-11-25.** The source audit,
heightmap baseline and dense-member pilot establish the two retain decisions.
Their measured inputs, commands, source/binary identities, focused checks,
independent reviews and limitations remain in this archived plan. The maintained
benchmark scenarios are retained for reproduction and future comparison; the
rejected dense prototype was never adopted into runtime source.

Closeout updates the feature overview, archives this plan and repairs its
incoming/outgoing links. Runtime, serialization, public APIs, project references,
dependency pins and provider source are unchanged by this investigation. This
documentation closeout does not rerun the recorded benchmark or runtime checks.

The full runtime-adoption matrix below was **not executed**. The focused builds,
tests and measured cases reported in Phases 0 and 2 are the completed evidence;
archival does not promote them to full-suite, coverage, OS or package-readiness
validation. The recorded Gravitas package-mode limitation remains unresolved.
No unrun gate is marked passed and no future runtime requirement is waived.

`TRB-Benchmark-007` and `TRB-Benchmark-008` remain open in the owning
[benchmark backlog](../benchmark-signal-hardening-backlog.md). They are separate
pre-existing allocation follow-ups, not unfinished adoption work or claimed fixes.
Reopen adoption only under the workload and budget conditions recorded above.

#### Gates For Any Reopened Runtime Adoption

These original implementation gates apply if reopened work selects a runtime
change; they are retained as requirements, not outstanding tasks for this closed
retain decision.

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

The adoption investigation is complete with explicit retained decisions for all
audited owners. No further adoption experiment is assigned. Release/distribution
readiness and the two allocation follow-ups remain separate work. Preserve this
tracked evidence and its reopening criteria; archival makes no commit, package
publication or release claim.
