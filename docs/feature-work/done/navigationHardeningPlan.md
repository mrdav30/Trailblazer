# Navigation Hardening Implementation Plan

**Created:** 2026-09-03

**Status:** Completed and archived on 2026-09-09. All three hardening phases
and their independent local reviews are complete. The approved acquisition
follow-up is closed in [Deterministic Guide Acquisition Scheduling](navigationSchedulingPlan.md).
Unrun CI and final released-package checks remain explicit
[coordinated release gates](../feature-work-overview.md#coordinated-release-gates);
archival does not claim those gates passed.

**Source baseline:** `cb0d744b76a377807a253f0efa1c480ba169a7e9`

**Goal:** Validate complete vertical movement, support safe runtime agent-profile
replacement, and establish whether cold public guide acquisition needs
frame-spread execution.

**Architecture:** Preserve context-owned deterministic navigation and the
host-owned collision/action boundary. Build on the current graph, guide leases,
Navigator, and locomotion modules. Change runtime behavior only for an accepted
profile-replacement contract or a reproduced defect; measure acquisition costs
before proposing scheduling changes.

**Tech Stack:** C# 11; library targets `netstandard2.1` and `net8.0`; .NET 10 SDK
with .NET 8 runtime; FixedMathSharp 7.1.0; GridForge 9.1.0; SwiftCollections;
Chronicler; xUnit v3; BenchmarkDotNet; DocFX.

**Spec:** The approved scope is recorded in [Agreed scope](#agreed-scope).
This document retains the decisions, implementation boundaries, and progress
evidence together; there is no separate design document required to understand it.

## Agreed scope

Execute in this order:

1. Combine end-to-end vertical traversal scenarios with deterministic movement-
   quality evidence. Prove the complete guidance-to-accepted-motion integration,
   rather than only individual graph or motor operations. Include fall, flight,
   and controlled-descent transitions, using a small Gravitas-backed 3D host.
2. Add safe runtime replacement of a Navigator's navigation profile for changes
   in body shape or traversal capabilities, without a full controller reset.
   Exercise complete posture cycles and coordinate physical collider changes.
3. Measure cold public guide acquisition in a representative fixed-frame
   workload, emphasizing 10K-100K-node Flow builds. Deliver measurements and a
   decision, not a scheduler implementation.

Phase 3 justified a separately approved scheduling follow-up, now completed.
Its CPU-cost improvements and full-lifecycle measurements are retained in the
[scheduling closeout](navigationSchedulingPlan.md), not duplicated here.

### Related plans and shared evidence

- [Gravitas Integration](gravitasIntegrationPlan.md) owns the reusable adapter
  and joint physics/controller contract. Its first 3D slice supplies this
  plan's concrete host; it does not require runtime profile replacement first.
  Build that slice alongside Phase 1, then reuse its tests and accepted-motion
  evidence here. Adapter packaging does not gate the headless scenarios.
- Phase 2 owns runtime profile replacement. The integration plan consumes that
  contract for coordinated collider changes after the initial 3D slice works.
- [First-Class 2D Navigation](../twoDimensionalNavigationPlan.md) is separate
  feature work. These hardening scenarios target the current world-Y-up 3D
  controller; a fixed-height projection is not evidence of native 2D support.
- Retain each shared scenario result once, with its exact adapter/library
  revisions and inputs. Link the evidence from both plans, but close their
  checkboxes only when each plan's own acceptance criteria are met.
- The subsequent [joint movement hardening record](gravitasIntegrationPlan.md#2026-09-20---joint-movement-hardening)
  retains the real collision-resolved ladder/dismount, low/high-speed contact,
  slope, push, platform replay, and shared raw-motion traces. It extends these
  archived scenarios without reopening the accepted performance campaign.

## Global constraints

- Correctness and determinism precede maintainability and performance. Runtime
  math remains fixed-point, ordering explicit, and work/capacities bounded.
- Keep core libraries engine-agnostic. Visual scenes, collision probes, terrain
  authoring, and gameplay action execution belong to a host integration.
- Preserve one coherent public API. Keep profiles immutable values, with any
  replacement coordinated by their owning Navigator rather than independent
  mutation of derived geometry or controller settings.
- Preserve exact guide dependencies and per-lease cursor, medium, and action
  ownership. Do not skip actions or reuse invalidated navigation proof.
- Use existing context-first fixtures and behavior tests. Do not add existence-
  only checks, duplicate tests, or unreachable branches solely to affect coverage.
- Maintain exact 100% reachable line, branch, and method coverage in both
  `Release` and `ReleaseLean`; do not lower gates or exclude new runtime logic.
- Preserve transactional populate-existing-instance serialization. JSON remains
  supported in both packages; MemoryPack remains supported in standard Release.
- Keep runtime fixes focused and record newly discovered correctness issues in
  [issue-tracker.md](../issue-tracker.md). Record measured performance signals in
  [benchmark-signal-hardening-backlog.md](../benchmark-signal-hardening-backlog.md).
  A suspected movement-quality risk is not automatically a confirmed bug.
- Do not stage, commit, tag, push, publish, or edit another repository without
  the corresponding user authorization. Preserve unrelated worktree changes.

## Progress dashboard

| Phase | Deliverable | Status | Completion gate |
| --- | --- | --- | --- |
| 1 | Vertical traversal and movement-quality evidence | Complete | Headless acceptance cases and deterministic accepted-motion evidence recorded; confirmed defects resolved or explicitly dispositioned |
| 2 | Runtime navigation-profile replacement | Complete | Atomic replacement, rejection behavior, guidance lifecycle, and serialization verified |
| 3 | Cold public guide acquisition measurements | Complete | Repeated cold/warm and containing-frame evidence; profile acquisition cost before scheduler design (`TRB-Benchmark-001`) |
| Closeout | Reviewed implementation and documentation | Complete locally | Local matrix, exact coverage, relevant benchmarks and review complete; CI/released-package validation transferred to coordinated release gates |

The completed checklists below preserve the accepted scope. Dated results are
condensed in the [execution record](#execution-record).

## Starting evidence

The baseline already contains useful component coverage. Reuse it rather than
recreating those tests under new names:

- [NavigationExplicitConnectionTests.cs](../../../tests/Trailblazer.Tests/Pathing/Graph/NavigationExplicitConnectionTests.cs)
  verifies step limits per semantic leg.
- [NavigationFlowFieldEquivalenceTests.cs](../../../tests/Trailblazer.Tests/Pathing/Graph/NavigationFlowFieldEquivalenceTests.cs)
  compares A* and Flow across composed maps, including vertically separated grids.
- [NavigationTransitionSimulationTests.cs](../../../tests/Trailblazer.Tests/Navigation/NavigationTransitionSimulationTests.cs)
  covers transition publication, held instructions, and invalidation.
- [ClimbLocomotion.Tests.cs](../../../tests/Trailblazer.Tests/Navigation/Motor/Locomotion/ClimbLocomotion.Tests.cs)
  verifies climbing from a host-provided ladder affordance.
- [JumpLocomotion.Tests.cs](../../../tests/Trailblazer.Tests/Navigation/Motor/Locomotion/JumpLocomotion.Tests.cs)
  and [FlyLocomotion.Tests.cs](../../../tests/Trailblazer.Tests/Navigation/Motor/Locomotion/FlyLocomotion.Tests.cs)
  cover individual jump/fall/flight rules. Extend them only for missing behavior;
  the additional value here is complete input/state/contact sequences.
- [NavigationFlowFieldGuideTests.cs](../../../tests/Trailblazer.Tests/Pathing/Graph/NavigationFlowFieldGuideTests.cs)
  exercises off-axis portal progression and explicit connection witnesses.
- [NavigationFlowFieldBenchmarks.cs](../../../tests/Trailblazer.Benchmarks/Pathing/NavigationFlowFieldBenchmarks.cs)
  measures internal cold integration at 100, 1K, 10K, and 100K settled nodes.
  [NavigationGuideServiceBenchmarks.cs](../../../tests/Trailblazer.Benchmarks/Pathing/NavigationGuideServiceBenchmarks.cs)
  currently measures warm public Flow acquisition, sampling, and return.

These establish existing capabilities, not completion of the end-to-end
scenarios or fresh performance evidence for this plan.

## Phase 1 - Vertical Traversal And Movement Quality

**Outcome:** A repeatable set of worlds demonstrates that legal routes become
successful accepted motion, and that invalid profiles fail explicitly.

### Code and documentation map

- Extend [GuidedPathTestScene.cs](../../../tests/Trailblazer.Tests/Support/GuidedPathTestScene.cs)
  and the matching navigation tests when those fixtures fit. Add a focused
  scenario fixture only when sharing actual setup avoids duplication.
- Exercise the public Navigator/guide lifecycle and the existing
  [motor](../../../src/Trailblazer/Navigation/Motor),
  [steering](../../../src/Trailblazer/Navigation/Steering), and
  [Flow sampling](../../../src/Trailblazer/Pathing/Search/Flow) implementations.
  These are investigation targets, not a mandate to edit every subsystem.
- Use [NavigatorHeightmapGrounding.Tests.cs](../../../tests/Trailblazer.Tests/Navigation/Navigator/NavigatorHeightmapGrounding.Tests.cs)
  for a narrow heightmap-layer handoff companion case.
- Reuse the fixed-frame host and accepted-motion boundary from
  [GravitasNavigatorScenarioFixture.cs](../../../tests/Trailblazer.Gravitas.Tests/Support/GravitasNavigatorScenarioFixture.cs)
  and its focused
  [adapter behavior tests](../../../tests/Trailblazer.Gravitas.Tests/GravitasNavigator3D.AcceptedMotion.Tests.cs).
  Extend scenario setup deliberately; do not duplicate the adapter lifecycle in
  a second fixture.
- Update [Transitions](../../wiki/Transitions.md), [Navigator](../../wiki/Navigator.md),
  [NavMotor](../../wiki/NavMotor.md), or [Path guides](../../wiki/PathGuides.md) only
  where the scenarios reveal missing integration guidance.

### Acceptance scenarios

| Scenario | Required observations |
| --- | --- |
| Flat ground, stairs, upper landing | Consume guidance through a flight of authored steps; resolve actual host contacts; arrive at the correct map/cell and foot position |
| Ladder between walkable levels | Approach the source; climb over fixed frames using the host affordance; report destination state; complete the exact instruction once; continue walking |
| Multiple stacked platforms | Include levels sharing X/Z, explicit endpoint/map selection, and distinct foot anchors; never arrive at the wrong level |
| Narrow or off-axis portals | Compare sampled guidance with combined steering and accepted motion for a large fitting body, an invalid body, and a group |
| Walk off an edge, fall, land | Reject a jump while falling; preserve the configured ascent-time extra-jump behavior in a separate positive control; report one landing transition |
| Fall, acquire flight, descend under control | Permit recovery only when flight is enabled and requested; clear mutually exclusive transient states; descending flight is not falling |
| Active flight plus jump input | Compare identical flight input with and without the jump request; retain flight, emit no jump event, do not change jump count, and produce the same accepted motion |
| Flight ends or becomes unavailable | Cover release, capability loss, and module disable while hovering, ascending, and descending; do not force upward momentum to become immediate downward motion |
| Free fall versus parachute-style descent | Use the same drop and deployment frame; observe bounded deceleration, descent speed, steering, cancellation/redeployment, and landing/fall-height events without granting powered ascent |

For Phase 1, test flight loss through existing motor permissions and requests.
Changing an installed Navigator profile is a Phase 2 case; do not make Phase 1
depend on that new API. Physical Gas medium, navigation Fly capability, and
active flight are different concepts: removing flight does not erase the
agent's airborne physical state or authorize an otherwise invalid route.

Parachute-style descent is a behavior to validate, not a commitment to a new
module or equipment system. Define deployment deceleration, terminal speed,
air steering, and landing policy in the scenario before implementation. Do not
use flight activation merely to slow falling: it clears fall state. Decide
explicitly whether fall-height events remain distance-based and how the host
uses accepted impact velocity; health/damage rules remain host owned. If the
existing host/tuning boundary cannot express the agreed behavior, record the
gap and review the smallest contract change before adding runtime machinery.

The Phase 1 controlled-descent contract keeps the behavior inside the required
`FallLocomotion` instead of adding an equipment module or overloading flight.
An explicitly configured agent may deploy only after ordinary falling has begun
in Gas. Deployment keeps `IsFalling` and distance-based fall events active,
approaches a configured downward terminal speed with bounded deceleration,
applies a separate configured lateral-control multiplier, and never creates
upward velocity. Releasing the request resumes ordinary gravity; redeployment is
allowed during the same fall; landing or disabling fall clears deployment.
Accepted impact velocity remains observable host motion and damage remains host
owned. Track this missing contract as `TRB-Issue-104` until its component and
Gravitas-backed regressions pass.

### Scenario inputs and bounds

- All Gravitas-backed worlds advance Trailblazer and Gravitas together at
  32 Hz and commit only collision-accepted pose and velocity.
- The stair world contains four automatically composed maps and three
  three-quarter-unit rises. A fitting profile has a one-sixteenth-unit radius,
  a quarter-unit body height, and three-quarter-unit step/drop limits; it must
  reach the fourth map within 512 frames with A* and Flow. Exact inclusive
  radius/headroom/step limits succeed, while one raw fixed-point unit beyond
  each limit rejects. Reversed A*/Flow queries separately prove the exact
  three-quarter-unit drop and a drop limit one raw unit below it.
- The stacked-floor world publishes lower, middle, and upper maps at shared X/Z.
  An explicit upper-map query from cell 0 to cell 2 must remain on that map and
  arrive within 256 frames with A* and Flow. The heightmap companion first
  proves the same graph truth without projection, then opts into the upper
  height layer without changing the graph destination.
- The ladder route walks from cell `(0,0,0)` to a source at `(1,0,0)`, holds one
  exact climb instruction for four host-action frames, reports completion once
  at `(1,2,0)`, and resumes to `(2,2,0)`. Approach and continuation each have a
  512-frame bound under A* and Flow. Missing climb capability rejects before
  action start; invalidation while held makes later completion stale.
- The off-axis world selects an explicit portal of radius three eighths over an
  expensive native middle cell. Single radii of one eighth and three eighths,
  plus a non-overlapping two-agent group, use Flow at `TrekRate.Fast` and finish
  within 384 frames. Graph-level A*/Flow equivalence also proves exact-fit and
  one-raw-unit-too-large clearance outcomes.
- Airborne scenarios use a 256-frame landing/jump bound, a deterministic
  12-frame powered-flight trace, all nine release/capability/module-loss by
  hover/ascent/descent combinations, and a 192-frame paired free-fall versus
  controlled-descent trace. Controlled descent deploys at frame 4, cancels at
  frame 12, redeploys at frame 15, targets one unit per second downward, and
  reduces excessive descent by at most four units per second squared.

- [x] **Establish scenario inputs and bounded expectations.** Record authored
  maps/transitions, profiles, fixed timestep, start/end addresses, action
  handler, and the maximum simulation frames for each case. Run the same
  applicable worlds with A* and Flow; compare legal outcomes, not identical
  frame-by-frame trajectories between algorithms.
- [x] **Build full motion acceptance tests.** Drive context simulation,
  guidance, Navigator/motor motion, host collision/contact refresh, and commit
  or finalization. Do not advance by teleporting directly to each guide point.
  Assert accepted foot positions, final address/medium, arrival, and action
  execution count. A static successful case must not rely on repeated retries
  or silent recovery to hide a failure.
- [x] **Add meaningful boundary cases.** Include insufficient step/drop limits,
  inadequate headroom/radius clearance, unavailable climb capability, and a
  transition invalidated before completion. Assert the documented failure or
  stale result and no unapproved action or wrong-floor arrival.
- [x] **Exercise airborne sequences rather than isolated flags.** Verify the
  scenario table over bounded fixed frames, including simultaneous jump/flight
  input against its no-jump flight control, denied flight activation, flight loss
  before the apex, landing reset, and controlled-descent cancellation. Assert
  accepted velocity/position, unchanged jump count, no jump event, and retained
  flight as well as other locomotion state. Replay identical inputs and compare
  state; preserve deliberately configured extra jumps during ascent.
- [x] **Separate graph truth from heightmap projection.** Establish stacked-
  floor correctness without relying on heightmap layer selection to choose the
  route. Then verify an opt-in layer handoff preserves the graph destination
  and actual grounded state. Heightmaps do not create stair/ladder connectivity.
- [x] **Capture movement-quality evidence.** Record selected edge/portal,
  sampled heading, combined group/avoidance heading, accepted displacement,
  completion frames, and any oscillation, portal misses, stuck states, or
  replanning. Compare single agents and groups at normal and larger valid sizes.
  Distinguish authoritative movement from presentation interpolation.
- [x] **Resolve demonstrated problems, not hypothetical ones.** Record each
  confirmed issue, capture a failing regression before a focused fix, and rerun
  the affected scenario. A steering adjustment must preserve body clearance and
  action boundaries; cosmetic smoothing cannot substitute for legal motion.
- [x] **Review and close Phase 1.** Run focused tests in both configurations,
  retain the deterministic accepted-motion evidence, and obtain independent
  review. Any unresolved defect requires an explicit disposition.

## Phase 2 - Runtime Navigation Profile Replacement

**Outcome:** A host can change an agent's body shape or traversal capabilities
without reconstructing the Navigator or leaving dependent state inconsistent.

### Code and documentation map

- [NavigationAgentProfile.cs](../../../src/Trailblazer/Pathing/Query/NavigationAgentProfile.cs)
  remains the immutable geometry/capability value.
- [Navigator.cs](../../../src/Trailblazer/Navigation/Navigator/Navigator.cs)
  owns replacement and coordinates geometry, foot position, and guidance.
- [NavTurning.cs](../../../src/Trailblazer/Navigation/Turning/NavTurning.cs),
  [steering](../../../src/Trailblazer/Navigation/Steering), and
  [Navigator occupancy](../../../src/Trailblazer/Navigation/Navigator/Occupancy)
  contain dependent state to inspect and update where affected.
- [Navigator.Serialization.cs](../../../src/Trailblazer/Navigation/Navigator/Navigator.Serialization.cs)
  and [NavigationAgentProfileRecord.cs](../../../src/Trailblazer/Navigation/Serialization/NavigationAgentProfileRecord.cs)
  define the existing persistence boundary.
- Extend [Navigator.Tests.cs](../../../tests/Trailblazer.Tests/Navigation/Navigator/Navigator.Tests.cs),
  [NavigatorSerialization.Tests.cs](../../../tests/Trailblazer.Tests/Navigation/Navigator/NavigatorSerialization.Tests.cs),
  and the Phase 1 scenarios. Update [Navigator](../../wiki/Navigator.md),
  [Serialization](../../wiki/Serialization.md), affected public XML, and runnable
  examples with the final contract.

### Replacement contract

`Navigator.TryReplaceNavigationProfile(NavigationAgentProfile replacement)`
returns a `NavigationProfileReplacementStatus`. The expected outcomes are
`Applied`, `Unchanged`, `InvalidProfile`, `UnsafeLifecycle`,
`ActionInProgress`, `FootAnchorUnrepresentable`,
`CurrentMediumNotAllowed`, `NavigationClearanceRejected`,
`GuidanceIncompatible`, and `PhysicalFitRejected`. Expected rejection never
throws, queues work, or partially changes live state.

Replacement is valid only on an active Navigator between fixed frames. It
returns `UnsafeLifecycle` while traversal is open, accepted motion has not been
committed, or public position/rotation motion remains queued. It returns
`ActionInProgress` without cancelling or completing a pending semantic action.
An exactly equal profile returns `Unchanged` without consulting the host,
releasing guidance, moving occupancy, or resetting controller state.

The exact pre-replacement `FootPosition` is the anchor. Checked fixed-point
arithmetic derives the candidate root from that foot and the replacement
`RootToFootOffsetY`; the root and previous root move by the same delta so the
change cannot create synthetic velocity. Current physical medium is preserved
and must be allowed by the replacement profile. Navigation clearance is tested
at the exact foot against the current graph cell's obstacle, medium,
capability, area-policy, radius, and height rules. An input-driven Navigator
without a materialized graph cell may still use the host's physical check.

When durable path intent exists, replacement rebuilds the immutable query with
the candidate profile, accepted foot, and actual medium while preserving its
destination, policy revision, target media, algorithm, budgets, transition
permissions, Flow options, and movement-group identity. Structurally
incompatible intent rejects; an otherwise valid changed route releases the old
lease exactly once and acquires fresh guidance. No replacement may silently
choose another goal or let an old guide/action token act on the new profile.

The engine-agnostic base Navigator accepts physical replacement through one
protected synchronous hook and a core-state publisher. A derived host adapter
must either reject without mutation or publisher invocation, or commit its
complete physical envelope and candidate root, invoke the publisher exactly
once, and only then expose physical post-commit notifications. The publisher
updates Trailblazer's profile, root and previous root, occupancy, turning radius,
movement-group radius, capability-loss locomotion state, query intent, and
committed cell without recreating the Navigator or its controllers. Losing
`Fly` ends active flight but does not change the actual medium or overwrite the
host's independent flight permission; gaining `Fly` never starts flight
implicitly.

The Gravitas adapter depends on one focused upstream `SolidBody` transaction:

```csharp
ColliderReconfigurationStatus TryReconfigureCollider(
    ColliderShapeDefinition definition,
    Vector3d localOffset,
    Vector3d position,
    out LSCollider? blocker,
    out Exception? notificationException,
    Action? publishSynchronizedState = null)
```

It requires the existing collider family, changes geometry/offset/root only,
and preserves body/collider identity, rotation, material, filters, hierarchy,
events, and motion state. `Blocked` means a valid candidate positively
penetrates an eligible collider; zero-depth support contact remains legal.
Invalid definitions, family mismatch, and unsafe Gravitas lifecycle use the
library's existing exception policy. The candidate is prepared and exact-fit
tested before authored geometry, committed shape, mass properties, host pose,
pair caches, and pure/mixed partitions publish together. Rejection leaves those
values, serialization, and replay hash unchanged. The local offset is explicit
because a vertical collider uses `height / 2 - RootToFootOffsetY`; root offset
is not required to equal half-height. After physical publication, the optional
synchronized-state publisher lets the adapter commit Trailblazer before any
separation callback runs. Publisher and separation-callback failures are
returned separately from the applied transaction status and rethrown only after
all post-commit notifications finish.

### Tasks and acceptance criteria

Required posture sequences are stand -> crouch -> traverse a low passage ->
stand and stand -> compact ball -> traverse -> stand. For each, reject expansion
inside the passage, preserve the compact/crouched profile and collider exactly,
then succeed after leaving it. Repeat both sequences on a moving platform. Feet
and attachment must remain stable under the agreed anchor policy. The compact
ball is a presentation plus the same profile/collider-change mechanism; actual
rolling, torque, or inertia is separate host/physics behavior, not a new
navigation mode.

- [x] **Settle the focused replacement contract before implementation.** Record
  the operation's final name/signature, safe fixed-frame boundary, and outcomes
  here. Specify which position remains fixed when the root-to-foot offset
  changes, how current physical fit is validated through host-owned collision
  information, and how navigation clearance is checked. Stage the corresponding
  host collider change with the profile, and specify rollback/rejection so no
  simulation frame observes mismatched physical and navigation geometry. Keep
  Gravitas-specific types in the adapter. Define rejection or
  deferral during an open traversal or pending semantic action; never silently
  complete, cancel, or replay a host action.
- [x] **Capture failing behavior tests for that contract.** Cover crouch/stand,
  shrinking/growing near clearance boundaries, changed foot offset, and gained
  or lost traversal capabilities. Include an identical-profile no-op, invalid
  values, an incompatible current medium, and an unsafe lifecycle boundary.
  Rejected changes must leave the prior profile and live state unchanged.
- [x] **Verify whole posture cycles against physical geometry.** Cover each
  stand/crouch and stand/compact sequence above, including traversal while small,
  blocked growth, successful retry after leaving the obstruction, and platform
  carry across entry and expansion. Assert both collider and navigation geometry,
  unchanged state after rejection, accepted foot position, stable identity, and
  preserved destination. Reuse the integration plan's collider-change tests
  rather than maintaining a second competing contract.
- [x] **Implement one coordinated replacement path.** Stage validation before
  mutation. Synchronize authoritative shape, derived foot position, turning's
  body-radius threshold, and affected group/occupancy state. Preserve stable
  identity, bindings, callbacks, tuning, and unrelated locomotion state. Do not
  use full `Reset()`/`Setup()` as the mutation mechanism.
- [x] **Reconcile navigation intent and active guidance.** Release incompatible
  guidance exactly once and build fresh query intent using the new profile and
  current foot/medium. Preserve a valid destination and other durable intent;
  report incompatibility explicitly rather than silently choosing another goal.
  Do not let an old lease or completion token act on the changed body/profile.
- [x] **Join capability changes to airborne recovery.** Repeat Phase 1 flight
  loss/recovery with actual profile replacement and a guided destination.
  Coordinate motor permission with navigation capability, invalidate unusable
  routes, and preserve the host's actual medium. Test removal of Fly while Gas
  remains allowed separately from a replacement that disallows the current
  medium; the latter must follow explicit rejection/recovery policy, not silently
  relabel a falling body as grounded.
- [x] **Verify persistence and replay.** Save after a successful replacement,
  create a shell with the recorded active profile, and verify transactional
  population plus fresh guidance acquisition. Preserve mismatched-shell and
  malformed-record rejection without partial mutation. Cover JSON in both
  packages and MemoryPack in Release. Keep runtime leases/actions transient;
  change schemas only if the actual wire contract requires it. Save/load while
  crouched or compact, resume the remaining posture sequence, and verify the
  restored collider and profile agree before the next fixed frame.
- [x] **Exercise mutation in the Phase 1 worlds.** Verify standing under a low
  ceiling rejects safely, a smaller body can use newly legal geometry, and
  capability changes cannot bypass an authored action or its completion rules.
  Repeat deterministic input sequences and compare resulting state.
- [x] **Review and close Phase 2.** Validate complete changed-path coverage,
  lifetime/allocation behavior, docs/examples, and the full package matrix.
  Obtain independent review of atomicity, lease ownership, and serialization.

## Phase 3 - Cold Public Guide Acquisition Measurement

**Outcome:** Reproducible end-to-end measurements establish whether synchronous
acquisition fits a concrete host's simulation budget.

### Measurement boundary

[TrailblazerGuideService.cs](../../../src/Trailblazer/Pathing/Search/Guide/TrailblazerGuideService.cs)
drained public A* and Flow requests synchronously at the Phase 3 baseline.
Internal work could resume in bounded slices, but query budgets bounded total
work, not elapsed time in one frame. The later scheduler adds an explicit
alternative without changing that synchronous contract.

Extend the existing
[benchmark project](../../../tests/Trailblazer.Benchmarks/README.md). Add focused
cold public-service cases alongside the existing internal cold and public warm
cases; do not replace the warm-cache controls or time world construction as if
it were acquisition.

- [x] **Define the host acceptance budget before interpreting results.** Record
  target runtime/hardware, fixed timestep, navigation's allotted portion of a
  frame, expected request burst, and representative geometry. Frame-time
  thresholds are measurement criteria, not wall-clock-driven simulation rules.
- [x] **Measure cold public Flow requests at 10K and 100K settled nodes.** Use
  the public request API, prepublished worlds, finite sufficient capacities,
  and a verified cache-miss setup for each measured operation. Establish small
  controls and representative portal/transition cases so a straight corridor
  alone does not stand in for all workloads.
- [x] **Measure the containing fixed-frame workload.** Include existing-agent
  simulation and realistic request bursts. Separate cold payload construction,
  warm reuse, public acquisition overhead, and any setup/pool warmup costs.
  Add a public A* control where relevant; it shares the synchronous boundary.
- [x] **Archive reproducible evidence.** Record exact commit/configuration,
  package versions, hardware/runtime, commands, workload sizes, cache state,
  latency distribution, whole-frame cost, work counters, allocations, retained
  bytes, and terminal statuses. Use repeated canonical runs for decisions, with
  the reviewed three-launch `SlowAcquisition` amendment for the multi-second
  cold rows; its nine samples support rejection, not a reliable P99 estimate.
  Short smoke runs are not decision evidence. Keep generated reports out of tracked
  source and reference their retained artifact location from the execution record.
- [x] **Write the acquisition decision.** Compare evidence against the stated
  frame budget. Record either why current acquisition is adequate, a focused
  defect/optimization to investigate, or a measured need for scheduled work.
  Put performance signals in the benchmark backlog with reproduction evidence.
- [x] **Describe follow-up constraints only if scheduling is justified.** Any
  later implementation needs context ownership, stable ordering, per-frame work
  slices distinct from total query limits, explicit pending/cancellation,
  bounded retained resources, and stale-proof publication. Background task
  completion order must not control lockstep results. Seek explicit approval
  for that implementation; it is not part of this phase's deliverable.
- [x] **Review and close Phase 3.** Independently review benchmark boundaries,
  cold/warm preconditions, reproducibility, and the decision. Writing benchmark
  code alone does not complete the phase; a justified no-scheduler outcome does.

## Verification And Closeout

- [x] Full local-source Release/ReleaseLean solution builds and tests, both
  library targets, exact reachable coverage and independent reviews completed.
- [x] Deterministic acceptance, replay, transactional load, allocation and
  ownership gates verified. `TRB-Issue-114` was mitigated but unattributed at
  this checkpoint. The [issue tracker](../issue-tracker.md) records its subsequent
  2026-09-10 inconclusive closure, not a confirmed production fix.
- [x] Changed examples, wiki publishing helpers and DocFX verified locally.
- [x] Phase 3's rejected timing target received a measured follow-up disposition:
  `TRB-Benchmark-001` is closed in the [scheduling plan](navigationSchedulingPlan.md#final-disposition).
- [x] Supporting measurement notes condensed here; profiling and scheduling
  evidence condensed into the scheduling plan; overview and inbound links updated.

These are Windows/local-source results. Windows/Linux CI for the eventual tested
commit and validation against the final released dependency chain were not run
for this closeout. They are retained as unchecked
[coordinated release gates](../feature-work-overview.md#coordinated-release-gates).
Engine-host visual playtesting belongs to future engine-specific adapter work.

Use the [contributor guide](../../../AGENTS.md) and
[coverage workflow](../../../.github/workflows/coverage.yml) for verification
commands. Do not restore/build different package families concurrently in one
checkout; validate unreleased coordinated changes with `UseLocalLsfStack=true`,
then repeat with package references after dependency releases.

## Execution record

### Phases 1 and 2 - accepted behavior

Phase 1 was reviewed on 2026-09-06 and closed on 2026-09-07. The shared Gravitas
host proves physical stairs, stacked floors, an exact four-frame ladder action,
off-axis Flow for single/grouped bodies, airborne transitions, deterministic
replay and paired free-fall/controlled-descent impact traces at 32 Hz.
`TRB-Issue-104` through `107` and upstream `GRV-Issue-069` were resolved.
Source baselines were Trailblazer `ef5da2142cd35bb47658e113ebb3a403dd542134`
and Gravitas `819e635cf06b416cfc81da82c474b5688d3063a7`, plus the reviewed diffs.

Phase 2 completed on 2026-09-08 from Trailblazer
`0ac474a3da3947c161b4ea57bc82b588b702c7e2` and Gravitas
`f25f1b8f617427f7e41824103f323da72d1a076f`, plus reviewed diffs.
`Navigator.TryReplaceNavigationProfile(...)` preserves the foot anchor and
identities while preflighting graph, occupancy, guidance, medium and physical
fit. The coordinated Gravitas capsule transaction commits matching physical
and navigation envelopes before separation notifications; callback failure
cannot leave cross-library split state or interrupt remaining pair retirement.
Tests cover stand/crouch/compact cycles, blocked growth/retry, translating and
rotating platforms, offset/capability changes, durable A*/Flow intent, action
ownership, deterministic replay and JSON/MemoryPack population. Restore rebuilds
the Gravitas body before the Navigator. `TRB-Issue-108` through `112` and
`GRV-Issue-070` were resolved, including duplicate parallel build-node races.

| Local checkpoint | Release core + adapter tests | Lean core + adapter tests | Lines / branches / fully covered methods (both configurations) |
| --- | ---: | ---: | --- |
| Phase 1 | 2,355 + 52 | 2,294 + 52 | 30,161 / 11,829 / 2,955, all exact |
| Phase 2 and Phase 3 | 2,395 + 65 | 2,333 + 65 | 30,628 / 11,977 / 2,982, all exact |

Phase 2 Gravitas separately passed 3,950 Release and 3,895 Lean tests, with exact
44,173 / 12,918 / 4,530 and 44,171 / 12,918 / 4,529 line/branch/method totals.
Both repositories passed DocFX warnings-as-errors and wiki helper checks.
The later scheduler's final-build matrix is recorded in its
[verification section](navigationSchedulingPlan.md#verification-and-provenance).

### Phase 3 - measurement protocol and baseline

This section condenses the former `navigationAcquisitionMeasurements.md`.
Measurements completed on 2026-09-08, before the later runtime optimizations.
The acceptance host was Windows 11 build 26200, i7-9700K (8 cores), 32 GiB,
.NET 8.0.29 / SDK 10.0.302, BenchmarkDotNet 0.15.8, Release, workstation
concurrent GC and the high-performance power plan. The declared budget was
**2 ms navigation observed P99 inside a 32 Hz (31.25 ms) frame**.

The host simulates 100 input-driven Navigators, without a collision solver,
guide-following agents, rendering, networking or gameplay. Worlds are unit-cell
Solid planes of 10x10, 100x100 and 400x250, plus a 100-node ladder joining two
50-cell landing strips separated by 2 Y units. The ladder has one Climb action
with cost 1. Profiles use radius 0.25, height 1, zero offset/step/drop and
Solid-only travel (plus Climb on the ladder); authored clearance is 4 with
zero added terrain cost. These are controlled workloads, not a typical-game claim.

For N nodes, query budgets provide N expansions, 512N lookups, 32N edges and
4N connection/transition/staged/trace/covered work. Endpoint limits are 2 on
planes and 8 on the ladder; simplification rays are disabled. One active query
slot has N node capacity, 2N-1 point capacity, two components and eight entries
per cache; payload caps use the benchmark's exact sizing helper.
Four-request bursts use distinct exact profile keys with identical graph
eligibility, not four consumers reusing one destination payload.

Cold cases use three launches, one warmup and three actual operations per launch:
nine observations justify reporting min/median/max, not a reliable P99.
Canonical warm/baseline cases use three launches, ten warmups and 100 actual
operations per launch. Accepted evidence contains 28 rows, 84 successful child
launches and 3,744 actual observations. Failed sizing/smoke runs are excluded;
reported outliers remain in the accepted distributions. Setup/publication/cache
preparation are untimed; cold queries verify empty caches before each invocation.
Public acquisition includes lease disposal but not sampling.

| Baseline workload | 10K median ms | 100K median ms |
| --- | ---: | ---: |
| Cold public Flow | 5,513.532 | 55,415.369 |
| Cold public A* | 2,138.129 | 21,920.730 |
| Frame containing four distinct cold Flow requests | 21,725.395 | 221,105.422 |

Every large cold observation exceeds even the entire 31.25 ms frame.
The baseline 100K four-warm-request frame has median 2.587 ms, observed P99
2.810 ms and maximum 3.122 ms; all 300 observations exceed 2 ms.
This justified profiling first, then a separately approved scheduler—not
treating scheduling as a substitute for reducing CPU cost.
Small 100-node plane medians were 71.578 ms Flow / 28.063 ms A*, and ladder
86.272 / 83.564 ms. **Those small controls were not remeasured after optimization.**

Matched internal Flow work settles exactly 10,000 / 100,000 nodes, evaluating
236,408 / 2,388,308 edges and 476,569 / 6,096,442 lookups. These are Flow counters,
not A* expansion counts. Flow immutable payloads retain 769,272 / 7,688,008 bytes;
A* payloads retain 31,560 / 160,696 bytes with 397 / 1,297 steps. A* returns a
route while Flow integrates a reusable field; their times are not interchangeable.
The benchmark-only internal workspace allocation preflight is 13,672,200 /
110,552,200 bytes, not a production retained-heap estimate.
The two older service controls print static cleanup zeros; only the new fixtures'
actual drained-ownership checks establish fresh teardown evidence.

The baseline used these commits plus recorded working-tree changes:

| Repository | Commit |
| --- | --- |
| Trailblazer | `040c886972f011d15eb9b3189314224817e331d5` |
| FixedMathSharp | `41eba3cc09e533e505faa66bcd3c7c68606f7258` |
| SwiftCollections | `df71a0fad083f42a8dde528f8a0634ab8d5a5a54` |
| GridForge | `b693e383c1a04e07fd8f4048a6c863b3f7af515d` |
| Gravitas (test matrix only) | `42af506fec0d1aabe5ff9750e0a2d148fe82a5b0` |

Independent review verified all distributions, timing boundaries, allocation,
payload and ownership evidence. Final post-capture builds/coverage passed the
Phase 2/3 totals above, DocFX had zero warnings/errors and wiki helpers passed
4/4. `TRB-Issue-113` corrected stale benchmark guidance; `TRB-Issue-114` records
the 616-byte allocation-guard failure and test-only warmup mitigation, not a
runtime allocator fix.

Reproduction commands and fixture contracts remain in the
[benchmark README](../../../tests/Trailblazer.Benchmarks/README.md#cold-public-acquisition-and-containing-frames).
Raw logs, all distributions, source hashes and report fingerprints remain in the
ignored local archive `artifacts/hardening-phase3/phase3-acquisition-evidence.zip`,
SHA-256 `D1ACA63C0FD9D9E357AC422E418EA4C79B534512BAE9AF4150E684ED200F0B06`.
This archive is local evidence, not a checked-in or publicly downloadable artifact.
The final profiling/optimization comparison is consolidated in the
[scheduling closeout](navigationSchedulingPlan.md#cpu-cost-improvements).
