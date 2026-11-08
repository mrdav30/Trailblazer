# Bounded Physics Queries — 2D And 3D Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` or
> `superpowers:subagent-driven-development` when implementation is requested.
> Complete the common contract and baseline review before runtime changes. Use
> independent correctness/simplicity review and the checkboxes below.

**Created:** 2026-09-24

**Status:** Queued scope captured for review; implementation has not started. This is
a separate follow-up to [first-class 2D navigation](twoDimensionalNavigationPlan.md),
not a prerequisite for its support queries or posture replacement.
Execution and release sequencing require an explicit decision when taking this
plan up; capturing it assigns no release milestone or release-policy exception.

**Goal:** Give both 2D and 3D physics consumers explicit, deterministic query
work/storage limits without losing complete-result semantics or changing the
existing synchronous query behavior.

**Architecture:** Gravitas owns physics-query budgets, geometry accounting and
query results. GridForge owns bounded discovery over its existing world/index;
SwiftCollections changes only if its existing spatial primitives cannot provide
that boundary. Trailblazer's optional adapter consumes the result. Share resource
accounting where valid, not geometry or a generic arbitrary-dimension framework.

**Tech Stack:** C# 11; `netstandard2.1` and `net8.0`; FixedMathSharp; GridForge;
SwiftCollections; Gravitas; existing xUnit and benchmark projects.

**Spec:** The agreed scope and contract requirements below capture this work.
Phase 0 must settle exact public signatures, work units and host failure policy
before implementation; the former 2D-only budget declarations are not approved
shared API names.

## Why this is separate

Today's 3D adapter calls `Query3D.SweepSphere`, which discovers covered voxels
and inspects collider memberships synchronously. Existing 2D circle queries also
use spatially indexed discovery. Neither provides a caller-set ceiling on all
inspected work. That is not inherently a correctness or lockstep defect: finite,
scene-dependent work can still produce deterministic results.

The 2D proposal added a stronger guarantee: explicit limits across discovery,
membership visits and geometry, with no storage growth and an explicit incomplete
outcome. That guarantee is useful to investigate for **both** dimensions. It is
not necessary merely to introduce a first-class 2D controller, and no measured
support-query performance defect has been established by the existing probes.

Keep this plan as the single cross-stack coordination record. Do not create an
issue ticket for an unimplemented feature or duplicate the plan in each repo.

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

The covered-address cursor bounds enumeration after eligible generations are
supplied; it does not supply bounded discovery. A navigation baseline allocates,
and an output candidate limit does not bound inspected work. Investigate one
GridForge-owned bounded discovery/bind boundary over the existing spatial index.
The current candidate is bounded, stamp-free spatial-hash membership access plus
existing BVH nodes with bounded caller scratch; finalize it only after checking
both dimensional consumers. Avoid an unconditional all-grid/all-collider scan.

Preserve one exact world spawn/version/change-sequence stamp through discovery,
binding and completion. Individually current generations cannot prove a newly
inserted grid was included. Preserve checked discovery expansion, the relevant
dimension's partition padding and separate physical-presence checks. Validate
sparse changes, grid replacement, world reset and reused registrations.

Gravitas must sample a current index at its documented host barrier. Account for
deferred callback refresh and grid/sparse reconciliation; an empty deferred set
alone is not proof of readiness. Specify where maintenance happens and include
it in full-frame measurements. A perpetually unready query is not a solution.

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

Paths below are relative to the named repository; abbreviated entries share the
preceding `src/<library>/` or `tests/<project>/` prefix. New public types get
separate files. Recheck source before execution because 2D work precedes this plan.

| Owner | Existing source / test entry points |
| --- | --- |
| SwiftCollections | `src/SwiftCollections/Query/SpatialHash/SwiftSpatialHash.cs`, `Query/BoundingVolume/SwiftBVH.cs`; corresponding collection tests |
| GridForge | `src/GridForge/Grids/Managers/GridWorld.cs`, `Grids/Managers/GridSpatialIndex.cs`, `Grids/Topology/GridCoveredAddressCursor.cs`; world/index/covered-address tests |
| Gravitas 2D | `src/Gravitas/Queries/2D/GravitasQuery2DService.Sweep.cs`, `Queries/2D/QueryDetection2D.cs`, `Core/2D/GravitasCollision2DService.cs`; `tests/Gravitas.Tests/Physics2D`, including sweep witnesses and host contracts |
| Gravitas 3D | `src/Gravitas/Queries/3D/GravitasQuery3DService.Raycast.Traversal.cs`, `Core/3D/SolidBody.ColliderReconfiguration.cs`; `tests/Gravitas.Tests/Queries/GravitasQuery3DServiceSweepTests.cs`, `Core/SolidBodyColliderReconfigurationTests.cs` |
| Trailblazer adapter | `src/Trailblazer.Gravitas/GravitasContactTranslator.cs`, `src/Trailblazer.Gravitas/GravitasNavigator3D.cs`, future native 2D counterparts; `tests/Trailblazer.Gravitas.Tests` |

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

- [ ] Read owning repo instructions, query/lifecycle docs and the completed 2D
  support/posture contracts. Inventory variable work/storage in both dimensions.
- [ ] Record exact common budget/work/status/scratch signatures and units,
  dimensional request/result types, readiness ownership, failure policy and
  proposed source files. Share accounting, not dimension-specific geometry.
- [ ] Capture matched support and posture controls using existing fixtures:
  open/floor/wall, crowded cells, compound/convex geometry, sparse and overlapping
  grids, mutation and query-stamp rollover. Include 3D mesh cases where supported.
  Retain inputs, source/binary identities, allocations and full-frame timings.
- [ ] Define semantic equivalence checks, exact/one-below accounting cases and
  measured overhead acceptance before implementation. No arbitrary speedup
  target: bounded worst-case work may trade throughput for predictability.
- [ ] Review the contract and proceed to implementation. Existing queries being
  fast does not disprove the feature's value; do not declare success or a no-go
  without testing the implemented bounded behavior against the same controls.

## Phase 1 - Shared Bounded Discovery

- [ ] Write failing owning tests for capped hash-cell/membership and BVH-node
  visits, duplicate/rejected candidates, scratch limits and rollover behavior.
  Add SwiftCollections access only where existing APIs cannot meet the contract.
- [ ] Implement GridForge's discovery/bind operation with checked bounds and one
  world stamp. Test insertion between discovery/bind, replacement, reset, sparse
  presence, dense storage, rectangular/both hex topologies, seams and tiny metrics.
- [ ] Prove exact/one-below allowances and capacities, cleanup after every failure,
  warmed no-growth allocation behavior, and complete discovery equivalence for
  2D and 3D. Run unchanged index/query controls before reviewing this boundary.

## Phase 2 - Native Bounded Queries In Both Dimensions

- [ ] Add failing support/clearance tests for each dimension before implementing
  its accounting. Exercise accepted/rejected filters and compound parts,
  initial contact, narrow scalar margins and large convex/mesh inputs.
- [ ] Reuse the existing narrowphase and stable reductions. Meter or conservatively
  precharge every variable kernel, sorting and cleanup path; test counter
  overflow and a budget exhausted immediately before the winning candidate.
- [ ] Make readiness and failure outcomes explicit. Test recurrent callback
  mutations, grid changes, stale registrations and reentrancy without relying
  on an unrelated query to repair the index.
- [ ] Compare complete bounded results against the corresponding synchronous
  operations in both dimensions. Preserve existing public controls and verify
  no usable result escapes an incomplete query. Review both implementations;
  a completed 2D path alone does not finish this phase.

## Phase 3 - Adapter Adoption And Closeout

- [ ] Integrate the reviewed explicit policy in 2D and 3D adapters, preserving
  existing caller behavior. Test repeated incomplete support, recovery, moving
  carriers, gravity and all-prepare/one-resolve/all-commit ownership.
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

## Execution record

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
