# Bounded Physics Queries in 2D and 3D

Implementation has yet to start. The target is to complete this work before the
next Trailblazer release. The [first-class 2D navigation plan](done/twoDimensionalNavigationPlan.md)
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
  registered collider set. These are existing synchronous costs. The previous
  probes established enumeration reuse, and Phase 0 still needs measured
  support and posture baselines.

## Ownership

Gravitas owns physics-query accounting, readiness, native geometry and results.
GridForge owns any new bounded world discovery required by an implemented query.
SwiftCollections owns any missing reusable spatial primitive, and FixedMathSharp
remains the owner of deterministic geometry shared across libraries. The optional
Trailblazer adapter owns the host policy for consuming results. Reuse existing
accounting where contracts match and keep geometry in its dimensional owner.

The current library targets are `netstandard2.1` (C# 11) and `net10.0` (C# 14).
Phase 0 will settle the public signatures, work units, scratch ownership and host
failure policy using the current code and existing fixtures.

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
These are semantic outcomes; exact shared type names are a Phase 0 deliverable.

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
drain deferred 2D updates. Phase 0 must identify what the bounded query charges
and where any separate maintenance runs. Include both costs in full-frame
measurements and verify that the normal host step restores readiness.

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

Before adapter adoption, define one explicit policy for incomplete support in
both dimensions. It must not masquerade as `NoSupport`, certify a stale carrier,
freeze gravity/platform/commit work, or retry without a limit inside the same
frame. Include repeated exhaustion and recovery. Incomplete posture clearance
must leave body, collider, host pose and navigation state unchanged and must
not invoke the synchronized-state publisher. Complete accepted transactions
retain the existing post-commit notification-failure rules.

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

- [ ] Trace both production support and posture paths, partition reconciliation,
  scratch lifetime and geometry costs against the current stack. Identify which
  work can be bounded by existing APIs and which needs a new upstream entry point.
  Record source, hosted pin and package readiness separately for each dependency.
- [ ] Record exact common budget/work/status/scratch signatures and units,
  dimensional request/result types, readiness ownership, failure policy and the
  intended production callers. Decide how 3D distinguishes incomplete support
  and how both posture APIs expose exhaustion while retaining atomicity.
- [ ] Capture matched support and posture controls using existing fixtures:
  open/floor/wall, crowded cells, compound/convex geometry, sparse and overlapping
  grids, mutation and query-stamp rollover. Include 3D mesh cases where supported.
  Retain inputs, source/binary identities, allocations and full-frame timings.
- [ ] Define semantic equivalence checks, exact/one-below accounting cases and
  measured overhead acceptance before implementation. No arbitrary speedup
  target: bounded worst-case work may trade throughput for predictability.
- [ ] Review the contract and choose a small production slice that reaches the
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
