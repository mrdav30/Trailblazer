# Climbing Locomotion Plan

## Purpose

This document captures a phased plan for adding a new climbing locomotion type to Trailblazer's
navigation stack without overfitting the feature to one game-specific model.

## Status

- Phase 1 complete on April 18, 2026.
  Landed climb profile composition, climb intent in `TrekRequest`, navigator plumbing, the
  dedicated resolver contract, climb events, and serialization coverage.
- Phase 2 complete on April 18, 2026.
  Landed deterministic attached climbing in `NavMotor` for ladder and simple wall-style
  affordances, including attach/continue/detach flow, climb-relative movement, gravity
  compensation, fall suppression, and focused `Release` test coverage.
- Phase 3 complete on April 18, 2026.
  Landed ledge-oriented mantle support through `ClimbAffordanceSnapshot` mantle targets,
  deterministic mantle entry/completion in `NavMotor`, climb-detach jump behavior, updated
  serialization coverage, and focused plus full-suite `Release` verification.
- Phase 4 complete on April 18, 2026.
  Hardened continuous-surface climbing by tolerating deterministic frame-to-frame attachment drift
  and changing climb normals when snapshots remain compatible, while preserving strict slip/detach
  behavior for flipped surfaces and host-forced detach cases. Added focused runtime coverage for
  continuous free-climb traversal, detach-into-fall, and forced-detach outcomes, then revalidated
  the broader motor and full `Release` suites.
- Phase 5 complete on April 19, 2026.
  Landed guided climb intent preservation for AI-driven requests, authored transition metadata for
  requesting climb entry and follow-up persistence, guided handoff serialization/runtime restore,
  deterministic intent clearing when guided traversal completes, and focused plus full-suite
  `Release` verification.

The immediate design target is broad enough to support:

- simple ledge climbs
- ladder climbing
- authored climb volumes or climb surfaces
- free-climb traversal over large surfaces, including "scale a mountain" style movement

The implementation should remain deterministic, avoid host-specific assumptions, and preserve the
current separation between path intent, locomotion state, and host-owned environment knowledge.

## Context From Current Runtime

The current runtime shape matters:

- `Navigator` owns high-level request creation and frame input assembly.
- `TrekRequest` currently carries movement direction plus jump and flight intent.
- `NavMotor` resolves locomotion behavior from a fixed built-in composition through
  `LocomotionHandler`, `LocomotionProfile`, and `LocomotionProfileBuilder`.
- Existing optional locomotions are concrete built-ins: `Platform`, `Jump`, `Slide`, `Swim`, and
  `Fly`.
- Capability gating already exists in a narrow form through events such as `Events.CanAffordJump`.
- Traversal state today is medium-oriented (`Solid`, `Gas`, `Liquid`) rather than surface-mode
  oriented.

That means climbing should not be bolted on as a one-off special case inside `Navigator.Simulate()`
or `NavMotor.TryTraversal(...)`. It needs a proper locomotion slot with explicit request/state
plumbing and a host-facing affordance seam.

## Design Goals

- Keep climbing abstract and host-driven. Trailblazer should not own stamina, animation clips,
  hand IK, or authored climb metadata formats.
- Support both binary climbables like ladders and more analog surfaces like free-climb walls.
- Preserve deterministic movement and stable decision ordering.
- Keep the first delivery scoped to locomotion and runtime state before broad pathing changes.
- Avoid forcing all climbing through chart/path transitions on day one.
- Keep serialization behavior aligned with the current populate-existing-instance model.

## Non-Goals For The First Pass

- Full authored pathfinding over arbitrary climbable manifolds.
- Automatic stamina systems.
- Animation state machines or IK-driven hand placement.
- General-purpose construct-from-data serialization changes.
- A broad refactor of all locomotion composition to a plugin architecture.

## Recommended Architecture Direction

### 1. Add A Built-In `ClimbLocomotion`

Treat climbing like the other optional locomotions:

- add `ClimbLocomotion`
- add `LocomotionKind.Climb`
- add `LocomotionHandler.Climb`
- add `LocomotionProfile` and `LocomotionProfileBuilder` support

This matches the current profile-driven motor composition and keeps the feature discoverable in the
same place users already configure jump, swim, and fly.

### 2. Add Explicit Climb Intent To `TrekRequest`

Climbing needs request-level intent rather than only inferred movement direction.

Recommended first addition:

- `bool IsRequestingClimb`

Possible follow-up if needed later:

- a climb mode hint such as `ClimbIntentMode` or `ClimbStyle`

Start with the boolean. It is enough to express "the agent wants to engage or stay engaged with a
climbable" without committing the API to ladder-only versus free-climb-only semantics too early.

### 3. Introduce A Host-Owned Climb Affordance Contract

This is the key abstraction seam.

Jump only needs a capability boolean, but climbing needs richer environmental data. The motor needs
to know whether a climb is possible, what surface or anchor it is bound to, and what movement space
is allowed while climbing.

Recommended direction:

- add a small dedicated climb query contract owned by the navigator host rather than assembling
  state through a large set of events
- return a deterministic climb snapshot rather than querying ad hoc values from multiple callbacks

The snapshot should be data-only and avoid engine-specific types beyond Trailblazer's fixed-point
math. A likely shape is:

- whether climbing can start
- whether active climbing can continue
- climb kind or affordance type such as ledge, ladder, or surface
- attachment normal or climb plane normal
- optional up direction when the climbable defines one, such as a ladder
- optional anchor position or attachment point
- movement constraints or capability flags such as lateral movement allowed, descent allowed, mantle
  allowed, detach jump allowed

The important point is not the exact field list. The important point is that Trailblazer should ask
for one deterministic climb snapshot and then simulate from that snapshot for the frame.

### 4. Keep Stamina And Similar Costs Outside The Core

Do not add stamina or resource concepts to `ClimbLocomotion`.

If hosts need climb-cost gating, mirror the existing jump pattern:

- `CanStartClimb`
- `CanContinueClimb`
- optional host callbacks for climb start, stop, mantle start, slip, or exhausted state

That keeps Trailblazer generic while still allowing a host to implement stamina, buffs, equipment,
wet-surface rules, or scripted lockouts.

### 5. Model Climbing As A Locomotion Mode, Not A Traversal Medium

Do not add a new `TraversalMedium.Climb` in the first phase.

Reasoning:

- a ladder may live in gas
- a ledge grab often happens near solid support
- free climbing is closer to "movement constrained by an attachment surface" than a world medium

A climb mode on top of existing medium state is the safer first step. If later pathfinding or
transition logic proves that a separate traversal medium is necessary, that decision can be revisited
with real integration pressure instead of speculation.

## Proposed Runtime Shape

### Climb State

`ClimbLocomotion` should likely own transient runtime state such as:

- `IsClimbing`
- `ActiveClimbKind`
- `AttachedSurfaceNormal`
- `AttachedUpDirection`
- `AttachmentPoint`
- `AttachmentId` when the host can provide a stable affordance identity
- `LastValidClimbFrame` or equivalent deterministic sticky-state guard
- `IsMantling`

Config fields should stay generic:

- `CanClimb`
- `MaxClimbSpeed`
- `MaxClimbAcceleration`
- `ClimbStartTolerance`
- `ClimbDetachCooldown`
- `GravityCompensationWhileClimbing`
- `AllowLateralTraverse`
- `AllowCeilingTraverse` only if there is a real use case later, not by default

The first version should prefer a smaller config set. Only add fields that directly affect movement
simulation.

### Attachment Model

Do not make climbing fully snapshot-only.

Recommended model:

- the host provides one deterministic `ClimbAffordanceSnapshot` for the frame
- `ClimbLocomotion` stores a small stable attachment record while climbing is active
- the motor validates the fresh frame snapshot against the active attachment before deciding to
  continue or detach

This keeps the API host-driven while avoiding frame-to-frame attach jitter on uneven surfaces or
ledges. If the host can provide a stable affordance id, use it. If not, Trailblazer should fall
back to deterministic geometric validation against the stored attachment point and surface data.

### Motor Integration

`NavMotor` will need a clear climb branch in the same areas where it already branches for swim and
flight:

- traversal preparation: refresh climb state from the frame request plus host climb snapshot
- control-state resolution: climbing should usually imply controlled movement
- desired velocity resolution: compute climb-relative movement instead of ground or flight velocity
- environmental forces: partially or fully cancel gravity while attached
- finalize step: exit climbing when the host snapshot no longer supports attachment

Important interaction rules to pin down early:

- climb should clear falling while attached
- jump from climb should be allowed only if the host says so
- flight and climbing should be mutually exclusive in the same frame
- swim-to-climb and climb-to-solid transitions need explicit ordering
- platform carry behavior should probably be disabled while actively climbing unless the climb
  snapshot explicitly references a moving attachment
- mantle should be treated as a climb sub-state, not a separate locomotion

### Navigator API

`Navigator` should gain the same level of support it already gives jump and flight:

- `ApplyInputTrekRequest(... bool? isRequestingClimb = null ...)`
- `ToggleGuidedClimb(bool status)`

Guided navigation should not attempt to invent climb routes in phase 1. It only needs to preserve
climb intent so hosts can combine path following with local climb engagement rules where appropriate.

`NavSteering` should stay largely unaware of climb affordances in the first implementation. Climb
engagement should remain a `NavMotor` decision driven by the frame request plus the host affordance
snapshot. Steering and pathing follow-up can be revisited later once runtime climbing semantics are
proven.

## Recommended Decisions

These decisions are intentionally resolved up front so implementation can stay focused.

### Mantle Ownership

Mantle should be implemented as a climb sub-state inside `ClimbLocomotion`, not as a separate
locomotion.

Reasoning:

- mantling is a short terminal phase of a climb interaction rather than a parallel movement family
- splitting it out would force two modules to coordinate attachment, detach, and top-out state
- keeping it in climb preserves a single owner for ledge-grab through top-out behavior

### Attachment Source Of Truth

Use both a frame snapshot and Trailblazer-owned active attachment state.

Reasoning:

- pure snapshot-only climbing is too vulnerable to attach/detach jitter
- a small stored attachment record gives the motor stable continuity across frames
- the host still remains the owner of environment truth by providing the frame snapshot

### Runtime Abstraction Scope

Do not introduce a generalized movement-mode abstraction for alpha.

Reasoning:

- the current locomotion system is concrete and readable
- adding a generalized mode layer now would broaden scope across the motor before climbing is proven
- if real duplication appears after climb lands, that abstraction can be extracted from working code

### Host Contract Shape

Prefer a dedicated climb resolver interface plus optional notification events.

Reasoning:

- climbing needs structured per-frame data, not a chain of narrow callbacks
- a single snapshot is easier to reason about and more deterministic
- events remain useful for side effects and permission hooks such as start, stop, mantle, or slip

### Steering Responsibility

Keep steering mostly unaware of climb affordances for the first delivery.

Reasoning:

- steering already owns a large amount of behavior
- local climb attach and detach rules belong in motor/runtime locomotion first
- guided climb-aware routing should remain an explicit later phase once the locomotion itself is
  stable

## Phased Plan

### Phase 1. Contract And Composition

Goal:
Define the core climbing API surface without changing pathfinding behavior.

Tasks:

- add `ClimbLocomotion`
- add `LocomotionKind.Climb`
- extend `LocomotionProfile`, `LocomotionProfileBuilder`, and `LocomotionHandler`
- add `IsRequestingClimb` to `TrekRequest`
- add navigator request plumbing and toggle helpers
- add a data-only climb affordance snapshot plus a dedicated host resolver seam
- add serialization support for the new locomotion and request field

Tests:

- profile composition tests
- handler install/remove/serialize tests
- `TrekRequest` clone/reset/serialization tests
- navigator request plumbing tests
- navigator serialization tests proving climb intent survives round-trip

Exit criteria:

- a navigator can be configured with or without climbing installed
- climb intent can be expressed and serialized
- no runtime climbing motion is active yet

### Phase 2. Basic Attached Climbing Runtime

Goal:
Support deterministic attached climbing with a host-provided affordance snapshot.

Scope:

- ladders
- simple wall climbing
- stable attach / continue / detach flow

Tasks:

- add climb-state evaluation in `NavMotor`
- add active attachment validation against the current frame snapshot
- compute climb-relative velocity from request direction and affordance axes
- suppress or compensate gravity while attached
- clear fall state while attached
- define deterministic detach conditions
- expose host callbacks for start and stop events

Tests:

- attach when climb request and affordance are both valid
- refuse attach when locomotion disabled or host says no
- continue climbing across frames
- detach when request stops
- detach when affordance disappears
- gravity behavior while attached
- fall-state interaction on detach

Exit criteria:

- ladder-style ascent and descent work
- planar wall climbing with host-supplied axes works
- state transitions are deterministic and serializable

### Phase 3. Ledge And Mantle Support

Goal:
Cover the common "grab edge and climb up" case without forcing all climbing to be free-form.

Tasks:

- add affordance fields for ledge top-out or mantle target
- add a short deterministic mantle phase that transitions from attached climbing back to solid
- define jump-from-ledge and drop-from-ledge behavior

Tests:

- ledge grab from airborne state
- mantle onto solid ground
- failed mantle when clearance is lost
- serialize and restore an active ledge-climb state

Exit criteria:

- ledge climbs do not need to be hacked through jump logic
- top-out back onto solid traversal is reliable

### Phase 4. Free-Climb Hardening

Goal:
Handle larger climb surfaces such as cliff faces or mountain walls.

Tasks:

- validate that the climb snapshot can represent arbitrary surface attachment, not just authored
  ladders or ledges
- support lateral traverse and vertical route changes on continuous surfaces
- add explicit slip or forced-detach hooks for hosts that use stamina or surface rules
- define how heading and facing should behave on curved or changing surfaces

Tests:

- continuous climb over multiple changing normals
- left/right traverse on a climb surface
- controlled detach into fall
- host-forced detach while preserving deterministic outcome

Exit criteria:

- the runtime can support a Breath-of-the-Wild-style "scale a mountain" host model as long as the
  host provides valid climb snapshots

### Phase 5. Guided Navigation And Pathing Follow-Up

Goal:
Allow AI-guided agents to intentionally use climb affordances without turning Trailblazer into a
general climb-surface path planner.

Scope:

- preserve climb intent while a guided request is active
- allow authored traversal transitions to request climb entry and climb exit at the right time
- keep free-climb surface selection host-owned rather than path-searched by Trailblazer

Non-goals:

- do not add path search over arbitrary climbable manifolds
- do not add a new climb-specific chart representation in this phase
- do not make `NavSteering` responsible for local affordance validation or climb runtime rules

Tasks:

- add guided-request plumbing so guided navigation can explicitly preserve `IsRequestingClimb`
  across frames instead of relying only on local input helpers
- define a narrow steering-to-motor handoff for "the current guided segment intends climb" versus
  "the motor should decide locally whether to attach"
- extend authored traversal transition handling so a transition can enter a climb interaction and
  optionally request climb persistence until the exit handoff completes
- ensure guided handoff back to non-climb travel is explicit, so agents can top out, detach, or
  finish a climb transition without sticky climb intent
- keep resolver ownership unchanged: hosts still decide whether a concrete climb affordance is
  valid for the current frame
- update navigator serialization coverage so guided climb intent and any transition-side climb flags
  survive round-trip

Tests:

- guided request preserves climb intent while traversing a climb-authored segment
- guided request does not spam start/stop events when climb intent remains active across frames
- authored transition can enter climb, remain guided, and hand back to normal traversal on exit
- guided climb intent clears correctly when the transition or request completes
- navigator serialization restores guided climb intent and resumes deterministic behavior after load
- steering remains stable when guided climb intent is active but the host resolver denies attach for
  the current frame

Exit criteria:

- AI-guided agents can intentionally enter and exit climb interactions through authored routes
- local affordance validation still lives in the host resolver and `NavMotor`
- phase 5 lands without introducing arbitrary climb-surface path search or broad steering bloat

Recommended execution slice:

1. Preserve climb intent in guided navigator request assembly.
2. Add the smallest authored transition hook needed to mark a segment as climb-capable.
3. Keep `NavMotor` as the only owner of attach/continue/detach runtime semantics.
4. Serialize the new guided climb state.
5. Prove the flow with focused tests before considering broader climb-aware path request changes.

### Phase 6. Authored Climb Topology And Routing

Goal:
Add authored climb-aware routing over marked climb surfaces so pathfinding can intentionally plan
parkour-style climb traversal without inferring arbitrary climbability from all solid space.

Scope:

- support authored climb-capable solid surfaces
- support explicit authored climb entry and exit seams
- support distinct climb adjacency rules instead of reusing normal solid neighbor expansion
- keep host-owned runtime affordance validation in place even when the path request is climb-aware

Non-goals:

- do not infer climb routing from every solid cell automatically
- do not replace the host resolver with path-authored runtime attachment decisions
- do not broaden this phase into full arbitrary manifold search over any geometric surface

Recommended authoring direction:

- extend chart authoring so legend entries can mark climb-capable solid topology explicitly
- `SC` should mean a solid cell or surface that participates in climb routing
- `SC!` should mean an authored climb transition or climb handoff seam
- treat these as authored climb topology markers layered on top of solid-backed data, not as a new
  traversal medium

Recommended runtime and pathing direction:

- build a distinct authored climb adjacency set from climb-capable solid data instead of modeling
  climbing as a child of normal solid partition neighbors
- allow path search to expand climb edges only when the request or agent capability permits climb
  routing
- track traversal mode in the search state when needed so enter-climb, remain-climbing, and
  exit-climb edges can be validated and scored deterministically
- keep `NavMotor` and the host resolver as the owners of local attach, continue, detach, mantle,
  and slip semantics after the path has selected a climb-capable route

Why this shape is preferred:

- climb routing has different topology than ground routing
- parkour-style climb paths may connect vertical, lateral, ledge, and corner transitions that
  should not inherit standard solid adjacency rules
- a distinct adjacency set keeps the existing medium model intact while giving pathing enough
  structure to plan intentional climb routes

Tasks:

- extend authoring legend parsing to recognize climb-capable solid markers such as `SC`
- add authored climb transition markers such as `SC!` for explicit entry and exit seams
- generate climb-specific adjacency from authored climb topology rather than relying on default
  solid partition neighbors
- extend path request or search-state data so climb-capable routing is opt-in and locomotion mode
  can be scored correctly
- define deterministic edge costs for entering climb, traversing climb, and exiting climb
- ensure guided and non-guided consumers can distinguish authored climb topology from host-only
  opportunistic climb affordances

Tests:

- authored climb-capable topology produces climb-specific neighbors without polluting normal solid
  neighbor expansion
- path search can intentionally route across authored climb surfaces when climb routing is enabled
- path search avoids authored climb surfaces when climb routing is disabled
- authored climb entry and exit seams produce deterministic path transitions
- parkour-style authored routes with vertical and corner climb segments rebuild consistently after
  serialization or chart reload
- runtime still rejects invalid local attach when the authored route says climb is possible but the
  host resolver denies the current frame affordance

Risks:

- this is the point where locomotion-aware routing can start broadening into a larger traversal
  authoring system
- search-state expansion may grow if climb mode is modeled too loosely
- authored climb topology must stay explicit enough that routing remains deterministic and readable

Exit criteria:

- authored charts can mark climb-capable surfaces and climb transition seams explicitly
- path search can intentionally route AI agents through climb-capable authored paths
- the implementation supports parkour-style climb routing without requiring all solid space to be
  treated as climbable
- runtime attach and detach ownership remains split cleanly between authored routing and host-owned
  frame validation

Recommended execution slice:

1. Add `SC` and `SC!` style legend support for authored climb topology.
2. Generate a separate climb adjacency set from authored climb-capable solid data.
3. Make climb-aware routing opt-in at the request level.
4. Add the smallest traversal-mode state needed to score enter, remain, and exit climb edges.
5. Prove the flow with one constrained authored parkour route before expanding the representation.

## Recommended First Execution Slice

The smallest useful first slice is:

1. Add `ClimbLocomotion` and the composition/profile plumbing.
2. Add `IsRequestingClimb` to `TrekRequest` and navigator request helpers.
3. Add a deterministic host climb snapshot contract.
4. Wire `NavMotor` to support attach, move, and detach for ladder-style climbing only.
5. Add focused tests for composition, request plumbing, serialization, and basic runtime climbing.

That slice is small enough to land coherently, but it still establishes the right abstraction for
more advanced ledges and free climbing instead of painting the API into a ladder-only corner.
