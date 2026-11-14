# Bounded Physics Queries in 2D and 3D

Phase 0 defines the shared contract and released-package baselines. Runtime work
starts in Phase 1 and is planned before the next Trailblazer release. The
[first-class 2D navigation plan](done/twoDimensionalNavigationPlan.md)
has its own Phase 3 package verification and already exercises synchronous
support and posture in real physics. This plan owns the opt-in work and storage
limits for those operations in both dimensions.

## Current baseline

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
- Use coordinated source validation for unreleased dependencies, then the
  [released-chain gates](feature-work-overview.md#coordinated-release-gates).
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

Posture clearance can start from its existing registered-collider scan, with
every visit charged and lookup order preserved. Consider indexed discovery for
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
and `UnsupportedRuntimeMode`. `WorldNotReady` means no stable initial view
could be sampled. `Stale` means a view was acquired and later invalidated.
Programmer errors such as null scratch, invalid geometry arguments, or an
already borrowed scratch throw before query work. Validate the mode and initial
readiness before traversing. During traversal, charge each inspection before
attempting it and check room before writing. If an inspection fits but its
result cannot be retained, return the storage failure with that inspection
charged. Check freshness before publishing a completed result.

Use a caller-owned `sealed class PhysicsQueryScratch` with constructor
`PhysicsQueryScratch(int gridCapacity, int traversalCapacity, int candidateCapacity)`
and read-only properties of the same names in PascalCase. Capacities are
nonnegative entry counts for generation slots, pending traversal entries, and
unique collider identities respectively. The scratch owns fixed backing storage
allocated before use. Hash probing, if used, must have a checked inspection
limit. Clear only used entries and account for that cleanup by reserving its
work when retaining an entry. Use a single exclusive borrow per synchronous
call and release on all exits. Reuse sequentially across queries and worlds
after cleanup. Scratch has no durable world ownership or serialized identity.
Posture can use zero discovery capacities while it scans registered colliders.
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
adapter's scratch reference. Both are between-frame changes. Expose nullable
`LastSupportQueryExecutionStatus` and `LastSupportQueryWork` on the adapters so
hosts can distinguish no bounded probe this frame from a completed miss or an
incomplete sample. Reset these diagnostics on reset, load, configuration change,
and frames without a bounded probe. In 3D, `SupportStatus` exposes the sample
separately from grounded state. Add `NavigationSupportStatus3D` with the same
values as `NavigationSupportStatus2D`, and keep the 2D API. Use `NotApplicable`
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
grid, through indexed discovery and one checked candidate and support result.
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

Require exact equality with the existing operation for complete results,
including eligibility, winning identity, tie order, fixed-point witness and
distance, and posture result and publication counts. With the reported work
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
  or authorize posture growth (Phases 2–3).
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

## Phase 1 - Shared Bounded Discovery

- [ ] Write failing owning tests for capped hash-cell/membership and BVH-node
  visits, duplicate/rejected candidates, scratch limits and rollover behavior.
  Add SwiftCollections access only where existing APIs cannot meet the contract.
- [ ] Reuse current GridForge discovery and binding where the contract permits.
  Introduce only the bounded operations exercised by the Gravitas query slice.
  Test insertion between discovery/bind, replacement, reset, sparse presence,
  dense storage, rectangular/both hex topologies, seams and tiny metrics.
- [ ] Prove exact/one-below allowances and capacities, cleanup after every failure,
  warmed no-growth allocation behavior, and complete discovery equivalence for
  2D and 3D. Verify the first production slice for exact results, warm allocations
  and containing-frame time, then obtain independent review before expanding it.

## Phase 2 - Native Bounded Queries In Both Dimensions

- [ ] Add failing support/clearance tests for each dimension before implementing
  its accounting. Exercise accepted/rejected filters and compound parts,
  initial contact, narrow scalar margins and large convex/mesh inputs.
- [ ] Reuse the existing narrowphase and stable reductions. Meter or conservatively
  precharge every variable kernel, sorting and cleanup path; test counter
  overflow and a budget exhausted immediately before the winning candidate.
- [ ] Make readiness and failure outcomes explicit. Test recurrent callback
  mutations, grid changes, stale registrations, unpartitioned posture blockers
  and reentrancy. Verify recovery through the normal host lifecycle.
- [ ] Compare complete bounded results against the corresponding synchronous
  operations in both dimensions. Preserve existing public controls and verify
  no usable result escapes an incomplete query. Review both implementations;
  a completed 2D path alone does not finish this phase.

## Phase 3 - Adapter Adoption And Closeout

- [ ] Integrate the reviewed explicit policy in 2D and 3D adapters, preserving
  existing caller behavior. Test repeated incomplete support, recovery, moving
  carriers, gravity and all-prepare/one-resolve/all-commit ownership. Report
  actor count and query counts per frame alongside per-call budgets.
- [ ] Exercise bounded posture clearance at exact/one-below limits. Rejection
  leaves all physical/navigation state and publication counts unchanged;
  acceptance and notification failures retain atomic transaction semantics.
- [ ] Repeat matched pre-change controls, allocation/replay tests and complete
  containing-frame measurements, including maintenance and readiness latency.
  Report overhead and incomplete-query rates as well as any saved work.
- [ ] Run owning and downstream Windows/Linux Release/ReleaseLean suites, both
  library targets, exact introduced-path coverage and DocFX. Verify source-stack
  binary provenance; keep final released-package verification separate.
- [ ] Document current public behavior in XML and DocFX guides with verified
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
2. If the implemented bounded path needs additional APIs, release their owning
   packages in dependency order.
3. Before publishing Trailblazer, validate the combined native 2D and budgeted
   adapter behavior against the final released Standard and Lean chains.
   Review versioning when the budget API is settled.

## Execution record

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

The [earlier support recheck](done/twoDimensionalNavigationPlan.md#2026-09-24---support-prerequisite-recheck)
retains source revisions and the 20,172-case 2D enumeration comparison. Those
results are discovery-reuse evidence, not a complete bounded query, a 3D proof
or a measured performance requirement. Rebaseline both dimensions in Phase 0.
