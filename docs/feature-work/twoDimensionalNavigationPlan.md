# First-Class 2D Navigation Implementation Plan

> Follow the `lsf-development` workflow and track progress in the checkboxes
> below. Complete Phase 0 contract review before implementing runtime types.

**Created:** 2026-09-03

**Status (2026-11-09):** Phase 1 is in progress. Top-down bakes publish cells
through the shared graph with selected-layer defaults and overlays. See the
[publication checkpoint](#2026-11-09---phase-1-planar-publication).
Spatial query and edge admission excludes those cells. The [strict top-down
placement check](#2026-11-09---phase-1-strict-top-down-endpoints) resolves a
current endpoint. [Swept-circle validation](#2026-11-09---phase-1-top-down-leg-clearance)
checks an ordinary leg between cell centers. [Immediate top-down routing](#2026-11-09---phase-1-immediate-top-down-routes)
uses those legs in the shared A* and Flow searches. Use
`UseLocalLsfStack=true` for adapter development. The core package references
GridForge 10.0.0. Released-package validation of the adapter still requires
Gravitas 1.1.0. Hard-budgeted physics queries remain a separate
[2D/3D workstream](physicsQueryBudgetPlan.md).

**Original draft baselines:** Trailblazer `cb0d744b76a377807a253f0efa1c480ba169a7e9`
and Gravitas `6ade2f0bb373665efc5b7b85a3d21d1a371aa936`. The dated execution
record identifies the revisions used at each checkpoint.

**Goal:** Provide first-class deterministic 2D navigation and character control
for top-down and side-view movement, with a Gravitas `SolidBody2D` integration
and no regression to the existing 3D API.

**Architecture:** Reuse dimension-independent search, publication, dependency,
budget, and action-lifecycle machinery where its semantics remain valid. Give
planar geometry, support, movement, and facing explicit contracts rather than
silently projecting the world-Y-up motor into another plane. Keep physics in the
host and the Gravitas-specific bridge in the optional adapter package.

**Tech stack:** `netstandard2.1` with C# 11 and `net10.0` with C# 14.
Tests and benchmarks target `net10.0`. Use FixedMathSharp `Vector2d` and fixed-point
scalar rotation, GridForge, existing A*/Flow services, Chronicler, xUnit v3,
and Gravitas `SolidBody2D` for joint acceptance.

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
  and [TraversalEvaluator.cs](../../src/Trailblazer/Pathing/Graph/Traversal/TraversalEvaluator.cs):
  authoritative body dimensions, capability admission, and vertical leg proof.
- [Motor](../../src/Trailblazer/Navigation/Motor),
  [steering](../../src/Trailblazer/Navigation/Steering), and
  [turning](../../src/Trailblazer/Navigation/Turning): separate dimension-neutral
  control rules from horizontal/vertical and quaternion assumptions.
- [Search](../../src/Trailblazer/Pathing/Search),
  [graph tests](../../tests/Trailblazer.Tests/Pathing/Graph), and
  [navigation tests](../../tests/Trailblazer.Tests/Navigation): preserve existing
  costs, stale handling, transition ownership, and 3D regression coverage.
- [Serialization guide](../api/guides/Serialization.md) and
  [NavigationAgentProfileRecord.cs](../../src/Trailblazer/Navigation/Serialization/NavigationAgentProfileRecord.cs):
  dimensional identity and staged population cannot be inferred from a shell.

In Gravitas, read `AGENTS.md`, `docs/api/guides/DIMENSIONS.md`,
`docs/api/guides/HOST_INTEGRATION.md`, `src/Gravitas/Core/2D/Bodies/SolidBody2D.cs`,
`src/Gravitas/Core/2D/Bodies/SolidBody2D.Grounding.cs`, and
`tests/Gravitas.Tests/Physics2D/SolidBody2DHostContractTests.cs`.

Planned responsibility boundaries, not files to scaffold ahead of the contract:

| Area | Deliverable |
| --- | --- |
| Trailblazer navigation/controller source | Explicit 2D controller, motion, support, and facing contracts; only genuinely shared internal helpers |
| Trailblazer query/graph source | Planar body/profile and traversal proof integrated with shared search machinery |
| Matching core test areas | Native 2D acceptance, graph semantics, replay, serialization, and preserved 3D behavior |
| Optional Gravitas adapter and joint tests | `SolidBody2D` target/accepted-pose bridge and native planar collision scenarios |
| DocFX guides, API reference, behavioral tests | Top-down/platformer guidance linked to tested host fixtures, with explicit coordinates and ownership; no separate sample project |

The upstream APIs below supply the prerequisite file and ownership boundaries.
Phase 0 must still finish the native controller, map and record signatures and
their acceptance proofs before Phase 1.

## Upstream prerequisite contracts

These prerequisite boundaries are grounded in the
[bounded-primitive investigation](#2026-09-21---bounded-planar-primitive-investigation).
Keep the existing 3D APIs and behavior intact. Reuse graph/search ownership,
GridForge's exact prism/address machinery, and Gravitas's collider identity;
do not rotate 2D inputs into the 3D navigation controller.

### 1. Exact planar clearance: FixedMathSharp, then GridForge

FixedMathSharp 8.0.0 ships exact strict-swept and closed-stationary capsule
relations. GridForge 10.0.0 consumes them in the layer-bound body trace, which
reuses the existing trace result, evidence, and scratch types. Rejected
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

The pinned Gravitas source exposes a support-specific operation on
`GravitasQuery2DService`.

```csharp
public Physics2DSupportQueryStatus QuerySupport(
    in Physics2DSupportQuery request, out Physics2DSupportHit support);
```

The request, result, and status contracts are:

| Type | Required fields / behavior |
| --- | --- |
| `Physics2DSupportQuery` | Registered source collider, probe center, positive radius/distance, upward direction, minimum upward-normal dot in `(0,1]`, include-layer mask. The constructor normalizes the nonzero upward direction using FixedMathSharp. Initial Trailblazer side-view supplies `Vector2d.Forward`. |
| `Physics2DSupportHit` | Existing `Physics2DHit` plus captured owner registration lifetime and sampled `long` frame from the shared clock; `IsCurrentLifetime` is false for default or recycled registrations. It does not promise geometric freshness. |
| `Physics2DSupportQueryStatus` | `Found`, `NoSupport`, `WorldNotReady`, `Unrepresentable`. Only the first two certify complete examination; all others clear the usable hit. |

Sample at a stable between-frame boundary. The query validates the source
registration and returns `WorldNotReady` when Gravitas cannot reconcile the
partitions at that boundary. It reuses indexed candidate collection and
query-owned buffers. Physics partitions use **world Y = 0**, independent of the
navigation embedding and actor host Y. Padded discovery uses checked probe
bounds. Measure allocation and full-frame cost in representative acceptance
cases because the query can grow its buffers and has no caller work ceiling.

Support probes accept positive distances down to one raw unit. Owning tests
exercise initial support and separated inputs around the generic sweep's
`Fixed64.Epsilon` cutoff.

The Gravitas sampling boundary reconciles GridWorld grid and sparse changes and
refreshes callback-deferred memberships. The adapter uses that readiness
contract and samples support without its own partition repair.

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

The query lives in `Queries/2D/GravitasQuery2DService.SupportQuery.cs`. The
request, hit, and status live under `Queries/2D/Support/`, with focused owning
tests in `Physics2D/Physics2DSupportQueryTests.cs`.

### 3. Atomic native posture replacement: Gravitas

```csharp
// Implemented SolidBody2D operation; the existing SolidBody signature is retained.
public ColliderReconfigurationStatus TryReconfigureCollider(
    ColliderShapeDefinition2D definition, Vector2d localOffset, Vector2d position,
    out LSCollider2D? blocker,
    out Exception? notificationException,
    Action? publishSynchronizedState = null);
```

Reuse `Applied`, `Unchanged`, `Blocked` without adding budget statuses; preserve
existing enum values and the 3D transaction contract. Invalid family/definition,
unsafe lifecycle and unrepresentable candidate/host pose retain the existing 3D
exception conventions. Support only
circle-to-circle and capsule-to-capsule initially, in pure TwoD mode; reject
mixed mode rather than silently skip 3D blockers. A compact side-view body is
still the same capsule with height `2r`, not a new collider identity.

Use native exact penetration classification and detached shape staging to prove
the **whole** replacement collider. No public capsule overlap API is needed.
Dynamic colliders remain eligible blockers even though they are not supported
carriers. Exact geometric touching is allowed; positive penetration rejects,
including intrusion whose solver depth would round to zero.
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

The transaction lives in `Core/2D/Bodies/SolidBody2D.ColliderReconfiguration.cs`.
Its lifetime-tokened pair retirement preserves hierarchy and filter
relationships and continues cleanup when callbacks throw or recycle objects.
The focused transaction and notification tests are in
`Physics2D/SolidBody2DColliderReconfigurationTests*.cs`.

Trailblazer's eventual replacement preserves the nominal foot and durable
destination, not the old physical surface contact. Fresh support follows an
accepted replacement. Radius growth on a slope may be blocked despite reduced
height; do not move the body silently to make it fit.

### Implementation order and acceptance gate

1. FixedMathSharp ships the strict relation with coverage for exact tangent,
   one-raw penetration/separation, stationary/vertical/diagonal sweeps, zero/odd
   axis lengths, containment, middle-only collision and scalar-domain limits.
2. GridForge ships the layer-bound union proof with coverage for wide bodies,
   sparse holes, world edges, both hex orientations, aligned seams, diagonal closure,
   non-neighbor and zero-radius boundary-axis rejection, explicit wrong
   layer/domain rejection, missing alternatives, stale generations, every
   exact/one-below work/scratch/output capacity and checked arithmetic failure.
3. Pinned Gravitas source supplies indexed support selection and the complete
   registry posture transaction. Its tests cover compound wall/floor ordering,
   symmetric filters, partition readiness, rejected growth, unchanged
   publication, callback failure, and recycled pairs.
4. Phase 0 binds native top-down and side-view map/action examples to those
   primitives and records the controller, graph, and record contracts.
   Start Phase 1 with the first failing native top-down acceptance cases.

Use the existing test projects, standard/Lean and both library targets, exact
changed-path coverage, and independent source review. Shared hot-path changes
require the existing matched performance controls.

## Progress dashboard

| Phase | Deliverable | Status | Gate |
| --- | --- | --- | --- |
| 0 | Reviewed dimensional and API contract | Complete | Exact geometry, support, graph, lifecycle, file, and serialization decisions plus early native physics proof |
| 1 | Native planar guidance and top-down movement | In progress | A*/Flow, planar clearance, accepted motion, and action boundaries verified |
| 2 | Side-view traversal and profile changes | Not started | In-plane support, locomotion, posture/capability sequences, and replay verified |
| 3 | Gravitas2D integration and closeout | Not started | Real planar collisions, behavioral examples, full matrix, and review |

## Phase 0 - Settle The 2D Contract

- [x] **Probe the real Gravitas2D boundary early.** Verify kinematic wall and
  floor/ceiling accepted motion, preserved host world Y, scalar rotation, and
  public support queries on bounded inputs. Identify missing upstream contracts
  before committing to the controller design. Reuse focused tests or an ignored
  throwaway probe; do not scaffold a sample application or production adapter.
- [x] **Review the [controller surface](#public-2d-controller-contract).**
  Record the native API, ownership, and frame lifecycle before implementation.
- [x] **Choose and validate the [graph representation](#planar-graph-query-and-action-contracts).**
  Retain the graph and search owners, with native leg and support proofs.
  Check the two bounded fixtures against the released geometry primitives.
- [x] **Define media, capability, and support semantics.** Explain how top-down
  traversability and side-view airborne/grounded state relate to navigation
  media. A downward direction alone cannot identify falling, and a permitted
  cell cannot stand in for actual physical support. No automatic gap-crossing
  action may arise from ordinary neighbor traversal.
- [x] **Set [compatibility and persistence boundaries](#records-proof-identity-and-parity-gates).**
  Keep spatial public types and records stable and define planar staged load.
- [x] **Set scheduling, proof identity, and performance boundaries.**
  Preserve shared queue and proof accounting with exact dimensional identity.
  Name matched 3D controls to capture before the first shared runtime change.
- [x] **Review [bounded acceptance inputs](#bounded-acceptance-inputs).**
  Record fixture geometry, motion, costs, budgets, expected addresses, and
  frame caps for the two modes. Runtime acceptance belongs to Phases 1–3.

### Public 2D controller contract

Use a native `Navigator2D` for both modes. It owns the accepted state and
coordinates the frame. Hosts implement the physics boundary through protected
hooks, as the optional Gravitas adapter does for 3D. These declarations
describe the Phase 0 design. The graph, acquisition, action, and record
contracts below complete the controller boundary.

In `Trailblazer.Pathing`, expose an immutable `NavigationAgentProfile2D`.
Its constructor arguments below have matching get-only properties in
PascalCase. Include these fields in value equality. Keep motor tuning
separate from the profile used for route admission.

```csharp
public enum NavigationMode2D : byte { TopDown = 1, SideView = 2 }

// Constructor on readonly struct NavigationAgentProfile2D.
public NavigationAgentProfile2D(
    NavigationMode2D mode, Fixed64 radius, Fixed64 axisLength,
    Fixed64 maxStepUp, Fixed64 maxDropDown, Fixed64 minimumSupportNormalDot,
    Fixed64 arrivalRadius, TraversalMedia allowedMedia, TraversalCapability capabilities);
```

In `Trailblazer.Navigation`, the value-type constructor signatures below
describe readonly structs with get-only properties named from their arguments.

```csharp
public NavigationPose2D(Vector2d center, Fixed64 rotationRadians);
public NavigationInput2D(
    Vector2d? move, bool jumpRequested, Vector2d? facing = null, bool blockJump = false);
public NavigationMotorSettings2D(
    Fixed64 moveSpeed, Fixed64 turnRateRadians, Fixed64 gravity, Fixed64 jumpSpeed);
// Issued by Navigator2D, with an internal constructor.
internal NavigationMotionRequest2D(long frame, NavigationPose2D target);
public enum NavigationSupportStatus2D : byte { NotApplicable, NoSupport, Found, Unavailable }

public NavigationCarrier2D(
    int stableId, long lifetime, NavigationPose2D pose, Vector2d localAnchor);
public NavigationSupport2D(
    Vector2d point, Vector2d normal, Fixed64 friction, NavigationCarrier2D? carrier);
public NavigationMotionResult2D(
    NavigationPose2D accepted, NavigationSupportStatus2D supportStatus, NavigationSupport2D? support);

public abstract class Navigator2D : IDisposable, IRecordable
{
    protected Navigator2D(
        TrailblazerWorldContext context, string initialMapId, Guid globalId, NavigationPose2D initialPose,
        NavigationAgentProfile2D profile, NavigationMotorSettings2D motorSettings);

    public Guid GlobalId { get; }
    public NavigationPose2D Pose { get; }
    public Vector2d Velocity { get; }
    public Vector2d Facing { get; }
    public NavigationAgentProfile2D NavigationProfile { get; }
    public NavigationSupportStatus2D SupportStatus { get; }
    public NavigationSupport2D? Support { get; }
    public bool IsGrounded { get; }
    public byte OccupantGroupId { get; set; }
    public NavigationAcquisitionMode AcquisitionMode { get; set; }
    public NavigationGuideStatus? GuidanceStatus { get; }
    public NavigationCommittedCellState? LastCommittedCell { get; }
    public NavigationTransitionInstruction2D? PendingTransition { get; }
    public event Action<NavigationCommittedCellState?>? CommittedCellChanged;

    public void PrepareFrameMotion(in NavigationInput2D input);
    public NavigationGuideStatus PrepareActionFrameMotion(
        in NavigationTransitionInstruction2D instruction, NavigationPose2D target);
    public void CommitFrameMotion();
    public void RefreshSupport();
    public void ApplyGuidedTrekRequest(PathQuery2D query, int groupId = -1);
    public void CancelGuidance();
    public NavigationGuideStatus CompletePendingTransition(in NavigationTransitionInstruction2D instruction);
    public NavigationProfileReplacementStatus TryReplaceNavigationProfile(
        NavigationAgentProfile2D replacement);
    public void Dispose();
    public void RecordData(IChronicler chronicler);

    protected abstract void SubmitMotionTarget(in NavigationMotionRequest2D request);
    protected abstract NavigationMotionResult2D ReadAcceptedMotion(
        NavigationMotionRequest2D? request);
    protected virtual bool TryReadCarrierPose(
        int stableId, long lifetime, out NavigationPose2D pose);
    protected abstract bool TryCommitPhysicalProfileReplacement(
        NavigationAgentProfile2D replacement, Vector2d replacementCenter,
        Action publishNavigationState, out Exception? notificationException);
}
```

**Coordinates and validation.** Center is the collider center in world X/Z.
Positions and distances use world units. Speed is units per second, gravity is
units per second squared, and turn rate is radians per second. Positive rotation
runs from planar Right toward Forward and is canonical in `[-π, π)`. Top-down
facing follows accepted rotation. Side-view uses physical rotation zero and a
logical Left or Right facing. It starts at Right, follows nonzero horizontal
intent, and holds its last facing when stopped. Carrier rotation can move the
passenger through its anchor while the capsule stays upright.
The constructor binds a published initial map of the matching mode. Its
embedding Y fixes the navigation domain for manual and guided motion. The
accepted position can resolve into another compatible map in that domain.
The physical host preserves its own world Y.

Require a positive radius and a non-negative cap-center separation `axisLength`
with representable total height `axisLength + 2*radius`. Top-down has zero axis
length, step, drop, support threshold, gravity, and jump speed. Side-view uses
positive gravity along `-Vector2d.Forward` and a support normal threshold in
`(0, 1]`. A compact side-view posture uses the same capsule with zero axis
length. Its nominal foot is the center minus Forward times
`radius + axisLength/2`. Materialize the non-negative half-axis by rounding
down to a raw unit for that point. Exact clearance still takes the full axis
length. Preserve the nominal foot across side-view posture changes and the
center across top-down radius changes.

`Move` is a dimensionless analog input of magnitude at most one. Zero requests a
stop. Null selects guidance, with zero voluntary heading while waiting or
without an active guide. A manual move replaces guidance, as on the 3D
Navigator. An optional nonzero facing direction overrides move-facing for
that frame. Ordinary side-view movement and facing are horizontal. The host
supplies `JumpRequested` on the input edge and can deny the jump with
`BlockJump`. Trailblazer admits the request through its motor and capability
policy. Completing an authored guide action remains a separate operation.
Motor speed and turn
rate are non-negative. Side-view facing needs zero physical turn rate and its
jump speed is non-negative. The host supplies a nonempty, world-unique actor
ID. Validate constructor arguments and known mode/media/capability values.
Construction invokes no host hooks. Check inputs and target representability
before submission, and reject invalid results or unrepresentable accepted
motion before changing pose or occupancy. A replacement with a different mode
returns `InvalidProfile` on the bound controller.

**Prepare, resolve, commit.** The host advances the contexts once, prepares
every controller in deterministic order, resolves the shared physics world
once, then commits every controller before the navigation late step. Core
holds one prepared request per controller with its context clock lifetime and
absolute frame. The request target is an absolute pose. Prepare stages motion
without publishing accepted pose, velocity, facing, occupancy, or cell
notifications.
Reject duplicate prepare, unprepared or repeated commit, and stale context or
frame before publication. The host supplies the matching resolved result.
The adapter validates body registration and the physics barrier where the
physics lifecycle exposes it. Consume the prepared state before notifying
listeners. Defer authoritative writes from listeners to ordered commands
after the barrier so one actor's notification cannot change another actor's
support sample. Failed target submission, an aborted physics step, or an
accepted result that cannot be committed invalidates the authoritative frame.
The host cleans up pending targets and recovers from a coordinated checkpoint.

Commit derives observable velocity from accepted displacement and the
prepared fixed step, including any accepted carry. Motor intent keeps its own
velocity state so the next frame cannot apply carry a second time. Commit
publishes one accepted pose and cell and samples support at the accepted
pose. `Found` supplies a physical witness and normal. Motor policy decides
whether that sample grounds the controller, including when it is jumping away.
`NoSupport` is a complete miss. `Unavailable` commits the accepted motion with
no fresh ground or carrier state and cannot reuse the prior sample.
`NotApplicable` is required for top-down and invalid for side-view. Only
`Found` carries a snapshot.
The carrier-local anchor is the physical witness transformed into the
carrier's frame at sampling time. For a live host-stable positive ID and
matching registration lifetime, carry is the difference between the new and
previous transforms of that same anchor. Equal poses produce exact zero
displacement. A failed carrier lookup drops the binding and suppresses carry.
Dispose releases navigation ownership and invalidates prepared motion. Host
body and world lifetimes remain with the host.

Call `RefreshSupport()` at the stable between-frame boundary to seed side-view
support after construction, successful restore, or accepted posture
replacement. The host hook receives a null request and reads the current
accepted pose, which must agree with the controller. This refresh updates
support without submitting or committing motion. After restore, seed from the
restored body and carrier poses before applying the first frame's commands.
Normal frames keep the previous committed sample for the carrier delta.
Rebind any recorded carry continuation to the newly sampled registration
before the first prepared frame. See the record contract below.

**Posture transaction.** Between frames, with no prepared motion or held
action, the controller stages navigation fit, occupancy, and durable intent
before asking the host to replace the physical envelope. An accepted host
change invokes the publisher exactly once before post-commit notifications.
A rejected change leaves both sides unchanged and skips the publisher.
Discard old support after an accepted change and acquire fresh support before
using ground or carry again. Surface post-commit notification failures
separately from a rejected replacement.

| Owner | Planned files and responsibility |
| --- | --- |
| Trailblazer core | `src/Trailblazer/Navigation/Navigator/Navigator2D.cs` and native motion/support values in the same area. Own input interpretation, motor state, frame guards, accepted pose and velocity, support policy, carry, replacement coordination, and occupancy publication. |
| Trailblazer pathing | `src/Trailblazer/Pathing/Query/NavigationAgentProfile2D.cs` owns geometry and traversal admission inputs. Existing graph/search owners supply dimensional clearance, leases, and scheduling. Existing `NavigationCommittedCellState` carries the committed cell without a pose conversion. |
| Optional adapter | `src/Trailblazer.Gravitas/GravitasNavigator2D.cs` binds a host-owned kinematic `SolidBody2D` in the same GridWorld. It validates the centered circle or upright capsule, submits X/Z targets, and reads the accepted pose. In side-view it translates `QuerySupport` at the lower cap. An expired hit or a witness that `Physics2DHit.TryGetPoint` cannot materialize becomes `Unavailable`. It calls `TryReconfigureCollider` for physical fit. |
| Host and Gravitas | Host owns ordered input, time advancement, body and map setup, carrier identity, and semantic action execution. Gravitas owns colliders, filtering, physical witnesses, resolution, and accepted body pose. |
| Existing test projects | `tests/Trailblazer.Tests/Navigation/Navigator/` covers accepted motion, stale and repeated frame calls, support/motor policy, and occupancy. `tests/Trailblazer.Gravitas.Tests/` covers the shared resolve barrier, native shape/support, carry, and posture with real bodies. |

### Planar graph, query, and action contracts

Use one published graph, one map per GridForge configuration, and one shared
search and acquisition pipeline. A separate planar graph would duplicate
publication, invalidation, and lifetime ownership. An additive binding on the existing
map identifies the mode, layer index, and embedding Y. Connected planar
maps share the mode and embedding Y. A spatial map has no planar binding.
Keep the existing configuration index and its uniqueness rule. A second
layer or a second mode on the same configuration would require a different
publication identity and is outside this contract.

Add these methods to `NavigationMapBuilder`, returning the same builder.
`NavigationMap.PlanarBinding` is a nullable get-only
`NavigationPlanarBinding` value with get-only `Mode`, `LayerIndex`, and
`EmbeddingY`. Keep existing constructors and default behavior spatial.

```csharp
public NavigationMapBuilder SetPlanarLayer(NavigationMode2D mode, int layerIndex, Fixed64 embeddingY);
public NavigationMapBuilder AddPlanarSupport(VoxelIndex index, Vector2d left, Vector2d right);

// Constructor on readonly struct NavigationEndpoint2D with get-only properties.
public NavigationEndpoint2D(
    string mapId, Vector2d position, EndpointResolutionPolicy resolution, Fixed64 maxResolutionDistance);

// Constructor on readonly struct PathQuery2D with corresponding get-only properties.
public PathQuery2D(
    NavigationEndpoint2D start, NavigationEndpoint2D end, NavigationAgentProfile2D agent,
    NavigationAreaPolicyKey areaPolicy, TraversalIntent traversal, PathAlgorithm algorithm,
    NavigationWorkBudget budget, bool allowTransitions, FlowFieldQueryOptions flowField = default);
```

- Bind and validate the exact layer and embedding during map admission. Limit
  defaults, explicit cells, overlays, connections, seams, and fallback endpoint
  search to that domain and layer. Reject spatial/planar, top-down/side-view,
  and incompatible embedding links before publication. Include the binding
  and canonical support segments in bake equality and dependency identity.
  Change support geometry by replacing the bake. Existing semantic overlays
  can disable a cell and its support. Planar transitions use the existing
  authored edge with canonical anchors. Reject world-Y rule generation and
  3D point overrides on planar maps.
- Require explicit map IDs on planar endpoints. Resolve top-down coordinates
  at the center and side-view Solid coordinates at the profile's nominal
  foot. Side-view support stores one finite left-to-right surface segment
  per supported cell in this increment. Author separate cells for separate
  support loci. Its X span is positive and its
  endpoints belong to the authored footprint, with boundary contact allowed.
  Project onto the profile-adjusted nominal-foot segment, which is the
  physical segment translated by `radius*(normal - Forward)`. Reconstruct
  the capsule center as witness plus `radius*normal + halfAxis*Forward` and
  re-prove the materialized body against exact clearance, including odd raw
  axes. Use the segment midpoint for the canonical cell anchor. `Strict`
  requires the requested point itself to be admissible. Only
  `NearestNavigable` can move the endpoint, within its distance in native
  query coordinates. At a footprint boundary enumerate closed-contact
  candidates in durable address order, including the owning support cell.
  Apply the same normal-dot threshold to graph and live support. Charge each
  inspected candidate.
- For an ordinary side-view Solid edge along a surface, prove continuous support across
  its ordered segments and certify the full swept capsule for each leg.
  A corner join must keep support and fit through the turn. A supported
  step uses directed step/drop limits and separate lift, advance, and
  supported landing proofs. A step may temporarily leave contact during its
  bounded lift. An unsupported gap requires a held action. Decline an
  ordinary edge when its join, landing, or candidate evidence is incomplete.
- Separate clearance from route state. Required footprint cells must be
  present and semantically open for body coverage, including headroom
  without a Solid support segment. For side-view Solid, use present authored
  cells with allowed area and capabilities and no active blocker for that
  coverage, without requiring their medium to be Solid. A missing or denied
  cell blocks the footprint. Endpoint and ordinary support admission still
  require the route medium and applicable clearance constraints. Apply
  entry costs only at route entry, never for headroom cells.
- Reuse the existing exact cell/medium state, edge enumeration, dependency
  recording, and cache admission. Dispatch only the planar geometry for
  endpoints, directed legs, direct rays, connectors, simplification, and
  sampling. Use `TracePlanarNavigationBodyInto` for one selected neighbor
  leg, with the existing graph generation snapshot. For longer rays prove
  each selected leg and retain nonincreasing shortcut cost. Use the existing
  lookup and covered-voxel budget counters for the corresponding grid and
  address work, with caller ceilings applied before trace entry. Leave the
  3D slab path and its mapping memo in the spatial evaluator.
- Reuse current native neighbor ordering, including four cardinal directions
  for rectangular ordinary edges. Charge checked, outward-rounded in-plane length on ordinary legs plus
  destination cell and area entry costs. Actions charge proved approach and
  exit lengths, authored action cost, and destination entry. Use a zero
  heuristic for initial planar A*. Skip spatial component early rejection
  for both planar A* and Flow. Flow uses the existing incoming index and
  re-evaluates the predecessor's forward edge, including asymmetric
  step/drop limits, costs, and action identity.

The controller is the native query entry for this increment. Normalize
`PathQuery2D` and spatial `PathQuery` once into a discriminated internal
request for existing search, lease accounting, and scheduling. Keep native
coordinates at the 2D boundary. Use ordinal map ID equality and the existing
deterministic hash conventions for keys. Retain the public 3D query and lease
shapes.
Validate a new query against the controller's exact profile before replacing
intent. Use current accepted geometry for reacquisition, as the 3D Navigator
does. `GuidanceStatus` reports the current admission or guide status and is
null without a session. `NavigationTransitionInstruction2D` is an immutable lease-issued value
with `Id`, `OwnerMapId`, `Type`, `SourceAddress`, `DestinationAddress`,
`SourceMedium`, `DestinationMedium`, and native `Vector2d SourcePosition` and
`DestinationPosition`. Reuse the existing hidden completion stamp and its
owner, acquisition generation, and edge checks.

While an action is held, ordinary guided advance pauses. The host supplies
an explicit target through `PrepareActionFrameMotion`, which verifies the
live token and uses the same prepare/resolve/commit transaction. The host
owns the trajectory during that action. Airborne state during a Solid-to-Solid
jump leaves the guide medium intact. Complete once after the accepted landing
proves the destination anchor and medium. Wrong landing returns `InvalidEnd`
and leaves the token held. Stale, cancelled, or consumed instructions return
`Stale` and cannot prepare or complete an action.

Register physical occupancy at the accepted center projected into the exact
selected map layer. Resolve coincident eligible candidates in canonical
durable address order. Resolve `LastCommittedCell` with that accepted-center
registration and the effective committed medium, matching 3D event semantics.
Use the side-view nominal foot for route and landing proof. An airborne actor
can occupy a cell without admitting an ordinary walking anchor. Leaving the
eligible map releases the old ticket. Share ticket and notification semantics
around the existing GridForge owner without exposing a 3D pose on the controller.

### Records, proof identity, and parity gates

Use a distinct `Trailblazer.Navigator2D` record kind and schema version 1
under `Navigation/Navigator`. Reuse the existing Chronicler staging and
populate-existing-shell pattern. Validate kind, version, mode, exact shell
profile and motor settings, stable ID, published map/policy binding, pose,
known media, and every nested continuation before mutation. The host
restores and publishes the matching world and collider poses before load
at a completed-frame boundary with no unresolved target.
Successful load rebuilds occupancy and committed-cell state without emitting
`CommittedCellChanged`. GridForge retains its normal occupancy events.
Rejected load preserves the old controller, lease, action, scheduled
interest, prepared frame, and occupancy. Keep spatial schema 9 unchanged.

Record pose, accepted velocity, logical facing and active/buffered turn,
motor and jump continuation, group and current medium, acquisition mode, and
durable destination/query options. Preserve exact carry continuation as
stable carrier ID, previous accepted carrier pose, and the raw carrier-local
anchor used by the motor. This follows 3D's exact attachment replay rule.
Rebind it only to a freshly sampled registration for the same stable carrier
at the recorded prior pose, before any frame commands. A mismatch starts a
new attachment with zero initial carry. Keep physical witness snapshots,
runtime lifetime tokens, graph objects, tickets, prepared frames,
leases/cursors, and pending action stamps transient. Cover JSON in both
builds and MemoryPack in Release. Exact replay
compares branches with the same published world, input order, and acquisition
state at the checkpoint. A fresh guide rebuild may change timing relative to a
warm lease, so exercise that separately against an equivalent reacquisition.

Keep Immediate as the default and Scheduled on the existing bounded FIFO.
Preserve frozen contiguous cohorts, oldest-first builds, independent follower
admission, source pin accounting, one result transfer, cancellation, and
the existing fair moved-origin connector validation. Waiting continues motor,
carry, and accepted commit. Keep route/direct flavor separate. A* keys include
normalized exact intent and resolved start/end/media. Flow keys omit the
source as today and keep exact destination/profile/policy/budget/options and
resolved media. Cohorts retain their current compatibility rules with an
explicit dimension and mode discriminator. Profile geometry, support policy,
and exact endpoint domain participate in keys. Bind layer, embedding, and
surface content through the immutable bake dependencies instead of copying
them into every token. Record missing cells and physical alternatives as
well as positive evidence. Use the existing conservative world stamp when a
negative dependency cannot be expressed more narrowly. Check exact retained
bytes and allocations before sharing any new descriptor or proof layout
with 3D. Keep spatial requests on their existing storage when a planar-only
field would inflate that path.

| Preserve from 3D | Reuse boundary |
| --- | --- |
| Frame lifecycle, occupancy, and replacement | Reuse clock/lifetime checks, GridForge ticket operations, committed-cell value, and validation-before-publication sequence. Share dimension-neutral operations once both callers exist. Keep root/foot math and accepted pose handling native. |
| Guidance, actions, and scheduling | Reuse graph publication, A*/Flow, costs/status policy, exact action-stamp rules, budgets, and per-lease ownership. Add planar geometry at proof boundaries. |
| Turning, locomotion, and carry | Preserve requested versus accepted motion, blocked-motion behavior, action/capability transitions, and continuation. Reuse state rules with identical semantics. Use fixed planar angle, capsule support, and native carrier-anchor math rather than wrapping the world-Y motor. |
| Performance | Capture matched existing `navigation-guided-frame`, `navigation-ray`, and `navigation-recovery` controls before the first affected shared runtime edit. Keep build, dependency pins, scenario and acquisition settings fixed, with full-frame time, allocation, counts, and replay. Add planar open/blocked and support/action rows to the existing benchmark project only after their production paths run. |

### Bounded acceptance inputs

Use a dense rectangular unit grid with center bounds `(-1,0,-1)` through
`(5,1,2)`, selected layer 1 and embedding Y = 1. The tables below use
planar coordinates `(x,z)`, corresponding to `VoxelIndex(x+1,1,z+1)` at
integer centers. Use separate maps/worlds for the two modes. Use Strict
endpoints, Solid intent, zero arrival radius, zero step/drop limits, and an
allowed zero-cost area except where specified. On open cells set the authored
radius and height limits to 1 and 2 respectively so the footprint decides fit.
Run A* and Flow with the same intent, first Immediate, then
Scheduled for lifecycle parity. Capture ordered frames, raw accepted
pose/velocity, committed addresses, action outcomes, and retained ownership.

| Case | Fixture and expected outcome |
| --- | --- |
| Top-down obstacle | Circle radius 1/4. Start `(0,0)`, end `(4,0)`. Open the lower row at x = 0, 1, 3, 4 and the entire upper row z = 1 at x = 0..4. Leave `(2,0)` denied. The direct line fails. Both searches reach `(4,0)` through `(2,1)` at exact cost 6 and never commit the denied address. In a separate corridor, permit only z = 1. Radius 1/2 fits with tangency. Radius 1/2 plus one raw unit requires denied rows. |
| Side-view ledge and ceiling | Supports at z = -1/2 span the full widths of x = 0, 1 and x = 3, 4 in row z = 0. The gap cell x = 2 is physically open and has no Solid anchor. Open headroom on the left and deny row z = 1 at x = 3, 4 for the ceiling. Radius 1/4 and axis 3/4 make standing height 5/4 and block the landing. Axis 0 keeps the same capsule and fits at height 1/2. Height 1 touches the ceiling and fits. Two extra raw axis units at the same nominal foot fail clearance. |
| Directed action | Use a Solid-to-Solid Jump from foot `(1,-1/2)` to `(3,-1/2)` with authored cost 3, Jump capability, and destination cell enter cost 1/2 at x = 3. Compact start/end feet are `(0,-1/2)` and `(4,-1/2)`. Expected cost is 11/2. Reverse, missing Jump, and disabled actions all yield NoPath. Standing with the ceiling fails end admission. Give the host ten 1/4-unit action increments, one up at x = 1, five right to x = 9/4, one down, and three right to x = 3. Check accepted landing and exactly one completion. A stale token and early completion fail. |

Use 16 Hz and speed 4. Top-down uses zero gravity, fixed physical rotation,
and a 40-frame cap. Side-view uses gravity 16, jump speed 4, minimum support
dot 1/2, and a 40-frame cap. Scripted action motion follows the action
targets above through the host barrier. Eight ordinary horizontal movement
frames plus ten action frames provide the ideal side-view movement count.
Use `NavigationWorkBudget(8192, 128, 128, 512, 512, 128, 128, 128, 1024, 4096, 0)`
and zero extra Flow integration cost. The route-cost oracle disables
simplification. Test shortcuts separately with a positive simplification
budget. Give the body scratch 8 grid entries and 128 addresses/output entries,
with 136 candidate-work units per primitive leg. Record actual work and retain
exact one-below tests at the proof boundary. Scheduled cases use
`NavigationSchedulingSettings.Default`, a fixed 128-frame cap, and separate
preparation and movement counts. These are test limits.

For the slope control, keep the earlier exact 45-degree witness and require
its normal to pass a 1/2 threshold and fail 3/4. For the step control, use the
height-1 capsule with radius 1/4. Place adjacent supports at z = -1/2 and -1/4
on cells x = 0 and 1. Lift the center 1/4 at x = 0, advance to x = 1 and
verify landing. Max step 1/4 allows the rise and max drop 1/8 rejects the
reverse. A ceiling starting at z = 1/2 above x = 0 blocks the lift.
For replay checkpoint the compact side-view body on a rotating carrier,
publish its restored pose before the first command, and compare 32 frames
of exact continuation plus an unchanged rotated pose with zero carry.
Exercise stale/duplicate frame calls, clock reset, unavailable support,
cancelled actions, wrong-mode restore, and exact cleanup as negative controls
in the existing test projects.

## Phase 1 - Native Planar Guidance And Top-Down Movement

- [x] **Capture the pre-edit 3D controls and bake the first fixtures.** Reuse
  the map builder for immutable top-down layer identity and canonical authored
  cells. Keep their metadata separate from the spatial bake layout.
- [x] **Publish layer-bound top-down cells.** Use the existing immutable
  map and overlay pipeline, capture only selected-layer defaults, and
  isolate spatial queries, raw trace coverage, committed cells, and edges.
- [x] **Admit a strict top-down circle endpoint.** Use exact stationary GridForge
  coverage on the selected layer and compatible neighbors. Expose a bounded
  placement check for hosts and keep native route acquisition controller-owned.
- [x] **Prove one ordinary top-down leg.** Use the same circle evaluator for the
  full center-to-center sweep. Preserve cardinal rectangular neighbors, native
  hex neighbors, and exact compatible footprints across maps.
- [x] **Route the obstacle detour and fitted portal.** Both shared searches
  admit center-address endpoints and prove each native leg on one planar map.
  Return a detached sequence and exact cost from the captured publication.
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
  any runnable documentation snippets and update XML and DocFX guides together.
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
Each entry records the evidence available at that checkpoint. The
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

### 2026-09-24 - Complete exact posture admission and final verification

The shared repair now classifies strict penetration before any solver-depth
rounding. Native circle/capsule and 3D sphere/capsule/cylinder replacements cover
all supported targets, compound leaves and transformed/scaled mesh geometry.
Closed convex meshes include enclosure; open/concave meshes remain surface
geometry. The dispatcher preserves physical filtering, stable root blocker
identity, complete registered-body discovery and transactional rejection.

FixedMathSharp owns the complete curved-feature math: cap-clipped polytope
features, capsule/rim quartics, finite cap disks, common-sign circle arcs and
cone generators. It uses fixed stack workspaces, exact rigid frames and bounded
integer sign decisions. Independent review checked feature completeness,
intermediate-width bounds and conservative broad-phase bounds. Factorized and
integer-geometry oracles cover repeated roots, exact tangency, sub-raw intrusion,
distinct oblique frames and full-domain sizes. No epsilon, rounded endpoint,
sampled separating-axis fallback or production arbitrary-precision dependency
was introduced. The six new curved-shape helpers have complete line/branch
coverage in both configurations.

Three review-found circle-sign defects were reproduced and repaired before
the final geometry commit `7f0b9a9`. Complete 3D dispatch is in Gravitas
`516327b`; capsule and cylinder/polytope kernels have their own earlier commits.
The independent ordinary-contact regressions remain separate commits rather
than being removed to make the stack appear green.

The investigation also explained a previously stalled contact test. Reducing
a cylinder-pair axis to small integer components made its floored-root depth
estimate tens of millions of raw units inaccurate. The old loop corrected one
unit at a time. FixedMathSharp `e7a001b` retains four exact correction attempts,
then uses its existing bounded exact search. This changes work, not accepted
geometry, normal/witness selection, nearest-even rounding or clamping. The
eight independent rounding controls pass; the owning durable RCA is
`FixedMathSharp/docs/feature-work/done/2026-09-24-cylinder-pair-depth-rounding.md`.

#### Complete functional matrix

All runs used Windows, the .NET 8 runtime, and `UseLocalLsfStack=true` with
serialized builds. No failing regressions were skipped or excluded.

| Suite | Release | ReleaseLean |
| --- | --- | --- |
| FixedMathSharp core | 3,123 passed; 6 known ordinary-contact failures | 3,102 passed; the same 6 failures |
| FixedMathSharp.Chronicler | 49 passed | 49 passed |
| Gravitas | 4,274 passed; 1 known ordinary-contact failure | 4,215 passed; the same failure |
| Trailblazer core | 3,245 passed | 3,146 passed |
| Trailblazer.Gravitas | 90 passed | 86 passed |

FixedMathSharp plus its FluentAssertions assembly and Gravitas each report
**100% line, branch and method coverage** in both configurations. Full
Trailblazer solution builds, including the sibling `netstandard2.1` and `net8.0`
library targets, complete with zero warnings and errors. Chronicler transport
tests were executed, not newly coverage-instrumented by this slice.
DocFX builds for FixedMathSharp, Gravitas and Trailblazer also complete with
warnings treated as errors and zero warnings/errors.

The six math failures are the retained FMS-Issue-023 and FMS-Issue-024 public
contact counterexamples. Gravitas's failure is GRV-Issue-082: a triangle really
enters a cylinder below its cap, but the ordinary contact path misses it. The
new posture transaction blocks that same intrusion. FMS-Issue-025 separately
records the existing cylinder/box contact false positive. Correct Boolean
admission does not manufacture a truthful solver normal, depth or witness;
those owning repairs remain open.

Evidence is under FixedMathSharp `artifacts/rounding-final-release`,
`rounding-full-lean`, and `strict-cylinder-final-coverage-*`; Gravitas
`artifacts/grv081-final-release` and `grv081-final-lean`; and Trailblazer
`artifacts/grv081-stack-build-*` / `grv081-stack-test-*`. Owning tests and
documented numerical reproducers are durable; ignored captures are supporting
evidence. These are source-stack Windows results, not Linux or released-package
certification. Phase 0 contract review and native controller implementation
remain unfinished; no upstream release is authorized by this closeout.

#### Final measured geometry controls

BenchmarkDotNet 0.15.8 default out-of-process jobs used the same Windows 11,
i7-9700K and .NET 8.0.29 environment as the earlier controls. Builds/tests were
serialized and finished before timing. Setup validates the intended results;
all 12 completed rows report **0 B managed allocation** in raw JSON.

| Matched positive geometry | Contact construction mean | Strict classification mean |
| --- | ---: | ---: |
| Cylinder/capsule | 27.477 us | 0.397 us |
| Cylinder/cylinder | 24.881 us | 0.859 us |
| Oriented box/cylinder | 518.14 us, including manifold | 0.315 us |

These are favorable overlap fixtures where a center or core witness can prove
intrusion immediately. They demonstrate work avoided when only a Boolean is
needed, not a universal speedup ratio or an end-to-end frame/transaction claim.
The matching ordinary cylinder controls are effectively unchanged from the
preceding 27.48 us / 25.08 us capture; no ordinary-path regression was observed.

Harder correctness-sensitive rows remain explicit: separated cylinder/capsule
rims cost **95.60 us**, separated cylinder rims **291.90 us**, and the
cap-clipped triangle intrusion **5.45 us**. Cone/capsule and cylinder/cone
positive controls cost **0.98 us** and **0.52 us**. Do not compare the separated
rows to an old contact query that returns the wrong geometric result.
The repaired reduced-axis contact-depth case costs **340.74 us**; its old
runtime exceeded diagnostic timeouts, so no fabricated before/after ratio is
claimed. Whole posture transactions still may scan the registry and allocate.

Capture: FixedMathSharp `artifacts/grv081-final-benchmarks`, with full JSON,
logs, setup checks and the existing runner's failure-aware exit status. The
retained benchmark rows live in `RigidFiniteShapeRelationBenchmarks` and
`OrientedBoxAnchorBenchmarks`; no new benchmark project or CI layer was added.

### 2026-11-09 - Upstream baseline and Phase 0 restart

- **Published foundations.** Chronicler.Core 1.0.0, FixedMathSharp 8.0.0,
  SwiftCollections 8.0.0, and GridForge 10.0.0 are available. FixedMathSharp
  8.0.0 includes the planar capsule predicates and the resolved
  `FMS-Issue-023` through `FMS-Issue-026` repairs. GridForge 10.0.0 includes
  `TracePlanarNavigationBodyInto` and uses FixedMathSharp 8.0.0 in both package
  families. GridForge's `GF-Issue-012` entry still describes the old math
  dependency. Its released-package Windows/Linux matrix passed with 8.0.0.
  [FixedMathSharp closeouts](https://github.com/mrdav30/FixedMathSharp/blob/v8.0.0/docs/feature-work/issue-tracker.md)
  · [GridForge v10 package CI](https://github.com/mrdav30/GridForge/actions/runs/42106692591)
- **Gravitas source baseline.** The pinned `13f73bdc94397a8fdb324b1f67a0c92b472acaac`
  contains `QuerySupport`, `SolidBody2D.TryReconfigureCollider`, and the partition
  readiness and callback lifetime fixes. `GRV-Issue-081` closed exact posture
  admission, and `GRV-Issue-082` independently closed triangle/cylinder contact
  generation. Windows and Linux passed in Release and ReleaseLean on x64 and
  arm64, including replay comparison across the matrix.
  [Gravitas closeouts](https://github.com/mrdav30/Gravitas/blob/13f73bdc94397a8fdb324b1f67a0c92b472acaac/docs/feature-work/issue-tracker.md)
  · [Pinned Gravitas CI](https://github.com/mrdav30/Gravitas/actions/runs/42404575272)
- **Trailblazer development baseline.** Develop at `f3562cd` passed the
  Windows/Linux Release and ReleaseLean source matrix, and the active issue
  and benchmark signal lists are clear. Earlier 2D preflight defects
  `TRB-Issue-150` through `TRB-Issue-152` are resolved. Use
  `UseLocalLsfStack=true` for Phase 0 proofs. The run pins Chronicler
  `7b90d59`, FixedMathSharp `e2c325a`, SwiftCollections `08b1b38`,
  GridForge `c4f80a0`, and Gravitas `13f73bd`.
  [Trailblazer develop CI](https://github.com/mrdav30/Trailblazer/actions/runs/42642641077)
- **Package gate at this checkpoint.** Trailblazer selected GridForge and
  GridForge.Lean 9.1.0 and needed 10.x for the planar trace. The adapter
  selected unpublished Gravitas and Gravitas.Lean 1.1.0. Validate the standard
  and Lean dependency graphs against released packages in dependency order,
  with Gravitas available before releasing Trailblazer.Gravitas.

Resume with the first unchecked [Phase 0 item](#phase-0---settle-the-2d-contract).
Specify the public controller surface and ownership, then use a top-down
obstacle and a side-view ledge/ceiling to settle graph representation, proof
identity, persistence, and bounded acceptance inputs. Phase 1 begins after
that contract review.

### 2026-11-09 - GridForge v10 and controller contract draft

Trailblazer now references `GridForge` and `GridForge.Lean` 10.0.0 in package
mode, matching the existing source pin. Current rectangular slab tracing
already uses the v10 surface. Trailblazer already targets the compatible
frameworks and uses the v8 collection types and shared Chronicler diagnostics,
so the upgrade required only the two package reference changes.
[GridForge v10 migration guide](https://github.com/mrdav30/GridForge/blob/v10.0.0/docs/MIGRATION.md#migrating-from-gridforge-v9-to-v10)

Windows validation with .NET SDK 10.0.302 used the existing projects.

| Validation | Release | ReleaseLean |
| --- | --- | --- |
| Released-package core | GridForge 10.0.0 on both library targets. 3,250 tests passed. | GridForge.Lean 10.0.0 on both library targets. 3,150 tests passed. |
| Released-package benchmark project | Build passed | Build passed |
| Local-source solution | Build passed. 3,250 core and 90 adapter tests passed. | Build passed. 3,150 core and 86 adapter tests passed. |

In each configuration, the combined coverage report used the released-package
core run and local-source adapter run. It covered 32,511/32,511 lines,
13,304/13,304 branches, and 3,194/3,194 fully covered methods across both
assemblies.

Source validation used the sibling checkouts, with Gravitas at `3f1a83fb`.
Adapter package validation remains gated by the Gravitas 1.1.0 release.

The [controller contract](#public-2d-controller-contract) records
the first signatures, owner files, and accepted-motion and support lifecycle.
Independent review checked the version bump and the frame, support, and
posture boundaries. The controller checkbox stays open for review against the
two graph examples and the exact replay and record contract.

### 2026-11-09 - Phase 0 contract closeout

Against Trailblazer `f3562cd`, review selected one shared graph, acquisition
pipeline, and scheduler, with separate planar proofs and a native controller
boundary. The acceptance inputs above supersede the early box-based design
examples. Independent review checked the connected owners and corrected the
slope endpoint projection, fixed-foot ceiling boundary, cardinal cost oracle,
and restored carrier binding. Exact costs 6 and 11/2 are the expected
forward graph costs, with reverse Flow required to re-evaluate those same
directed edges.

On Windows with .NET SDK 10.0.302, a disposable `net10.0` console probe
referenced GridForge 10.0.0 and then GridForge.Lean 10.0.0. Each passed 98
assertions, including fixture guards. The probe captured the public
`GridNavigationBaseline` generation, then used
`TracePlanarNavigationBodyInto` and the FixedMathSharp v8 capsule predicates
on the bounded fixtures. It established the six detour legs, denied-cell
evidence, corridor tangency and one-raw wider failure, separate headroom and
support requirements, all ten compact jump sweeps and exact landing, the
slope offset, and clear and blocked step lifts. The one-raw ceiling control
translated the exact-tangent capsule up one raw unit. The fixed-foot growth
control used two extra raw axis units.

| Existing test controls | Release | ReleaseLean |
| --- | --- | --- |
| Gravitas native support, host motion, and posture tests at `d7ffa17b` | 105 passed | 105 passed |
| Trailblazer transition edges and guides, Flow equivalence, scheduling, A* connectors, profile replacement, and serialization filter | 386 passed | 335 passed |
| Trailblazer 3D moving/rotated carrier replay on the existing local-source adapter build | 2 passed, JSON and MemoryPack | 1 passed, JSON |

The executed probes establish the primitive geometry and existing 3D
controls. New planar search, controller, transactional restore, and joint
replay remain implementation acceptance tests. No production path changed in
this checkpoint, so the matched 3D timings remain a pre-edit gate for the
first affected runtime path. Phase 1 starts with the bounded top-down
obstacle and portal cases.

### 2026-11-09 - Phase 1 authoring and 3D baselines

Captured the 3D baseline before changing shared code at Trailblazer
`0923a116847d37268cef3199631bebd1061a0479`. The Release source build used
Chronicler `7b90d59`, FixedMathSharp `e2c325a`, SwiftCollections `08b1b38`,
and GridForge `c4f80a0`. The Windows host used an i7-9700K, .NET SDK 10.0.302,
.NET runtime 10.0.10 x64, and BenchmarkDotNet 0.15.8.

Use the existing benchmark project with `UseLocalLsfStack=true`. Build the
Release host and sibling binaries before starting the measurement so generated
children consume the intended prebuilt dependencies. For the BDN cases use
three launches, one warmup, and three actual iterations per launch.

| Control | Workload and baseline |
| --- | --- |
| `navigation-guided-frame` | `Simulate64GuidedFramesInstrumentedBalancedPhases`, 100 agents each for A* and Flow. At 32 Hz with a 16-frame LOS interval, actual frame medians were 1.876 and 1.884 ms, observed P95s 2.317 and 3.584 ms. Both had 576 actual frames, 0 B/frame, and matching replay and ray counts across launches. |
| `navigation-ray` | `Execute` for Medium and SparseBlocked. Each visited 64 trace and 64 covered intervals, with 125 and 0 evaluated edges respectively. Both allocated 0 B in the warm preflight. BDN means were 5.831 ms and 405.1 µs per operation. |
| Scheduled recovery | Run the frozen Release host with `recovery-capture <launch> Scheduled Default Flow100` for launches 1–3. Each launch has five warmups and fifteen actual episodes. Exact completion frames were 1,281 cold, 203 warm, 1,419 rebake, 1,419 replacement, and 303 restore. |

Accepted captures are under `artifacts/phase1-0923a11-source-baseline`, with
frozen DLL hashes, generated children, console output, and analyzer summaries.
The package-host attempt failed to load the expected prebuilt GridForge
assembly in the generated children. The first ray filter selected no cases,
and the Flow100/500 attempt was stopped before all launches completed.
Only completed children and the completed Flow100 launches count above.

The first authoring tests used the Phase 0 obstacle cell and one-row portal
fixtures. On `0923a11`, they failed to compile because `SetPlanarLayer` and
`PlanarBinding` were absent. The implementation extends the existing map
builder and bake equality with the exact layer and embedding. The first
inline layout added 24 bytes to every spatial bake and broke existing exact
capacity tests when accounted. The binding now lives in the planar bake's
read-only cell view, preserving spatial layout, reservations, and hashes.
Planar bakes reject off-layer explicit cells and spatial edge authoring. Preparation
rejects them before they reach the spatial graph.

Windows package-mode core validation passed 3,264 Release and 3,164
ReleaseLean tests, including the existing exact-capacity cases. Both
library targets built in each configuration. Each configuration covered
32,354/32,354 lines, 13,302/13,302 branches, and 3,188/3,188 fully covered
methods. The local-source adapter at Gravitas `d7ffa17b` passed 90 Release
and 86 ReleaseLean tests. Combined reports for both configurations covered
32,547/32,547 lines, 13,332/13,332 branches, and 3,201/3,201 fully covered
methods across both assemblies. The DocFX build passed with warnings as
errors.

The matched rerun under `artifacts/phase1-authoring-post` used the same
dependency DLL hashes and reversed workload order. Guided and recovery
frame hashes and work counts matched exactly, as did recovery completion
frames and frame allocations. Both warm ray preflights and all guided
frames again allocated 0 B. Post-edit guided medians were 1.869 ms for A*
and 1.683 ms for Flow, with P95s of 2.078 and 3.645 ms. Ray means were
5.818 ms for Medium and 421.3 µs for SparseBlocked. All five recovery
median-of-episode frame medians moved by less than 0.007 ms. These are
observed timings for this capture.

The next slice uses the existing publication pipeline and adds a planar
endpoint caller for the stationary body proof. Keep spatial endpoint,
seam, and committed-cell readers isolated when admitting planar cells.
Charge the planar cell-view payload in preparation when it becomes
admissible. The fitted portal, detour, A*/Flow, and accepted controller
motion remain runtime acceptance cases.

### 2026-11-09 - Phase 1 planar publication

Before changing shared code, the Release source stack captured fresh 3D
controls at Trailblazer `f0a0e08`, FixedMathSharp `e2c325a`, SwiftCollections
`08b1b38`, Chronicler `7b90d59`, and GridForge `c4f80a0`. The controls used
the same Windows host, .NET runtime, benchmark cases, and launch settings
as the authoring checkpoint. The frozen host and dependency hashes, logs,
and analyzer summaries are under `artifacts/phase1-f0a0e08-publication-baseline`.

The publication tests failed on `f0a0e08` because preparation still
rejected planar bakes. The existing map commit, baseline capture, semantic
page, and overlay owners now publish top-down cells. Closed coverage can
touch two layers at an embedding face. The baseline keeps only addresses
on the selected layer and charges every GridForge probe. An off-layer
sparse mutation advances the mirrored generation without creating a slot.
Off-layer overlays reject atomically. The prepared bake charges its binding
and uses the existing sorted lookup rather than a direct table sized to
the entire grid.

A live map ID keeps its exact domain binding. Remove and republish to
change mode, layer, or embedding. Spatial component building and seam
discovery omit planar cells. Existing spatial A*/Flow endpoint and recovery
workspaces bind only spatial generations. They check eligible capacity,
including when a retained Flow guide sees later spatial graph growth.
Direct spatial headings cannot use an explicit planar endpoint map.
Raw spatial rays ignore planar intervals, and spatial body traces fail
closed if planar geometry would supply required coverage. Directory scans
and raw GridForge candidates still consume the existing work budgets.

Planar connection and transition authoring remains closed. Spatial
connections and transitions reject known planar endpoints and witnesses,
including definitions whose target map appears after the definition.

The rerun under `artifacts/phase1-publication-post` used the same dependency
DLL hashes and reversed workload order. All expected launches completed.
Replay checksums, work counts, and allocations matched across 576 guided
frames per algorithm, both ray preflights, and 45 scheduled recovery episodes.
Guided and warm ray controls again allocated 0 B. Recovery completion frames
were 1,281 cold, 203 warm, 1,419 rebake, 1,419 replacement, and 303 restore.

| 3D timing | Before | After |
| --- | ---: | ---: |
| Guided A* frame median / observed P95 | 1.957 / 2.399 ms | 1.855 / 2.206 ms |
| Guided Flow frame median / observed P95 | 1.723 / 3.386 ms | 1.560 / 3.609 ms |
| Medium ray mean | 5.829 ms | 6.083 ms |
| SparseBlocked ray mean | 438.4 µs | 420.2 µs |

The recovery median-of-episode frame medians moved by at most 0.014 ms.
These timings describe this capture. The replay and work comparisons are
exact.

Windows package-mode core validation passed 3,277 Release and 3,177
ReleaseLean tests. The adapter used `UseLocalLsfStack=true` with Gravitas
`4b0d83af` and passed 90 Release and 86 ReleaseLean tests. Both library
targets built in each configuration. Combined coverage in each configuration
was 32,598/32,598 lines, 13,410/13,410 branches, and 3,207/3,207 fully covered
methods across both assemblies. The DocFX build passed with warnings as errors.

Runtime acceptance still starts with native endpoint admission and the
stationary circle proof. The shared graph remains the publication owner.
Keep each public or internal type in its own file as the 2D surface grows.

### 2026-11-09 - Phase 1 strict top-down endpoints

Before shared edits, the Release source stack captured the matched 3D controls
at Trailblazer `c6aa4de`, FixedMathSharp `e2c325a`, SwiftCollections `08b1b38`,
Chronicler `7b90d59`, and GridForge `c4f80a0`. The accepted captures are under
`artifacts/phase1-c6aa4de-endpoints-baseline`. The same workloads, build, and
launch settings as the publication checkpoint were used.

The initial placement cases failed to compile on `c6aa4de` because the
production entry was absent. `context.Pathing.ResolveTopDownEndpoint` gives
hosts a strict check at the exact requested X/Z center for spawn and goal
placement. The 2D controller remains the planned route-acquisition entry.
The result is a snapshot cell address from the explicit map and selected
layer. Closed-boundary candidates use durable address order. The whole
stationary circle is proved by GridForge's native planar trace. Present and
unblocked required cells must pass the shared medium, capability, area, and
radius policy. World Y selects the layer and spatial height clearance is
outside this placement check.

All materialized planar maps at the same embedding count toward the bounded
layer and generation work. The exact generations are bound before tracing,
with center candidates filtered to the named map. The first integration run
exposed a dormant map with no usable generation. A dormant source now returns
`NoMap`, and dormant neighbors are excluded. A closed publication or a changed
world or graph snapshot cannot return an address. Footprint tests cover exact
tangency and one-raw penetration, a selected-layer sparse hole, hex corners,
compatible neighbors, obstacles, overlays, and finite budgets. A warmed series
of 64 placements returned the same address and allocated 0 B on the caller.

The disposable Release placement probe used one dense 3 × 2 × 3 grid with a
single authored row and a tangent radius-1/2 circle on layer 1. After 2,000
warmup calls, five blocks of 5,000 calls each returned the same exact address
and allocated 0 B on the caller thread. Block averages were 78.9–81.7 µs per
placement on the Windows baseline host. The result is specific to that fixture.

The matched rerun under `artifacts/phase1-endpoints-post` used the same dependency
DLL hashes. Replay checksums, work counts, allocations, and completion frames
matched across 576 guided frames per algorithm, both ray preflights, and 45
scheduled recovery episodes. The first ray launch attempt found a duplicate
benchmark project in the isolated validation copy. That attempt ran zero
children. The completed retry used the same frozen binaries.

| 3D timing | Before | After |
| --- | ---: | ---: |
| Guided A* frame median / observed P95 | 1.928 / 2.165 ms | 1.879 / 2.081 ms |
| Guided Flow frame median / observed P95 | 2.040 / 3.021 ms | 1.860 / 3.044 ms |
| Medium ray mean | 5.855 ms | 5.866 ms |
| SparseBlocked ray mean | 418.2 µs | 420.9 µs |

The recovery median-of-episode frame medians moved by at most 0.011 ms.

On Windows, package-mode core validation passed 3,295 Release and 3,195
ReleaseLean tests. The adapter used `UseLocalLsfStack=true` against the
immutable develop CI pins, including Gravitas `13f73bdc`. It passed 90
Release and 86 ReleaseLean tests. Both library targets built in each
configuration. Combined coverage was 32,724/32,724 lines, 13,494/13,494
branches, and 3,216/3,216 fully covered methods in each configuration.
DocFX passed with warnings treated as errors.

Native swept legs and route admission for the obstacle detour and fitted
portal are the next Phase 1 work. The shared A*/Flow and scheduler contracts
remain the implementation target.

### 2026-11-09 - Phase 1 top-down leg clearance

Before shared edits, the Release source stack captured the matched 3D controls
at Trailblazer `66e94cf`, FixedMathSharp `e2c325a`, SwiftCollections `08b1b38`,
Chronicler `7b90d59`, and GridForge `c4f80a0`. The accepted captures are under
`artifacts/phase1-66e94cf-sweeps-baseline`. The workload and launch settings
match the placement checkpoint.

The initial leg cases failed to compile on `66e94cf` because the production
entry was absent. `context.Pathing.ValidateTopDownLeg` checks one leg between
canonical cell centers. It uses the existing placement layer binding, policy,
trace storage, semantic checks, work meter, and final world/graph stamp check.
Legs debit one evaluated edge plus actual lookup and covered-voxel work.
Ordinary adjacency excludes rectangular diagonals and coincident footprints.
GridForge supplies the exact immediate-neighbor footprint proof, including
compatible selected layers of different local indices or heights.

In the pointy-hex fixture, a circle of radius `1/2 + one raw unit` fit at both
endpoints but penetrated the flanks midway along the leg. Radius `1/2` was
tangent and succeeded. Authoring both flank cells through the overlay made
the larger sweep succeed in both directions. A removed sparse destination
returns `Stale` before publication and `NoPath` after the update. Tests also
cover cardinal and cross-map adjacency, exact embedding, finite work, and
zero allocations in a warmed repeated leg check.

The disposable Release leg probe used a tangent radius-1/2 sweep between
cardinal neighbors in an authored row of a dense 3 × 2 × 3 rectangular grid.
After 2,000 warmup calls, five blocks of 5,000 calls each passed and allocated
0 B on the caller thread. Block averages were 115.5–119.6 µs per leg on the
Windows baseline host. The result is specific to that fixture.

The matched rerun under `artifacts/phase1-sweeps-post` used the same dependency
DLL hashes and reversed the workload order. Replay checksums, work counts,
allocations, and completion frames matched across 576 guided frames per
algorithm, both ray preflights, and 45 scheduled recovery episodes.

| 3D timing | Before | After |
| --- | ---: | ---: |
| Guided A* frame median / observed P95 | 1.879 / 2.182 ms | 1.911 / 2.174 ms |
| Guided Flow frame median / observed P95 | 1.396 / 3.608 ms | 1.668 / 3.441 ms |
| Medium ray mean | 5.892 ms | 5.886 ms |
| SparseBlocked ray mean | 420.5 µs | 417.5 µs |

The recovery median-of-episode frame medians moved by at most 0.026 ms.
The guided Flow median increase did not repeat in the follow-up control under
`artifacts/phase1-sweeps-flow-abba-complete`. The same generated child and
dependencies ran eight alternating launches in ABBA/BAAB order with only the
hash-verified `Trailblazer.dll` swapped. Each launch contributed three actual
64-frame blocks. Before launch medians ranged from 1.523 to 2.375 ms and
after medians ranged from 1.422 to 2.460 ms. The pooled medians were 2.075 and
1.705 ms. Replay, work, and allocation counts matched in every launch. The
timing variation across these captures leaves the latency effect inconclusive.

On Windows, package-mode core validation passed 3,305 Release and 3,205
ReleaseLean tests. The adapter used `UseLocalLsfStack=true` with the immutable
develop CI pins, including Gravitas `13f73bdc`, and passed 90 Release and 86
ReleaseLean tests. Both library targets built in each configuration. Combined
coverage was 32,772/32,772 lines, 13,528/13,528 branches, and 3,223/3,223 fully
covered methods in each configuration. DocFX passed with warnings treated as
errors.

The next Phase 1 acceptance cases wire ordinary planar legs into shared
A* and Flow for the fitted portal and obstacle detour.

### 2026-11-09 - Phase 1 immediate top-down routes

Before shared edits, the matched Release source-stack controls were captured
at Trailblazer `26d446b`, FixedMathSharp `e2c325a`, SwiftCollections `08b1b38`,
Chronicler `7b90d59`, and GridForge `c4f80a0`. The accepted baseline is under
`artifacts/phase1-26d446b-routes-baseline`. Launch and workload settings match
the leg-clearance checkpoint.

`context.Pathing.FindTopDownRoute` accepts two durable center addresses on one
materialized top-down map. It borrows a configured A* or Flow workspace
through the existing admission coordinator, binds the compatible planar
generations once, and reuses the stationary and swept circle proof. Search
uses the existing native direction order, heap and relaxation for each
algorithm. A* uses zero heuristic and returns the reconstructed addresses.
Flow considers each predecessor's forward leg, retains the forward ordinal,
and follows the settled successors. Exact leg cost is outward-rounded
in-plane length plus forward destination cell and area costs.

The immediate route returns caller-owned addresses and cost after a graph
root and world stamp check. It uses no spatial component rejection, spatial
shortcut, or retained guide payload. A previously unauthored flank may lack
a dependency page, so the complete snapshot covers semantic openings as
well as physical changes. Caller output stays unchanged on failure. Flow
clears the borrowed search records after extraction so its cleanup cannot
consume a later scheduled spatial query's node slice.

Both algorithms found the cost-6 detour through `(2,1)` and the cost-4
direct route after an overlay opened `(2,0)` without a raw-world change.
The fitted portal admitted radius `1/2` and rejected radius `1/2` plus one
raw unit. A target toll gave exact forward and reverse costs of `4 1/4`
and `4`. The pointy-hex regression proved an independent swept-only failure.
Both endpoint circles fit, but the leg failed until the two flank cells were
authored. Tests also cover finite edge, lookup, endpoint, node and coverage
budgets, exact output capacity, overflow, lane ownership, and stale geometry.

The matched rerun under `artifacts/phase1-routes-post` kept the dependency
hashes and reversed workload order. Replay checksums, work counts,
allocations, and completion frames matched across 576 guided frames per
algorithm, both ray preflights, and 45 scheduled recovery episodes.

| 3D timing | Before | After |
| --- | ---: | ---: |
| Guided A* frame median / observed P95 | 1.942 / 2.131 ms | 1.839 / 1.999 ms |
| Guided Flow frame median / observed P95 | 2.019 / 3.540 ms | 1.948 / 3.314 ms |
| Medium ray mean | 5.888 ms | 5.893 ms |
| SparseBlocked ray mean | 421.3 µs | 422.3 µs |

The recovery median-of-episode frame medians moved by at most 0.004 ms.
These timings describe the matched captures.

The disposable Release probe used the cost-6 rectangular detour with a
caller-owned 16-address buffer. After 64 warmups per algorithm, five blocks
of 100 queries each returned the exact route length and cost. A* block
averages were 1.508–1.676 ms and 4,480 B per query. Flow block averages were
1.515–1.595 ms and 11,104–11,155 B per query on the Windows baseline host.
The route borrows search buffers and scratch. Each immediate call still
constructs query and kernel work objects.

Windows package-mode core passed 3,317 Release and 3,217 ReleaseLean tests.
The local-source adapter at the immutable develop CI pins, including Gravitas
`13f73bdc`, passed 90 Release and 86 ReleaseLean tests. Both library targets
built in both configurations. Combined coverage was 33,048/33,048 lines,
13,669/13,669 branches, and 3,244/3,244 fully covered methods per configuration.
DocFX passed with warnings treated as errors.

This checkpoint enumerates ordinary neighbors within the named map. The
standalone leg check continues to prove compatible cross-map legs. Planar
route acquisition through retained guides and scheduling, cross-map edge
discovery, and accepted controller motion remain Phase 1 integration work.

### 2026-11-09 - Phase 1 retained top-down center guides

`context.Guides.RequestTopDownGuide` runs the same immediate planar search
as `FindTopDownRoute` and retains the selected canonical centers in order.
Both algorithms use the existing pooled A* route cursor and its lease,
single-payload, and active-byte ceilings. Flow uses its own search workspace
and retains its selected route from the requested origin. Planar payloads
stay detached from the spatial reusable cache.

The retained stamp uses the monotonically increasing graph version and the
raw GridWorld spawn, version, and change sequence. The stamp does not retain
a graph root. Each read or advance validates the stamp and fails closed
through graph maintenance, reset, or disposal. An overlay that opens the
missing detour cell invalidates the old route without a raw world change.
Fresh acquisition returns exact cost 4 in place of the cost-6 detour.

The fitted portal covers radius `1/2` and the first penetrating raw unit.
The guide tests also cover independent cursors, stale aliases after pool
reuse, default failure outputs, raw changes before graph publication, reset,
attached-world disposal, detached byte accounting, and acquisition with one
available graph lease.

The pre-edit Release controls are under
`artifacts/phase1-ec9e9df-guides-baseline` at Trailblazer `ec9e9df`. The
matched reverse-order rerun is under `artifacts/phase1-guides-post`. Frozen
dependency hashes matched. The two captures matched 3D replay checksums,
work counts, allocations, and completion frames across 576 guided frames
per algorithm, both ray preflights, and 45 scheduled Flow recovery episodes.

| 3D timing | Before | After |
| --- | ---: | ---: |
| Guided A* frame median / observed P95 | 1.932 / 2.247 ms | 1.924 / 2.226 ms |
| Guided Flow frame median / observed P95 | 1.375 / 3.156 ms | 1.851 / 3.136 ms |
| Medium ray mean | 5.953 ms | 5.915 ms |
| SparseBlocked ray mean | 431.5 µs | 428.0 µs |

The recovery median-of-episode frame medians moved by at most 0.005 ms.
For the guided Flow follow-up, the same generated child and dependencies ran
eight launches in ABBA/BAAB order, swapping only the hash-verified
`Trailblazer.dll`. Each launch contributed three actual 64-frame blocks.

| Flow follow-up | Before median / observed P95 | After median / observed P95 |
| --- | ---: | ---: |
| One warmup, default tiering | 1.585 / 3.349 ms | 2.496 / 3.748 ms |
| One warmup, tiering disabled | 0.850 / 1.157 ms | 0.834 / 1.102 ms |
| Eight warmups, default tiering | 0.686 / 0.988 ms | 0.693 / 0.983 ms |

Replay, work, and allocations matched in each launch. The short-warmup
capture shows a timing increase that largely disappears with more warmup or
disabled tiering. The runs do not attribute the difference to a specific
method. Raw logs are under `artifacts/phase1-guides-flow-abba`,
`artifacts/phase1-guides-flow-abba-tiering-off`, and
`artifacts/phase1-guides-flow-long-warm`.

The disposable Windows Release probe acquired the cost-6 detour, advanced
through six steps, verified the exact destination, and disposed the lease.
After 64 warmups, five blocks of 100 operations averaged 1.516–1.674 ms
and 5,384 B for A*, and 1.514–1.585 ms and 12,008–12,019 B for Flow.
The route contains seven centers. These figures include search and retained
payload construction. The raw probe is under `artifacts/phase1-guides-probe`.

Windows and Ubuntu under WSL each passed 3,324 Release and 3,224 ReleaseLean
package-mode core tests. On both hosts, the adapter at the immutable develop
source pins, including Gravitas `13f73bdc`, passed 90 Release and 86
ReleaseLean tests. Both library targets built in both configurations.
Combined coverage was 33,122/33,122 lines, 13,689/13,689 branches, and
3,264/3,264 fully covered methods per configuration and host. Windows DocFX
passed with warnings treated as errors.

Scheduled planar acquisition, cross-map edge discovery, and accepted
controller motion remain in Phase 1.
