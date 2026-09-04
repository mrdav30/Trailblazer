# Gravitas Kinematic Integration Implementation Plan

> **For agentic workers:** Use `superpowers:subagent-driven-development` or
> `superpowers:executing-plans` when execution is requested. Work through the
> checkboxes in order and use independent review. This draft does not authorize
> implementation, sibling-repository edits, or package publication.

**Created:** 2026-09-03

**Status:** Draft; no adapter, integration tests, or playtest evidence created.

**Source baselines:** Trailblazer `cb0d744b76a377807a253f0efa1c480ba169a7e9`;
Gravitas `6ade2f0bb373665efc5b7b85a3d21d1a371aa936`.

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

At the recorded baselines, Gravitas declares FixedMathSharp 7.0.0 and GridForge
9.0.0, while Trailblazer declares 7.1.0 and 9.1.0. Source compatibility and a
joint released-package chain have not been verified by writing this plan.

Proposed additions, subject to Phase 0's location/package decision:

| Location | Responsibility |
| --- | --- |
| `src/Trailblazer.Gravitas/` | Optional adapter project, 3D Navigator subclass, contact translation, and explicit frame orchestration |
| `tests/Trailblazer.Gravitas.Tests/` | Real-body joint behavior, replay, shape-change, and teardown tests; no duplicate core unit suite |
| Adapter README and runnable host sample | Setup, supported collision behavior, controls, frame order, and presentation-only observation |

These directories do not exist yet. Before scaffolding, record exact project,
type, and sample paths plus the approved package names. Do not add empty projects
or a second adapter implementation just to reserve future 2D space.

## Motion contract

The adapter's first proof uses the following explicit sequence for every fixed
frame; all actors prepare before the shared physics resolution, and all commit
after it:

1. Apply ordered host commands and call each context's `Simulate()` once with
   matching fixed timesteps. Supply the previous accepted body pose and current
   contact state to the Navigator.
2. Run Navigator simulation. Form the requested pose from current position plus
   queued locomotion/platform displacement and the calculated rotation.
3. Write each requested pose to its kinematic Gravitas host transform before
   `GravitasWorldContext.LateSimulate()`; execute that world step once.
4. Read accepted body poses and refreshed contacts after collision resolution.
5. Replace the Navigator's pending total with accepted displacement, clear the
   original pending contributions, and supply accepted rotation/contact state.
   Call the base `CommitFrameMotion()` once so its private coordination remains
   intact. Do not first commit requested motion and then correct it afterward.
6. After every Navigator commits, call `TrailblazerWorldContext.LateSimulate()`
   once. Observe committed cells and events after this late phase; only then
   expose presentation snapshots.

The subclass approach is a feasibility direction, not a verified adapter.
Preflight must pin exactly where commands and hooks run in both contexts, test
rotation already changed by turning during `Simulate()`, and prove that contact
refresh or optional heightmap projection cannot overwrite the accepted pose.
Aborted or failed physics steps must close or abort open traversal without
publishing a fictitious successful move. Trailblazer late hooks must observe the
collision-accepted, fully committed state, never the unfulfilled target pose.

## Progress dashboard

| Phase | Deliverable | Status | Gate |
| --- | --- | --- | --- |
| 0 | Dependency, ownership, and package contract | Not started | Recorded compatible stack, exact frame order, file/API map, and viewer choice |
| 1 | Fixed-profile 3D adapter proof | Not started | Wall-clipped pose, contacts, velocity, occupancy, and guide state agree |
| 2 | Shared hardening and shape-change evidence | Not started | Joint scenarios, observable movement, mutation, replay, and teardown verified |
| 3 | Package and documentation closeout | Not started | Runnable consumer, full matrix, coverage, and independent review |

## Phase 0 - Integration Preflight

- [ ] **Verify the dependency chain.** Inspect both project files and resolve
  compatible standard/Lean package sets. Record exact versions and baseline
  commands/results. If unreleased sibling changes are necessary, obtain scope
  approval before editing those repositories; document the temporary source
  validation path without representing it as package-release evidence.
- [ ] **Record context and body ownership.** Decide whether the contexts attach
  to one host-owned GridWorld; verify that binding is supported and has one
  disposal owner. Specify frame synchronization, stable actor/platform IDs,
  kinematic body configuration, collision modes, gravity, ground snapping,
  platform transfer, and registration/disposal order.
- [ ] **Review contact semantics before mapping fields.** Gravitas reports a
  supporting surface normal independently of its transform; Trailblazer derives
  its current ground normal from platform orientation. Include a sloped mesh
  under an unrotated transform and moving support with varying contact normals.
  Do not fake carrier rotation to encode a normal. If the existing boundary
  cannot preserve both facts, capture the reproducer and approve a focused fix.
- [ ] **Set the adapter contract and first fixture.** Record exact project/type
  paths, prepare/resolve/commit operations, ownership, lifecycle failure results,
  timestep, collision settings, and bounded wall-test expectations. Start from
  the existing subclass seam; require evidence before adding a new core API.
- [ ] **Choose the observation host.** Reuse an available playground or select a
  minimal viewer for the same deterministic scenarios. Record its location,
  controls, replay input format, and artifact destination. Rendering is optional
  for early joint tests but required for the movement-quality closeout.

## Phase 1 - Fixed-Profile 3D Adapter Proof

- [ ] **Capture the accepted-motion regression first.** Put a wall between a
  guided agent and its requested endpoint. Drive real Gravitas collision; assert
  body and Navigator poses agree, velocity derives from the accepted delta, and
  the committed cell is the reachable cell rather than the requested cell.
  Record the intended failure before implementing the bridge.
- [ ] **Implement the smallest adapter that passes the proof.** Prepare all
  targets, resolve Gravitas once, replace pending movement, and use base commit.
  Preserve platform and turning rotation order. Test unobstructed motion,
  completely blocked motion, and collision-driven turning notification.
- [ ] **Prove lifecycle and contact boundaries.** Cover multiple actors in one
  world, duplicate prepare/commit, commit without prepare, a discarded frame,
  stale-frame finalization, and teardown while work is open. Verify finalization
  and committed-cell events occur once, with no stale contact or guide reuse.
  Register an ordered Trailblazer late hook and assert it runs once after all
  accepted commits with the same final pose/cell state visible to consumers.
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
- [ ] **Record exact commands after project selection.** Include adapter
  restore/build/test/coverage commands in its README and this execution record
  once Phase 0 fixes its paths. Reuse each repository's actual scripts/settings;
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
