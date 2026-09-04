# First-Class 2D Navigation Implementation Plan

> **For agentic workers:** Use `superpowers:subagent-driven-development` or
> `superpowers:executing-plans` when execution is requested. Complete the contract
> review before implementing runtime types. Use independent reviewers and the
> checkboxes below. This draft does not start implementation.

**Created:** 2026-09-03

**Status:** Draft; dimensional contracts and implementation are not complete.

**Source baselines:** Trailblazer `cb0d744b76a377807a253f0efa1c480ba169a7e9`;
Gravitas `6ade2f0bb373665efc5b7b85a3d21d1a371aa936`.

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
  [Gravitas integration plan](gravitasIntegrationPlan.md).
- Runnable host examples and actual movement-quality observations, not only
  compile-time API checks or a 3D scene viewed by an orthographic camera.

This is separate from [3D navigation hardening](navigationHardeningPlan.md).
Start runtime implementation after the initial 3D Gravitas accepted-motion
slice is reviewed; consume the reviewed hardening profile-replacement contract
before implementing its 2D counterpart. Neither plan depends on completed 2D
work. A fixed-height top-down projection can inform an early experiment but is
not the deliverable or a permanent parallel public compatibility API.

## Global constraints

- Preserve deterministic fixed-point math, stable ordering, finite work budgets,
  exact guide dependencies, and per-acquisition action/cursor ownership.
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
- Record discovered defects in [issue-tracker.md](issue-tracker.md) and measured
  performance concerns in the [benchmark backlog](benchmark-signal-hardening-backlog.md).
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
| Wiki, API documentation, executable sample | Separate top-down/platformer examples with unambiguous coordinate and ownership guidance |

Phase 0 must replace this responsibility map with exact proposed file paths and
public signatures before runtime work. A dedicated 2D surface is the direction;
this draft does not pretend that its type names or graph representation have
already passed design review.

## Progress dashboard

| Phase | Deliverable | Status | Gate |
| --- | --- | --- | --- |
| 0 | Reviewed dimensional and API contract | Not started | Exact geometry, support, graph, lifecycle, file, and serialization decisions |
| 1 | Native planar guidance and top-down movement | Not started | A*/Flow, planar clearance, accepted motion, and action boundaries verified |
| 2 | Side-view traversal and profile changes | Not started | In-plane support, locomotion, posture/capability sequences, and replay verified |
| 3 | Gravitas2D integration and closeout | Not started | Real planar collisions, runnable examples, observation, full matrix, and review |

## Phase 0 - Settle The 2D Contract

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
- [ ] **Define media, capability, and support semantics.** Explain how top-down
  traversability and side-view airborne/grounded state relate to navigation
  media. A downward direction alone cannot identify falling, and a permitted
  cell cannot stand in for actual physical support. No automatic gap-crossing
  action may arise from ordinary neighbor traversal.
- [ ] **Set compatibility and persistence boundaries.** Record how 2D profiles,
  graph/guide identity, and records differ from 3D. Reject cross-dimensional
  shell or guide misuse explicitly; preserve existing 3D record behavior and
  standard/Lean support. Do not hide dimensional conversion in legacy aliases.
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

## Phase 3 - Gravitas2D Integration And Closeout

- [ ] **Extend the optional adapter with native planar ownership.** Consume
  `SolidBody2D` accepted position, scalar rotation, and support data. Preserve
  host world Y, use exact X/Z conversion, and prepare/resolve/commit once per
  frame for all actors. Reuse the 3D adapter's ordering contract, not its vector
  arithmetic or surface-normal assumptions.
- [ ] **Run real collision acceptance for both modes.** Cover a top-down wall,
  corner, narrow portal and group; then a side-view slope, ceiling, ledge, ladder,
  moving support, and posture change. Verify physics/Navigator pose agreement,
  guide cells, collision settings, and appropriate native 2D collider shapes.
- [ ] **Observe movement in a host.** Use the selected integration playground or
  viewer with reproducible controls and replay inputs. Record sticking, turning,
  oscillation, support loss, and platform drift; visual interpolation must not
  influence authoritative results. Missing observations leave this gate open.
- [ ] **Document and run the public integration path.** Add native top-down and
  side-view examples, coordinate/units guidance, ownership rules, and migration
  notes only where behavior actually changes. Build/run all purportedly runnable
  examples and update affected XML/wiki/API pages together.
- [ ] **Run the full verification matrix.** Use the
  [hardening verification commands](navigationHardeningPlan.md#verification-and-closeout),
  the adapter's recorded commands, and relevant Gravitas2D tests in both package
  families. Verify Windows/Linux behavior, exact new/core coverage, 3D regression
  tests, replay, allocations, resource lifetime, and DocFX warnings-as-errors.
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
