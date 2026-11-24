# Bounded Physics Queries in 2D and 3D

Completed in coordinated source on 2026-11-17. All four phase checklists are
complete. The [published dependency recheck](#2026-11-24---released-package-dependency-chain-recheck)
records the separate public-package prerequisites.

Phase 0 defines the shared contract and released-package baselines. Phase 1
proves the first bounded support paths in Gravitas. Phase 2 extends those
paths to native shapes and grid topologies, adds bounded posture clearance,
and verifies exact results and work across equivalent ready worlds.
The work is planned before the next Trailblazer release. The
[first-class 2D navigation plan](twoDimensionalNavigationPlan.md)
has its own Phase 3 package verification and already exercises synchronous
support and posture in real physics. This plan owns the opt-in work and storage
limits for those operations in both dimensions.

## End-user goal

Give hosts predictable physics-query work in fixed steps, especially for large
crowds or scenes with dense geometry. Gravitas owns the bounded operations.
Trailblazer keeps synchronous sampling as the default and lets hosts opt in
where they can size and validate the allowance for their workload.

- Fixed caller-owned scratch sets a storage ceiling. Count discovery, candidate
  visits, and geometry separately so a small result or buffer cannot conceal
  expensive traversal.
- Early exit is useful once the result is conclusive. Bounded posture visits
  blockers in stable registration order and can stop at the first proven
  penetration. Clear posture must exhaust the eligible blocker set, and a
  support hit must win under the existing selection rules.
- The Phase 1 prototype replays with identical prepared storage. For lockstep
  adoption, logically equivalent ready worlds must reproduce the same complete
  result or explicit exhaustion outcome after restore, with the same request,
  budget, scratch capacity, and logical collider order. Measure full
  fixed steps as well, since per-query limits give the host control over only
  one part of the frame.

Repeated exhaustion can visibly drop grounding or carrier motion, or delay a
posture change. Treat it as an exceptional, observable outcome. Judge the
opt-in policy by the frequency and consecutive duration of unavailable samples
in representative workloads as well as by frame cost.

## Starting baseline

- In side-view 2D, `GravitasNavigator2D` calls `Query2D.QuerySupport` on the
  accepted pose after the physics resolve. Gravitas checks the source and
  partition readiness, finds the nearest eligible support, and reports
  `Found`, `NoSupport`, `WorldNotReady`, or `Unrepresentable`. The adapter
  already distinguishes `NoSupport` from `Unavailable` and checks the sampled
  frame and carrier lifetime. Top-down 2D skips the support query.
- In 3D, `GravitasContactTranslator` calls `Query3D.SweepSphere` and checks
  the closest hit's normal and witness afterward. `GravitasNavigator3D`
  currently treats an empty contact as airborne. Preserve that synchronous
  selection contract and decide how bounded 3D calls represent incomplete
  support before wiring retained contact or carrier state to the new result.
- Both body reconfiguration methods validate a detached posture candidate
  against registered colliders before publishing. In 2D this explicitly covers
  unpartitioned blockers that the grid-backed support index omits. Preserve the
  complete blocker set and the accepted transaction's publication semantics.
- Support discovery can refresh partitions, grow candidate storage, and visit
  scene-dependent addresses and memberships. Posture clearance visits the
  registered collider set. The [Phase 0 package baselines](#2026-11-14---phase-0-package-baselines)
  measure those existing synchronous operations and their containing steps.

## Ownership

Gravitas owns physics-query accounting, readiness, native geometry and results.
GridForge owns any new bounded world discovery required by an implemented query.
SwiftCollections owns any missing reusable spatial primitive, and FixedMathSharp
remains the owner of deterministic geometry shared across libraries. The optional
Trailblazer adapter owns the host policy for consuming results. Reuse existing
accounting where contracts match and keep geometry in its dimensional owner.

The current library targets are `netstandard2.1` (C# 11) and `net10.0` (C# 14).

## Agreed scope

- Cover pure 2D and pure 3D support queries and whole-body posture-clearance
  checks. The latter bounds clearance, not arbitrary host callbacks or the
  complete reconfiguration transaction.
- Use explicit bounded-query policy. Existing synchronous callers retain their
  behavior; no hidden finite defaults, automatic fallback to unbudgeted work,
  truncated success, or duplicate geometry implementations.
- Reuse support/clearance semantics established by the owning implementations.
  A generic closest hit and the closest eligible support are different queries;
  changing one into the other is not a budget optimization.
- Include shared GridForge/SwiftCollections work only where the approved query
  contract requires it. Prefer existing index/storage facilities.
- Measure complete containing frames, readiness, allocation and failure rate,
  not just the loop whose counter was reduced.

Not included: a new index, scheduler, continuation framework, general rewrite
of every ray/overlap/all-hit API, mixed-dimensional query budgeting, a physics
solver rewrite, engine packages, or a new sample/benchmark/CI framework. Keep
mixed-mode behavior explicit and unchanged; reject unsupported bounded modes
rather than silently skipping blockers. Wider query coverage needs a scope review.

## Global constraints

- Correctness, lockstep determinism, maintainability, then performance. Budget
  decisions use integer work units and fixed simulation state, never elapsed
  time, hardware speed, wall-clock deadlines or nondeterministic iteration.
- Keep geometry native: pure 2D uses X/Z and physics partition storage Y = 0;
  3D uses its actual volume. Do not reuse a planar proof for a spatial query.
- Preserve existing synchronous results, stable tie ordering, collision filters,
  witnesses, registration lifetimes and transaction/callback semantics.
- Do not weaken existing navigation, scheduling or GridForge coverage limits.
- Keep core libraries engine-agnostic and independent of each other's consumers;
  Trailblazer core must not acquire a Gravitas dependency.
- Build both library targets; verify Release and ReleaseLean without mixing
  package families. Preserve exact reachable coverage for introduced paths.
- Use coordinated source validation for unreleased dependencies. Keep public
  package availability separate, as recorded in the
  [released-chain recheck](#2026-11-24---released-package-dependency-chain-recheck).
- No staging, commits, upstream edits or releases without the applicable user
  authorization. This plan is not execution or publication approval.

## Contract requirements

### Work, storage and completion

Account for grid/index discovery, address lookup, memberships including rejected
and duplicate candidates, compound parts and geometry features. Every variable
loop needs a charged operation or a documented conservative bound derived from
charged input. Sorting, deduplication, setup, cleanup and failure paths count too.
Zero allowance means no work in that category. Charge before entering work;
checked counter/precharge overflow must fail, never wrap or become unlimited.

Preallocated query storage must not grow during a bounded call. Do not hide
candidate-list materialization, array rental, query-stamp rollover scans or a
whole-world refresh before applying the allowance. Distinguish storage
exhaustion from work exhaustion and from stale/unready/unrepresentable input.

Only a complete query can certify a hit, no support or clear posture. Exhaustion
clears any usable result: a partial nearest hit is not authoritative and an
incomplete clearance check cannot authorize growth. Report consumed work on
failure as well as success, never above the supplied ceilings.

Counted work is **not a millisecond guarantee**. Explicitly document any derived
kernel bound, context maintenance, transaction publication and host-callback
work outside the query. Do not move costs into an unmeasured lifecycle phase and
claim they disappeared.

### Discovery and lifecycle

`GridWorld.TryBeginCoveredAddresses` can bind supplied grid generations for
covered-address enumeration. Inspect the current `GridSpatialIndex` paths over
the fixed spatial hash and the BVH, including its active-grid scan fallback.
An output limit alone leaves internal hash cells, memberships, BVH nodes and
deduplication unbounded. Choose the smallest traversal that can account for
inspections and stop within caller limits. Reuse one discovery boundary where
both support paths share the same requirements.

Posture clearance uses the registered-collider scan in live registration
order, with every visit charged. Consider indexed discovery for
posture only if its complete candidate set includes unpartitioned blockers and
matched measurements justify the change. Account for creation and preparation
of the detached candidate when placing the bounded-call boundary.

Preserve the existing world lifetime and change-sequence checks from discovery
through binding and completion. Individually current generations cannot prove a
newly inserted grid was included. Preserve checked discovery expansion, the
relevant dimension's partition padding and separate physical-presence checks.
Validate sparse changes, grid replacement, world reset and reused registrations.

Gravitas must sample a current index at its documented host barrier. Today
`RefreshQueryPartitions` can rebuild both partitions after a topology batch and
drain deferred 2D updates. Bounded support checks readiness at the between-step
boundary and leaves reconciliation to the existing host step. Measure both
costs and verify recovery through that step.

### Geometry and consumer behavior

The reviewed 2D circle/convex overlap considers multiple axes and rescans
vertices; a sweep can repeat overlap work. A linear vertex charge is insufficient
for that path. Derive checked conservative charges or meter the existing kernel
without changing geometry. Audit 3D convex, compound and supported mesh paths
independently; a 2D coefficient is not a 3D work model.

With sufficient allowances, bounded results must match the corresponding
complete query's admission, hit/witness and ordering contract. Carry existing
initial-overlap, tiny-positive-distance and scalar-boundary semantics through
each operation; do not make generic sweeps adopt support-specific behavior.

On incomplete support in either adapter, commit the accepted pose and mark the
sample unavailable for that frame. Release retained support and carrier evidence,
skip support-dependent advancement, and continue ordinary airborne gravity and
frame processing. That is a conservative motor decision while the public sample
status remains unavailable. Preserve input and jump state under the existing
airborne rules. Sample again on the next committed frame only. No new serialized
sample or scratch state is needed.

Incomplete posture leaves body, collider, host pose and navigation state
unchanged and skips the synchronized-state publisher. Complete transactions
retain the existing post-commit notification-failure rules.

## Phase 0 API contract

The declarations below are the implementation draft. The result semantics and
deterministic ceilings are acceptance criteria. Review the API and scratch
layout after the Phase 1 production slice, before expanding or publishing them.

In `Gravitas.Queries`, use an immutable `PhysicsQueryBudget` with `ulong`
properties `DiscoveryLimit`, `CandidateLimit`, and `GeometryLimit`, and an
immutable `PhysicsQueryWork` with `ulong` properties `Discovery`, `Candidates`,
and `Geometry`. Each has a constructor taking its three fields in that order.
The same units apply to support and posture.

| Counter | One unit accounts for |
| --- | --- |
| `Discovery` | An index/binding inspection, including grid records, hash cells and inspected entries, BVH nodes, generation/address probes, and physical voxel lookups. Charge failed inspections and internal lookup probes or precharge their proven bound. |
| `Candidates` | A collider membership or registered collider visit, including duplicates and rejected candidates. Include inspections of retained candidate identities and variable ordering/cleanup work in this counter. |
| `Geometry` | A primitive kernel step, compound part visit, convex feature or support-map inspection, or mesh node/feature inspection. Charge repeated passes and iterations or precharge a checked conservative bound for the existing kernel. |

Each call starts with zero work. Charge before inspection, using remaining-budget
subtraction. For derived precharges, checked overflow reports
`WorkBudgetExceeded` without entering that work. Report charges already taken on
every outcome and never exceed a limit. The charged count includes conservative
precharges and so can exceed the actual number of inspections. Fixed scalar
admission and assigning outputs require no work units. `default` grants zero
units. If no inspection is needed, a call may complete with zero work.

Use one `PhysicsQueryExecutionStatus` with `WorldNotReady = 0`, `Completed`,
`WorkBudgetExceeded`, `ScratchCapacityExceeded`, `Stale`, `Unrepresentable`,
`UnsupportedRuntimeMode`, `UnsupportedTopology`, and `UnsupportedGeometry`.
`WorldNotReady` means a ready world or partition view could not be sampled.
`Stale` means an acquired world view could no longer be certified.
Programmer errors such as null scratch, invalid geometry arguments, or an
already borrowed scratch throw before query work. Validate the mode and initial
readiness before traversing. During traversal, charge each inspection before
attempting it and check room before writing. If an inspection fits but its
result cannot be retained, return the storage failure with that inspection
charged. Check freshness before publishing a completed result.

Use a caller-owned `sealed class PhysicsQueryScratch` with constructor
`PhysicsQueryScratch(int traversalCapacity, int candidateCapacity)` and
read-only `TraversalCapacity` and `CandidateCapacity`. Capacities are
nonnegative entry counts for retained voxel references and unique collider
identities. Grid slots are streamed and charged to discovery. The scratch owns
fixed backing storage allocated before use. The 3D mesh overload of the
scratch constructor adds `meshTriangleCapacity`, with a matching read-only
`MeshTriangleCapacity` property. The BVH stack and candidate triangle buffer
reuse that capacity across sweep and witness passes. Hash probing, if used, must have a
checked inspection limit. Reserve cleanup work when retaining each entry and
clear only used entries. Use a single exclusive borrow per synchronous
call and release on all exits. Reuse sequentially across queries and worlds
after cleanup. Scratch has no durable world ownership or serialized identity.
Posture can use zero traversal and mesh capacity while it scans registered
colliders. Scratch retains one detached shape per supported posture family.
Bounded calls report through their return values and counters, with no per-query
diagnostic callback inside the bounded portion.

Keep existing synchronous entry points. Add these overloads to their owners.
All signatures shown are planned API declarations.

```csharp
// GravitasQuery2DService
PhysicsQueryExecutionStatus QuerySupport(
    in Physics2DSupportQuery request, in PhysicsQueryBudget budget,
    PhysicsQueryScratch scratch, out Physics2DSupportHit? support,
    out PhysicsQueryWork work);

// GravitasQuery3DService. Reuse the directional overload's endpoint calculation.
PhysicsQueryExecutionStatus SweepSphere(
    Vector3d origin, Fixed64 radius, Vector3d direction, Fixed64 maxDistance,
    PhysicsLayerMask layerMask, LSCollider? excludedCollider,
    in PhysicsQueryBudget budget, PhysicsQueryScratch scratch,
    out Physics3DHit? hit, out PhysicsQueryWork work);

// SolidBody2D and SolidBody. For SolidBody use ColliderShapeDefinition,
// Vector3d and LSCollider in place of their 2D counterparts.
PhysicsQueryExecutionStatus TryReconfigureCollider(
    ColliderShapeDefinition2D definition, Vector2d localOffset, Vector2d position,
    in PhysicsQueryBudget budget, PhysicsQueryScratch scratch,
    out ColliderReconfigurationStatus? result, out LSCollider2D? blocker,
    out Exception? notificationException, out PhysicsQueryWork work,
    Action? publishSynchronizedState = null);
```

For support, `Completed` with a null hit means an exhaustive miss. For posture,
`Completed` carries `Blocked`, `Unchanged`, or `Applied`. A proven first blocker
in registration order is a complete negative clearance proof and may terminate
early. All incomplete outcomes clear hit, posture result, blocker and
notification exception. Keep positive-penetration and exact-touch semantics.
The bounded 3D sweep reports `Unrepresentable` for an unrepresentable endpoint
or padded bounds. The legacy directional overload retains its existing failure
behavior. Reuse `Physics2DSupportHit`'s sampled frame and lifetime check, and
consume a 3D hit at the same between-step boundary. A sample is not a retained
registration proof across frames.
On each optional adapter, `ConfigureSupportQueryBudget(PhysicsQueryBudget budget,
PhysicsQueryScratch scratch)` opts into one bounded probe per applicable commit.
`ClearSupportQueryBudget()` restores synchronous sampling and releases the
adapter's scratch reference. Both are between-motion changes. Explicit planar
`RefreshSupport()` uses the same policy. Expose nullable
`LastSupportQueryExecutionStatus` and `LastSupportQueryWork` on the adapters so
hosts can distinguish no bounded probe this frame from a completed miss or an
incomplete sample. Reset these diagnostics on reset, load, configuration change,
and frames without a bounded probe. In 3D, `SupportStatus` exposes the sample
separately from grounded state. Both dimensions use the core
`NavigationSupportStatus` enum. Use `NotApplicable`
before any 3D sample. With bounded 3D sampling, a completed sweep that misses or
fails the adapter's normal test gives `NoSupport`. An incomplete sweep or
unrepresentable witness gives `Unavailable`.

Expose `TryReplaceNavigationProfile(replacement, in PhysicsQueryBudget budget,
PhysicsQueryScratch scratch, out PhysicsQueryExecutionStatus? clearanceStatus,
out PhysicsQueryWork work)` on the optional adapters, returning the existing
`NavigationProfileReplacementStatus` with `PhysicalFitUnavailable` appended.
`clearanceStatus` is null when navigation rejects before calling Gravitas.
Keep the existing shared profile preflight and publication paths. One caller
can share scratch across actors that prepare and commit sequentially.

For bounded posture, detached circle or capsule preparation and clearance are
inside the query boundary, including a geometry precharge for candidate setup.
Reuse detached preparation with fixed scratch so even rejected calls allocate
zero bytes after setup.
Keep authoritative publication, collision cache cleanup, mass updates after
clearance, and callbacks in the surrounding transaction. Do not hold the scratch
borrow across those callbacks. There is no caller or mutation boundary between
successful clearance and the beginning of authoritative publication. Record
those costs separately in containing-frame measurements. Broader authored shape
changes stay with existing operations.

### Discovery boundary and first production slice

The existing covered-address cursor accepts sorted eligible generations and
already meters generation and address probes. Its normal entry points can wait
for publication. Bounded support needs a non-waiting path that captures the
world lifetime and published sequence, gathers eligible generations under that
same view, then binds and visits physically present addresses. Return unavailable
for an unpublished prefix or busy view, and stale when an acquired view changes.
Use the existing cursor core or address math and require an exact current
collider registration before accepting a hit. No query-owned partition refresh,
unbounded wait, or reuse of the synchronous collider or spatial-hash stamps.

Start with the real side-view box-floor `QuerySupport` caller on one rectangular
grid, through bounded discovery and one checked candidate and support result.
Use the 3D sphere sweep through that same discovery seam as the first spatial
control, before generalizing shapes or adopting posture. GridForge needs a
bounded, non-waiting discovery/bind entry point for the indexed production path.
The current SwiftCollections hash and BVH APIs can emit capped outputs only at
the consumer and cannot stop or account for internal visits. If retained for
that path, add the minimal bounded visits needed for hash cells/memberships and
BVH nodes. Reuse existing storage and key lookups where their probe cost can be
bounded. Gravitas and GridForge should land their first exercised seams together
in coordinated source before pinning the new commits. No FixedMathSharp change
has been identified.

For the first box-floor slice, enumerate only fixed-shape kernels whose maximum
steps can be derived from the current code. Record the checked formula beside
each production kernel before admitting it. Apply the same requirement to
compound, convex and mesh paths as they are added. A collider-count precharge
alone says nothing about the geometry work inside each visit.

### Acceptance before expanding

Require exact equality with the existing support operation for complete
results, including eligibility, winning identity, tie order, fixed-point
witness and distance. Match posture decisions and publication counts. For
multiple blockers, verify the bounded result against registration order and
replay it after deletion and slot reuse. With the reported work
and used capacity, rerun a successful representative case at exact limits and
one below each nonzero unit/capacity in isolation. Check late exhaustion, zero,
checked overflow, repeats, scratch reuse, and the first ready sample after grid
or callback mutation. Compare collider touches in both dimensions, including an
unpartitioned posture blocker. Require zero steady allocation in bounded
support and the bounded clearance portion, and retain existing synchronous
allocation counts. Run paired complete-step timing for the matched controls
with ordinary and mutation frames. Repeat noisy rows before drawing a regression
conclusion and get user agreement for any confirmed throughput tradeoff. No
speedup target is needed to prove the work ceiling.

## Source and test ownership

Paths are relative to each repository. Keep new public and internal types in
separate files and dimensional code under its existing owner.

| Owner | Existing source / test entry points |
| --- | --- |
| SwiftCollections | `src/SwiftCollections/Query/SpatialHash/SwiftSpatialHash.cs`, `src/SwiftCollections/Query/BoundingVolume/SwiftBVH.cs` and the fixed-point spatial hash integration |
| GridForge | `src/GridForge/Grids/Managers/GridSpatialIndex.cs`, `src/GridForge/Grids/Managers/GridWorld.CoveredAddresses.cs` and the world/index/covered-address tests |
| Gravitas readiness | `src/Gravitas/Runtime/GravitasWorldContext.QueryPartitions.cs` and the partition reconciliation tests |
| Gravitas 2D | `src/Gravitas/Queries/2D/GravitasQuery2DService.SupportQuery.cs`, `src/Gravitas/Queries/2D/Detection/QueryDetection2D.SupportQuery.cs`, `src/Gravitas/Core/2D/Bodies/SolidBody2D.ColliderReconfiguration.cs` and `tests/Gravitas.Tests/Physics2D` |
| Gravitas 3D | `src/Gravitas/Queries/3D/GravitasQuery3DService.Raycast.cs` and its `Raycast.Traversal` partial, `src/Gravitas/Core/3D/Bodies/SolidBody.ColliderReconfiguration.cs` and the 3D sweep and posture tests |
| Trailblazer adapter | `src/Trailblazer.Gravitas/Planar/GravitasNavigator2D.cs`, `src/Trailblazer.Gravitas/GravitasContactTranslator.cs`, `src/Trailblazer.Gravitas/GravitasNavigator3D.cs` and the support, posture and replay fixtures in `tests/Trailblazer.Gravitas.Tests` |

## Review focus

- Exhaustion after discovering a plausible hit must not produce false support
  or authorize posture growth (Phases 1–3).
- New grids, sparse cells and recycled collider IDs must not escape the proof's
  world/lifetime boundary (Phases 1–2).
- Tiny metrics, scalar faces and dense duplicate memberships must not bypass
  work/storage accounting or omit valid candidates (Phases 1–2).
- Compound, convex and mesh loops must not hide work behind a single candidate
  charge or change support ordering (Phase 2).
- Callback mutations and sustained budget pressure must not permanently stall
  sampling or weaken prepare/resolve/commit ownership (Phases 2–3).

## Phase 0 - Contract And Matched Baselines

- [x] Trace both production support and posture paths, partition reconciliation,
  scratch lifetime and geometry costs against the current stack. Identify which
  work can be bounded by existing APIs and which needs a new upstream entry point.
  Record source, hosted pin and package readiness separately for each dependency.
- [x] Record exact common budget/work/status/scratch signatures and units,
  dimensional request/result types, readiness ownership, failure policy and the
  intended production callers. Decide how 3D distinguishes incomplete support
  and how both posture APIs expose exhaustion while retaining atomicity.
- [x] Capture matched support and posture controls using existing fixtures:
  open/floor/wall, crowded cells, compound/convex geometry, sparse and overlapping
  grids, mutation and query-stamp rollover. Include 3D mesh cases where supported.
  Retain inputs, source/binary identities, allocations and full-frame timings.
  [Package baseline record](#2026-11-14---phase-0-package-baselines)
- [x] Define semantic equivalence checks, exact/one-below accounting cases and
  measured overhead acceptance before implementation. No arbitrary speedup
  target: bounded worst-case work may trade throughput for predictability.
- [x] Review the contract and choose a small production slice that reaches the
  shared boundary through the normal caller. Record the dependency changes it
  actually needs and use the matched controls before expanding scope.

## Phase 1 - Prove Support End To End In 2D And 3D

- [x] Start with a failing box-floor support contract through the real Gravitas
  2D `QuerySupport` entry point on a rectangular grid. Prototype bounded,
  non-waiting GridForge discovery and reuse the existing candidate and geometry
  rules. Charge the grid-slot and address visits that this slice needs.
- [x] Exercise the same discovery and accounting boundary through the 3D sphere
  sweep on the corresponding box-floor scene. Match each dimension's synchronous
  hit or miss, exact witness, and winner, including a tie or duplicate candidate.
- [x] Prove exact and one-below work and scratch limits, including exhaustion
  after a plausible hit, safe reuse after failure, deterministic replay, and
  readiness recovery through a complete host step. Measure warm allocation and
  full-step timings in both dimensions against the Phase 0 controls.
- [x] Obtain independent review of both entry points and the shared boundary.
  Record runtime cost, production dependencies, and any API or scratch changes
  justified by the results. Use this checkpoint to decide how much of the
  planned surface earns expansion and publication.

## Phase 2 - Expand Native Support, Then Add Posture Clearance

- [x] Make the existing box-floor slice replayable across checkpoint rebuilds
  before expanding shape support. Prove partition lookup, linked-collider
  filtering, and candidate deduplication under the same equivalence contract.
  Require the same charged work and terminal status at exact and one-below
  limits. Preserve the existing tie contract for matching collider order.
  - [x] Visit live GridForge grids in existing normalized configuration order.
    Cover vacant slots, remapped slots, and reset followed by slot reuse.
  - [x] Visit live Gravitas memberships in collider ID order within each
    mobility class. Keep deduplication charges independent of removed slots.
  - [x] Make GridForge partition lookup charge the same work when the requested
    partition was retained empty or was never attached. Exclude caches owned
    by the inactive dimension or mixed solver from native discovery charges.
  - [x] Make Gravitas linked-collider filtering charge from live pairs
    independently of retained lookup capacity.
- [x] Repeat the many-grid and dense-member controls, including full steps
  with publication and mutation. Measure where any prepared view shifts work or
  retains extra storage. Review the proof through both native query callers
  before taking on new shapes.
- [x] Extend verified support in both dimensions. Cover filters, initial contact,
  narrow scalar margins, compound/convex/mesh inputs where supported, sparse
  presence, both hex topologies, seams, and dense duplicate memberships. Bound
  the hash, BVH, and scan paths exercised by the supported cases, including
  generation changes, stamp rollover controls, and checked accounting overflow.
- [x] Reuse the existing narrowphase and stable reductions. Prove ready, stale,
  and exhausted behavior under recurrent callback mutations, resets, and
  registration reuse. Compare complete results to the synchronous operations.
- [x] Once native support passes in both dimensions, add bounded posture. Test
  exact touch, a conclusive early blocker including an unpartitioned blocker,
  full clearance, and exhaustion before clearance completes. Verify fixed
  scratch and transactional rejection before adopting posture in the adapters.

## Phase 3 - Adapter Adoption And Closeout

- [x] Integrate the reviewed explicit policy in 2D and 3D adapters, preserving
  existing caller behavior. Test repeated incomplete support, recovery, moving
  carriers, gravity and all-prepare/one-resolve/all-commit ownership. Report
  actor count and query counts per frame alongside per-call budgets.
- [x] Run representative crowded scenes at a useful allowance and under
  sustained pressure. Record support exhaustion rate, longest consecutive
  unavailable stretch per actor, and grounding or carrier losses alongside
  complete-step times. Review whether the opt-in policy is usable on those
  workloads before finalizing the adapter surface.
- [x] Exercise bounded posture clearance at exact/one-below limits. Rejection
  leaves all physical/navigation state and publication counts unchanged;
  acceptance and notification failures retain atomic transaction semantics.
- [x] Repeat matched pre-change controls, allocation/replay tests and complete
  containing-frame measurements, including maintenance and readiness latency.
  Report overhead and incomplete-query rates as well as any saved work.
  - [x] Capture single-actor and crowded support, pressure/recovery, moving-floor,
    and topology readiness controls with exact replay.
  - [x] Add bounded posture controls and repeat affected final checks.
- [x] Run owning and downstream Windows/Linux Release/ReleaseLean suites, both
  library targets, exact introduced-path coverage and DocFX. Verify source-stack
  binary provenance; keep final released-package verification separate.
- [x] Document current public behavior in XML and DocFX guides with verified
  examples, without feature-plan links in evergreen pages. Obtain independent
  review, resolve findings, record evidence here and archive only after both
  dimensions meet the contract and consumer gates.

## Verification workflow

Run each changed upstream repo's documented focused tests first, then its full
matrix in dependency order. Serialize builds sharing sibling output paths.
These existing downstream commands are a starting point, not evidence of a run:

```powershell
# Run from F:\gamedevrepos\Trailblazer.
dotnet test ../Gravitas/tests/Gravitas.Tests/Gravitas.Tests.csproj -c Release -p:UseLocalLsfStack=true
dotnet test ../Gravitas/tests/Gravitas.Tests/Gravitas.Tests.csproj -c ReleaseLean -p:UseLocalLsfStack=true
dotnet test Trailblazer.slnx -c Release -p:UseLocalLsfStack=true
dotnet test Trailblazer.slnx -c ReleaseLean -p:UseLocalLsfStack=true
```

Record exact coverage denominators, test counts, runtime/OS, benchmark parameters
and complete/failing captures. Unit tests of counters do not prove frame-time
bounds; source builds do not prove package readiness.

## Release sequencing

1. Start query-budget baselines and contract work with released GridForge
   `10.1.0` and Gravitas `2.0.1`, the native 2D Phase 3 package versions.
2. Publish the SwiftCollections fixed BVH API and sorted-list support first,
   then update and publish GridForge and Gravitas in dependency order.
   Update hosted source pins after the corresponding upstream changes land.
3. Before publishing Trailblazer, validate the combined native 2D and budgeted
   adapter behavior against the final released Standard and Lean chains.
   Review versioning when the budget API is settled.

## Execution record

### 2026-11-24 - Released-package dependency-chain recheck

The SwiftCollections v9 dependency migration rechecked the existing pending
released-chain requirement. This is a package-availability result, not a
reopening of the completed source implementation or evidence of a new
SwiftCollections runtime defect.

The consumer revisions were GridForge
`4b2bc64e503a1bd3c8cdf7e81b4e565cfa9651e8`, Gravitas
`af7a9eb1468e6fc2dc6c2714871a6e6649a92b0b`, and Trailblazer
`3ac67b18a8351a55b178cdc600dcba0ee250f0f4`. The dependency edit updates
SwiftCollections and its FixedMathSharp companion from `8.0.0` to `9.0.0` in
both package families; SwiftCollections develop and tag `v9.0.0` resolve to
`7c2fa696f10d88194f5a895aaafd7debd8e27574` at this check. It changes no
consumer runtime source or serialized schema.

Run these commands from the corresponding repository root, using the indicated
consumer revision for the unchanged control and the dependency migration for
the comparison:

```bash
dotnet build src/Gravitas/Gravitas.csproj -c Release -f net10.0 -p:UseLocalLsfStack=false -p:DisableTransitiveProjectReferences=false -p:GeneratePackageOnBuild=false -m:1
dotnet build src/Trailblazer.Gravitas/Trailblazer.Gravitas.csproj -c Release -f net10.0 -p:UseLocalLsfStack=false -p:DisableTransitiveProjectReferences=false -p:GeneratePackageOnBuild=false -m:1
```

**Expected:** Released dependencies supply the bounded voxel-discovery and
physics-query contracts required by these already implemented consumers.
**Observed:**

- Gravitas fails with CS0246 for `GridBoundedVoxelStatus` against its declared
  GridForge `10.0.0`. A separate check against `10.1.0` confirms that it also
  lacks the contract introduced by GridForge commit
  `72740cc7420d59cf948bfb76d8a69e74b5066f0f`.
- The Trailblazer Gravitas adapter fails against Gravitas `2.0.1`; its missing
  contracts are `PhysicsQueryScratch`, `PhysicsQueryBudget`,
  `PhysicsQueryWork`, and `PhysicsQueryExecutionStatus`. The focused command
  above reports 26 CS0246 errors.
- Both failures also occur in unaltered archives of the original consumer
  revisions with their SwiftCollections `8.0.0` references. They are confirmed
  pre-existing released-provider gaps, not failures introduced by the
  SwiftCollections v9 dependency edit. Local-source validation cannot turn
  these package builds into passing released-chain results.

Both `Release` and `ReleaseLean` solution builds reproduce those missing
provider contracts with SwiftCollections `9.0.0`. Tests and coverage cannot run
for these failed package lanes. GridForge itself passes both package families
against the released SwiftCollections v9 packages: 1,209 tests per configuration
and 100% reported lines, branches and executable methods.

Coordinated source validation passes both runtime targets (`netstandard2.1`
and `net10.0`) and the maintained `net10.0` suites:

| Assembly | Release tests | ReleaseLean tests | Reported coverage in both configurations |
| --- | ---: | ---: | --- |
| GridForge | 1,209 | 1,209 | 100% lines, branches and executable methods |
| Gravitas | 8,572 | 8,495 | 100% lines, branches and executable methods |
| Trailblazer | 3,788 | 3,674 | 100% lines, branches and executable methods |
| Trailblazer.Gravitas | 150 | 139 | 100% lines, branches and executable methods |

These runs use the consumer revisions above with the dependency/documentation
patch, SwiftCollections `7c2fa696f10d88194f5a895aaafd7debd8e27574`,
FixedMathSharp `8fdf889322f1bb82501c2afec63d6bb2f304ace2`, and Chronicler
`0281469ecc40e65a05867f473bc10f030e7f69f8`. Build each solution with
`-p:UseLocalLsfStack=true -p:DisableTransitiveProjectReferences=true -m:1`,
then collect each test project's coverage using its existing runsettings and
run that repository's unchanged `assert-full-coverage.ps1` gate. Source/package
caches and fresh configuration-specific results remain separate. No runtime
source, coverage exclusions, reviewed replay expectations or CI policy changed.

The Gravitas replay comparator also passes the explicit local Windows x64
Release/ReleaseLean subset: all eight fixtures agree with shared expectations
and directly across the two lanes. This does not establish the full native
Windows/Linux x64/ARM64 CI matrix. The `netstandard2.1` assets were built, not
executed through separate test hosts in this recheck.

**Disposition / next action:** Keep the source phases complete and the existing
released-chain verification pending. Release the GridForge provider containing
bounded voxel discovery, then update and release Gravitas against that package,
then update the Trailblazer adapter to the released Gravitas provider containing
the bounded query contracts. Choose actual published versions when those
releases exist; no future minimum is inferred here. Re-run Standard and Lean
package-mode builds, tests, coverage and adapter continuation checks against the
resulting chain. Do not substitute local-source builds or weaken assertions to
claim package completion.

### Phase 3 - Bounded posture and final hardening

Both adapters now expose per-call bounded `TryReplaceNavigationProfile` with
caller-owned budget and scratch. `PhysicalFitUnavailable` is appended to the
shared outcome enum, preserving its existing numeric values. Completed blockage
returns `PhysicalFitRejected`; incomplete clearance preserves the complete live
physical/navigation state. Preflight rejection or an unchanged profile reports
null native status and zero work. Existing synchronous calls retain their hooks
and behavior. No serialized schema changed.

The core transaction accepts one invocation-specific commit delegate. Both
adapters reuse the existing navigation preflight and publisher, with shared
geometry construction within each dimension and one common native-result mapper.
Budget and diagnostics remain local to the call. The existing 2D busy guard and
exactly-once publisher rules remain intact. 3D retains its existing notification
reentrancy, including a nested synchronous profile change. Scratch is released
before notifications; committed state and native diagnostics remain observable
when a notification throws. No ambient policy, second transaction framework,
new physics kernel, or record field was introduced.

Regression fixtures cover upright capsules in both dimensions and planar
top-down circles. Exact work reproduces the conclusive blocker; independently
reducing discovery, candidates, or geometry by one leaves the world hash,
navigation record, collider identity, cell publication and contact events intact.
The same checks include blockers outside every indexed voxel. A direct unready
world attempt rejects transactionally and succeeds with the same scratch after
an ordinary reconciliation step. Acceptance preserves the foot, publishes both
systems before callbacks, and survives post-commit notification failures.
JSON and Standard MemoryPack tests restore the body before the navigator and
compare exact continuation through real host steps.

The source baseline is Trailblazer `d581c64be0589c370ec1c5c88f94cc6f7ec14ff8`.
Both platforms use Gravitas `becdd5d61cd8608f508af4ef9933433a7cbf107c`,
GridForge `24ca0f51600c3f1ade727bc1e57ac85d2b22c2b3`, FixedMathSharp
`3da3a8ad9964d3e0413080737fec12ba107b48e4`, and SwiftCollections
`17307f1460313e64043da7995fbc4a4458da09a3`. Windows uses local Chronicler
`3c1cfb536fb009e43d4667158607611dd94ba854`; Ubuntu uses its hosted pin
`7b90d5923d4bb6e0e42c033ec697b9678d6b8ece`. Ubuntu source checks run in an
isolated WSL snapshot with SDK 10.0.203. Actual runtime assemblies are hashed
for every lane. The final Windows Release runtime hashes match the measured
candidate payload.
All 40 consumer assembly bindings (ten per platform/configuration lane) match
their owning source project's output. The posture slice adds 164 net runtime
C# lines across seven files, including the shared commit delegate and native
result mapper in two new type files; tests and documentation are separate.

The matched timing control starts from Trailblazer `d581c64` and holds the
upstream binaries fixed. Windows x64 Release uses .NET 10.0.10, SDK 10.0.302,
tiered compilation disabled and concurrent GC disabled. Four fresh processes
per dimension and mode balance revision, policy and dimension order. Each
warms 1,500 replacement attempts, measures seven batches of 3,000, then warms
64 complete host steps and measures seven batches of 500. Every measured step
includes navigation simulation, preparation, physics resolution, commit and
late simulation before one blocked posture attempt. Before/after oracles prove
unchanged accepted poses and physics hashes. The table gives medians of the
four process medians, in microseconds.

| Dimension / operation | Query or transaction | Complete containing step | Warm bytes per attempt |
| --- | ---: | ---: | ---: |
| 2D / prior synchronous | 225.475 | 269.735 | 1,376 |
| 2D / current synchronous | 222.305 | 266.389 | 1,376 |
| 2D / bounded adapter | 224.225 | 265.598 | 440 |
| 2D / native bounded clearance | 11.857 | 49.068 | 0 |
| 2D / adapter with zero native work | 205.118 | 256.583 | 440 |
| 3D / prior synchronous | 8.483 | 41.756 | 1,888 |
| 3D / current synchronous | 8.382 | 43.039 | 1,888 |
| 3D / bounded adapter | 7.608 | 41.724 | 648 |
| 3D / native bounded clearance | 5.717 | 38.633 | 0 |
| 3D / adapter with zero native work | 1.336 | 30.303 | 648 |

The 2D scene reuses the one-actor crowd fixture, including its eight local side
obstacles and floor. It starts with radius 1/2, shrinks the capsule axis from
1 to 0, and registers a ceiling centered at height 15/8 with height 1/4.
The 3D profile-replacement fixture uses a floor and a capsule compacted to
radius 1/4 and height 1/2, with a ceiling at foot + 7/8 and height 1/4.
Both scenes attempt blocked growth while preserving the current foot. Ample
native limits of `8,192/8,192/8,192` complete every measured attempt with
`2/11/93` work in 2D and `2/3/3` in 3D. Zero limits make every adapter attempt
unavailable with zero native work, zero publication and unchanged physical state.

Native detached preparation/clearance remains zero-allocation after setup.
The surrounding navigation transaction allocates 440 bytes in 2D and 648 in
3D, including when native work is zero. The existing navigation preflight is
outside the native allowance and dominates the 2D rejected transaction. These
limits therefore do not certify complete transaction or frame time. Existing
synchronous allocation counts are unchanged. Complete-step process ranges
are 266.017-272.521 us before and 263.008-269.490 us after in 2D, and
41.582-45.271 us before and 40.774-44.664 us after in 3D. The overlapping
ranges do not establish zero overhead or a repeatable default-path regression.
The 2D zero-work control retains an adverse 413.896 us process median;
its complete-step range is 241.476-413.896 us. No sample was discarded.

Raw inputs, probes, executable/dependency hashes, individual timing rows,
source revisions, validation commands and replay captures are under
`artifacts/query-budget-phase3-closeout`. The disposable timing command is
`pwsh -NoProfile -File artifacts/query-budget-phase3-closeout/measure.ps1`.
The source tests above retain the executable behavioral inputs; this record
retains the timing protocol and limits if disposable artifacts are removed.
Reopen performance investigation on a reproducible matched default-path increase
or an actual target-workload budget miss, retaining inputs and loaded binaries.

The final Windows and Ubuntu x64 source suites pass 3,788 core / 150 adapter
tests in Release and 3,674 core / 139 adapter tests in ReleaseLean, with no
skips. Every combined report contains both assemblies and covers all 36,164
lines, 15,865 branches and 3,631 methods. Core alone covers 35,776 lines,
15,765 branches and 3,589 methods; the adapter covers 388 lines, 100 branches
and 42 methods. Both library targets build in both configurations. The full
unchanged Gravitas suites pass 8,572 Release and 8,495 ReleaseLean tests on
each platform. DocFX passes with warnings as errors, and all local links in
its 176 generated HTML pages resolve.

Twelve bounded-posture traces match across platforms, configurations and JSON /
Standard MemoryPack. They retain native physics hashes and raw accepted poses,
motion, support, geometry and committed-cell observations. All 112 final
crowded-support captures, each containing 160 observed frames, match the earlier
crowd baseline byte for byte. All 32 shared native replay payloads match the
rotation-admission baseline after CRLF/LF normalization only; values and field
ordering are unchanged. Final capture provenance records the source mode,
configuration, runtime and actual assembly identities. Windows uses .NET
10.0.10; Ubuntu 24.04.1 under WSL uses .NET 10.0.7.

Validation uses `UseLocalLsfStack=true` with
`DisableTransitiveProjectReferences=true`, `-m:1`, and `BuildInParallel=false`.
Core and adapter tests use their separate checked-in coverage settings; no
coverage exclusions or workflow changes were added. Refreshing the Ubuntu
snapshot initially reused older Lean test outputs because copied source
preserved older timestamps. Those results are excluded from closeout. Forced
`--no-incremental -p:BuildProjectReferences=false` builds of both Lean test
projects, followed by their full suites, produce the final counts above.
The final runtime binaries are unchanged by that test-only rebuild.

Independent reviews covered the first runtime seam, final runtime/tests/docs,
and the final readiness and core-hook regressions. No actionable findings
remain. The synchronous path retains its original host override. Geometry
construction and result mapping are shared where the contracts match, and
existing dimensional publication rules remain with their current owners.

Phases 0-3 are complete in the coordinated source stack. The completed plan is
archived with these results. Hosted validation of the final committed consumer,
native ARM64 replay, and final released Standard/Lean dependency-chain checks
remain explicit coordinated release gates in the feature overview. They are
not inferred from local source runs. Release the changed upstream packages in
dependency order before validating and publishing Trailblazer. No dependency
pins, versions, commits or release artifacts were published by this closeout.


### Phase 3 - Crowded support and readiness

The crowd fixtures exercise the existing adapters with one floor, eight nearby
box obstacles, and distinct actors two units apart. Native 2D uses one row of
upright capsules at 16 Hz. Native 3D uses a square group of spheres at 32 Hz.
Both use actual floor collisions, gravity, a stable carrier identity, and the
all-prepare/one-resolve/all-commit barrier. Maintained tests cover 4 and 16
actors. They compare complete bounded and synchronous batches by physics replay
hash and navigation pose, velocity, support, and carrier state.

Alternating insufficient and useful allowances on one shared scratch lets the
next actor complete after an earlier probe exhausts. Repeated exhaustion keeps
accepted body/navigation poses aligned and releases support/carrier evidence.
Subsequent prepares still request downward gravity and omit stale carry. A
topology addition after physics resolution makes every commit probe report
`WorldNotReady` with zero work. Accepted motion commits, and the following
ordinary host step reconciles partitions and recovers support.

The Windows x64 `Release` controls use 16 or 64 actors, with 96 warm frames,
32 useful frames, a 96-frame comparison window, and 32 recovery frames. One
support probe runs per actor per frame. All queries share scratch with 256
voxel slots and 256 candidate slots. Useful per-call limits are
`8,192/8,192/8,192` (discovery/candidates/geometry). Insufficient geometry
limits are 174 in 2D and 57 in 3D, one below the least expensive completed
probe in the respective fixture. Four processes per lane balance policy and
dimension order with tiered compilation disabled on .NET 10.0.10. Values below
are medians across the four process medians for complete host steps.

| Scene | Synchronous | Useful bounded | All actors under pressure | Alternating pressure |
| --- | ---: | ---: | ---: | ---: |
| 2D / 16 actors | 333.600 μs | 313.700 μs | 1,772.950 μs | 1,069.150 μs |
| 2D / 64 actors | 1,370.250 μs | 1,258.050 μs | 3,679.800 μs | 2,448.150 μs |
| 3D / 16 actors | 522.150 μs | 494.850 μs | 333.850 μs | 422.200 μs |
| 3D / 64 actors | 2,048.500 μs | 1,971.100 μs | 1,324.800 μs | 1,656.950 μs |

Each static 64-actor comparison window contains 6,144 probes. Useful limits complete
every probe with zero support/ground/carrier losses. Full pressure leaves all
6,144 probes unavailable, with a maximum consecutive stretch of 96 frames per
actor and one ground/carrier loss per actor. Alternating pressure makes 3,072
probes unavailable and loses support on 32 actors. All recover on the first
useful-budget frame. The largest per-call work components are `29/30/175` in
2D and `56/182/62` in 3D. These are component-wise maxima, not frame totals.

The useful policy is usable on these fixtures. Sustained insufficient limits
leave grounded actions unavailable even while physics keeps actors against
the floor. In 2D, the resulting airborne gravity and collision clipping make
complete frames slower despite stopping the support kernel early. Budget
selection must consider that motor and physics response alongside query cost.
Static synchronous and useful-budget frames allocate zero warm bytes. The
3D pressure/recovery samples include transition allocations, including up to
21,472 bytes in a 64-actor recovery frame. Moving-floor and topology
controls include their publication and reconciliation work. Raw inputs,
loaded-binary hashes, observations, and all timing samples are under
`artifacts/query-budget-phase3-crowd`.

The full adapter suites passed 134 `Release` and 125 `ReleaseLean` tests on
Windows and Ubuntu x64. Both library targets and configurations built. Every
adapter report covers all 361 sequence points, 90 branches, and 37 methods.
The core runtime source is untouched by this slice. DocFX passed with warnings
as errors. The source baseline is `f996aaf`, using the coordinated upstream
revisions already recorded for the support slice. The Ubuntu run uses the
hosted dependency closure.

Each of the four x64 platform/configuration lanes captured 28 scenarios with
160 frames each. All 112 captures matched exactly by authoritative physics
hash and selected navigator observations. These include stationary,
moving-floor, insufficient-budget, mixed-budget, readiness, and recovery
cases at 16 and 64 actors. Synchronous and useful-budget stationary traces
also match after excluding bounded-query diagnostics. Existing moving-carrier
checkpoint tests continue to verify full attachment and rotation continuation
in JSON and Standard MemoryPack. Native ARM64 and released-package checks
remain release gates. Bounded profile replacement is the next API slice.

### Phase 3 - Opt-in adapter support

Both adapters now use the same host-owned budget policy and preserve their
synchronous default. The 2D adapter applies the policy on an explicit support
refresh as well as on side-view commit. The 3D adapter checks the foot and
probe origin separately before starting bounded work. The same witness,
normal, and carrier translation serves bounded and synchronous hits within
each dimension. `SupportStatus` and the bounded status/work are separate from
the grounded decision and are transient. A load uses the host policy already
set on that navigator and clears prior diagnostics. Both dimensions expose
the core `NavigationSupportStatus` enum with the same byte values. The shared
type replaces the dimension-specific names in the public support contracts.

Two actors can prepare and commit against one resolved 3D frame with the same
scratch. A one-below geometry allowance on the rider clears its carrier and
retains the accepted pose, while the second actor's probe still completes.
The following frames continue ordinary gravity and sample support once each.
The 2D fixture also checks repeated exhaustion, gravity, and exact recovery
on a moving surface. Completed misses and unavailable samples stay distinct.
The edge witness fixture demonstrates that an upstream completed hit can have
an unmaterializable world point. It leaves the bounded adapter unavailable.
Guided arrival and action completion still require their existing support
proofs. Policy changes are rejected during prepared motion and steering
callbacks. Scratch remains outside snapshots, with no record schema change.

Moving-carrier fixtures compare the same synchronous and bounded poses,
velocities, rotations, and attachment state through checkpoint continuation
in JSON and, in Standard, MemoryPack. The Windows and Ubuntu x64 coordinated
source suites passed 3,786 core / 130 adapter tests in `Release` and 3,672
core / 121 adapter tests in `ReleaseLean`. All four exact coverage reports
contain both assemblies and cover 36,134 lines, 15,847 branches, and 3,624
methods. Both library targets built and DocFX passed with warnings as errors.
The Ubuntu run used the exact hosted
dependency closure, including Gravitas `becdd5d6`, GridForge `24ca0f51`, and
FixedMathSharp `3da3a8ad`. Gravitas `2.0.1` packages lack the bounded APIs.
Released Standard/Lean verification follows the upstream package releases.

The matched Windows x64 `Release` control uses the committed `43d9859`
adapter and the new adapter against the same upstream binaries. Each fixture
has one actor and one support probe per full host frame (2D at 16 Hz and 3D
at 32 Hz). Four processes per lane alternate revision and dimension order
with tiered compilation disabled on .NET 10.0.10. Each uses seven batches
of 3,000 invocations. The planar invocation is a two-frame motion pair.
All lanes pass the same floor and accepted-pose oracles and allocate zero
bytes per warm frame. The `8,192/8,192/8,192` allowance completes using
`12/5/175` work in 2D and `56/53/58` in 3D (discovery/candidates/geometry).

| Full step | Prior synchronous | Current synchronous | Current bounded |
| --- | ---: | ---: | ---: |
| 2D floor | 62.026 μs | 62.207 μs | 63.474 μs |
| 3D floor | 24.074 μs | 24.142 μs | 22.372 μs |

Keeping bounded preparation in the shared sampler raised the synchronous 3D
process medians by roughly 0.4–0.6 μs in two runs. Separating it from
the shared hit translation brought the default back within the committed
baseline's process range. The final comparisons establish the one-actor floor
control. Crowded scenes, readiness changes, and bounded posture remain the
next checks. Inputs, binary hashes, replays, and raw timings are in
`artifacts/query-budget-phase3-support`.

### Phase 2 - Native shapes, topology, and posture

GridForge reuses its normalized grid order, physical coverage rules, and
quantized hex range in bounded discovery. Sparse lookup charges the
direct block lookup plus the comparison bound from the block's live voxel
count. The address loop includes physically present voxels only. For hex,
the one-address halo uses widened arithmetic before narrowing to the
grid's range. The bounded prepared view uses `SwiftSortedList`. Grid
publication and interval tracing share one allocation-free key comparer.

Gravitas support reuses the synchronous hit reducers and reserves the final
view check in both dimensions. The 2D path admits boxes, circles, capsules,
convex polygons, and flat compounds. The 3D sweep admits cuboids, spheres,
capsules, finite cylinders and cones, meshes, and authored compounds.
Meshes use a caller-sized triangle buffer and traversal span. The ordinary
and fixed-storage Swift BVH queries share traversal order. The reservation
covers both traversals, patch inspection, the sweep, and the closest witness.
One box costs 175 geometry units in 2D and 58 in 3D, including admission.
The earlier 2D box cost of 10 omitted work inside the convex kernel.

Bounded posture uses the same strict penetration kernels as synchronous
posture, including exact touch and blockers outside indexed voxels. Live
registration links allow early exit in the same relative order after
removal and ID reuse, with constant-time link maintenance. The synchronous
scan retains service order. The first bounded blocker can differ when
multiple colliders penetrate after churn. Circle and capsule changes in
2D and sphere and capsule changes in 3D reuse detached scratch. Finite
cylinder replacement remains available through the synchronous API.
An unrepresentable host position leaves both poses and collider state
unchanged, even when the strict parent transform precheck succeeds and
the position setter rejects a saturated intermediate. Warm, valid clearance
rejections allocate zero bytes in the fixtures. Existing exceptional
shape-validation failures can allocate the exception caught by preparation.

Four paired Windows x64 processes per binary ran each control in balanced
order with tiering disabled under .NET 10.0.10. They verified exact witnesses
and exact/one-below work at one and 512 grids and one, 16, 64 and 256 coincident
members. The table reports the median of process medians for one containing
step, including the query. Mutation adds and removes a remote grid each step.
The dense check uses a geometry ceiling of 100,000 in both lanes so the new
44,800-unit 2D query and the 2,560-unit baseline both complete.

| Bounded workload | Before | After |
| --- | ---: | ---: |
| 2D, 512 grids, stable with floor | 74.20 μs | 74.73 μs |
| 2D, 512 grids, mutation with floor | 88.55 μs | 88.46 μs |
| 3D, 512 grids, stable with floor | 23.39 μs | 23.94 μs |
| 3D, 512 grids, mutation with floor | 64.41 μs | 65.50 μs |
| 2D, 256 coincident floors | 16.308 ms | 16.325 ms |
| 3D, 256 coincident floors | 2.316 ms | 2.285 ms |
| 2D, 512 grids, stable miss | 5.13 μs | 5.03 μs |
| 3D, 512 grids, stable miss | 9.38 μs | 9.43 μs |

Queries and stable/dense steps allocated zero bytes in all sampled rows.
Grid churn returned to 220 B per 2D step and 380 B per 3D step after removing
48 B of enum boxing from the published-grid comparison. The 3D floor
control showed a small containing-step increase at 512 grids. The absolute
change was 0.55 μs stable and 1.09 μs with publication. The equivalent
synchronous steps moved from 20.81 to 21.45 μs and 61.99 to 63.19 μs.
Phase 3 still needs useful-allowance selection and crowded adapter scenes.
Matched sources, hashes and captures are under
`artifacts/query-budget-phase2-native`. The dense timings preceded the
shared comparer cleanup and final posture fixes, which those stable floor
queries do not exercise.

Independent review checked the sparse and hex discovery boundary, scratch
capacity and traversal order, geometry reservations, and transactional
posture publication. Both source configurations built both library targets.
Windows x64 (.NET SDK 10.0.302) and Ubuntu x64 under WSL (.NET SDK 10.0.203)
passed the same test counts:

| Suite | Release | ReleaseLean |
| --- | ---: | ---: |
| SwiftCollections core and FixedMathSharp companion | 1,463 | 1,433 |
| GridForge | 1,206 | 1,206 |
| Gravitas | 8,548 | 8,471 |
| Trailblazer core | 3,786 | 3,672 |
| Trailblazer Gravitas adapter | 118 | 111 |

The SwiftCollections core suites covered all 2,398 branches in both Windows
source configurations. GridForge covered all 9,428 lines and 4,207 branches in
both configurations. DocFX passed with warnings as errors in SwiftCollections,
GridForge, and Gravitas.

The coverage follow-up exercises discovery and partition exhaustion, pending
publication, native shape admission, fixed mesh scratch, and transactional
posture rejection. The capsule-blocker case checks the first blocker, exact
work, and authoritative hashes after collider ID reuse. Both support paths
share the same view check at discovery and result certification. They retain
the underlying non-waiting snapshot checks. The 24 matched controls preserved
their exact work and one-below outcomes. Eight shared replay fixtures matched
frame for frame across Windows and Ubuntu x64 under WSL in Release and
ReleaseLean. The raw reports and captures are under
`artifacts/query-budget-phase2-coverage`.

The final Windows source runs passed 8,568 Gravitas tests in Release and
8,491 in ReleaseLean. The Ubuntu focused runs passed 232 and 228 tests,
including the replay fixtures. The downstream Ubuntu adapter suites passed
118 and 111 tests. The exact Gravitas reports have no uncovered Phase 2
points. Release covered 54,826 of 54,830 lines and 5,786 of 5,786 methods.
ReleaseLean covered 54,824 of 54,828 lines and 5,785 of 5,785 methods.
Both covered 22,853 of 22,857 branches.

The rotation-admission follow-up closed the four remaining Gravitas coverage
gaps from `a83b76e3`. Mesh candidate discovery uses the committed source
bounds. Cuboid witness refinement uses the same checked inverse rotation as
sweep support. Mixed contact and mesh face normals retain their exact rotations
and assert the representability guaranteed by geometry and pose admission.

The full Windows source suites passed 8,568 Release tests and 8,491
ReleaseLean tests. Release covered all 54,808 sequence points and 5,786
methods. ReleaseLean covered all 54,806 sequence points and 5,785 methods.
Both covered all 22,843 branches. DocFX passed with warnings as errors.
Ubuntu passed 595 focused tests in each configuration, plus 118 Release
and 111 ReleaseLean Trailblazer adapter tests. All 32 payloads from the
four x64 replay lanes matched the previous Phase 2 captures byte for byte.
Raw reports and captures are under `artifacts/rotation-admission-coverage`.

Hosted pins, native ARM64 replay, and released packages remain release
verification.

### Phase 2 - Optimization and hardening review

The reviewed baseline is Gravitas `89701a48`, GridForge `32fe579`,
FixedMathSharp `30d76f1`, and SwiftCollections `17307f1`. The review covered
grid and collider traversal, pair filtering, support narrowphase, fixed
scratch, and posture publication.

- A removed, bodyless 2D collider left a native partition on a voxel shared
  with a 3D sweep. A restored checkpoint lacked that cache, so the restored
  exact allowance completed in one world and exhausted in the other. The
  `BoundedSweep_ReplaysAfterRemovedMembershipsAndCheckpointRestore` fixture
  reproduced the difference in both transports. Its 2D counterpart now also
  checks that retaining a 3D partition leaves the hit, status, exact work,
  and one-below results unchanged. The new GridForge lookup omits up to two
  exact cache types and still charges host partitions. Both pure-mode Gravitas
  callers use the same lookup and accounting path.
- A child moved to world X `1,500,000,000` beneath parents translated by
  `1,000,000,000` and `-1,000,000,000` could pass the combined parent proof
  while overflowing in child-first composition. The 2D body applied the pose
  and the 3D body threw during collider publication. A second FixedMathSharp
  fixture uses parent scales `5/4` and `2048` to show that representable
  child-first composition can also miss the requested position by 512 raw
  units, outside the 256-raw tolerance. Both posture paths now check the
  prospective pose before publication. The 3D path also checks the live
  collider transform when the host exposes a transform distinct from the
  body's cached position transform. Regression tests verify unchanged state
  and replay hash and zero synchronized callbacks on rejection.
- The shared 3D sphere sweep prepares the convex kernel once, when it first
  reaches a cylinder or cone. A worker reuse test checks opposite-direction
  sweeps so prepared state cannot carry across calls.

The paired Windows x64 Release controls used four processes per revision,
alternated revision and query order, and disabled tiered compilation on .NET
10.0.10. All 24 many-grid, dense-membership, and empty controls preserved their
charged work and exact/one-below outcomes. Warm queries still allocated zero
bytes per operation, and step allocation samples matched. Representative
medians in microseconds per operation follow.

| Control | Query before → after | Full step before → after |
| --- | ---: | ---: |
| Bounded 2D empty, one stable grid | 0.753 → 0.840 | 2.035 → 2.158 |
| Bounded 3D empty, one stable grid | 2.014 → 2.083 | 5.730 → 6.007 |
| Synchronous 3D empty, 512 stable grids | 3.055 → 2.849 | 7.015 → 6.717 |
| Bounded 3D hit, 512 stable grids | 16.578 → 16.727 | 23.296 → 23.560 |
| Bounded 3D hit, 512 grids with mutation | 16.843 → 16.808 | 66.239 → 64.619 |
| Bounded 3D hit, 256 memberships | 2,057.544 → 2,070.388 | 2,283.638 → 2,301.369 |

Synchronous 3D empty query medians fell 6.7–7.8%. The bounded empty cases
showed the clearest cost of partition accounting, about 0.09 μs per 2D query
in the smallest fixture. Most many-grid hit and dense timings overlapped
across the two revisions. Phase 3 uses representative adapter scenes for the
frame-level decision. Raw samples, red reproductions, and replays are under
`artifacts/query-budget-phase2-hardening`.

All eight shared Gravitas replay fixtures matched the previous captures byte
for byte on Windows and Ubuntu x64 in `Release` and `ReleaseLean`. Ubuntu
passed 402 and 398 focused Gravitas tests. The FixedMathSharp transform suite
passed 43 tests in each Windows configuration and covered every line and
branch in the changed transform methods. Both GridForge configurations
passed 1,209 tests and covered all 9,454 sequence points, 4,233 branches,
and 1,161 methods. GridForge and Gravitas DocFX builds passed with warnings
as errors.

The full Windows Gravitas runs passed 8,572 tests in `Release` and 8,495
in `ReleaseLean`. Both covered all 22,847 branches. `Release` covered all
54,812 sequence points and 5,787 methods. `ReleaseLean` covered all 54,810
sequence points and 5,786 methods. The Trailblazer Gravitas adapter passed
118 and 111 tests in both Windows and Ubuntu source mode.

The full Ubuntu FixedMathSharp suites passed 7,440 `Release` core tests,
7,421 `ReleaseLean` core tests, and 49 Chronicler tests in each configuration.
The `Release` run preceded two extra transform cases verified in both Windows
configurations and in the full Ubuntu `ReleaseLean` run. That Lean report
covers the changed transform code and leaves three branch points at two
untouched geometry guard sites (`FixedSegment.CenteredAxis.cs` and
`WideFiniteAxisIntersection.CenteredSurfaceAnchors.cs`).

Coordinated source mode selects the new APIs. Hosted development validation
requires updating pins after the upstream commits are available. Package-mode
validation requires new FixedMathSharp and GridForge packages before the
Gravitas package can consume them. Native ARM64 replay remains a hosted gate.

### Phase 2 - Linked-collider filtering

Both constraint services now use one implementation of the suppressed pair
index. Each distinct collider pair occupies one sorted entry and counts its
suppressing joint and ragdoll owners. The same binary lookup serves synchronous
collision filters and bounded queries. The maximum comparisons equal the bit
length of the live pair count and are reserved before lookup. Capacity and
discarded entries do not contribute to this bound.

The native 2D and 3D checkpoint tests retain a suppressed, lower-ID box
coincident with an eligible floor. They register and deactivate 32 additional
linked bodies in the live world, then populate the surviving joint in the
rebuilt world. The baseline exhausted the live query at the restored candidate
allowance. The new queries agree on the exact winner, witness and charged
work, and both exhaust without a hit one candidate unit below. Standard JSON
and MemoryPack and Lean JSON exercise the same contract.

The 32-link constraint chain control changes only the Gravitas binary. Four
fresh Windows x64 processes per lane ran in balanced order with tiering
disabled under .NET 10.0.10. The table gives medians of process medians.

| Workload | Before | After |
| --- | ---: | ---: |
| 2D linked lookup hit | 24.60 ns | 9.89 ns |
| 2D lookup miss | 12.96 ns | 13.04 ns |
| 2D complete step | 2.614 ms | 2.627 ms |
| 3D linked lookup hit | 25.52 ns | 9.78 ns |
| 3D lookup miss | 12.73 ns | 12.71 ns |
| 3D complete step | 3.467 ms | 3.408 ms |

The complete-step and miss ranges overlap. All captures agree on authoritative
hashes at frames 16 and 280 and exact constraint row totals. Lookup allocated
0 B on the calling thread. Maximum measured step allocations matched at 57 B
in 2D and 114 B in 3D. A separate control removed and re-registered one joint
at a time between physics steps with the remaining links live. At 31 pairs,
2D cycles took 510 vs. 440 ns and 3D took 601 vs. 524 ns. At 127 pairs, the
medians were 873 vs. 487 ns and 952 vs. 567 ns. Allocation ranges matched,
including joint creation and occasional growth of the joint arrays. The
control exercised chains. Arbitrarily ordered all-links ragdolls can cause
more shifts and were outside these captures.

The combined source stack also passed the many-grid and dense-member semantic
checks at up to 512 grids and 256 coincident floors per query. These checks
replayed the hit witnesses and exact and one-below work outcomes.

Windows source validation passed 8,514 Release and 8,438 ReleaseLean
Gravitas tests. Ubuntu passed 208 and 197 focused tests, and both Trailblazer
adapter suites passed (117 and 110). Both Gravitas targets built on Windows
in both configurations. DocFX passed with warnings as errors. Both constraint
services and their shared pair index reached full line and branch coverage.
The other bounded query and readiness paths still have 23 uncovered lines
and 34 uncovered branches in each configuration. Hosted and released-package
validation remain pending.

Probe sources, binary hashes, and logs are under
`artifacts/query-budget-phase2-linked`.

### Phase 2 - Retained empty partition lookup

GridForge reserves one unit for the requested type and one per other attached
partition. The lookup holds the same non-waiting lock for the provider count
and the existing inline/overflow search. It reserves the attached count before
inspection and accounts for the requested type explicitly on a miss. An
insufficient allowance is fully charged. With two other payloads, both an
absent request and an attached request complete with three units and exhaust
with the same work at every smaller allowance.

The 2D and 3D box-floor checkpoint fixtures now register and deactivate a box
at the probe start. They verify the live world has the retained empty payload
and the rebuilt world has no requested payload there. The old lookup failed
the exact restored budget in both dimensions and both Standard transports.
Both worlds now agree on the exact witness and work, and on exhaustion and
cleared results one unit below the discovery or candidate limit. Standard JSON
and MemoryPack and Lean JSON agree.

The Windows x64 probes used .NET 10.0.10 with tiering off. Matched captures
changed only the GridForge binary and ran four fresh processes per lane in
balanced order. Floor-hit allowances held steady. With no floor and one grid,
discovery went from 31 to 46 in 2D and from 91 to 136 in 3D, with one new unit
per queried voxel lacking the requested payload. Candidate and geometry work
stayed at zero.

| Bounded step with 512 base grids | Before, microseconds | After, microseconds |
| --- | ---: | ---: |
| 2D floor, stable | 75.52 | 75.48 |
| 3D floor, stable | 23.51 | 23.91 |
| 2D miss, grid mutation | 13.06 | 13.22 |
| 3D miss, grid mutation | 23.66 | 23.54 |

The table shows medians of process medians. The before and after process ranges
overlap in each row. Warm queries allocated 0 B on the calling thread. Grid
mutation frames retained their prior maxima of 220 B in 2D and 380 B in 3D.
Probe sources, hashes, and logs are under
`artifacts/query-budget-phase2-empty-partition`.


GridForge passed 1,185 tests per configuration on Windows and Ubuntu. Both
library targets built on Windows, and DocFX passed with warnings as errors.
The Windows runs covered all lines and branches in the changed lookup. Six
uncovered branches remain in the existing bounded voxel discovery path.
Gravitas passed 22 Release and 20 ReleaseLean focused tests on both platforms.
Hosted Gravitas must pin the updated GridForge commit when it is available.
Released-package validation remains pending.

### Phase 2 - SwiftCollections membership storage

Gravitas uses SwiftCollections' `SwiftSortedList<int>` for the three pure
mobility roles. `TryAddUnique` rejects comparer-equivalent IDs and shares
insertion with the existing duplicate-allowing `Add`. This removes Gravitas's
local sorted-membership helper. The solver still snapshots memberships before
processing callbacks, using bulk copies from the live span.

The upstream regressions reproduced growth from 8 to 16 slots at a fixed live
count of six, and a removed reference left in the backing array. Insertion now
reuses available space and shifts the shorter side when both sides have room.
Removal releases vacated references and recenters once when crossing below a
quarter full. This avoids repeated recentering during a drain.

The matched Windows x64 Release probes used .NET 10.0.10 with tiering off.
The three-way dense control reuses the same GridForge and other dependency
binaries. Each lane ran in two fresh processes, in forward and reverse order.
Each frame runs one support query. Times are medians of process medians.

| 256 coincident floors | Original hash-backed | Staged BCL list | SwiftSortedList |
| --- | ---: | ---: | ---: |
| 2D bounded containing frame | 16.25 ms | 16.18 ms | 16.12 ms |
| 3D bounded call | 3.02 ms | 2.10 ms | 2.06 ms |
| 3D bounded containing frame | 3.25 ms | 2.32 ms | 2.29 ms |

The Swift and BCL list lanes produced the same exact charged work, hit
witnesses and one-below outcomes at all four floor counts. Both kept the
candidate-work reduction from the earlier membership change. Every measured
warm query and frame allocated 0 B on the calling thread.

The separate 128-mover churn control compared the final Swift implementation
with the staged BCL list and with the previous Swift implementation between
reverse-order captures. It measured 479.5 vs. 473.7 microseconds per full 2D
step and 1,631.5 vs. 1,633.7 in 3D. The 2D difference was about 1.2% in this
synthetic case. The 3D process ranges overlapped. The original hash-backed
before control measured 471.3 and 1,634.6 microseconds in the same fixture,
respectively. Raw results and input hashes are in
`artifacts/query-budget-phase2-swift-membership`.

Local source-mode verification built both library targets and ran both
configurations. SwiftCollections passed 1,460 Release and 1,430 ReleaseLean
tests across core and companion, with all instrumented lines and branches
covered. Gravitas passed the focused replay, partition, and query checks
(526 and 514). Trailblazer's adapter passed 117 and 110 tests. SwiftCollections
DocFX passed with warnings treated as errors. The new API is available to
the coordinated source build. Hosted source pins and package validation are
pending an upstream commit and package.

### Phase 2 - Canonical collider memberships

Gravitas bounded support and sphere sweeps now enumerate live collider IDs in
ascending order within the existing dynamic, kinematic, and static roles.
Each role replaces its hash set with one sorted list. The solver keeps its
existing callback-safe snapshot and can skip sorting the dynamic copy. The
mixed partitions and awake subsets retain their current storage.

Candidate deduplication binary-searches the existing fixed scratch array.
It charges each comparison and reserves the work for any shift before
moving entries. Repeated IDs across voxels now need logarithmic comparisons.
Inserting IDs out of order can still shift the array, and sorted partition
publication can still move IDs during updates.

The replay regressions started red after removing 16 colliders. They now also
move the lower-ID floor away and back while preserving both eligible floor IDs.
The exact budget gives the same hit, witness, and charged work. One fewer
candidate unit gives the same incomplete status and clears the hit. An
additional scratch test verifies that failed shift admission preserves the
IDs for retry. Standard JSON and MemoryPack and Lean JSON agree. The original
one-floor probe completes at `37/20/10` in 2D and `100/53/58` in 3D. With one
fewer candidate unit, both worlds exhaust and charge `37/19/10` and
`100/52/58`, respectively.

The dense coincident-floor control retained the same selected hit, discovery
work, and geometry work on both binaries. Candidate work at 256 floors fell
from 198,671 to 12,348 in 2D and from 894,902 to 55,584 in 3D. Four paired
Windows x64 Release captures changed only the Gravitas binary and disabled
tiering. The 256-floor 3D bounded query went from 3.42 to 3.50 ms per call
to 2.49 to 2.65 ms. Its containing step went from 3.68 to 3.87 ms to 2.82
to 2.99 ms. The 2D timings overlapped across the two binaries. Every warm
dense control allocated 0 B on the calling thread.


A separate publication control moved 16 or 128 coincident kinematic colliders
between disjoint voxel sets every frame and ran one floor query per step. The
first sorted-list version added about 9% to the 128-mover 2D median. The final
list appends IDs already in order and removes a matching head without a binary
search. Across four paired captures of the final version, the 128-mover median
was 594.0 vs. 600.2 microseconds in 2D and 2,189.5 vs. 2,089.4 in 3D. The paired
absolute differences varied in sign in both dimensions. The 2D churn-to-stable
ratio rose from 2.81 to 2.90, so a smaller publication cost remains plausible.
All warm steps allocated 0 B on the calling thread. Packed removals can still
shift members. The probe sources, raw timings, and binary hashes are in
`artifacts/query-budget-phase2-membership`.

Coordinated Windows source validation passed 8,510 Gravitas tests in Release
and 8,436 in ReleaseLean. Both library targets and the benchmark project built
in both configurations. Ubuntu passed the focused suites (91 Standard and
89 Lean). Trailblazer's adapter passed 117 Standard and 110 Lean tests,
including exact stationary yaw replay. Gravitas DocFX passed with warnings
treated as errors. The full Release coverage capture exposed remaining gaps
in the bounded query paths and their readiness, linked filtering, and
status handling. Hosted and released-package validation remain pending.

### Phase 2 - Canonical live-grid discovery

GridForge now visits live grids in the existing normalized configuration order.
The world keeps one dense list of references under its current registration,
removal, and reset locks. Removal and reset release those references before
returning grids to the pool. The bounded query charges each grid before reading
it. This adds four net runtime lines, no new types or public APIs, and one list
reference per grid plus spare list capacity. Discovery still scans every live
grid. The synchronous spatial index follows its existing path.

Two regression tests failed on the old slot scan. The passing cases cover the
same overlapping grids at different slot IDs and insertion orders, identical
prefixes at tight limits, and active reset followed by slot reuse. Two
independent reviews checked mutation lifetime, configuration order, and
pre-inspection accounting.

The native checkpoint probe now returns the same exact hit and work on both
sides of restore for the removed-grid history. Both worlds complete at
`38/26/10` in 2D and `101/53/58` in 3D. With discovery reduced by one, both
worlds exhaust without a hit, charging `37/25/10` in 2D and `100/51/58` in 3D.
Standard JSON and MemoryPack and Lean JSON agree. The separate removed-collider
history still exhausts in the live world at the restored budget. Gravitas
membership work is the next replay gap.

GridForge passed 1,183 tests per configuration on both Windows and Ubuntu.
Both library targets built on Windows, and DocFX passed with warnings treated
as errors. The five focused Gravitas tests passed in `Release` and `ReleaseLean`
against the coordinated sources. The GridForge change uses the existing
SwiftCollections package contract. Hosted and released validation of the
downstream bounded query chain remains pending.

The many-grid control used one box-floor probe per step with one or 512 base
grids. The mutation rows add or remove one remote grid before every containing
step. Four paired Windows x64 Release captures used the same Gravitas binary,
changed only the GridForge binary, and disabled tiering in both lanes. Each
scene used 64 warm steps and seven alternating blocks of 32 calls and 32 full
steps. Selected ranges of per-process medians are in µs.

| Bounded scene, 512 base grids | Before call | After call | Before step | After step |
| --- | ---: | ---: | ---: | ---: |
| 2D stable | 74.8–86.7 | 77.4–81.7 | 80.8–88.0 | 81.0–84.9 |
| 2D mutation | 76.9–86.8 | 77.1–83.8 | 99.4–105.6 | 99.0–114.3 |
| 3D stable | 17.1–22.6 | 18.2–18.8 | 26.0–34.9 | 25.9–27.7 |
| 3D mutation | 17.8–19.5 | 17.0–20.8 | 67.8–88.7 | 71.9–81.9 |

The paired ranges overlap. Warm query allocations stayed at 0 B. The mutation
steps retained their prior measured allocation maxima of 220 B in 2D and
380 B in 3D. The captures do not establish the cost of cold list growth.
Raw results, binary hashes, and disposable probes are under
`artifacts/query-budget-phase2-grid`.

### 2026-11-14 - Checkpoint replay and scaling

On Gravitas `b406bd98`, GridForge `72740cc`, and SwiftCollections `b7f5f2c`,
a tight budget changes a correct box-floor hit into an incomplete query solely
because the live world has a different storage history from the restored world.
Standard JSON and MemoryPack and Lean JSON reproduce the same split in both
dimensions. Ample budgets find the same floor at distance 1/2 with its contact
at the origin.

The replay probe uses pure 2D or 3D, manual grounding on the 2D source,
16 Hz, zero gravity, and a dense rectangular grid from -4 to 4 on each active
axis. The static floor is a 3-by-1 planar box or 3-by-1-by-3 cuboid, centered
half a unit below zero. Probe from height 1 with radius 1/2 and reach 1. In
one history, register 16 additional identical floors and deactivate them,
retaining the first floor. In the other, add two remote unit grids at 16 and
24 and remove the first. Keep the remote grid at 24 in the restored world.
Prepare fresh matching host shells for the surviving objects, populate the
body and floor records, and match the live and restored frame at 1. Query both
with ample budgets, then retry both at the restored world's exact three limits.
The live and restored floor IDs match in these experiments.

| History | Live work | Restored work |
| --- | ---: | ---: |
| 2D removed colliders | 37/122/10 | 37/26/10 |
| 2D removed grid | 39/26/10 | 38/26/10 |
| 3D removed colliders | 100/485/58 | 100/53/58 |
| 3D removed grid | 102/53/58 | 101/53/58 |

Work is discovery / candidates / geometry. At the restored limits, each live
query returns `WorkBudgetExceeded` and clears the hit. Each restored query
returns `Completed` with the exact hit. The grid scan charges vacant physical
slots, and membership enumeration prepays slots left by removed colliders.
Scratch and budgets are rebuilt by the host. The recorded body and collider
fields matched between the live and restored shells.

Independent review identified additional paths that need equivalence tests:
voxel partition lookup can charge retained empty payloads, linked-collider
lookup prepays dictionary capacity, and linear scratch deduplication depends
on encounter order. The existing closest-hit tie rule uses the collider ID.
Keep the host's logical registration order matched in these controls and test
equal-distance winners separately from budget work.

The Windows Release scale probe used one support call per 16 Hz host step
with the same floor geometry and an excluded 3D kinematic source. The grid
control adds distant nonintersecting rectangles at `X = 16 + 4*i` and
`Z = 16`, each one unit wide and deep, for 1, 64, or 512 total grids. The
membership control registers 1, 16, 64, or 256 coincident static floors on
one grid. Both paths found the first registered floor with the exact normal,
point, and distance. Every bounded row repeated the exact charged work and
cleared its hit at one below the limiting counter.

| Case | 2D work | 3D work |
| --- | ---: | ---: |
| 1 grid, 1 floor | 37/26/10 | 109/107/58 |
| 512 grids, 1 floor | 548/26/10 | 620/107/58 |
| 1 grid, 16 floors | 37/911/160 | 109/4,142/928 |
| 1 grid, 64 floors | 37/12,815/640 | 109/57,878/3,712 |
| 1 grid, 256 floors | 37/198,671/2,560 | 109/894,902/14,848 |

The grid scan adds exactly one discovery unit per remote grid in this input.
Increasing floors from 64 to 256 increases 3D candidate work 15.5 times for
four times the geometry work. Physical memberships repeat across voxels, and
each identity lookup can scan the previously retained candidate prefix. Both
layouts matter when redesigning the budgeted view.

Four fresh timing processes alternated synchronous and bounded query order
on Windows x64, i7-9700K, .NET 10.0.10, with tiered compilation disabled.
Each path measured seven alternating query-only and complete-step blocks of
32 calls, reduced to 16 at 64 floors and eight at 256. Each path warmed two
blocks of complete steps first. Ranges below are the four process medians in µs.

| Case | Sync call | Bounded call | Sync step | Bounded step |
| --- | ---: | ---: | ---: | ---: |
| 2D 512 grids | 67.4–69.5 | 71.3–71.9 | 71.5–72.3 | 75.2–77.1 |
| 3D 512 grids | 13.8–14.2 | 17.1–17.5 | 20.9–21.4 | 23.8–26.7 |
| 2D 256 floors | 16,902–17,242 | 17,105–17,505 | 17,007–17,434 | 17,310–17,901 |
| 3D 64 floors | 517–529 | 573–608 | 588–614 | 646–661 |
| 3D 256 floors | 2,074–2,128 | 3,051–3,168 | 2,329–2,360 | 3,303–3,390 |

All warm samples allocated 0 B on the calling thread. Coincident floors
stress duplicate memberships with one query per step. The measurements describe
that case. Raw logs, probes, and DLL hashes are in
`artifacts/query-budget-checkpoint` and `artifacts/query-budget-scale-checkpoint`.

Start with live-grid discovery in GridForge and a deterministic membership
view at Gravitas's existing publication boundary. Prefer the existing owner
and a dense logical order over storing runtime holes in checkpoints. Account
for changes at the publication barrier and measure its frame cost. Once the
raw traversal order and charged work are stable, compare bounded ways to
deduplicate identities based on measured dense-member cost. Keep construction,
lookup, and clearing within their respective measured boundaries.

### Phase 1 - Bounded support through the native queries

The prototype adds the bounded overloads to Gravitas `Query2D.QuerySupport`
and `Query3D.SweepSphere`. It supports pure dimensional worlds with dense
rectangular grids and box targets. Intersecting unsupported grids and eligible
unsupported shapes return an incomplete status. Both paths use the existing
native geometry and lowest-ID tie reducers. The 2D contract test initially
failed compilation on the missing public API. The new tests exercise exact
support, all three work limits one below, scratch exhaustion, and recovery
through the host step. GridForge separately checks exact and one-below voxel
capacity, overlapping grids, and non-waiting readiness.

GridForge initially uses a metered grid-slot scan, then the existing rectangle
range and planar filter. This keeps the box-floor slice small and makes vacant
slots, address lookups, partition probes, and retained-reference cleanup
visible. SwiftCollections exposes physical enumeration and lookup bounds
because a small number of live members can hide deleted slots or long probe
chains. Gravitas prepays those bounds and fixed box geometry stages. Candidate
deduplication scans only the used scratch prefix and charges its comparisons.
Review the slot scan and deduplication against many-grid and crowded workloads
before expanding the surface. Work units can include conservative fixed-kernel
precharges, and do not correspond to individual arithmetic operations.

Independent review identified and the prototype addressed uncharged hash-set
holes, partition lookup work, atomic scratch borrowing, and inconsistent status
on a stale view. Repeated success and exhaustion are deterministic for the same
prepared storage. A checkpoint can rebuild partition sets with different slot
high-water marks or dictionary capacities. Verify or canonicalize that boundary
before relying on tight bounded outcomes after restore in lockstep.

The coordinated source baseline is Trailblazer `a73f959`, Gravitas `cd1b8322`,
GridForge `269da48`, and SwiftCollections `08b1b38`, plus the working changes.
The new APIs are available only in coordinated source. Hosted pins and released
packages still validate the prior synchronous paths. Provider order for this
slice is SwiftCollections, GridForge, then Gravitas. Trailblazer's adapters have
no runtime dependency on the new overloads yet.

The bounded paths share scratch and work accounting. Each dimension owns its
partition admission and geometry. Runtime growth in this slice is 12 net lines in
SwiftCollections, 240 in GridForge, and 590 in Gravitas, including XML and
license headers. Tests and guides are separate. Existing synchronous entry
points continue to use their existing discovery and result semantics.

The Windows coordinated-source suites passed in both configurations. Gravitas
and its source dependencies also built for `netstandard2.1` and `net10.0`.
The five focused Gravitas tests passed on Ubuntu in each configuration.

| Windows suite | Release | ReleaseLean |
| --- | ---: | ---: |
| SwiftCollections core | 1,411 | 1,381 |
| GridForge | 1,182 | 1,182 |
| Gravitas | 8,505 | 8,433 |
| Trailblazer core | 3,780 | 3,667 |
| Trailblazer adapter | 117 | 110 |

The timing probe reused the Phase 0 open, wall, floor, and grid mutation
inputs in both dimensions. It compared synchronous and bounded calls on the
same Release source assemblies on Windows x64, .NET 10.0.10, with four fresh
processes in alternating query order. Each row warmed 128 steps, then measured
seven alternating 128-operation call and containing-step blocks per path.
Default tiering produced noisy 2D floor captures. The repeat below disables
tiering for both paths and is a within-run comparison. Values are the range of
the four process medians in µs. The containing step includes partition
maintenance, `Simulate`, `LateSimulate`, and one query.

| Scene | Sync call | Sync step | Bounded call | Bounded step | Discovery / candidates / geometry |
| --- | ---: | ---: | ---: | ---: | ---: |
| 2D open | 1.1–1.2 | 2.5–2.6 | 1.1–1.2 | 2.5–2.5 | 34/9/0 |
| 2D wall | 1.2–1.3 | 2.9–3.0 | 1.2–1.2 | 2.8–2.9 | 35/17/0 |
| 2D floor | 67.0–67.7 | 70.3–71.7 | 66.9–67.8 | 70.6–71.3 | 37/26/10 |
| 2D mutation | 66.9–68.1 | 84.3–85.4 | 67.0–67.4 | 84.7–86.6 | 37/26/10 |
| 3D open | 3.2–3.4 | 6.8–7.4 | 2.8–2.9 | 6.4–6.9 | 100/27/0 |
| 3D wall | 3.8–4.0 | 8.8–9.7 | 3.3–3.3 | 8.3–8.4 | 106/98/1 |
| 3D floor | 12.7–12.9 | 19.4–19.8 | 11.9–12.0 | 18.9–19.2 | 109/107/58 |
| 3D mutation | 12.8–13.0 | 57.2–58.8 | 11.8–12.0 | 58.0–59.3 | 109/107/58 |

Warm calls allocated 0 B, and mutation-containing steps retained their
synchronous allocation counts in both dimensions. The runtime and plan now
have enough evidence for the narrow Phase 1 proof. Before expanding, measure
many-grid and dense-member cases and verify checkpoint/rebuild budget outcomes.
Linux full-suite coverage, exact coverage gates, released packages, and
adapter adoption remain open. Logs, the disposable probe, and its binary
hashes are under `artifacts/query-budget-phase1`.

### 2026-11-14 - Phase 0 package baselines

Trailblazer `8fd7e2b` is the pre-change consumer. The hosted source workflow
passed with GridForge `269da48`, Gravitas `cd1b8322`, SwiftCollections `08b1b38`,
FixedMathSharp `e2c325a`, and Chronicler `7b90d59`. The measurements below use the
published GridForge `10.1.0`, Gravitas `2.0.1`, SwiftCollections `8.0.0`,
FixedMathSharp `8.0.0` and Chronicler `1.0.0` graphs. Loaded DLL hashes matched
their NuGet assets in both families. No new upstream API is present in those
packages or the hosted pins. [Hosted baseline](https://github.com/mrdav30/Trailblazer/actions/runs/43567530447)

The package-mode Trailblazer adapter fixtures passed 117 cases in `Release`
and 110 in `ReleaseLean`, including support, posture, and replay in both
dimensions. Filter on `GravitasNavigator2D|GravitasNavigator3D` using
`FullyQualifiedName` and `-p:UseLocalLsfStack=false`.

A disposable console probe adapted the existing support and posture fixture
patterns to exercise the released native entry points with identical logical
scenes. It used one kinematic capsule on a unit rectangular grid from -12 to 12
per active axis (Y = 0 in 2D), at 16 Hz with gravity zero and manual grounding.
Support starts one unit above the floor with radius 1/2 and downward reach 1.
The open and wall-only rows assert a miss. Floor rows assert distance 1/2 and an
upward normal. The full-step column includes `Simulate`, `LateSimulate`, then
the query or posture operation. Trailblazer host orchestration is covered by
the adapter fixtures and by the archived planar frame measurements.

- The floor spans 3 horizontally and is 1 thick, with its top at zero. The
  wall-only blocker has width 1/4 and is centered at horizontal 3/4. Crowded
  adds 64 radius-1/16 contacts at horizontal 3/4 and height 1/2. Compound uses
  the same floor plus a unit part 4 units to the side. The convex floor is a
  four-vertex polygon in 2D and a closed five-vertex pyramid in 3D. The mesh
  floor is the 3D pyramid's top face as two static surface triangles.
- Overlap adds a second dense grid from -2 to 2 on the active axes. Sparse
  materializes only the center cell of that secondary grid. Mutation alternates
  adding/removing a distant unit grid before the full step, so the isolated call
  column measures the same floor without an intervening topology change. The
  rollover control uses the crowded scene and a prebound setter to force the
  existing collider stamp to `uint.MaxValue` before each call.
- Posture uses a capsule at zero, radius 1/2, alternating heights 1 and 2 for
  clear attempts. Both rows have 64 remote registered colliders at horizontal
  4 and height 1/2. The blocked row repeats a growth attempt into a height-1/2
  ceiling centered at height 1. The probe asserts `Applied` or `Blocked` each
  time. The calls include full native transactions, including detached-candidate
  preparation and any publication; their bytes are not clearance-only bytes.

Windows x64, i7-9700K, SDK 10.0.302, .NET 10.0.10. Two fresh processes per
package family, 128 warm steps per row, then seven alternating blocks of 128
operations per column. Timing cells give the range of the four process medians
in microseconds. B gives managed bytes per call/full step, with the same counts
in both families. This is a matched workload baseline. The 2D floor warm blocks
had outliers, and posture full-step medians varied between processes. Repeat
paired captures before attributing time deltas to the change. Full outputs,
probe source, dependency graphs and hashes are in `artifacts/query-budget-phase0`.

| Scene | 2D call µs | 2D step µs | 2D B | 3D call µs | 3D step µs | 3D B |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Open | 6.9–7.3 | 14.8–15.4 | 0/0 | 9.3–10.6 | 17.3–21.5 | 0/0 |
| Wall | 7.0–7.1 | 16.9–17.5 | 0/0 | 11.6–12.7 | 22.5–26.9 | 0/0 |
| Floor | 74.0–80.6 | 89.3–99.7 | 0/0 | 22.4–23.6 | 36.1–41.6 | 0/0 |
| Crowded | 64.6–69.1 | 119.0–149.7 | 0/0 | 78.3–83.1 | 179.5–232.0 | 0/0 |
| Compound | 114.6–118.1 | 128.2–133.5 | 0/0 | 28.0–30.4 | 44.7–47.0 | 0/0 |
| Convex | 60.5–64.2 | 70.1–78.5 | 0/0 | 93.5–99.3 | 114.1–117.4 | 0/0 |
| Overlap | 64.2–69.1 | 74.6–79.7 | 0/0 | 29.5–32.8 | 44.1–46.1 | 0/0 |
| Sparse | 63.4–66.6 | 73.9–78.7 | 0/0 | 20.1–23.1 | 35.4–37.1 | 0/0 |
| Mutation | 62.0–67.3 | 114.1–129.3 | 0/596 | 19.7–22.6 | 135.5–146.1 | 0/784 |
| Rollover | 61.4–68.8 | 83.9–150.2 | 0/0 | 69.2–82.7 | 170.1–199.0 | 0/0 |
| Posture clear | 18.4–26.9 | 38.0–99.7 | 1120/1120 | 32.5–38.2 | 117.7–165.6 | 1424/1424 |
| Posture blocked | 10.2–12.7 | 28.6–84.9 | 1080/1080 | 45.6–46.2 | 132.7–177.6 | 1384/1384 |
| Mesh | — | — | — | 50.7–53.6 | 69.8–71.6 | 0/0 |

### 2026-11-13 - Post-release stack and native consumer review

The released baseline is Chronicler `1.0.0`, FixedMathSharp `8.0.0`,
SwiftCollections `8.0.0`, GridForge `10.0.0` and Gravitas `2.0.0`.
Trailblazer's hosted source matrix passes with GridForge `269da48` and Gravitas
`cd1b8322`, the candidates for `10.1.0` and `2.0.1`. The Phase 3 package
matrix was pending at this checkpoint. Matched query-budget baselines and
implementation are still open. [Pinned-source CI](https://github.com/mrdav30/Trailblazer/actions/runs/43547586342)

### 2026-09-24 - Scope separated from native 2D delivery

The maintainer requested shared 2D/3D scope after confirming that existing 3D
support uses an indexed synchronous query without a hard work budget. The
2D plan now retains native support/posture correctness and uses existing discovery.
This follow-up owns the stronger resource-control guarantee; no runtime changes,
new test results or performance improvements are claimed by capturing it.

The [earlier support recheck](twoDimensionalNavigationPlan.md#2026-09-24---support-prerequisite-recheck)
retains source revisions and the 20,172-case 2D enumeration comparison. Those
results are discovery-reuse evidence, not a complete bounded query, a 3D proof
or a measured performance requirement. Rebaseline both dimensions in Phase 0.
