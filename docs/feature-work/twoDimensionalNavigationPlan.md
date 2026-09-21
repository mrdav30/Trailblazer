# First-Class 2D Navigation Implementation Plan

> **For agentic workers:** Use `superpowers:subagent-driven-development` or
> `superpowers:executing-plans` when execution is requested. Complete the contract
> review before implementing runtime types. Use independent reviewers and the
> checkboxes below. This draft does not start implementation.

**Created:** 2026-09-03

**Status:** Phase 0 contract review in progress after the completed 3D integration
and accepted performance hardening. No native 2D runtime has been implemented.
Upstream releases remain deferred; Phase 1 requires review of the contract below.

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

- Preserve deterministic fixed-point math, stable ordering, finite work budgets,
  exact guide dependencies, and per-acquisition action/cursor ownership.
- Carry forward Immediate/Scheduled acquisition, bounded shared-work scheduling,
  safe waiting, cancellation, moved-origin adoption, and optional deterministic
  LOS phases. Planar geometry must certify its own endpoints, rays, and connector
  proofs; sharing control flow does not authorize reuse of 3D geometric proof.
- Preserve the existing 3D contract. Do not rename it or add forwarding aliases
  merely to make a new naming scheme symmetrical.
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

Phase 0 must replace this responsibility map with exact proposed file paths and
public signatures before runtime work. A dedicated 2D surface is the direction;
this draft does not pretend that its type names or graph representation have
already passed design review.

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
  bounded work, ownership, and changed-path coverage. Mark only this phase
  complete; side-view movement and real Gravitas2D collision remain open.

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
| Correct surface witnesses | Fix TRB-Issue-150 in Gravitas with exact-touch, initial-overlap and ordinary-sweep regressions; repeat the native probe. |
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
