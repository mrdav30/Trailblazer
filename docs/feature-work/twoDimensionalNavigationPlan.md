# First-Class 2D Navigation Implementation Plan

> **For agentic workers:** Use `superpowers:subagent-driven-development` or
> `superpowers:executing-plans` when execution is requested. Complete the contract
> review before implementing runtime types. Use independent reviewers and the
> checkboxes below. The approved upstream clearance slice is implemented;
> native controller implementation still requires completion of Phase 0.

**Created:** 2026-09-03

**Status:** Phase 0 is in progress after the completed 3D integration and accepted
performance hardening. Exact planar capsule geometry and layer-bound GridForge
coverage are implemented upstream. No native Trailblazer 2D controller exists yet.
Upstream releases remain deferred; Phase 1 requires review of the contract below.
Hard-budgeted physics queries are tracked separately for both 2D and 3D in the
[physics query budget plan](physicsQueryBudgetPlan.md); they do not block this plan.

**Current prerequisite blocker (2026-09-24):** Native support and posture work is
in progress, not ready to merge. Independent review reproduced strict-clearance
failure in both dimensions: genuinely positive penetration can round to zero
contact depth and be admitted. Gravitas `GRV-Issue-081` owns the evidence. Exact
FixedMathSharp-owned classification is required before declaring posture complete;
the contract will not be weakened to a rounded-depth test or an epsilon.
The shared FixedMathSharp repair was authorized on 2026-09-24, with explicit
determinism and performance requirements. Work includes classification-only
predicates and measured unchanged-contact controls. Review also reproduced a
pre-existing finite cylinder/capsule rim-classification defect, now owned by
FixedMathSharp `FMS-Issue-023`; passing the simple strict predicates does not
certify the remaining 3D curved-shape families.

**Original draft baselines:** Trailblazer `cb0d744b76a377807a253f0efa1c480ba169a7e9`;
Gravitas `6ade2f0bb373665efc5b7b85a3d21d1a371aa936`. The dated execution record
below identifies the revisions actually used for the current preflight.

**Goal:** Provide first-class deterministic 2D navigation and character control
for top-down and side-view movement, with a Gravitas `SolidBody2D` integration
and no regression to the existing 3D API.

**Architecture:** Reuse dimension-independent search, publication, dependency,
budget, and action-lifecycle machinery where its semantics remain valid. Give
planar geometry, support, movement, and facing explicit contracts rather than
silently projecting the world-Y-up motor into another plane. Keep physics in the
host and the Gravitas-specific bridge in the optional adapter package.

**Tech Stack:** C# 11; `netstandard2.1` and `net8.0`; FixedMathSharp `Vector2d`
and fixed-point scalar rotation; GridForge; existing A*/Flow services; Chronicler;
xUnit v3; Gravitas `SolidBody2D` for joint acceptance.

**Spec:** [Agreed scope](#agreed-scope) and [Dimensional contract](#dimensional-contract)
capture the feature boundary. Phase 0 must record approved exact public
signatures and file responsibilities before production implementation.

## Agreed scope

- Native planar position, velocity, movement intent, body clearance, and
  rotation/facing at the controller boundary.
- Top-down free movement and side-view locomotion with gravity inside the
  simulation plane. Finishing only top-down movement does not finish this plan.
- A* and Flow guidance, explicit action completion, invalidation, occupancy,
  safe profile replacement, persistence, and deterministic replay for 2D agents.
- A `SolidBody2D` adapter extension built on the ownership lessons from the
  [Gravitas integration plan](done/gravitasIntegrationPlan.md).
- Executable headless behavioral tests and readable integration guidance, not
  hollow API checks or a 3D scene viewed by an orthographic camera. No standalone
  sample application or new scenario framework is required. Engine-host visual
  playtesting remains deferred until engine-specific adapter work.

This is separate from [3D navigation hardening](done/navigationHardeningPlan.md).
The 3D Gravitas integration and hardening profile-replacement contract are now
complete locally. Reuse their proven ownership and replacement rules, not their
world-Y arithmetic. A fixed-height top-down projection can inform an early experiment but is
not the deliverable or a permanent parallel public compatibility API.

## Global constraints

- Preserve deterministic fixed-point math, stable ordering, finite navigation
  work budgets, exact guide dependencies, and per-acquisition action/cursor
  ownership.
- Reuse synchronous indexed physics discovery for recurring support checks.
  Rare explicit posture transactions retain complete registry clearance, matching
  3D so blockers outside physics grids cannot be silently omitted.
  Hard per-query physics work/storage ceilings belong to the separate 2D/3D
  plan; this does not weaken existing navigation or planar-coverage budgets.
- Carry forward Immediate/Scheduled acquisition, bounded shared-work scheduling,
  safe waiting, cancellation, moved-origin adoption, and optional deterministic
  LOS phases. Planar geometry must certify its own endpoints, rays, and connector
  proofs; sharing control flow does not authorize reuse of 3D geometric proof.
- Preserve the existing 3D contract. Do not rename it or add forwarding aliases
  merely to make a new naming scheme symmetrical.
- Review every shared behavior or improvement against both 2D and 3D. Retain
  explicit geometry-specific differences, and record whether each counterpart
  is already equivalent, is fixed together, or needs separately approved work.
  Parity means equivalent guarantees, not forced API or implementation symmetry.
- Keep core engine-agnostic and Gravitas-independent. Rendering, sprite facing,
  physical contacts, and semantic action execution remain explicit host concerns.
- Reuse proven dimension-independent machinery. Do not copy the entire search
  stack or build a generic arbitrary-dimension framework without demonstrated need.
- Keep planar clearance and physics consistent. A navigation envelope is not a
  replacement for an actual host collider, nor proof that a rendered shape fits.
- Retain exact reachable line/branch/method coverage in Release and ReleaseLean
  for new runtime paths. Use bounded behavioral sequences, not hollow API tests.
- Preserve transactional populate-existing-instance serialization; support JSON
  in both package families and MemoryPack in standard Release.
- Record pre-existing or out-of-scope defects in [issue-tracker.md](issue-tracker.md).
  Regressions introduced by this feature remain here until resolved. Record
  reproducible out-of-scope performance concerns in the
  [benchmark backlog](benchmark-signal-hardening-backlog.md); do not reopen the
  accepted TRB-Benchmark-005 stopping point without its stated evidence.
- Do not stage, commit, publish, or change sibling repositories without the
  corresponding authorization. This plan records scope, not release readiness.

## Dimensional contract

Gravitas's existing 2D domain is X/Z: `Vector2d.X` maps to world X and
`Vector2d.Y` maps to world Z. World Y is outside pure planar collision; in mixed
physics it describes embedding. This plan follows that integration convention,
not an implicit XY convention borrowed from an engine.

| Concern | Top-down 2D | Side-view 2D |
| --- | --- | --- |
| Motion plane | X/Z with no out-of-plane movement | The same X/Z plane |
| Gravity/support | Free planar motion; no invented world-Y ground | Fixed in-plane gravity and its opposing support direction |
| Gameplay vertical | Not needed for ordinary movement | Planar second axis for the initial contract; world Z at the host boundary |
| Facing | Planar heading/scalar rotation | Facing and locomotion support axis are distinct; sprite mirroring is presentation |
| Clearance | Planar occupied footprint | Body width/height and foot/support anchor within the plane |
| Navigation actions | Explicit authored transitions where required | Explicit jumps, drops, ladders, and other supported actions; no implicit jumping across gaps |

Begin side-view acceptance with one fixed in-plane gravity direction. Changing
gravity direction at runtime, arbitrary curved-world support frames, mixed
2D/3D traversal, equipment systems, and dynamic rolling-body control are not
requirements of this first increment. Any need for them requires a separate
scope decision rather than speculative generalization.

The current graph's Solid foot anchors and step/drop admission use world Y.
Consequently the implementation must explicitly decide how planar support,
clearance, and semantic connectivity are represented and certified. Reusing
GridForge's spatial index does not make its world-Y prism height equivalent to
side-view gameplay height. Do not rotate physical coordinates behind the shared
world's back or reuse a 3D traversal proof whose geometry means something else.

## Current code and test map

Review these existing contracts before selecting the 2D files:

- [Navigator.cs](../../src/Trailblazer/Navigation/Navigator/Navigator.cs) and
  [INavigate.cs](../../src/Trailblazer/Navigation/Navigator/INavigate.cs): current
  Vector3d/quaternion state, Y foot offset, frame lifecycle, and committed cells.
- [KinematicBodyShape.cs](../../src/Trailblazer/Pathing/Query/KinematicBodyShape.cs),
  [NavigationAgentProfile.cs](../../src/Trailblazer/Pathing/Query/NavigationAgentProfile.cs),
  and [TraversalEvaluator.cs](../../src/Trailblazer/Pathing/Graph/TraversalEvaluator.cs):
  authoritative body dimensions, capability admission, and vertical leg proof.
- [Motor](../../src/Trailblazer/Navigation/Motor),
  [steering](../../src/Trailblazer/Navigation/Steering), and
  [turning](../../src/Trailblazer/Navigation/Turning): separate dimension-neutral
  control rules from horizontal/vertical and quaternion assumptions.
- [Search](../../src/Trailblazer/Pathing/Search),
  [graph tests](../../tests/Trailblazer.Tests/Pathing/Graph), and
  [navigation tests](../../tests/Trailblazer.Tests/Navigation): preserve existing
  costs, stale handling, transition ownership, and 3D regression coverage.
- [Serialization guide](../wiki/Serialization.md) and
  [NavigationAgentProfileRecord.cs](../../src/Trailblazer/Navigation/Serialization/NavigationAgentProfileRecord.cs):
  dimensional identity and staged population cannot be inferred from a shell.

In Gravitas, read `AGENTS.md`, `docs/wiki/DIMENSIONS.md`,
`docs/wiki/HOST_INTEGRATION.md`, `src/Gravitas/Core/2D/SolidBody2D.cs`,
`src/Gravitas/Core/2D/SolidBody2D.Grounding.cs`, and
`tests/Gravitas.Tests/Physics2D/SolidBody2DHostContractTests.cs`.

Planned responsibility boundaries, not files to scaffold ahead of the contract:

| Area | Deliverable |
| --- | --- |
| Trailblazer navigation/controller source | Explicit 2D controller, motion, support, and facing contracts; only genuinely shared internal helpers |
| Trailblazer query/graph source | Planar body/profile and traversal proof integrated with shared search machinery |
| Matching core test areas | Native 2D acceptance, graph semantics, replay, serialization, and preserved 3D behavior |
| Optional Gravitas adapter and joint tests | `SolidBody2D` target/accepted-pose bridge and native planar collision scenarios |
| Wiki, API documentation, behavioral tests | Top-down/platformer guidance linked to tested host fixtures, with explicit coordinates and ownership; no separate sample project |

The upstream contract below supplies the concrete prerequisite file/API boundary.
Phase 0 must still finish the native controller, map and record signatures and
their acceptance proofs before Phase 1. Section 1 is implemented in the local
source stack; sections 2 and 3 remain proposed, not current APIs.

## Proposed upstream prerequisite contract

These prerequisite boundaries are grounded in the
[bounded-primitive investigation](#2026-09-21---bounded-planar-primitive-investigation).
Keep the existing 3D APIs and behavior intact. Reuse graph/search ownership,
GridForge's exact prism/address machinery, and Gravitas's collider identity;
do not rotate 2D inputs into the 3D navigation controller.

### 1. Exact planar clearance: FixedMathSharp, then GridForge

**Implemented locally:** native exact strict-swept and closed-stationary capsule
relations in FixedMathSharp, consumed by one layer-bound GridForge body trace. Reuse existing
trace result/evidence types and scratch where their semantics match. Rejected
alternatives are a copied Trailblazer geometry solver, radius-expanded boxes
(false rejection at rounded corners), and two end-circle sweeps (misses against
the capsule's middle). Closed first-contact distance is not a strict-overlap
predicate: both exact tangency and initial penetration can return zero.

Implemented signatures, **declarations rather than a runnable host example**:

```csharp
// FixedMathSharp.Geometry.FixedConvex2dRelations
public static bool IntersectsSweptUprightCapsuleStrict(
    Vector2d startCenter, Vector2d endCenter,
    Fixed64 axisLength, Fixed64 radius,
    Vector2d convexOrigin, ReadOnlySpan<Vector2d> convexVertexOffsets);

// Exact touch-inclusive endpoint admission, sharing the same wide kernel.
public static bool IntersectsUprightCapsule(
    Vector2d center, Fixed64 axisLength, Fixed64 radius,
    Vector2d convexOrigin, ReadOnlySpan<Vector2d> convexVertexOffsets);

// GridForge.Utility.GridTracer
public static GridNavigationBodyTraceReport TracePlanarNavigationBodyInto(
    GridWorld world, ReadOnlySpan<GridPlanarLayer> eligibleLayers,
    Fixed64 embeddingY, WorldVoxelIndex source, WorldVoxelIndex target,
    Vector2d startCenter, Vector2d endCenter,
    Fixed64 axisLength, Fixed64 radius,
    SwiftList<GridNavigationBodyTraceCell> results,
    GridNavigationBodyTraceScratch scratch,
    int gridCandidateLimit, int addressCandidateLimit,
    int outputLimit, long candidateWorkLimit);
```

`GridPlanarLayer` is an immutable pair of `GridCoveredAddressGeneration
Generation` and `int LayerIndex`, with a validating constructor. Entries are
strictly ordered by durable configuration identity, one selected layer per
generation. Source/target must belong to those exact selected generations and
layers. Every selected layer must contain the declared embedding Y; topology
layer height is **not** planar character height. The native adapter's preserved
host world Y is independent of this navigation embedding in pure TwoD physics.

The math relation uses a capsule axis parallel to `Vector2d.Forward`; axis
length zero is a circle. Total height is `axisLength + 2*radius`. Reject negative
dimensions and fewer than three polygon vertices. As in existing convex APIs,
boundary-ordered convexity is an authoring precondition; repeated/collinear
edges are allowed and a wholly collinear polygon has no interior. For the strict
sweep, boundary-only contact returns false; strict overlap at either endpoint
or anywhere between returns true. The stationary predicate includes exact
boundary contact. Zero radius
uses strict footprint entry by the swept axis, not positive capsule area.
Use wide existing math internally: do not narrow conceptual half-axis endpoints,
normalize a rounded travel vector, or infer penetration by comparing rounded
first-hit parameters. Odd-raw axis lengths and full-domain chords are explicit
tests, not reasons to silently change the shape.

The GridForge operation traces **one ordinary planar graph leg**: the same
footprint or one immediate neighbor in the source planar lattice, including a
congruent neighbor in another eligible grid. It is not an arbitrary long-chord
operation. Longer navigation rays retain selected-edge progression and prove
each leg. Rectangular diagonal legs retain both side-cell closure requirements;
hex legs retain their planar neighbor closure. Compare planar footprints here,
not 3D prism heights; selected layers still have to contain the embedding Y.

Require **positive radius** at the GridForge boundary. A zero-radius axis can
leave a grid along cell boundaries without entering any neighbor's strict
interior; the positive-overlap closure below cannot certify that degenerate
body. This restriction does not change the math relation's zero-radius behavior.
Even an adjacent leg can sweep a body spanning many cells: certify its **entire
swept footprint union**, not just the endpoint cells or intersecting addresses.
The trace's responsibilities are:

1. Check bounds arithmetic; fail with `ArithmeticOverflow`, never saturated
   bounds or a smaller body. Validate source/target closed contact separately
   from strict cell-interior coverage.
2. Enumerate only eligible layers and preserve missing sparse addresses.
   Canonical ordering and exact generation/run stamps remain mandatory.
3. Close every positive-overlap planar lattice neighbor, including virtual
   neighbors outside a grid's bounds. A missing neighbor must fail coverage;
   a completed address cursor alone cannot certify an outer boundary or hole.
   Reuse the existing congruent-cell union rule across aligned grids. Arbitrary
   overlapping incompatible lattices do not gain a new success path.
4. Keep selected physical cells and missing coincident alternatives as distinct
   evidence, following `RequiredCoverage` and `PhysicalAlternativeDependency`.
   A physically coincident alternative cannot bypass Trailblazer map-domain or
   semantic admission. Any overlapping unselected navigation domain stays out.
5. Preserve the report's units: `CandidateWorkCount` is exactly grid plus
   address candidate work, **not** an exact total-work counter. Charge before
   each generation/address inspection, including eligible-layer validation;
   do not scan the entire supplied span before applying its ceiling. Closure,
   congruent-alternative lookup and sorting must have a documented derived
   bound over the admitted candidate count and fixed planar neighbor degree.
   Native candidate ranges use the actual quantized forward lattice, with at
   most four binary coordinate searches of 31 probes per selected grid; these
   are range arithmetic, not physical-address inspections. Do not reuse an
   idealized hex inverse that can omit cells at small valid metrics.
   Establish that bound in implementation/tests; no world-wide scan or storage
   growth may occur outside it.
6. Treat existing scratch/output capacities as hard ceilings for this new
   entry point, even though their lists can grow in other operations. Before
   an insertion would grow storage, return `GridCandidateLimitExceeded` for
   grid scratch, `AddressLimitExceeded` for address/union scratch, or
   `OutputLimitExceeded` for results. The corresponding caller limit can fail
   earlier. Reuse `CandidateWorkLimitExceeded` for the combined counter. Clear
   scratch on exit. Limit/invalid/arithmetic failures clear results; never
   publish a partial proof. `IncompletePhysicalCoverage` may retain the complete
   canonical negative evidence, as in the existing report contract, but never
   certifies passable coverage.
7. Use `InvalidOrUnrepresentableGeometry` for stale/missing eligible generations,
   invalid/duplicate/unsorted layer bindings, wrong-layer endpoints,
   non-neighbor legs and nonpositive radius. The reused report has no `Stale`
   status; do not imply covered-address cursor statuses carry through unchanged.
   Preserve ordinary null/negative-budget argument validation separately.

This entry point has deterministic candidate ceilings and bounded derived
closure work, not a new continuation service or a wall-clock guarantee. Reuse
the existing scheduler at the caller.
Internal helper extraction must retain existing 3D work counts and performance
controls; no generic dimension framework or unrelated tracer cleanup is needed.

Owning files: FixedMathSharp
`Geometry/Primitives/Relations/FixedConvex2dRelations.PlanarSweep.cs` plus its
owning internal wide-math partial; GridForge
`Utility/GridTracer.PlanarNavigationBody.cs`,
`Grids/Topology/GridPlanarLayer.cs`, and
the `Utility/GridTracer.PlanarNavigationBody.Geometry.cs` and `.Range.cs`
partials. Matching math and
GridForge behavior tests cover the new paths; existing 3D source stays intact.

### 2. Native support queries: Gravitas

Reuse Gravitas's existing synchronous indexed discovery, as the current 3D
adapter does. Neither dimension inherently requires a caller-supplied work
ceiling to be deterministic. Hard discovery/geometry budgets and no-growth
scratch are a separate [2D/3D workstream](physicsQueryBudgetPlan.md), not an
upstream prerequisite for this operation. Do not add hidden default limits or
discard candidates to imitate a budget.

Add one support-specific operation to `GravitasQuery2DService`, not a new
general-purpose all-shape query API:

```csharp
public Physics2DSupportQueryStatus QuerySupport(
    in Physics2DSupportQuery request, out Physics2DSupportHit support);
```

Proposed request/result contracts:

| Type | Required fields / behavior |
| --- | --- |
| `Physics2DSupportQuery` | Registered source collider, probe center, positive radius/distance, upward direction, minimum upward-normal dot in `(0,1]`, include-layer mask. The constructor explicitly normalizes the nonzero upward direction using FixedMathSharp and rejects unrepresentable normalization. Initial Trailblazer side-view supplies Forward. |
| `Physics2DSupportHit` | Existing `Physics2DHit` plus captured owner registration lifetime and sampled `long` frame from the shared clock; `IsCurrentLifetime` is false for default or recycled registrations. It does not promise geometric freshness. |
| `Physics2DSupportQueryStatus` | `Found`, `NoSupport`, `WorldNotReady`, `Unrepresentable`. Only the first two certify complete examination; all others clear the usable hit. |

Validate arguments before work. Operate at a stable between-frame boundary;
reject reentrancy/active resolution. Reuse existing candidate collection and
query-owned buffers; do not introduce another index or an every-agent/all-collider
scan. Physics partitions use **world Y = 0**, not the navigation embedding or
actor host Y. Preserve padded discovery and physical-presence checks. Use
checked probe bounds and reject unrepresentable inputs. Buffer reuse is not a
hard no-growth or worst-case work guarantee; measure the actual allocation and
full-frame cost in representative acceptance cases.

Preserve positive probe distances down to one raw unit; the generic sweep's
early rejection at `Fixed64.Epsilon` is not the proposed support contract. Test
initial support and separated inputs at one raw unit, epsilon and epsilon plus
one raw unit. Keep this support-specific behavior distinct from existing generic
sweep semantics.

An empty deferred-refresh set alone is not proof of a ready index. Finish
callback-deferred partition refresh at the normal owning sampling barrier and
settle GridWorld grid/sparse-change reconciliation there. Test recurrent callback
changes and changed grid generations without relying on an intervening unrelated
query to repair membership. Reuse the owning refresh lifecycle, not an adapter
world scan. A permanently-not-ready query is not an acceptable implementation.

Use the real source collider and full `RequireCollisionPair` admission, including
both ignore masks, layer matrix, siblings and linked-joint exclusions. Reject
triggers/dynamic supports; permit bodyless, static and kinematic supports.
Filter each candidate **and compound part** for support eligibility before
nearest-hit reduction. Rank by distance, owner ID, authored part index, then
stable feature order. A rejected wall part must not hide an eligible floor part.
An incomplete or unrepresentable query must not publish a usable grounded result.

Keep the existing rigid-frame hit anchor; no new witness representation.
The adapter still resolves a host-owned stable carrier key with explicit
lifetime, derives a carrier-local anchor, and samples accepted planar carrier
pose. Collider IDs and a captured anchor origin are not durable carrier bindings.

Proposed files are `Queries/2D/GravitasQuery2DService.SupportQuery.cs`, one file
per new public value/status type beside it, and focused changes to existing
candidate/narrowphase helpers only where support filtering requires them.

### 3. Atomic native posture replacement: Gravitas

```csharp
// Proposed SolidBody2D operation; no change to the existing SolidBody overload.
public ColliderReconfigurationStatus TryReconfigureCollider(
    ColliderShapeDefinition2D definition, Vector2d localOffset, Vector2d position,
    out LSCollider2D? blocker,
    out Exception? notificationException,
    Action? publishSynchronizedState = null);
```

Reuse `Applied`, `Unchanged`, `Blocked` without adding budget statuses or changing
existing enum values or 3D behavior. Invalid family/definition, unsafe lifecycle
and unrepresentable candidate/host pose retain the existing 3D exception
conventions. Support only
circle-to-circle and capsule-to-capsule initially, in pure TwoD mode; reject
mixed mode rather than silently skip 3D blockers. A compact side-view body is
still the same capsule with height `2r`, not a new collider identity.

Reuse native exact collision detection and detached shape staging to prove
the **whole** replacement collider. No public capsule overlap API is needed.
Dynamic colliders remain eligible blockers even though they are not supported
carriers. Exact zero-depth touch is allowed; positive penetration rejects.
Certify complete clearance through the registered collider set, matching the
existing 3D posture transaction, and choose a deterministic blocker. Unlike
recurring support queries, this rare explicit operation must also detect blockers
outside physics grids. Gravitas preflights host pose, shape and mass properties
before mutation. Existing partition storage may grow on publication; neither
dimension currently guarantees rollback after process-level allocation failure.
No new storage reservation scheme is implied by this transaction. The
coordinating Trailblazer caller must finish its
own fallible navigation preparation **before** invoking the transaction;
Gravitas cannot inspect navigation state. The publisher only publishes that
prepared state. Rejection preserves every live physical/controller/session
field; it neither queues a replacement nor calls the publisher.

On acceptance, publish collider/body/host XZ together, preserving world Y,
rotation and runtime identities; invalidate CCD/constraint caches, synchronize
partition membership, publish navigation once, then finish pair retirement.
`Unchanged` retains the existing once-only publisher semantics. Post-commit
publisher/separation failures are returned separately and do not roll back an
accepted transaction. This synchronous transaction makes no hard work or
wall-clock guarantee; budgeted clearance belongs to the separate 2D/3D plan.

Do not reuse 2D teardown pair removal: it clears runtime relationships and is
not exception-safe reconfiguration. Add the focused counterpart of 3D
lifetime-tokened pair retirement, retaining hierarchy/filter relationships and
continuing cleanup when callbacks throw or deactivate/reinitialize objects.
Put the transaction in `Core/2D/SolidBody2D.ColliderReconfiguration.cs`; extend
existing shape/pair lifecycle partials only where required.

Trailblazer's eventual replacement preserves the nominal foot and durable
destination, not the old physical surface contact. Fresh support follows an
accepted replacement. Radius growth on a slope may be blocked despite reduced
height; do not move the body silently to make it fit.

### Implementation order and acceptance gate

1. Implement/test the strict FixedMathSharp relation, including exact tangent,
   one-raw penetration/separation, stationary/vertical/diagonal sweeps, zero/odd
   axis lengths, containment, middle-only collision and scalar-domain limits.
2. Implement/test GridForge layer-bound union proof: wide bodies, sparse holes,
   world edges, both hex orientations, aligned multi-grid seams, diagonal closure,
   non-neighbor and zero-radius boundary-axis rejection, explicit wrong
   layer/domain rejection, missing alternatives, stale generations, every
   exact/one-below work/scratch/output capacity and checked arithmetic failure.
3. Implement/test Gravitas support selection and the native posture transaction
   over existing indexed discovery, without waiting for hard query budgets,
   preserving 3D behavior. Include compound wall/floor ordering, symmetric
   filters, dirty index, compound/polygon geometry, duplicate memberships,
   rejected growth, unchanged publication, callback failure and recycled pairs.
4. Bind native top-down and side-view map/action examples to those primitives;
   finish support corners/slopes/steps and the controller/map/record signatures.
   Then obtain the full Phase 0 contract review before Navigator2D runtime work.

These are prerequisite features, not unresolved bug tickets. Use the existing
test projects, standard/Lean and both library targets, exact changed-path
coverage, and independent source review. Shared hot-path changes require the
existing matched performance controls; do not introduce another benchmark
project. Probes below establish why these boundaries are needed, not that the
proposed operations already work.

## Progress dashboard

| Phase | Deliverable | Status | Gate |
| --- | --- | --- | --- |
| 0 | Reviewed dimensional and API contract | In progress | Exact geometry, support, graph, lifecycle, file, and serialization decisions plus early native physics proof |
| 1 | Native planar guidance and top-down movement | Not started | A*/Flow, planar clearance, accepted motion, and action boundaries verified |
| 2 | Side-view traversal and profile changes | Not started | In-plane support, locomotion, posture/capability sequences, and replay verified |
| 3 | Gravitas2D integration and closeout | Not started | Real planar collisions, behavioral examples, full matrix, and review |

## Phase 0 - Settle The 2D Contract

- [x] **Probe the real Gravitas2D boundary early.** Verify kinematic wall and
  floor/ceiling accepted motion, preserved host world Y, scalar rotation, and
  public support queries on bounded inputs. Identify missing upstream contracts
  before committing to the controller design. Reuse focused tests or an ignored
  throwaway probe; do not scaffold a sample application or production adapter.
- [ ] **Record the minimum supported controller surface.** Define exact public
  types/signatures for planar pose, motion requests/results, scalar rotation or
  facing, profile, and support snapshots. State units, coordinate conversion,
  invalid-input behavior, and prepare/accepted-commit lifecycle. Review the
  proposed source/test file map before implementing any of those types.
- [ ] **Choose and validate the graph representation.** Compare reuse of the
  existing graph with explicit planar geometry semantics against a focused
  planar adapter over shared search internals. Use one top-down obstacle and
  one side-view ledge/ceiling example to prove body fit, support, cost, and action
  boundaries. Record the selected representation and why the smaller alternative
  is insufficient if additional core contracts are needed.
- [x] **Define media, capability, and support semantics.** Explain how top-down
  traversability and side-view airborne/grounded state relate to navigation
  media. A downward direction alone cannot identify falling, and a permitted
  cell cannot stand in for actual physical support. No automatic gap-crossing
  action may arise from ordinary neighbor traversal.
- [ ] **Set compatibility and persistence boundaries.** Record how 2D profiles,
  graph/guide identity, and records differ from 3D. Reject cross-dimensional
  shell or guide misuse explicitly; preserve existing 3D record behavior and
  standard/Lean support. Do not hide dimensional conversion in legacy aliases.
- [ ] **Set scheduling, proof identity, and performance boundaries.** Include
  dimensions/mode/body geometry in every applicable cache/cohort/proof identity;
  never share a proof merely because addresses match. Record matched 3D controls
  before shared runtime changes and bounded representative 2D cases in the
  existing benchmark project. Compare equivalent configurations and work; no
  new benchmark infrastructure or arbitrary 2 ms acceptance gate.
- [ ] **Approve bounded acceptance inputs.** Specify maps, profiles, gravity,
  timestep, action handlers, expected addresses, and maximum frame counts for
  both modes. Obtain contract review before moving to Phase 1.

## Phase 1 - Native Planar Guidance And Top-Down Movement

- [ ] **Write the first failing planar acceptance cases.** Test obstacle
  avoidance and a narrow gap at fitting/non-fitting body sizes through the
  public 2D contract, including no out-of-plane drift. Record the intended
  failure, then implement the minimum graph/profile support and controller.
- [ ] **Exercise A* and Flow through accepted motion.** Compare route legality,
  exact costs where applicable, final cell, and arrival, not identical sampled
  trajectories. Cover off-axis approaches, groups, explicit transitions,
  budget exhaustion, stale publication, and held-action completion exactly once.
- [ ] **Exercise shared acquisition and visibility policies in 2D.** Test
  Immediate and opt-in Scheduled acquisition, frozen shared-work cohorts,
  cancellation/retry, moved-origin connectors, and mode changes. Waiting must
  preserve ordinary motor/platform/commit work with zero guided heading. Test
  phased LOS and invalidation without cross-dimensional proof or cache reuse.
- [ ] **Preserve 3D behavior while extracting shared logic.** Keep dimension-
  specific geometry and units explicit. Run existing graph/controller tests
  after each shared change; do not clone the whole search service or couple a
  planar consumer to the 3D motor merely to reuse its state fields.
- [ ] **Review native top-down delivery.** Verify fixed-point repeatability,
  existing navigation work limits, ownership, and changed-path coverage. Mark
  only this phase complete; side-view movement and real Gravitas2D collision
  remain open.

## Phase 2 - Side-View Traversal And Runtime Changes

- [ ] **Implement the reviewed in-plane support/motor contract test-first.**
  Cover standing, horizontal movement, slopes/steps, ceiling contact, jumping,
  falling, and landing using planar accepted poses and actual support snapshots.
  Apply gravity and grounding once; world Y must not become gameplay height.
- [ ] **Prove authored traversal.** Approach and execute a jump, a drop, and a
  ladder between supported regions over bounded frames. Test insufficient
  clearance/capability and invalidated actions without teleporting through guide
  points or skipping completion tokens. Use both A* and Flow where applicable.
- [ ] **Apply the reviewed replacement contract to 2D.** Reuse hardening Phase
  2's atomicity and lifecycle rules for stand/crouch/compact cycles, blocked
  growth, foot-anchor stability, preserved destinations, and gained/lost
  capabilities. Keep collider and navigation envelope consistent.
- [ ] **Port the relevant airborne sequences deliberately.** Test denied jumps
  during falling/flight, permitted ascent-time extra jumps, flight loss and
  recovery, and controlled descent where included by the approved 2D contract.
  Do not copy world-Y calculations or turn a 3D test green by rotating its view.
- [ ] **Verify persistence and deterministic replay.** Save/load each supported
  mode and changed posture; reject incompatible dimensional shells transactionally.
  Resume durable intent with fresh guides, comparing accepted states and event
  order over repeated inputs. Cover JSON and standard-package MemoryPack.
  Preserve active/buffered turning, restore the body before the controller, and
  clear prepared motion/support only after successful population; rejection
  must preserve the valid live state. Never restore pending proof/lease ownership.

## Phase 3 - Gravitas2D Integration And Closeout

- [ ] **Extend the optional adapter with native planar ownership.** Consume
  `SolidBody2D` accepted position, scalar rotation, and support data. Preserve
  host world Y, use exact X/Z conversion, and prepare/resolve/commit once per
  frame for all actors. Reuse the 3D adapter's ordering contract, not its vector
  arithmetic or surface-normal assumptions.
- [ ] **Run real collision acceptance for both modes.** Cover a top-down wall,
  corner, narrow portal and group; then a side-view slope, ceiling, ledge, ladder,
  translating and rotating supports, and posture change. Verify physics/Navigator pose agreement,
  guide cells, collision settings, and appropriate native 2D collider shapes.
- [ ] **Verify headless movement quality and frame failures.** Assert bounded
  arrival, retreat/tangent movement, no action-entry oscillation, stable support
  and platform carry, and prepared-frame reset/load/abort behavior. Record exact
  accepted states and repeated event order. Engine-host observations remain
  deferred and are not a completion gate for this engine-agnostic plan.
- [ ] **Document and run the public integration path.** Add native top-down and
  side-view examples, coordinate/units guidance, ownership rules, and migration
  notes only where behavior actually changes. Link selected real behavioral
  tests and fixtures instead of maintaining another example application. Verify
  any runnable documentation snippets and update XML/wiki/API pages together.
- [ ] **Run the full verification matrix.** Use the
  [hardening verification commands](done/navigationHardeningPlan.md#verification-and-closeout),
  the adapter's recorded commands, and relevant Gravitas2D tests in both package
  families. Verify Windows/Linux behavior, exact new/core coverage, 3D regression
  tests, replay, allocations, resource lifetime, and DocFX warnings-as-errors.
  Rerun matched 3D benchmark controls when shared runtime paths change and the
  focused 2D scenarios selected in Phase 0; retain full-frame work/timing context.
- [ ] **Complete independent review and evidence reconciliation.** Resolve
  blocking findings, link shared integration evidence, update the overview, and
  archive only when native top-down and side-view deliverables both meet their
  gates. Any scope reduction needs an explicit user decision.

## Execution record

Record approved contract decisions, exact revisions, files/signatures, test and
coverage commands/results, scenario inputs, observation artifacts, and linked
issues after each reviewed milestone.
Entries below are historical. The
[2026-09-24 scope split](#2026-09-24---physics-query-budgets-separated-from-2d-delivery)
supersedes earlier statements making hard physics-query budgets a 2D prerequisite;
the current contract above reflects that decision.

### 2026-09-03 - Draft captured

- Recorded separate native top-down and side-view deliverables, X/Z integration,
  dimension-sensitive graph proof, and the optional Gravitas2D adapter boundary.
- No implementation or runtime verification has started. Next task when
  authorized: Phase 0 contract review, informed by the initial 3D integration.

### 2026-09-21 - Source development and native 2D preflight

**Outcome:** The initial native-physics investigation is complete, not the full
Phase 0 contract. There is enough evidence to reject a projected-3D controller
and retain shared search ownership, but not enough to approve exact shape,
support, authoring, or replacement APIs. Phase 1 has not started. Resolve the
specific prerequisites below and finish contract review before scaffolding
runtime types. Finding a missing contract is not a no-go for 2D navigation.

#### Development and verification boundary

The removed sample's stale references were amended into the preceding integration
commit, now Trailblazer `bfcfb929a648b0fc24fb30af0fc74de75d5835a5`. This pass used
Gravitas `df1915142ec9c8c7937ee7aa8aa6d25a99cee4cd`, GridForge
`2f37202f45725a68db7b13d709ce5aa12fb1f54c`, FixedMathSharp
`517016680dfa184ad71d2d50f4ca4398dab72509`, and SwiftCollections
`4b5715b2232930480c207fa6e00944713f5000d6`. No authored upstream files changed.

Use the existing process environment `UseLocalLsfStack=true` for the IDE and
every sibling restore; see [contributor setup](../../CONTRIBUTING.md#coordinated-source-development-and-ide-restore).
A root-only props/targets experiment was discarded: reference metadata could
select source for compilation while NuGet still selected published transitive
packages. It produced an adapter `TypeLoadException` for `GridTraceSlab` and is
not retained. This was an experimental setup regression, not an upstream bug.
No package pins were lowered, local fixture packages installed, or new build
hooks retained. VS Code itself was not restarted by the agent; the documented
full exit/relaunch remains necessary for an already-running IDE.

From a fresh PowerShell process with `$env:UseLocalLsfStack = 'true'`:

```powershell
dotnet build Trailblazer.slnx -c Debug
dotnet build Trailblazer.slnx -c Release
dotnet test Trailblazer.slnx -c Release --no-build --no-restore
dotnet build Trailblazer.slnx -c ReleaseLean
dotnet test Trailblazer.slnx -c ReleaseLean --no-build --no-restore
```

All three solution builds passed with zero warnings/errors, including both
library targets. Release passed **3,164 core + 84 adapter** tests; ReleaseLean
passed **3,081 core + 80 adapter** tests. Debug adapter design-time `Compile`
evaluation also passed separately for `netstandard2.1` and `net8.0`, using
`DesignTimeBuild=true`, `BuildProjectReferences=false`,
`SkipCompilerExecution=true`, and `ProvideCommandLineArgs=true`. These are
Windows source-stack checks, not new 2D coverage, Linux evidence, a measured
performance result, or a released-package gate.

DocFX also passed with warnings-as-errors (zero warnings/errors). All 113 local
link targets checked across the affected/current integration documents exist,
and `git diff --check` passed. The explicit global override
`-p:UseLocalLsfStack=false` was evaluated from a source-enabled environment and
correctly selected the normal Gravitas package reference; no public-package
restore or release readiness is claimed by that property check.

#### Native physics observations

A disposable, ignored probe used public Gravitas APIs, not a new sample or
production adapter. It ran twice with all six loaded LSF DLL hashes matching
their source `bin/Release/net8.0` outputs. Both runs reported **52 passing checks
and two failing surface-witness expectations**, with nonzero exit. The latter
are two reproductions of one defect, now
[TRB-Issue-150](issue-tracker.md#trb-issue-150---gravitas2d-exact-touch-circle-sweeps-return-the-probe-center-as-the-surface-point).
This preflight is deliberately not reported as all-green.

Reproduction inputs: pure `PhysicsRuntimeMode.TwoD`, 60 Hz, grid bounds
`(-16,0,-16)` to `(16,0,16)`, unit-scale kinematic bodies, continuous CCD,
circle radius `1/2` or upright capsule radius `1/2` and total height `2`.
Body host world Y is `7`; bodyless obstacle world Y is `99`. All coordinates in
the table are planar `(world X, world Z)`. Each move prepares the host target,
advances the world once, then reads accepted motion; it is not a per-agent step.

| Case | Input | Observed circle / capsule accepted position |
| --- | --- | --- |
| Wall | From `(-4,0)` to `(4,0)`; wall X extent `[-1/2,1/2]` | `(-1,0)` / `(-1,0)` |
| Floor | From `(0,3)` to `(0,-3)`; floor top at `0` | `(0,1/2)` / `(0,1)` |
| Ceiling | From the floor result to `(0,8)`; ceiling underside at `5` | `(0,9/2)` / `(0,4)` |

Accepted host/body positions agreed and host world Y remained exactly `7`.
Positive planar `HalfPi` turned Right toward Forward, embedding as negative
world-Y yaw. Kinematic `CheckGround()` did **not** produce automatic grounding:
that path requires a dynamic translating body. Support is explicitly owned by
the host/adapter, consistent with the existing 3D bridge.

A downward radius-`1/2` circle sweep from `(0,1/2)` to `(0,0)` against the floor
reported the correct collider and `(0,1)` normal but returned `(0,1/2)` as the
surface point instead of `(0,0)`. The same defect occurs when probing the lower
cap of the capsule. Do not compensate by subtracting a radius in Trailblazer:
the query owner must supply a valid witness for the actual target geometry.

Queries expose collider references, context-local IDs and body roles. They
include dynamic targets, and an include-layer mask is not symmetric physical
collision filtering. Support admission must explicitly reject unsupported
roles, triggers and ignored collision pairs. Raw collider IDs are not durable
platform identities; retain the host-owned stable-carrier/lifetime policy.

The probe does not establish sustained sliding, slopes, steps, ledges, ladders,
jump trajectories, moving-platform carry, posture transactions, replay,
allocation bounds, Lean, or Linux behavior. Those remain acceptance work.

#### Selected direction and remaining contract decisions

- **Shared ownership, native geometry.** Keep one context-owned graph,
  publication/dependency pipeline, A*/Flow implementation, cache accounting and
  scheduler. Add explicit immutable map domains (`Spatial3D`, `TopDown2D`,
  `SideView2D`) and a planar geometry evaluator; do not duplicate the whole
  service or normalize a planar request into today's public 3D `PathQuery`.
  Domain dispatch must cover incoming Flow edges as well as outgoing A* edges,
  endpoint admission, direct rays, simplification, sampling and moved-origin
  connectors. Existing Solid anchors use world-Y prism minima and are not valid
  planar support anchors.
- **Explicit embedding.** A planar map binds one GridForge layer and fixed world
  Y. New 2D endpoints must identify their map; do not guess an embedding from a
  `Vector2d`. Reject cross-domain endpoints, neighbors, seams, transitions and
  guide adoption. Same cell addresses do not imply compatible geometric proof.
- **Media and physical state.** Top-down Solid means an authored traversable
  planar region, not world-Y ground. Side-view Solid means an authored supported
  locus: ordinary movement needs continuous support and full-body clearance.
  Start with gravity `(0,-g)`, `g > 0`. Gas/Liquid retain explicit capability
  admission; falling does not grant flight or turn unsupported air into a
  walking route. Physical grounded/airborne state is separate from guide medium.
  Jumps, unsupported drops and ladders are authored held actions, never inferred
  from an allowed height delta. Initially reject world-Y transition-rule
  generation for planar maps instead of silently reusing it.
- **Action authority.** Existing transitions certify approach, exit and endpoint
  admission, not the whole action trajectory. The host-authored jump/ladder
  handler owns execution and failure/recovery. A clear landing does not prove a
  clear jump arc. Preserve exact once-only token completion after accepted
  landing; do not promise automatic arc certification under the current API.
- **Dimensional identity and records.** Preserve existing 3D public signatures
  and record interpretation. Native 2D inputs/outputs may wrap shared internal
  lifetime owners but cannot be exposed as 3D leases. Mode, map domain, embedding,
  shape and support policy must participate wherever applicable in payload keys,
  frozen scheduler cohorts and geometric proofs. Native 2D records must validate
  mode/schema before mutating a bound shell. Restore bodies before controllers;
  do not serialize guides, queued work, support contacts or prepared frames.
- **Scheduling and performance.** Use the existing FIFO, capacities and fair
  validation turns across both dimensions. Immediate remains the default;
  scheduled waiting preserves motor/platform/commit work. Before changing shared
  runtime code, capture matched existing `navigation-guided-frame`,
  `navigation-ray` and recovery controls as applicable. Add focused top-down
  open/blocked and side-view support/action cases to the existing benchmark
  project, preserving workloads, source revisions, LOS/acquisition settings,
  semantic preflight and full containing-frame costs. No benchmark framework or
  new 2 ms target is authorized by this plan.

The proposed file split is additive native query/profile/endpoint types beside
`src/Trailblazer/Pathing/Query`, planar map authoring beside
`src/Trailblazer/Pathing/Map`, native proof work within the existing graph/search
areas, a `Navigator2D` and native motion/support records under navigation, and
`GravitasNavigator2D` in the optional adapter. Corresponding behavior tests belong
in the existing core/adapter test projects. This is a responsibility decision,
**not an approved exact public API**; the signature/file checklist remains open.

#### Geometric acceptance inputs and open gates

These are hand-worked design checks, not executed navigation tests or a final
choice of body shape:

1. **Top-down finite body:** box half extents `(1/4,1/4)`, start `(0,0)`, goal
   `(4,0)`, obstacle `[7/4,9/4] x [-1/2,1/2]`. The forbidden center region is
   `[3/2,5/2] x [-3/4,3/4]`; direct length `4` is illegal. Authored orthogonal
   legs through `(0,1)` and `(4,1)` are clear and cost exactly `6` with zero
   extra costs. A corridor of width `3/4` admits a height-`1/2` box but not a
   height-`1` box. Prove contact/equality policy explicitly, without epsilon.
2. **Side-view ledge/action/ceiling:** width-`1/2`, height-`1` upright box;
   support segments `[0,2]` and `[3,5]` at planar height `0`. Under conservative
   full-bottom support, anchor ranges are `[1/4,7/4]` and `[13/4,19/4]`. There is
   no ordinary route across the gap. A Jump from `(7/4,0)` to `(13/4,0)` with
   action cost `2` gives total cost `7/2` from `(1,0)` to `(4,0)` (two `3/4`
   approaches plus the action), not the straight gap distance as a hidden cost.
   Remove Jump capability: no route. A destination ceiling at `3/4` blocks the
   standing body but admits height `1/2`; endpoint admission is still not arc
   clearance. Run the eventual examples through both A* and Flow.

The next work is narrow and ordered:

| Gate | Owner and required evidence |
| --- | --- |
| Correct surface witnesses | Implemented in Gravitas; the follow-up below records the regressions, repeated probe and verification boundary. |
| Body, support and slope/step contract | Choose one finite planar envelope consistent with the real collider. A box's bottom-center placed on a slope penetrates; define the actual contact/anchor convention and prove it, including step paths and maximum slope, before approving signatures. Circle/capsule wall clipping alone does not decide this. |
| Native bounded geometry | GridForge exposes planar prism vertices and bounded covered-address enumeration; FixedMathSharp supplies planar sweep primitives. Its current navigation body-segment proof is world-Y-specific. Establish a small GridForge-owned planar proof or reviewed composition, including wide bodies, holes, exact touching, overflow and complete dependency capture. Do not duplicate physical geometry authority in Trailblazer. |
| Support query work and identity | Gravitas public all-hit circle queries have no candidate/hit budget and can grow buffers. Decide a real bounded support-query boundary and stable carrier lifetime; one call per frame or truncating returned hits is not a work bound. |
| Atomic posture changes | No public exact capsule clearance query or `SolidBody2D.TryReconfigureCollider` equivalent exists. Define the owning native transaction, preserving foot/pose, rollback and synchronized navigation publication; a bottom-circle probe cannot certify growth of a whole capsule. These are feature prerequisites, not newly discovered runtime bugs. |
| Final contract approval | Record exact controller/query/map/overlay/support/record signatures and file paths after the geometry decisions. Bind the above design examples to actual map addresses, timestep/action inputs and maximum frame counts. Then review and complete the remaining Phase 0 checkboxes. |

There is no reason to publish dependencies merely to clear the IDE error. The
preflight has identified potential upstream work before releases; keep the
coordinated release gates deferred as agreed.

Independent final source/documentation and simplicity review found no blocking
findings in this partial preflight. That review approves the evidence and scope
record, not the still-open Phase 0 API contract or entry into Phase 1.

### 2026-09-21 - Correct witnesses and slope-anchor proof

This follow-up starts at Trailblazer `1e96da4` and Gravitas `df19151`, with the
same FixedMathSharp, SwiftCollections and GridForge revisions recorded above.
The owning source change is in Gravitas; Trailblazer has no new runtime code.

#### Owning fixes

`TRB-Issue-150` is owned by Gravitas `GRV-Issue-074`: the initial-overlap circle
sweep now carries the overlap's rigid-frame target anchor while independently
reporting zero sweep travel. It neither substitutes the probe center nor
materializes an absolute point that might exceed the scalar range.

Regression work also exposed `GRV-Issue-075`. A circle probe centered on a
capsule's axis could receive an inward/opposite-side witness or an axial offset
inside the capsule. The shared circle/capsule overlap path now uses a consistent
radial fallback: world +X for coincident circles, capsule-local +X for a
centerline tie. It reuses the capsule's cached axis and the existing contact
kernel; no additional geometry solver or public API is introduced.

The new owning test file, Gravitas's
`tests/Gravitas.Tests/Physics2D/Physics2DSweepWitnessTests.cs`, contains 32 cases.
It covers touch, penetration and ordinary entry against circle, capsule, box,
polygon and compound targets; stable compound ties; witnesses beyond either
scalar face; and upright/rotated/degenerate centerline cases. Expected points
come from explicit target geometry, not another call to the same overlap code.
Two old expectations that asserted the probe center were corrected.

The original disposable native probe was rerun against the updated source-stack
Release binaries: **52 checks passed, zero unresolved witness gaps**. Both floor
queries now return `(0,0)`. This closes the previously recorded witness failures,
not the unimplemented 2D controller's acceptance gates.

#### Executed slope check and proposed shape convention

A second ignored probe uses the same pure TwoD/60 Hz world, host Y `7` and
bodyless obstacle Y `99`. The triangle `(-8,-8), (8,-8), (8,8)` has the support
edge `v = x`. Each continuous kinematic body moves vertically from `(0,4)`
toward `(0,-1)`:

| Body | Analytic accepted center | Observed Q32.32 center |
| --- | --- | --- |
| Upright box, width 1 / height 2 | `(0, 3/2)` | `(0, 6442450944 raw)` |
| Upright capsule, radius 1/2 / height 2 | `(0, 1/2 + sqrt(2)/2)` | `(0, 5184484148 raw)` |

Both accepted centers match the fixed-point analytic result exactly and preserve
the host/body pose and embedding Y. The capsule support sweep returns point
`(1518500250,1518500250)` raw and outward normal
`(-3037000500,3037000500)` raw. They reconstruct the accepted center exactly.
All **15 assertions** pass. The probe allows 16 raw units in final-coordinate
comparisons, including accepted center and witness-based center reconstruction;
the observed errors are zero. This is not a clearance tolerance or permission
to admit penetration.

For upright capsule radius `r`, total height `h >= 2r`, upward unit vector `u`,
support point `p`, and outward support normal `n`:

```text
body center       = p + r*n + (h/2 - r)*u
nominal foot      = body center - (h/2)*u = p + r*(n - u)
lower-cap center  = body center - (h/2 - r)*u
```

These relations describe tangency on a single supporting face, not an arbitrary
corner/contact manifold. On flat ground `n = u`, so the nominal foot and surface
point coincide. On a slope they do not. Never place the capsule by treating the
physical surface witness as its nominal bottom-center; never manufacture a
surface witness from a root-height subtraction.

The proposed first controller envelope is a **circle for top-down movement and
an upright capsule for side-view movement**, matching real Gravitas colliders.
A capsule of height `2r` gives the compact circular posture. This avoids a
rotating-box clearance framework in the first increment; it is a contract
proposal, not an already-approved restriction or a public API addition.
Side-view physical orientation stays upright while scalar facing/presentation
is separate. Existing 3D behavior remains unchanged.

Keep nominal foot/root offset and the physical contact/carrier anchor separate.
For posture replacement, preserve the nominal foot as the established hardening
contract requires, then certify the **entire** proposed collider transactionally.
A radius change on a slope may fail clearance even when height shrinks; do not
silently move the body to preserve a different contact. After accepted
replacement, obtain fresh support. The current probe does not test replacement.

Before approving the final API, still prove slope limits, corners, crest/valley
support, and bounded step paths with that shape. Slope admission must use a
fixed-point normal/up comparison; an upward-facing contact alone is not a
walkable slope. A step needs separate lift, full-body forward clearance, and
supported landing checks, not only an endpoint or lower-cap sweep. The native
bounded GridForge proof, Gravitas query-work bound and atomic 2D posture
transaction remain the next concrete prerequisites. No extra Phase 0 checkbox
is complete solely because the physics probe is green, and Phase 1 has not
started.

#### Verification boundary

Fresh Windows source-stack solution builds pass in Release and ReleaseLean,
including `netstandard2.1` and `net8.0`, with zero warnings/errors:

| Suite | Release | ReleaseLean |
| --- | ---: | ---: |
| Gravitas | 4,053 | 3,998 |
| Trailblazer core | 3,164 | 3,081 |
| Trailblazer Gravitas adapter | 84 | 80 |

Gravitas retains exact **56,196/56,196 lines, 16,018/16,018 branches and
5,346/5,346 fully covered methods** in both Windows configurations. The two
removed branches were the inconsistent center-normal override, not exclusions.
Coverage reports are under Gravitas `artifacts/issue150/`. This pass reran the
unchanged downstream suites; it does not claim a new downstream coverage audit.

All three suites also pass with the same counts in both configurations when
those managed test outputs run on Ubuntu/WSL with .NET 8.0.26. Both Release probes
produce identical Windows and Linux traces; all six probe LSF DLL hashes match
the freshly built adapter test output. These are Linux execution checks, not a
separate Linux restore/build or released-package verification. Gravitas and
Trailblazer DocFX builds pass with warnings-as-errors. No performance change is
claimed.

Independent read-only correctness/simplicity review found no source blockers.
One evidence-wording correction clarified the probe's comparison tolerance;
the separate observed-zero-error result is retained. Review approves these
owning fixes and the partial Phase 0 evidence, not the unimplemented controller
or final dimensional API contract. Upstream publication remains deferred.

### 2026-09-21 - Bounded planar primitive investigation

Baseline: Trailblazer `a2f2453`, Gravitas `e9aee10`; other upstream revisions are
unchanged from the preceding record. This pass changes the proposed contract,
not production source. The proposal above turns the remaining upstream work
into three concrete boundaries rather than starting a speculative Navigator2D.

An ignored public-API probe passes **45 assertions** on Windows and Ubuntu/WSL,
with identical output. Its Release build is warnings-as-errors clean:

| Input | Observed result / design consequence |
| --- | --- |
| Capsule radius `1/2`, axis length `2`, center `(0,0)`, sweep Right for `4`; box centered `(2,0)`, half-extents `1/8` | Whole capsule hits at `11/8` (raw `5905580032`); both end-circle sweeps miss. Full body proof cannot use only the caps. |
| Capsule axis length `1`, radius `1/2`, center `(1,0)` or `(1 - 1 raw,0)` against a centered unit box; upward sweep | Existing closed sweeps return distance zero for both exact touch and penetration. This API is not a strict-overlap predicate. |
| Sparse grid centers `(0,0,0)` through `(2,0,0)`; only endpoints materialized; one lookup/address/output per call | Three calls emit all three canonical addresses including the missing middle. Completion means enumeration completed, not physical coverage. Filling the hole invalidates a prior run with `Stale`, zero output and an empty run stamp. |
| Single rectangular / flat-hex / pointy-hex cell; bounds of a radius-4 body | Each cursor completes after emitting that one cell. Four/six-vertex footprints are accepted by the native convex kernel, but outside-body coverage is not certified. A separate union-closure proof is necessary. |
| Body bounds require `MaxValue + 1/2` | Checked arithmetic rejects it; do not substitute saturated bounds. |

These tests exercise existing primitives and counterexamples. They do **not**
implement or prove the proposed strict capsule relation, complete footprint
union, per-query support budgets, or posture transaction. The sparse cursor
probe uses an allocated baseline capture during setup; it is not a model for
per-frame generation discovery or a zero-allocation claim.

A second public query probe passes **11 assertions** on both operating systems,
also with identical traces and a warnings-as-errors build. A compound has a wall
`x=[1/2,3/2], v=[-1,2]` and floor `x=[-2,2], v=[-1,0]`. A radius-`1/2` probe
sweeps from `(0,1/2)` to `(0,-1/2)`. Both parts initially touch. Wall-first
authoring returns normal `(-1,0)`; floor-first returns `(0,1)` with point `(0,0)`.
Post-filtering the single result by `normal.Y >= 1/2` loses the usable floor
when the wall is first. This is the generic query's documented tie behavior,
not a newly filed defect; the proposed support operation must filter before
reducing parts.

Source review also confirms that whole-capsule posture clearance can reuse
Gravitas's native narrowphase; a public all-shape clearance framework is not
needed. Its current teardown pair removal is not a reconfiguration transaction:
relationship preservation and exception-safe retirement need the focused 2D
counterpart of the existing 3D path. The proposed indexed support path avoids
both unbounded all-hit collection and an every-agent/every-collider scan.

Independent correctness/simplicity review tightened the proposal to positive
radius and same/neighbor planar legs, preserved existing report units, made
no-growth storage failures explicit, and kept navigation preflight with the
Trailblazer caller. The zero-radius boundary-axis case is a design counterexample
to be covered by the future implementation tests, not a newly executed probe.
The targeted recheck found no remaining blockers to this prerequisite proposal;
that approval does not certify unimplemented geometry, query or transaction
behavior and does not close Phase 0.

No upstream source, package references, releases or tracked sample projects are
changed. Full library suites/coverage and performance controls from the prior
fix are historical evidence, not rerun or claimed as verification of these
unimplemented APIs. Phase 0 remains open for final controller/map/record
signatures and executable native navigation acceptance.

### 2026-09-21 - Native planar clearance implementation

Baselines: Trailblazer `4eead5f`, FixedMathSharp `5170166`, GridForge `2f37202`,
Gravitas `e9aee10`. This implements prerequisite section 1 in the existing
develop checkouts; it does not start the native Trailblazer controller or
complete Phase 0. No package references or releases change.

FixedMathSharp now owns exact strict swept-upright-capsule and closed stationary
capsule predicates. They share one linear-time kernel using existing wide
arithmetic. Doubled raw coordinates preserve odd half-axis lengths without
normalizing or rounding contact axes. Tests include exact tangency versus one
raw unit, middle-only intersections, winding/travel reversal, full-domain
chords, degenerate cores and a 1,225-case independent integer-distance oracle.

The separate closed predicate is intentional: navigation needs an exact boolean
classification, not physics contact depth or a rounded normal. Investigation
reproduced an older 3–4–5 corner-tangency rejection in the generalized contact
solver against unchanged binaries. `FMS-Issue-020` / `TRB-Issue-151` retain that
defect for owning repair before support/posture acceptance. No epsilon or
contact-output wrapper is introduced, and that older solver is not fixed here.

GridForge now owns explicit generation/layer selection and one bounded native
planar body trace, reusing existing evidence and scratch types. It certifies
whole-body union coverage, not merely the endpoints: sparse holes, outside-grid
neighbors, rectangular diagonal side cells, both hex orientations, aligned
cross-grid alternatives and stale revisions have behavior tests. Existing
storage capacities are hard ceilings. The proof holds world-read then
committed-change locks so physical presence and generation evidence agree.

Independent review found and blocked an initially green implementation: the
legacy idealized hex inverse can omit cells from a quantized forward lattice.
A radius-six-raw grid reproduced an endpoint lookup exception in both
orientations. The new trace now derives inclusive candidate bounds with four
bounded searches over actual forward coordinates. Translated tiny-cell,
coupled-neighbor, empty-range and overflow regressions cover the correction;
there is no minimum-size restriction, missing-endpoint workaround or full-grid
scan. `GF-Issue-010` / `TRB-Issue-152` keep the unchanged legacy inverse and its
other consumers open. Shared 3D runtime source remains unchanged.

The independent correctness/simplicity re-review approves the corrected source.
It also caught the benchmark fixture's old broad-range count: the capsule now
inspects three addresses, not nine, so its exact setup proof was updated. The
implementation adds no new sample application, regression project, scheduler,
dimension framework or copied downstream geometry solver.

An unrelated existing anchor-allocation guard reported 7,240 bytes once.
`FMS-Issue-019` retains the failure as unattributed. The test now warms and
measures the identical operation through the existing helper and checks all
calls succeed. A temporary allocation still fails it; no retry, tolerance,
runtime workaround or claim of a cured allocator is retained.

#### Coverage and remaining scope

Fresh source-stack Windows suites pass in both configurations:

| Suite | Release | ReleaseLean |
| --- | ---: | ---: |
| FixedMathSharp core | 2,780 | 2,759 |
| FixedMathSharp Chronicler integration | 8 | 8 |
| GridForge | 1,140 | 1,140 |
| Gravitas | 4,053 | 3,998 |
| Trailblazer core | 3,164 | 3,081 |
| Trailblazer Gravitas adapter | 84 | 80 |

All of those managed suites also pass on Ubuntu/WSL with the same counts.
These are Linux execution checks, not a separate Linux source build. Both
library targets (`netstandard2.1` and `net8.0`) build without warnings/errors
in both configurations. FixedMathSharp, GridForge and Trailblazer DocFX builds
pass with warnings-as-errors. The six LSF DLLs in the adapter test output match
their freshly built source counterparts in both configurations. This validates
the local source chain, not released packages or source-mode package artifacts.

Both configured coverage surfaces are exactly covered, with no new exclusions:

| Surface | Configuration | Lines | Branches | Fully covered method entries |
| --- | --- | ---: | ---: | ---: |
| FixedMathSharp coverage filter | Release | 47,975/47,975 | 8,918/8,918 | 3,465/3,465 |
| FixedMathSharp coverage filter | ReleaseLean | 48,068/48,068 | 8,918/8,918 | 3,461/3,461 |
| GridForge | Both | 9,332/9,332 | 4,159/4,159 | 1,145/1,145 |

FixedMathSharp reports are under `artifacts/planar-clearance`; GridForge reports
are under `tests/GridForge.Tests/TestResults`. Reusable source tests, not the
ignored coordination workspace, own the acceptance regressions.

The new FixedMathSharp out-of-process ShortRun cases measure approximately
0.60-5.06 microseconds per operation on this host, with zero-byte diagnostics.
GridForge's first benchmark build failed because the generated host could not
reference the report-valued return; the fixture now returns the existing
primitive semantic-counter pattern while setup still validates the full report.
The next generated run selected published math and could not load the new API.
This is the already documented `GF-Issue-009` generated-child boundary, not a
geometry failure. Both incomplete captures remain excluded; no project graph,
DLL injection or package workaround was added.

A subsequent direct-host **in-process ShortRun** passes all five setup/workload
cases after all four local dependency DLL hashes are verified. Observed means
are 170.98 microseconds for nine-cell planar circle coverage and 98.29 for the
three-cell capsule. The three unchanged 3D controls also complete. This is
observational smoke evidence, not an isolated canonical comparison or a host-
frame improvement. The formatted allocation column rounds to zero, but the
two planar raw diagnostic windows each contain 960 total bytes; do not present
that benchmark as an absolute zero-allocation proof. Separate warmed,
result-bearing unit guards measure exactly zero bytes. The successful capture
is under GridForge `artifacts/planar-body-smoke`; both failed captures remain
under `artifacts/planar-navigation-body-final*`.

At this checkpoint, upstream contact-depth and legacy hex-conversion repairs
were the next prerequisites. Their subsequent resolution is recorded below.

### 2026-09-22 - Upstream contact and hex defects resolved

`TRB-Issue-151` and `TRB-Issue-152` are resolved after reviewing the owning fixes
and rerunning their regressions and downstream suites against current source.
The preceding dated implementation record describes the earlier state, not
current blockers.

- FixedMathSharp `d53bf28` retains exact capsule/convex axes and radial extents
  through classification and minimum-depth ordering. The original 3-4-5 corner
  case passes at scales 1, 10, 100 and 1000 with zero depth; rotated capsules,
  one-raw-unit boundaries and independent integer-distance oracles also pass.
  `da6b6f1` corrects side-contact witnesses. Gravitas `0ddcb50` and `21a22ab`
  provide consumer collision, query and witness regressions.
- GridForge `315e0cc` inverts its quantized forward basis and preserves authored
  corners during dimension rounding. Both orientations pass tiny-metric and
  distant-address lookup, snapping, normalization and legacy coverage/trace
  checks. The new planar trace still uses bounded forward-coordinate searches.
  Unsaturated arithmetic remains the conversion contract; rebuild dimensions
  and configuration keys derived from previously affected authored inputs.

Fresh Windows verification on 2026-09-22:

| Suite | Release | ReleaseLean |
| --- | ---: | ---: |
| FixedMathSharp core | 2,824 | 2,803 |
| GridForge | 1,162 | 1,162 |
| Gravitas | 4,062 | 4,007 |
| Trailblazer core | 3,164 | 3,081 |
| Trailblazer Gravitas adapter | 84 | 80 |

All listed tests passed with no skips. Downstream commands used
`dotnet test <test-project> -c <configuration> -p:UseLocalLsfStack=true`.
FixedMathSharp's focused capsule/planar checks passed 205 cases per
configuration before its complete core suites were run. The downstream
FixedMathSharp and GridForge DLL hashes matched their source build outputs in
both configurations. This is source-stack test verification, not a new coverage,
Linux, performance or released-package certification.

Next: implement the bounded Gravitas support/posture prerequisites, then finish
the native controller/map/record contract and acceptance inputs. Phase 0 remains
in progress; Phase 1 and public release gates remain pending.

### 2026-09-24 - Support prerequisite recheck

Resumed after issue-tracker cleanup at Trailblazer `437348e`, Gravitas `198d272`,
GridForge `83132cf`, FixedMathSharp `b7a6b02`, SwiftCollections `3668bb4` and
Chronicler `5397348`. The wide clock and held-jump fixes remain the current
baseline. No runtime source or dependency references changed in this recheck.

The focused Gravitas witness/host-contract suites pass **48/48** in both
source-stack Release and ReleaseLean. An ignored public-API probe compares
covered-address cursor results against existing covered-voxel enumeration,
resolving physical presence and applying the same planar padded-center filter.
It passes **20,172 comparisons** with zero differences or omissions: rectangular,
flat-hex and pointy-hex grids, dense/sparse storage, two vertical layer offsets,
and half-unit query boxes translated in quarter-unit increments. Cursor calls
allow only one lookup, one address and one output each. This is a bounded corpus,
not proof of all seams, extreme coordinates, concurrent mutation or body support.
The navigation-baseline allocation is setup only, not a proposed hot-path design.
The Windows-built managed probe also passes under Ubuntu/WSL with exactly
matching output; this is Linux execution, not an independent Linux source build.

A touching-floor control also confirms that the existing generic circle sweep
returns no hit for positive distances of 1 and 256 raw units, but returns the
correct zero-distance floor hit at 257 raw units (`Fixed64.Epsilon` is 256 raw).
The overlap control sees the floor. This establishes a reuse boundary for the
new support operation, not a newly implemented support API or a generic sweep
repair.

Independent source review confirms the missing bounded generation-discovery
boundary, world-stamp handoff requirement, quadratic convex-work accounting and
sampling-barrier obligations. The budget-specific findings are now retained in
the [separate physics query plan](physicsQueryBudgetPlan.md); current support
correctness obligations remain in section 2. Grid-change membership
reconciliation is a source-identified design risk, not a reproduced new runtime
defect. No issue ID is allocated for these unimplemented feature prerequisites.

Local probe/build/test evidence is under `artifacts/2d-support-preflight`; all
six probe LSF DLL hashes match the freshly built Gravitas Release test host.
No full-suite, coverage, performance or released-package certification is claimed.
At this checkpoint the additional upstream discovery scope awaited approval.
The following scope decision separates it from 2D delivery. Phase 0 stays open;
no native controller types are scaffolded.

### 2026-09-24 - Physics query budgets separated from 2D delivery

The maintainer requested a separate workstream covering **both 2D and 3D**.
The current 3D adapter uses synchronous `Query3D.SweepSphere`; it does not have
the proposed per-query work ceiling. Existing 2D indexed discovery is also
usable without that stronger guarantee. Deterministic results and caller-set
work/storage ceilings are distinct contracts.

[Bounded Physics Queries](physicsQueryBudgetPlan.md) now owns the discovery,
geometry accounting, no-growth scratch and budget-exhaustion design, including
the upstream GridForge/SwiftCollections investigation. This is not a dependency
for completing native 2D support/posture. No measured performance defect or new
issue is implied by the split.

Sections 2 and 3 retain support filtering before nearest-hit reduction, exact
witnesses/lifetimes, tiny-positive probe behavior, current partition membership,
whole-body posture clearance and atomic publication. Their proposed signatures
no longer depend on unimplemented budget/scratch types. Existing navigation and
GridForge planar-coverage limits are unchanged. Next: implement those focused
Gravitas prerequisites and complete Phase 0 contract review. This revision is
documentation only; no runtime or new test result is claimed.

### 2026-09-24 - Native support/posture implementation and parity review

The maintainer authorized continuing the focused prerequisites and explicitly
required considering shared improvements in both dimensions. Work remains in
the existing develop checkouts, based on Trailblazer `7ab3fc9` and Gravitas
`198d272`; nothing is staged or committed. Phase 0 is **not complete**, and this
working change is **not ready to merge**.

Implemented in Gravitas, pending the remaining acceptance gates:

- `Physics2DSupportQuery`, `Physics2DSupportHit` and `Query2D.QuerySupport` reuse
  native indexed discovery and geometry. Physical pair eligibility, carrier
  role and support-normal filtering happen before nearest-hit reduction,
  including individual compound parts. Tiny positive probes work without
  changing the existing generic sweep contract. Hits retain sampled frame and
  registration-lifetime evidence; neither is a geometric freshness guarantee.
- `SolidBody2D.TryReconfigureCollider` stages circle/capsule shape, position and
  mass changes before publishing accepted state. Rare explicit replacements
  inspect the complete registered blocker set, including out-of-grid geometry.
  Notification failures do not undo accepted state, and lifetime-safe retirement
  preserves fresh pairs created by callbacks. Pure 2D is the explicit scope;
  mixed-mode replacement is rejected.
- Shared query readiness reconciles committed memberships after physical grid
  changes in both dimensions. It neither publishes pending authored geometry
  nor scans the registry on every ordinary query. Final-step 2D refresh no
  longer relies on an incidental grounding query.
- Existing 3D posture retirement now protects recycled registrations and uses
  an invocation-local snapshot when callbacks can reset the world or begin a
  nested transaction. Native 2D reset and holder-owned pair controls also pass.

Parity review records distinct outcomes rather than assuming matching names
mean matching guarantees:

| Boundary | Current disposition |
| --- | --- |
| Partition readiness after grid changes | Reproduced and repaired in both dimensions; Gravitas `GRV-Issue-079` tracks the existing defect. |
| Posture callback lifetime and reset | Native 2D controls and repaired 3D regressions pass; `GRV-Issue-080` retains the existing 3D defect. |
| Filter-before-reduction physical support | Native 2D implemented. Equivalent dedicated 3D selection, including compound/mesh features, is a separate scope choice still awaiting maintainer direction. The generic 3D sweep has not silently changed. |
| Strict posture clearance | Four regressions fail across both dimensions; `GRV-Issue-081` blocks completion. |
| Hard query work/storage ceilings | Remain in the separate shared physics-query budget plan. |

#### Blocking exact-classification repair

A candidate can penetrate by less than half a Q32.32 raw unit while its contact
depth rounds to zero. Circle/capsule 2D and sphere/capsule 3D regressions all
return `Applied` instead of `Blocked`. The owning issue records exact candidate
and box coordinates and the independent squared-distance proof. This is an
existing 3D defect as well as a gap in the new 2D implementation, not an accepted
precision tolerance.

The required repair is to retain exact separation/tangency/penetration
classification in FixedMathSharp and consume it at Gravitas posture admission.
Do not alter normal solver depth rounding, add an epsilon, skip the regressions
or weaken the strict-clearance contract.

The native 2D source families can reuse centered-axis and rotated convex kernels.
Complete 3D coverage is larger: existing posture supports sphere, capsule and
cylinder replacement against radial, box, cylinder, cone, mesh and compound
targets. Fixing only the four reproduced radial-versus-box cases would not
establish complete parity. This coordinated math expansion requires a scope
decision before implementation; no FixedMathSharp source changes were made in
this slice.

#### Verification checkpoint

The untouched Gravitas Release baseline passed 4,103 tests. Initial support,
posture, membership and callback regressions were observed failing before their
corresponding fixes. An intermediate full instrumented Release run passed
4,181 cases and failed one old mixed-grid membership assertion: repaired 2D
memberships enabled real collision response, invalidating the assumption that
both colliders' first covered cell would be identical. The corrected test now
checks every owned replacement-grid membership and a genuinely shared cell;
the restored collision response was not suppressed.

The latest focused Windows source-stack checks cover support, posture,
registration/reset/nested callbacks, partition readiness, mixed broad phase and
3D projected-circle queries. **Release and ReleaseLean each pass 204 and fail
the same four strict-clearance cases, with zero skips.** The reset fix passes
independent re-review; the whole change remains unapproved due to exact
clearance and incomplete final verification. Logs are under Gravitas
`artifacts/support-posture-review-followup*.log` (the final Release repeat is
`support-posture-final-focused-Release.log`); original RED evidence remains
in `support-posture-red.log`, `posture-3d-lifetime-red.log` and
`posture-review-red.log` in the same directory.

Both solution configurations build for `netstandard2.1` and `net8.0` with zero
warnings/errors. The initial benchmark compile caught mixed integer/fixed-point
constructor arguments; explicit `Fixed64.Zero` fixes the fixture without a
runtime change. Its in-process Dry smoke executes all eight cases successfully,
including setup's eight complete frames per scene. This only checks runnable
fixtures; one measured iteration is not usable throughput, allocation or
improvement evidence. Capture: `artifacts/support-query-smoke-executed.log`.

Intermediate coverage is not exact (44,583/44,588 lines and 13,189/13,208
branches), and does not cover the final working snapshot. Final full suites,
coverage, Linux, API-site, downstream and performance gates are not certified.
The new benchmark fixture is not evidence of a speedup. Keep the failing
regressions visible and resolve the shared geometry scope before completing
posture, approving Phase 0, or beginning native controller implementation.

### 2026-09-24 - Exact-classification implementation and finite-rim blocker

The maintainer authorized the shared FixedMathSharp/Gravitas repair, emphasizing
deterministic correctness and speed. This supersedes the preceding request for
that scope decision. Work is being checkpointed in logical commits on develop. Native controller
implementation and complete 3D posture admission are still blocked.

Implemented in FixedMathSharp:

- Strict centered-capsule pair predicates for 2D scalar frames, explicit world
  axes, and exact 3D quaternion/local-axis frames. They compare exact squared
  distances without rounding endpoints or the sum of radii.
- Strict 2D capsule/polygon classification through the existing complete SAT
  traversal, skipping contact-depth ranking and witness construction.
- Internal classification-only box/sphere, box/capsule, convex-hull/capsule,
  and finite-cylinder/cone versus sphere predicates. Triangle-as-hull controls
  establish reuse for sphere/capsule versus individual concave-mesh triangles.
- Normal solver contact rounding is unchanged. No epsilon, radius inflation,
  floating-point arithmetic, runtime BigInteger, new dependency, or generic
  collision framework was introduced. BigInteger is used only by independent
  test oracles.

Native Gravitas 2D posture now consumes exact predicates for every target leaf
family, including compound parts. Physical filtering, stable root blocker
identity, complete registry scans, transactional publication and callback
lifetime rules remain intact. Scalar rotation is authoritative: the cached
normalized `WorldAxis` is not interchangeable with the contact kernel's authored
sine/cosine coefficients. Near-boundary direct/compound cases and an independently
constructed noncardinal rotated-capsule compound fixture protect that distinction.

Independent reviews covered each author's math through another reviewer and
reviewed the 2D consumer separately. One public degenerate-polygon wording gap
was corrected with a behavioral test. Lower-dimensional noncollapsed boundaries
use positive separating translation; ordinary polygon interior wording alone
would have been inaccurate. A full-domain test oracle initially multiplied two
`long` values before promoting to BigInteger; explicit early promotion corrected
that test-side overflow, with no corresponding runtime relaxation.

#### Why 3D is not declared complete

FixedMathSharp `FMS-Issue-023` preserves a geometric counterexample against the
pre-existing cylinder/capsule contact authority: cylinder center zero, +Y axis,
length 2 and radius 10; capsule +X axis, length 20 and radius 1, centered at
`(20.75, 1.75, 0)`. Its nearest endpoint-to-rim distance squared is `9/8 > 1`,
yet contact reports overlap. At center `(20.75, 2, 0)` and radius `5/4`, exact
tangency reports depth `0.25`; a one-raw outward offset is also admitted.
Three permanent tests remain red. This is not contact-depth rounding noise and
cannot be fixed by making a sampled-axis comparison strict.

Complete 3D posture also requires a complete authority for cylinder/capsule,
cylinder/cylinder, capsule/cylinder versus cone, and cylinder versus mesh.
Existing sampled axes, rounded support GJK, or sampled triangle contacts are
not accepted as proofs of exact strict clearance. Sphere/capsule mesh dispatch
can reuse the reviewed hull/triangle predicate, but has not been integrated in
isolation behind an approximate fallback. Existing 3D posture remains unchanged
at this boundary; `GRV-Issue-081` stays open.

#### Performance checkpoint

Windows x64, .NET 8.0.29, SDK 10.0.302, BenchmarkDotNet 0.15.8 default
out-of-process jobs on the same machine. Capture the existing contact baseline
before implementation and use matching fixture inputs for strict comparisons:

| Fixture | Ordinary contact/depth query | Strict classification |
| --- | ---: | ---: |
| 2D capsule/polygon side overlap | 8.98 us | 6.98 us |
| 2D capsule/polygon corner tangency | 8.15 us | 6.50 us |
| Box/sphere | 3.58 us | 0.43 us |
| Box/capsule, final rerun | 151.41 us | 51.91 us |
| Convex hull/capsule | 1372.66 us | 619.22 us |

The first box/capsule strict implementation still ranked depths (130.30 us).
After measurement and independent review, it now uses the same candidate
traversal but skips ranking and unnecessary squared norms. All measured rows
report zero bytes allocated per operation. Ordinary contact controls show no
material regression against the pre-change captures (side 9.017 us, corner
8.207 us, box/sphere 3.580 us, box/capsule 155.562 us, hull/capsule 1373.40 us;
unchanged cylinder controls remain near 27.5/25.1 us). These are geometry-query
measurements, not full posture-transaction or simulation-frame speedup claims.

Reproduce in FixedMathSharp after building its Release benchmark project:

```bash
dotnet tests/FixedMathSharp.Benchmarks/bin/Release/net8.0/FixedMathSharp.Benchmarks.dll centered-capsule-convex rigid-finite-shape-relation oriented-box-anchor --filter '*SideOverlap' '*CornerTangency' '*OrdinaryCylinderCapsule' '*OrdinaryCylinderCylinder' '*OrdinaryHullCapsule*' '*CapsulePrimary' '*SpherePrimary' '*CapsuleStrict' '*SphereStrict' --exporters json
```

Supporting captures are `artifacts/strict-clearance-baseline`,
`strict-box-baseline`, `strict-clearance-candidate` and `strict-box-final` in
FixedMathSharp. The first candidate's box strict row predates ranking removal;
use the final capture for that row. No new benchmark project or CI gate was added.

#### Verification checkpoint

The complete FixedMathSharp core test project runs unfiltered: Release passes
2,939 and fails the three finite-rim regressions; ReleaseLean passes 2,918 and
fails those same three, with zero skips. Both instrumented configurations have
exact 100% reachable line, branch and method coverage for the core and included
FluentAssertions assembly. This does not make the red correctness gate green.
Captures are `artifacts/strict-final-coverage-release*` and
`strict-final-coverage-lean*`. Existing solver controls, exact quaternion and
full-domain oracles, sub-raw boundaries, degenerate contracts and warmed
allocation tests execute alongside the new classifiers.

Native Gravitas 2D posture passes all 53 focused Release cases, including the
two original strict-clearance failures. Unfiltered Gravitas runs pass 4,217
Release / 4,158 ReleaseLean cases, and each fails only the two unchanged 3D
sub-raw posture regressions; zero skips. Those runs include all 53 native 2D
posture cases. Captures: Gravitas `artifacts/strict-full-Release.log` and
`strict-full-ReleaseLean.log`.

Downstream `dotnet test Trailblazer.slnx -c <configuration>
-p:UseLocalLsfStack=true -p:BuildInParallel=false -m:1` builds the source chain
for both `netstandard2.1` and `net8.0` without warnings/errors. Release passes
3,245 core and 90 adapter tests; ReleaseLean passes 3,146 core and 86 adapter
tests, all with zero skips. Captures: Trailblazer
`artifacts/strict-upstream-Release.log` and `strict-upstream-ReleaseLean.log`.
The working-tree diff checks are clean. No package-release, Linux, API-site,
full Gravitas coverage or complete 3D correctness certification is implied by
these source-mode checks. The whole prerequisite remains **not ready to merge**.

### 2026-09-24 - Commit cleanup and complete curved-feature repair

The maintainer authorized completing `GRV-Issue-081` without intermediate
approval stops and requested logical local commits of the accumulated work.
No push or release is authorized. FixedMathSharp's strict-predicate foundation
is `9d532a9`; the independent finite-rim regressions are retained separately in
`123221b`. The existing Chronicler test rename is isolated in `625d749`.
Gravitas checkpoints separate partition readiness (`622753b`), callback pair
lifetime (`4bc5010`), native physical support (`154df77`), and native posture
(`c8dbb5b`). Its two unresolved 3D clearance regressions are isolated from
lifetime tests in `e899e2b`.

Fresh unfiltered Release checks reproduce the documented boundary: FixedMathSharp
core 2,939 passed / 3 known failures, Chronicler 49 passed; Gravitas 4,217 passed /
2 known failures, with no skips. Captures: each owning repository's
`artifacts/checkpoint-release.log`. These are explicit implementation checkpoints,
not declarations of a green release gate. Continue with complete curved-feature
classification, then wire the whole 3D dispatch and rerun the full verification
matrix. Do not substitute rounded contact depth or sampled axes for exact truth.
