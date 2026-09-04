# Gravitas Kinematic Integration Implementation Plan

> **For agentic workers:** Use `superpowers:subagent-driven-development` or
> `superpowers:executing-plans` when execution is requested. Work through the
> checkboxes in order and use independent review. This plan is not standing
> authorization for future implementation, additional sibling-repository edits,
> or package publication; follow the active user scope.

**Created:** 2026-09-03

**Status:** Phase 0 preflight complete, including working-tree Gravitas
dependency alignment and independent review; no adapter, integration tests, or
playtest evidence created. Phase 1 package-backed work requires an aligned
Gravitas release. Slope-enabled work also requires
[`TRB-Issue-102`](issue-tracker.md#trb-issue-102---ground-contact-conflates-the-support-normal-with-platform-orientation).
Solution-wide local-source validation remains tracked by
[`TRB-Issue-103`](issue-tracker.md#trb-issue-103---local-source-graph-can-build-swiftcollections-with-two-assembly-versions).

**Source baselines:** Trailblazer `609beaaf9b3058b6e6881094ccc91ae5c383331d`;
Gravitas `6ade2f0bb373665efc5b7b85a3d21d1a371aa936`; GridForge
`d73c5c1313db759f8ea7dcbe2e60a27303ba1145`; FixedMathSharp
`010be577a4c3a10ea676f239c48d739b34d112ae`; SwiftCollections
`ee3884c550d1240ad4b3fc018fc152e973b4dc95`.

**Goal:** Deliver a reusable, engine-agnostic Gravitas-backed 3D kinematic
Navigator whose state reflects collision-accepted motion, with a concrete host
for navigation hardening and a documented path to the later 2D integration.

**Architecture:** An optional adapter depends on Trailblazer and Gravitas;
neither core depends on the other. Start with the existing Navigator subclass
boundary, write kinematic target poses before Gravitas resolves the fixed step,
then commit accepted poses and contact state once through Trailblazer. Change a
core contract only when a focused joint test demonstrates a missing boundary.

**Tech Stack:** C# 11; `netstandard2.1` and `net8.0` library targets; .NET 10 SDK
and .NET 8 test runtime; FixedMathSharp; GridForge; SwiftCollections; Chronicler;
xUnit v3; Gravitas `SolidBody` and deterministic queries.

**Spec:** [Agreed scope](#agreed-scope) and [Motion contract](#motion-contract)
retain the approved direction and the pre-implementation decisions in this plan.

## Agreed scope

1. Establish compatible dependencies, world ownership, and fixed-frame order.
2. Prove one fixed-profile 3D Navigator against actual Gravitas collisions.
3. Reuse that adapter in the vertical and movement-quality scenarios from
   [Navigation Hardening](navigationHardeningPlan.md).
4. Integrate coordinated physical shape changes after hardening Phase 2 defines
   the profile-replacement contract; verify replay, loading, and teardown.
5. Package and document the verified integration with executable examples.

The first 3D slice is intentionally independent of profile replacement and
first-class 2D. The [2D plan](twoDimensionalNavigationPlan.md) owns the later
`SolidBody2D` adapter extension and planar acceptance cases. Finishing this 3D
plan does not imply that 2D support exists, and 2D does not gate its closeout.

## Global constraints

- Deterministic fixed-point simulation, ordered commands, bounded work, and
  explicit ownership apply across both contexts. Rendering cannot feed physics.
- Keep Gravitas types out of Trailblazer core and Trailblazer types out of
  Gravitas core. Do not introduce engine dependencies in the adapter.
- Kinematic motion is a target pose, not a force or impulse. Trailblazer's
  displacement already includes the timestep; do not multiply it by time again.
- One component owns each gravity, ground-snap, step, and platform-transfer
  contribution. Collision resolution and the resulting pose remain authoritative.
- Preserve guide/action ownership, committed-cell notifications, and the
  existing JSON/MemoryPack populate-existing-instance boundaries.
- Maintain exact reachable line/branch/method coverage for affected production
  code, including the adapter, in both package families. Do not weaken existing
  gates or manufacture tests that only check type/member existence.
- Package references are the release-validation path. Use local sibling source
  only for explicit coordinated validation, then revalidate released packages.
- Record discovered defects in [issue-tracker.md](issue-tracker.md) with the
  owning library and a reproducer. Keep unproven integration questions here;
  record measured costs in the [benchmark backlog](benchmark-signal-hardening-backlog.md).
- Preserve worktree ownership. Staging, commits, sibling edits, publishing, and
  external playground creation require their corresponding user authorization.

## Current evidence and code map

Existing Trailblazer source and tests to reuse:

- [Navigator.cs](../../src/Trailblazer/Navigation/Navigator/Navigator.cs): protected
  pending motion, virtual simulation/commit, occupancy, and committed-cell state.
- [NavMotor.Traversal.cs](../../src/Trailblazer/Navigation/Motor/NavMotor.Traversal.cs)
  and [NavMotor.Finalization.cs](../../src/Trailblazer/Navigation/Motor/NavMotor.Finalization.cs):
  requested displacement and same-frame accepted-motion finalization.
- [GroundCondition.cs](../../src/Trailblazer/Navigation/Motor/Surface/GroundCondition.cs)
  and [PlatformSnapshot.cs](../../src/Trailblazer/Navigation/Motor/Surface/PlatformSnapshot.cs):
  contact normal, stable surface identity, carry, and transfer semantics.
- [Navigator tests](../../tests/Trailblazer.Tests/Navigation/Navigator),
  [GuidedPathTestScene.cs](../../tests/Trailblazer.Tests/Support/GuidedPathTestScene.cs),
  [Navigator guide](../wiki/Navigator.md), and [motor guide](../wiki/NavMotor.md).

In the Gravitas checkout, read `AGENTS.md`, `docs/wiki/HOST_INTEGRATION.md`,
`docs/wiki/DIMENSIONS.md`, and `docs/wiki/SERIALIZATION.md`, then inspect:

- `src/Gravitas/Core/3D/SolidBody.cs` and `SolidBody.Motion.cs` for pose ownership;
- `src/Gravitas/Core/3D/SolidBody.ContinuousCollision.Kinematic.cs` for conditional
  continuous collision detection (CCD), first-hit clipping, and dynamic pushes;
- `src/Gravitas/Core/3D/SolidBody.Grounding.cs` for support, normals, and snapping;
- `src/Gravitas/Runtime/GravitasWorldContext.cs` for fixed-step transactions;
- `src/Gravitas/Core/2D/SolidBody2D.cs` only to preserve the future dimension
  boundary, not to claim this 3D adapter serves planar physics.

At the recorded baselines, Gravitas declared FixedMathSharp 7.0.0,
FixedMathSharp.Chronicler 7.0.0, SwiftCollections.FixedMathSharp 7.0.0, and
GridForge 9.0.0. Trailblazer declares the corresponding 7.1.0, 7.1.0, 7.1.0,
and 9.1.0 packages. Phase 0 aligned and verified the Gravitas working tree and a
joint source-project probe against the higher 7.1.0/9.1.0 stack in both package
families. Those results are not released-package evidence: Gravitas must still
release the aligned packages before package-backed adapter implementation begins.

Phase 0 selected the following Phase 1 additions:

| Location | Responsibility |
| --- | --- |
| `src/Trailblazer.Gravitas/` | Optional adapter project, one 3D Navigator subclass, and internal contact translation |
| `tests/Trailblazer.Gravitas.Tests/` | Real-body joint behavior, replay, shape-change, and teardown tests; no duplicate core unit suite |
| `samples/Trailblazer.Gravitas.HeadlessSample/` | Runnable deterministic consumer, scenario inputs, trace output, and bounded failure results |
| Adapter README and Gravitas wiki guide | Setup, supported collision behavior, frame order, limitations, and presentation-only observation |

These directories do not exist yet. Scaffold them only after the recorded
dependency prerequisites are satisfied. Do not add empty projects or a second
adapter implementation just to reserve future 2D space.

## Motion contract

The adapter's first proof uses the following explicit sequence for every fixed
frame; all actors prepare before the shared physics resolution, and all commit
after it:

1. Apply ordered host commands, call `TrailblazerWorldContext.Simulate()`, then
   call `GravitasWorldContext.Simulate()` once, using matching fixed timesteps.
   Supply the previous accepted body pose and cached contact state to each
   Navigator.
2. In stable host actor order, run each adapter Navigator's `Simulate()` once.
   The override calls the base simulation, forms the requested pose from current
   position plus queued locomotion/platform displacement and rotation, and writes
   that target to its kinematic Gravitas host transform.
3. After every target has been written, execute
   `GravitasWorldContext.LateSimulate()` once for the shared world.
4. Read accepted body poses after collision resolution, then run the declared
   deterministic support query and cache contact facts without moving the body.
5. In the same stable actor order, call each adapter's `CommitFrameMotion()`
   once. Its override replaces pending motion with accepted displacement, clears
   the original pending contributions, supplies accepted rotation/contact state,
   and calls the base commit once so its private coordination remains intact. Do
   not first commit requested motion and then correct it afterward.
6. After every Navigator commits, call `TrailblazerWorldContext.LateSimulate()`
   once. Observe committed cells and events after this late phase; only then
   expose presentation snapshots.

Phase 0 source review confirms that the subclass seam can express this order;
it does not prove an adapter. Phase 1 must test rotation already changed by
turning during `Simulate()` and prove that contact refresh cannot overwrite the
accepted pose. Heightmap projection remains disabled until a later test chooses
it as the sole snap owner. A discarded or failed physics step is an aborted
authoritative frame, not a locally recoverable movement result.

## Progress dashboard

| Phase | Deliverable | Status | Gate |
| --- | --- | --- | --- |
| 0 | Dependency, ownership, and package contract | Complete | Compatible target stack, exact frame order, file/API map, viewer choice, and prerequisites recorded |
| 1 | Fixed-profile 3D adapter proof | Not started | Wall-clipped pose, contacts, velocity, occupancy, and guide state agree |
| 2 | Shared hardening and shape-change evidence | Not started | Joint scenarios, observable movement, mutation, replay, and teardown verified |
| 3 | Package and documentation closeout | Not started | Runnable consumer, full matrix, coverage, and independent review |

## Phase 0 - Integration Preflight

- [x] **Verify the dependency chain.** Inspect both project files and resolve
  compatible standard/Lean package sets. Record exact versions and baseline
  commands/results. If unreleased sibling changes are necessary, obtain scope
  approval before editing those repositories; document the temporary source
  validation path without representing it as package-release evidence.
- [x] **Record context and body ownership.** Decide whether the contexts attach
  to one host-owned GridWorld; verify that binding is supported and has one
  disposal owner. Specify frame synchronization, stable actor/platform IDs,
  kinematic body configuration, collision modes, gravity, ground snapping,
  platform transfer, and registration/disposal order.
- [x] **Review contact semantics before mapping fields.** Gravitas reports a
  supporting surface normal independently of its transform; Trailblazer derives
  its current ground normal from platform orientation. Include a sloped mesh
  under an unrotated transform and moving support with varying contact normals.
  Do not fake carrier rotation to encode a normal. If the existing boundary
  cannot preserve both facts, capture the reproducer and approve a focused fix.
- [x] **Set the adapter contract and first fixture.** Record exact project/type
  paths, prepare/resolve/commit operations, ownership, lifecycle failure results,
  timestep, collision settings, and bounded wall-test expectations. Start from
  the existing subclass seam; require evidence before adding a new core API.
- [x] **Choose the observation host.** Reuse an available playground or select a
  minimal viewer for the same deterministic scenarios. Record its location,
  controls, replay input format, and artifact destination. Rendering is optional
  for early joint tests but required for the movement-quality closeout.

### Phase 0 decisions

#### Dependency and package boundary

The compatible lower-stack target is exact and matched by family:

| Dependency | Standard | Lean |
| --- | --- | --- |
| FixedMathSharp | `FixedMathSharp` 7.1.0 | `FixedMathSharp.Lean` 7.1.0 |
| FixedMathSharp Chronicler bridge | `FixedMathSharp.Chronicler` 7.1.0 | `FixedMathSharp.Chronicler.Lean` 7.1.0 |
| SwiftCollections | `SwiftCollections` 7.0.0 | `SwiftCollections.Lean` 7.0.0 |
| SwiftCollections FixedMathSharp bridge | `SwiftCollections.FixedMathSharp` 7.1.0 | `SwiftCollections.FixedMathSharp.Lean` 7.1.0 |
| GridForge | `GridForge` 9.1.0 | `GridForge.Lean` 9.1.0 |
| Chronicler | `Chronicler.Core` 0.4.0 | `Chronicler.Core.Lean` 0.4.0 plus `Chronicler.MemoryPackShim` 0.4.0 |
| MemoryPack | `MemoryPack` 1.21.4 | omitted |

The adapter assembly, root namespace, and standard package ID are
`Trailblazer.Gravitas`; the Lean package ID is `Trailblazer.Gravitas.Lean`.
During repository development it references the local
`src/Trailblazer/Trailblazer.csproj`, which NuGet packing must convert into the
matching Trailblazer package dependency, and the aligned released Gravitas
package. It has no direct lower-stack package references. Release validation
must inspect the generated nuspec and restore a clean consumer to prove that the
standard package depends only on `Trailblazer` plus `Gravitas`, and the Lean
package only on their Lean variants.

The Gravitas working tree now explicitly declares this dependency table in both
package families and gives its local-stack project references the same assembly
versions. Its test project also selects the 7.1.0 FluentAssertions package in
both package-backed and local-stack runs rather than pulling 7.0.0 back in.
Both package configurations pass Gravitas's full build/test matrix, and their
configuration-specific assets resolve the requested 7.1.0/9.1.0 packages. An
aligned Gravitas release is still required before that version is pinned in the
adapter.

#### World, body, and frame ownership

- The host creates and solely disposes one `GridWorld`. Both contexts attach
  with `takeOwnership: false`; their independent ownership registries permit the
  same world to be bound once per context type. A 32 Hz joint probe ran one
  simulate/late cycle in `Release` and `ReleaseLean`, disposed both contexts,
  and confirmed the shared world remained active.
- The host registers grids and maps before actors. It assigns deterministic
  Navigator actor identities and stable nonzero `int` carrier IDs; Gravitas
  collider IDs are context-local and never become serialized platform IDs.
- The host owns each `FixedTransform`, `SolidBody`, and collider. The adapter
  owns only its Navigator and prepare/commit state; it does not create, dispose,
  subscribe to, or serialize physics objects in the first slice.
- The first adapter body is a 3D kinematic `SolidBody` with a host-owned
  `FixedTransform`, `ContinuousCollisionMode.Continuous`, and gravity scale
  zero. Trailblazer owns requested locomotion, jump/fall gravity, friction,
  platform carry, and departure transfer. Gravitas owns physical clipping,
  contacts, and the accepted body pose.
- Ground snap and step behavior are disabled for the first wall proof. A later
  scenario must choose exactly one owner before enabling either Gravitas step/
  snap or Trailblazer heightmap projection. Navigation clearance and the
  physical collider remain separately configured facts.
- The host drives the explicit all-prepare / one-resolve / all-commit barrier in
  stable actor order. No coordinator, generic runner, or future-2D placeholder
  is approved.
- Duplicate prepare, commit without a prepared frame, stale-frame commit, and
  duplicate commit throw `InvalidOperationException` before a second lifecycle
  mutation. Committing before Gravitas late resolution remains an explicit host-
  order violation that the current public Gravitas surface cannot detect.
- If the host discards a prepared frame or Gravitas late simulation throws, the
  exception propagates and no Navigator commit or committed-cell notification
  occurs. `NavMotor.AbortTraversalFrame()` closes bookkeeping for teardown, but
  it is not a rollback: turning, locomotion, guide, clock, and partial physics
  mutations make that authoritative frame unusable. Continuing requires the host
  to restore a full pre-frame simulation checkpoint or reset and rebuild every
  affected shell; the adapter must not advertise local continuation.
- `GravitasNavigator3D.Reset()` is the adapter's reusable lifecycle endpoint,
  not a fictional Navigator disposal operation. It aborts an open traversal
  frame, clears adapter phase/pose snapshots, and calls `base.Reset()` so owned
  guide leases and Trailblazer registrations are released. Repeated reset is
  harmless; `Simulate()` and `CommitFrameMotion()` reject until the host sets up
  and initializes the adapter again.
- Teardown resets adapters, deactivates bodies and colliders, disposes both non-
  owning contexts, and disposes the host world last. The first slice has no
  Gravitas contact-event subscription to remove. A session reset follows the
  same actor-before-context discipline and rebuilds transient contacts and
  guides.

#### Contact boundary and focused core issue

Post-resolution grounding comes from one explicit Gravitas closest-hit support
sweep with fixed origin, radius, distance, layer filter, and mover exclusion.
The support layer is an adapter authoring contract: it contains non-trigger
support geometry, so the closest query's distance/collider-ID order remains the
only tie rule. A hit is accepted only when its world normal has positive Y. The
adapter caches its point/height, actual world-space normal, dynamic friction,
supporting transform, and host carrier ID before committing; a miss or rejected
normal calls `SetAirborne()`. It never fabricates platform rotation from a
contact normal.

After resolution, requested/accepted pose divergence is the first slice's sole
obstruction signal. The adapter calls `NotifyCollision()` at most once before
the base commit. It intentionally does not subscribe to `LSCollider` contact
callbacks: those callbacks identify the other body but provide no normal or
support/obstruction classification, so treating every body-backed floor or
platform contact as an obstruction would spuriously arm collision turning. A
future callback-only signal requires a reliable obstruction classifier and a
grounded body-backed support regression. CCD wall clipping needs no such
callback.

Trailblazer cannot currently preserve the real support normal independently of
the carrier transform. The confirmed reproducer and focused fix are tracked as
[`TRB-Issue-102`](issue-tracker.md#trb-issue-102---ground-contact-conflates-the-support-normal-with-platform-orientation).
The fix must establish one explicit serialized normal contract and remove the
derived-only ambiguity; it must not add Gravitas types to Trailblazer core.
The initial flat wall proof may proceed after dependency alignment, but slope
support cannot be claimed until that issue is resolved and the static/moving
slope regressions pass.

#### Adapter, test, and sample map

| Path | Contract |
| --- | --- |
| `src/Trailblazer.Gravitas/Trailblazer.Gravitas.csproj` | Optional `netstandard2.1;net8.0` adapter and `Trailblazer.Gravitas` / `.Lean` packages |
| `src/Trailblazer.Gravitas/GravitasNavigator3D.cs` | The only public adapter type; a 3D `Navigator` subclass that prepares a kinematic target and commits one accepted pose |
| `src/Trailblazer.Gravitas/GravitasContactTranslator.cs` | Internal cached support/contact translation; no second public contact model |
| `tests/Trailblazer.Gravitas.Tests/` | Real Gravitas joint behavior, lifecycle, replay, coverage, and teardown tests |
| `tests/Trailblazer.Gravitas.Tests/Support/GravitasNavigatorScenarioFixture.cs` | Shared-world setup and bounded stable-order frame driver used only by tests |
| `tests/Trailblazer.Gravitas.Tests/GravitasNavigator3D.AcceptedMotion.Tests.cs` | First red accepted-motion wall proof |
| `tests/Trailblazer.Gravitas.Tests/GravitasNavigator3D.CollisionTurning.Tests.cs` | Isolated public collision-turn timeline with no ordinary turn request |
| `samples/Trailblazer.Gravitas.HeadlessSample/` | Runnable package consumer using the same versioned scenarios and emitting deterministic traces |
| `docs/wiki/Gravitas.md` | Verified setup, frame order, ownership, supported behavior, and limitations |

`GravitasNavigator3D.Simulate()` is the prepare operation and
`GravitasNavigator3D.CommitFrameMotion()` is the accepted-motion commit. The
host's single `GravitasWorldContext.LateSimulate()` is the resolve operation.
The adapter rejects duplicate prepare, commit-before-prepare, stale-frame commit,
and duplicate commit. Committing after prepare but before Gravitas late resolution
is an explicit host-order violation; the current public Gravitas surface does not
expose its internal late-step token, so this plan does not falsely promise that
the adapter can detect it. A post-prepare failure invalidates the authoritative
frame as described above; no adapter-only rollback or coordinator API is approved.

The first fixture uses one shared rectangular world at 32 Hz with default one-
unit cells and bounds `(-4,-4,-4)` through `(8,8,8)`. Map `gravitas-wall`
contains connected solid cells `(4,4,4)`, `(5,4,4)`, and `(6,4,4)`, each with
radius/height clearance `4`, zero cost, and no required capability. Their exact
foot anchors are `(1/2,0,1/2)`, `(3/2,0,1/2)`, and `(5/2,0,1/2)`.

The actor starts at root pose `(1/2,1/2,1/2)` already facing positive X with
rotation `FixedQuaternion.FromDirection(Vector3d.Right)`. It uses a fixed
`KinematicBodyShape(radius=1/2, height=1, rootToFootOffset=1/2)` plus a matching
`LSSphereCollider(radius=1/2)` on layer 0 with no ignored collision layers. Its
A* query targets the end anchor in `TraversalMedium.Solid`, and `TrekRate.Fast`
requests positive-X motion at `1/2` unit per second. A non-trigger bodyless
`LSCuboidCollider` on layer 1, also with no ignored collision layers, has center
`(2,1,1/2)`, size `(1/4,2,2)`, identity rotation, and zero restitution. All
three map cells and their native connections remain navigable, so guidance
requests motion through physical geometry that Gravitas independently blocks.

Gravity and damping are zero for this isolation case, the kinematic body uses
`ContinuousCollisionMode.Continuous`, and the loop is bounded at 128 frames.
The post-resolution support sweep starts at `acceptedFoot + Up * 1/8`, uses
radius `1/4`, direction `Down`, distance `1/4`, layer mask
`PhysicsLayerMask.FromLayer(0)`, and excludes the mover collider. This first
fixture deliberately has no support geometry, so the query must miss and the
adapter must report airborne; the layer-1 vertical wall cannot become support.

With exact `1/64`-unit frame steps, the first requested/accepted divergence is
frame 56 (zero-based): requested X is `89/64` and accepted X is exactly `11/8`,
the wall's near face `15/8` minus the sphere radius. The accepted root keeps
`Y=1/2`, `Z=1/2`, and `FixedQuaternion.FromDirection(Vector3d.Right)`. The test
also asserts body/Navigator pose equality,
`Velocity == (Position - LastPosition) * InvDeltaTime`, middle cell `(5,4,4)`
as the last committed cell, no committed transition into `(6,4,4)`, airborne
contact state, and `Motor.TraversalInProgress == false`. It does not count
private commit/finalize calls or pretend that map clearance and collision
geometry are the same subsystem.

Collision turning is a separate wall scenario, not a second assertion on the
fully blocked fixture. It starts at the same root position facing positive Z at
identity rotation and repeatedly applies unguided positive-X movement with
`facingDirection: Vector3d.Forward`, so no ordinary turn is requested. Its
bodyless wall has the same shape and orientation but center X `515/256`; its
near face is therefore `483/256`. On zero-based frame 56, the request is again
X `89/64`, but CCD partially accepts X `355/256`. The accepted displacement
from the previous X `11/8` is `3/256`, which is greater than the exact
collision-turn threshold `radius / frameRate / 2 == 1/128`.

On that divergence frame the adapter signals collision before commit, then
input stops. The first idle simulation buffers the accepted positive-X
displacement while `Turning.TargetReached` remains true and `TargetRotation`
remains identity. The second idle simulation consumes that buffer:
`TargetReached` becomes false, `TargetRotation` equals
`FixedQuaternion.FromDirection(Vector3d.Right)`, and the applied Navigator
rotation differs from identity. These public states prove the signal without
method counters or conflating it with guided turning.

#### Observation host and evidence format

Use the local `F:\gamedevrepos\GridForge-Unity` Unity 6000.5 workspace as the
visual observation host because it already presents a GridForge world and trace
visualization. Do not modify its published GridForge package sample. After
separate authorization and compatible Unity-facing packages exist, derive a
viewer under `Assets/Trailblazer.Gravitas.Playground/` with scenario selection,
single-step, play/pause, and reset controls.

The headless fixture remains authoritative. It and the viewer consume the same
versioned JSON scenario commands and line-oriented trace records: scenario hash,
package versions, frame/command order, requested pose, accepted pose/velocity,
contact normal and carrier ID, guide/action state, committed cell, and bounded
outcome. Generated traces go to ignored
`artifacts/gravitas-integration/<scenario>/`; reviewed observations and exact
trace hashes are summarized in this execution record. Rendering and interactive
input capture never become a second collision or navigation implementation.

## Phase 1 - Fixed-Profile 3D Adapter Proof

- [ ] **Capture the accepted-motion regression first.** Put a wall between a
  guided agent and its requested endpoint. Drive real Gravitas collision; assert
  body and Navigator poses agree, velocity derives from the accepted delta, and
  the committed cell is the reachable cell rather than the requested cell.
  Record the intended failure before implementing the bridge.
- [ ] **Implement the smallest adapter that passes the proof.** Prepare all
  targets, resolve Gravitas once, replace pending movement, and use base commit.
  Preserve platform and turning rotation order. Test unobstructed motion and
  completely blocked motion. Prove collision-driven turning in the separate
  unguided/idle scenario above so ordinary guide turning cannot satisfy it.
- [ ] **Prove lifecycle and contact boundaries.** Cover multiple actors in one
  world, duplicate prepare/commit, commit without prepare, a discarded frame,
  stale-frame finalization, repeated adapter reset, and reset while work is open.
  Verify finalization and committed-cell events occur once, with no stale
  contact or guide reuse, and perform host-owned physics/context teardown in the
  specified order.
  From the public host loop, capture pose/cell state immediately after
  `TrailblazerWorldContext.LateSimulate()` returns and assert every actor already
  exposes its accepted committed result. Do not add a test-only friend or public
  late-hook API merely to observe the internal callback.
- [ ] **Review before sharing the slice.** Run focused tests in both package
  families and obtain an independent review of fixed-frame order and pose
  ownership. Link the verified revision and fixture from hardening Phase 1.

## Phase 2 - Joint Movement And Shape Changes

- [ ] **Run the hardening worlds through the actual adapter.** Reuse stairs,
  ladders, stacked floors, portals/groups, and airborne scenarios from the
  [hardening acceptance table](navigationHardeningPlan.md#acceptance-scenarios).
  Preserve action completion and actual contact/medium reporting.
- [ ] **Prove collision-controller behavior.** Cover a wall and corner, slopes,
  steps, thin blockers at high speed, dynamic-body pushes, and translating/
  rotating platforms. Establish the intended stop/slide/step behavior before
  judging results. Conditional CCD clipping alone is not proof of a finished
  character controller; inspect low-speed as well as high-speed motion.
- [ ] **Capture observable movement quality.** Record host observations and
  deterministic inputs for the shared scenarios. Track sticking, oscillation,
  sliding, platform drift, and unexpected replanning separately from visual
  smoothing. Retain one evidence record referenced by both plans.
- [ ] **Coordinate collider and profile replacement.** After hardening Phase 2
  defines the runtime operation, add stand/crouch/compact cycles, blocked growth,
  and platform-attached mutation. Validate before changing either side; rejection
  must preserve both live shapes, foot anchor, destination, and bindings. Include
  guided flight loss/recovery without confusing capability with physical medium.
- [ ] **Verify restore, replay, and teardown.** Restore body/collider and
  navigation shells before resuming movement; cover changed posture and platform
  state. Rebuild transient contacts/guidance rather than serializing live handles.
  Verify JSON and standard-package MemoryPack, deterministic repeated input,
  disposed leases, and no leaked context/body registrations.
- [ ] **Resolve and review joint findings.** Put defects in their owning library,
  add a focused regression, rerun both affected consumers, and link the evidence.
  Do not duplicate physics solvers or locomotion rules in the bridge.

## Phase 3 - Packaging, Documentation, And Verification

- [ ] **Integrate the approved adapter project into build/test/coverage.** Keep
  standard and Lean dependency families aligned, target the agreed portable
  frameworks, and ensure core-only consumers do not pull in Gravitas.
- [ ] **Provide an executable consumer example.** Demonstrate world creation,
  body/collider setup, map publication, one guided movement sequence, correct
  prepare/resolve/commit order, and disposal. Document collision settings,
  supported shapes, contact limitations, and presentation separation. Compile
  and execute every example presented as runnable.
- [ ] **Run the release-validation matrix without publishing.** Run the
  [Trailblazer matrix](navigationHardeningPlan.md#verification-and-closeout),
  adapter tests, and relevant Gravitas tests in Release and ReleaseLean; record
  Windows/Linux evidence and exact adapter/core coverage. Verify released package
  references, allocation/lifetime behavior, and deterministic replay.
- [ ] **Record exact implementation commands for the selected projects.** Include adapter
  restore/build/test/coverage commands in its README and this execution record
  as the projects are added. Reuse each repository's actual scripts/settings;
  do not copy unverified coverage switches or silently omit the adapter assembly.
- [ ] **Obtain final independent review.** Resolve blocking findings, update
  [feature-work-overview.md](feature-work-overview.md), and archive only after
  the 3D deliverables and shared evidence are complete. Package publication
  remains a separate user-authorized operation.

## Execution record

Append dated milestones with exact revisions/package versions, commands,
scenario inputs, results, artifact locations, linked issues, and next steps.

### 2026-09-03 - Draft captured

- Recorded the approved adapter direction, initial 3D host slice, and later 2D
  handoff. Source review supports feasibility, not integrated runtime behavior.
- No implementation or verification has started. Next task when authorized:
  dependency and lifecycle preflight, followed by the accepted-motion wall case.

### 2026-09-03 - Phase 0 preflight completed

- Inspected the Trailblazer, Gravitas, GridForge, FixedMathSharp, and
  SwiftCollections source and package boundaries at the baselines recorded
  above. Trailblazer resolves the 7.1.0/9.1.0 line. The authorized Gravitas
  working-tree change now declares FixedMathSharp, its Chronicler bridge, and
  the SwiftCollections bridge at 7.1.0 plus GridForge 9.1.0 in both package
  families. The local-stack project references carry matching assembly
  versions, and Gravitas tests select the 7.1.0 FluentAssertions package
  consistently.
- Package-based baseline verification passed with zero warnings/errors:
  Trailblazer `Release` 2,287 tests, Trailblazer `ReleaseLean` 2,238 tests,
  Gravitas `Release` 3,930 tests, and Gravitas `ReleaseLean` 3,875 tests.
- After dependency alignment, Gravitas again built both target frameworks with
  zero warnings/errors and passed 3,930 `Release` plus 3,875 `ReleaseLean`
  tests. Its configuration-specific restore assets resolve the requested 7.1.0
  FixedMathSharp family and 9.1.0 GridForge family; SwiftCollections core stays
  at 7.0.0 by design. No Gravitas runtime or public API changed.
- Fresh `Release` coverage passed all 3,930 tests and reports 55,867/55,867
  lines, 15,833/15,833 branches, and 5,320/5,320 ReportGenerator methods. The
  standard and Lean generated nuspecs contain the exact dependency families in
  the Phase 0 table for both target frameworks; Lean omits MemoryPack and the
  standard packages, while standard includes MemoryPack 1.21.4.
- Configuration-specific local-stack source builds and test-project runs also
  passed in both modes with the same test counts. Solution-wide local-stack
  validation first read stale configuration-less `obj` metadata from a prior
  isolated NuGet cache; after that generated state was repaired, it exposed the
  distinct duplicate SwiftCollections assembly-version defect recorded as
  [`TRB-Issue-103`](issue-tracker.md#trb-issue-103---local-source-graph-can-build-swiftcollections-with-two-assembly-versions).
  That convenience path is not used to claim success. The exact direct source/
  test validation below and the joint probe passed without an authored lower-
  stack change. The unnecessary experiment that substituted the test-only
  FluentAssertions package with its source project was reverted before these
  results.
- A temporary source-project consumer restored the matched package families and
  resolved FixedMathSharp 7.1.0, FixedMathSharp.Chronicler 7.1.0,
  SwiftCollections 7.0.0, SwiftCollections.FixedMathSharp 7.1.0, GridForge
  9.1.0, and Chronicler 0.4.0. Its `Release` and `ReleaseLean` runs attached both
  contexts non-owning to one world, advanced the fixed-step order at 32 Hz, and
  left the host world active after context disposal.
- Confirmed the existing protected Navigator seam can replace queued motion with
  the accepted Gravitas pose and call the base commit once. No new pose API,
  coordinator, or 2D placeholder is justified for the first slice.
- Confirmed and recorded `TRB-Issue-102`: physical support normal and platform
  transform are independent facts, but Trailblazer currently derives one from
  the other. Slope-enabled integration is blocked on its focused fix.
- Selected the GridForge-Unity workspace for later presentation-only observation
  and fixed the package/test/sample paths, wall fixture, controls, trace schema,
  artifact destination, ownership order, and reset/teardown contract. The wall
  and collision-turn proofs are separate, and callback-only obstruction signals
  are deferred because Gravitas contact callbacks cannot distinguish support.
  No visual-host files were changed.
- Extended the ignored probe with the exact bodyless wall and support sweep. In
  both configurations, 1/64-unit steps first diverged on zero-based frame 56:
  requested X was 89/64, accepted X was exactly 11/8 (raw 5905580032), and the
  layer-0 downward support query returned no hit. A distinct collision-turn
  wall centered at X 515/256 partially accepted X 355/256 (raw 5955911680),
  producing an accepted delta of 3/256 above the exact 1/128 turn threshold.
- Independent documentation review caught that the original collision-turn
  wording reused the fully blocked wall and therefore supplied no accepted delta
  for `NavTurning`. The distinct partial-clipping fixture above replaced it and
  passed re-review. Technical review then separated the unrelated stale-restore
  failure from `TRB-Issue-103` and required the exact SwiftCollections baseline;
  both findings are resolved in this record and tracker.
- Next dependency-order work: review and release Gravitas against the
  7.1.0/9.1.0 family, then capture the Phase 1 wall regression before
  scaffolding the smallest adapter implementation. Resolve `TRB-Issue-102`
  before slope-enabled integration; resolve `TRB-Issue-103` before advertising
  solution-wide local-source validation as a supported green path.

Package-backed baseline and post-alignment commands were run serially from each
named repository root:

```powershell
# F:\gamedevrepos\Trailblazer
dotnet restore Trailblazer.slnx --property:Configuration=Release
dotnet build Trailblazer.slnx --configuration Release --no-restore
dotnet test Trailblazer.slnx --configuration Release --no-build
dotnet restore Trailblazer.slnx --property:Configuration=ReleaseLean
dotnet build Trailblazer.slnx --configuration ReleaseLean --no-restore
dotnet test Trailblazer.slnx --configuration ReleaseLean --no-build

# F:\gamedevrepos\Gravitas
dotnet restore Gravitas.slnx --property:Configuration=Release
dotnet build Gravitas.slnx --configuration Release --no-restore
dotnet test Gravitas.slnx --configuration Release --no-build
dotnet restore Gravitas.slnx --property:Configuration=ReleaseLean
dotnet build Gravitas.slnx --configuration ReleaseLean --no-restore
dotnet test Gravitas.slnx --configuration ReleaseLean --no-build
```

The aligned Gravitas working tree also passed direct local-stack source and test
validation with configuration-specific restore assets:

```powershell
dotnet restore Gravitas.slnx --property:Configuration=Release --property:UseLocalLsfStack=true --force --no-cache
dotnet build src/Gravitas/Gravitas.csproj --configuration Release --property:UseLocalLsfStack=true --no-restore
dotnet test tests/Gravitas.Tests/Gravitas.Tests.csproj --configuration Release --property:UseLocalLsfStack=true --no-restore
dotnet restore Gravitas.slnx --property:Configuration=ReleaseLean --property:UseLocalLsfStack=true --force --no-cache
dotnet build src/Gravitas/Gravitas.csproj --configuration ReleaseLean --property:UseLocalLsfStack=true --no-restore
dotnet test tests/Gravitas.Tests/Gravitas.Tests.csproj --configuration ReleaseLean --property:UseLocalLsfStack=true --no-restore
```

Fresh coverage and package-metadata evidence used isolated ignored output:

```powershell
dotnet restore Gravitas.slnx --property:Configuration=Release
dotnet build Gravitas.slnx --configuration Release --no-restore
dotnet test tests/Gravitas.Tests/Gravitas.Tests.csproj --configuration Release --no-build --collect:"XPlat Code Coverage" --settings tests/Gravitas.Tests/coverlet.runsettings --results-directory artifacts/coverage-phase0-gravitas-alignment-20260903
reportgenerator "-reports:artifacts/coverage-phase0-gravitas-alignment-20260903/**/coverage.cobertura.xml" "-targetdir:artifacts/coverage-report-phase0-gravitas-alignment-20260903" "-reporttypes:JsonSummary;MarkdownSummaryGithub" "-filefilters:-**/MemoryPack.Generator/**;-**/*.g.cs;-**/obj/**"
dotnet restore Gravitas.slnx --property:Configuration=ReleaseLean
dotnet build Gravitas.slnx --configuration ReleaseLean --no-restore
tar -xOf src/Gravitas/bin/Release/Gravitas.0.0.0.nupkg Gravitas.nuspec
tar -xOf src/Gravitas/bin/ReleaseLean/Gravitas.Lean.0.0.0.nupkg Gravitas.Lean.nuspec
```

The temporary probe was created under the ignored planning workspace at
`.superpowers/sdd/gravitasIntegrationPlan/shared-world-probe/`. Its `net8.0`
console project directly referenced the Trailblazer and Gravitas source projects;
the program created one `GridWorld`, attached both contexts non-owning, set 32 Hz,
ran both simulate phases plus Gravitas late then Trailblazer late, exercised the
layer-1 bodyless wall and layer-0 support query, disposed the contexts, and
required the world to remain active. These exact commands passed:

```powershell
$probe = '.superpowers/sdd/gravitasIntegrationPlan/shared-world-probe/SharedWorldProbe.csproj'
dotnet run --project $probe --configuration Release
dotnet run --project $probe --configuration ReleaseLean
dotnet restore $probe -p:Configuration=Release
dotnet list $probe package --include-transitive --framework net8.0 --no-restore
dotnet restore $probe -p:Configuration=ReleaseLean
dotnet list $probe package --include-transitive --framework net8.0 --no-restore
```

Both runs printed `shared-world-ok frameRate=32 worldActive=True`, the fully
blocked result `wall-divergence frame=56 requested=(1.390625, 0.5, 0.5)
accepted=(1.375, 0.5, 0.5) acceptedXRaw=5905580032 supportHit=False`, and the
separate partial result `turn-divergence frame=56 previous=(1.375, 0.5, 3)
requested=(1.390625, 0.5, 3) accepted=(1.38671875, 0.5, 3)
acceptedDelta=(0.01171875, 0, 0) acceptedXRaw=5955911680
threshold=0.0078125`. The package list matched the standard and Lean tables
above. The ignored probe and ordinary `bin`/`obj` outputs are local validation
artifacts, not product or release files.
