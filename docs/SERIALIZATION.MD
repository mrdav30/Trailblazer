# Serialization Reference

This document is the Trailblazer-specific reference for how the runtime currently uses the Chronicler serialization layer.

If you only need the high-level architecture, read `OVERVIEW.md`.
If you need the reusable Chronicler API details, transport behavior, or stable-link design, read [`../src/Trailblazer/Serialization/README.md`](../src/Trailblazer/Serialization/README.md).
If you need the runtime systems currently covered by this Trailblazer implementation, read:

- `NAVIGATOR.MD`
- `NAVMOTOR.MD`

The code referenced here lives primarily in:

- `src/Trailblazer/Navigation/Navigator.cs`
- `src/Trailblazer/Navigation/Steering/NavSteering.cs`
- `src/Trailblazer/Navigation/Turning/NavTurning.cs`
- `src/Trailblazer/Navigation/Motor/NavMotor.cs`
- `src/Trailblazer/Navigation/Motor/Locomotion/*`

## 1. Trailblazer Coverage

The current Trailblazer serialization coverage is intentionally limited to:

- `Navigator`
- `NavSteering`
- `NavTurning`
- `NavMotor`
- `LocomotionHandler`
- `MoveLocomotion`
- `PlatformLocomotion`
- `JumpLocomotion`
- `FallLocomotion`
- `SlideLocomotion`
- `SwimLocomotion`

In practice that means the current covered branch handles:

- navigator transform and motion state
- navigator steering session state
- navigator turning configuration
- navigator motor-owned traversal state
- locomotion configuration
- locomotion resumable runtime state
- moving-platform attachment state inside the motor branch and guided steering branch

## 2. Trailblazer Load Model

The current implementation loads into existing initialized instances.

This is a deliberate constraint.

The current load path assumes:

1. the host has already created the runtime object
2. the runtime object has already been initialized enough to own its nested controllers
3. a Chronicler transport such as `JsonRecordSerializer.Populate(...)` or `MemoryPackRecordSerializer.Populate(...)` then overwrites supported state

This means the current system is designed for:

- restoring state into a known runtime shell
- deterministic snapshot/restore experiments
- proving the shape of the API before broadening scope

Trailblazer intentionally does not treat Chronicler as a construct-from-data runtime factory.

Another important rule applies across the Trailblazer `RecordData(...)` implementations:

- `RecordValues.Look(...)` defaults must be canonical defaults for that field, not the instance's current runtime value
- value entries equal to their declared default are omitted during save
- missing value entries restore to the declared default during load
- deep record paths are split intentionally: reference-owned nested objects use `RecordDeep`, non-nullable structs use `RecordDeepStruct`, and optional structs use `RecordNullableDeep`

This keeps payloads smaller and makes partial or older payloads deterministic.

It also means omitted entries do not preserve whatever value happened to already be present in the target runtime shell. If a field is absent from the payload, load assigns that field's declared default.

That means this runtime is not designed for:

- constructing arbitrary controller graphs from serialized data alone
- polymorphic navigator creation
- full save-game bootstrap of the entire framework

## 3. What Is Serialized Today

### 3.1 Navigator

`Navigator.RecordData(...)` currently persists:

- `Position`
- `LastPosition`
- `Rotation`
- `Velocity`
- `Speed`
- `Acceleration`
- `Size`
- `FootPositionAdjust`
- guided-request defaults such as `GuidedPathMode`
- `GlobalId`
- `OccupantGroupId`
- `IsLockedOn`
- `AnimDampTime`
- `StuckThresholdSpeed`
- `IsGuideded`
- `_frameCondition`
- `_frameRequest`
- `Steering`
- `Turning`
- `Motor`

On load it also:

- rebuilds `Forward` from `Rotation`
- clears frame deltas
- refreshes steering radius-derived tolerances from the loaded navigator size
- clears and recomputes occupancy
- reinitializes `Turning` if present

### 3.2 NavSteering

`NavSteering.RecordData(...)` currently persists:

- movement-session flags such as `ShouldMove`, `IsStuck`, `IsAtDestination`, and `HasLineOfSightPath`
- destination, requested destination, target direction, and distance-to-target state
- steering tuning such as stop multiplier, group/avoid factors, behavior weights, and braking power
- cooldowns, stuck counters, repath counters, and auto-stop state
- movement-group id and current travel mode
- the active request shape through a serializable request record

The request record currently captures:

- request type (`AStar` or `FlowField`)
- origin
- target position
- unit size
- walkability override
- max search range
- request-type-specific settings such as heuristic, max climb height, or extra flood range
- whether an active guide existed
- current A* waypoint index when the guide is waypoint-based

On load it also:

- rebuilds the active `IPathRequest` from the recorded request data
- reacquires a guide from `PathGuideFactory` when the session was actively guide-driven
- restores A* waypoint progress when the guide is waypoint-based
- clears any stale guide or movement-group session state already attached to the existing runtime shell
- preserves per-session movement-group intent while treating the shared coordinator as rebuild-only runtime state
- allows hosts to prewarm grouped sessions explicitly through `Navigator.PrewarmMovementGroup()` or `NavSteering.PrewarmMovementGroup(...)`
- falls back to a new path request on the next steering tick if a guide could not be reacquired immediately

### 3.3 NavTurning

`NavTurning.RecordData(...)` currently persists:

- `CanTurn`
- `TurnRate`

On load it intentionally rebuilds buffered runtime state instead of restoring it directly.

That means load resets:

- the current turn target
- any pending buffered turn request
- one-shot interpolation overrides
- collision-triggered auto-turn buffering

This keeps turn configuration durable while treating active turn execution as frame-local runtime state that is safer to rebuild.

### 3.4 NavMotor

`NavMotor.RecordData(...)` currently persists:

- `Handler`
- current `TrekCondition`
- previous `TrekCondition`
- `IsInitialized`

On load it also:

- rebuilds `CurrentState`
- clears frame lock state
- clears frame-local cached output such as `_forceOutput`
- resets `FrameSlopeAngle`

### 3.5 LocomotionHandler

`LocomotionHandler.RecordData(...)` currently persists:

- `IsInControl`
- `installedKinds` for the currently installed built-in locomotion set
- the required `Move` and `Fall` locomotions through `RecordDeep`
- optional built-in locomotions through named `RecordDeep` slots when installed

### 3.6 Individual Locomotions

Each locomotion persists its configuration plus its resumable runtime state.

Examples:

- `MoveLocomotion`: speed/acceleration/gravity configuration, core slope limit, passive water drag, plus `FrameVelocity`
- `JumpLocomotion`: jump configuration plus jump/cooldown state
- `FallLocomotion`: fall configuration plus fall start/end state
- `SlideLocomotion`: slide-specific configuration plus `IsSliding`
- `SwimLocomotion`: swim configuration plus swim/dive/underwater timers
- `PlatformLocomotion`: platform configuration plus active/previous/hold platform state, movement-transfer state, local attachment state, and tracked platform velocities

## 4. What Is Not Serialized Yet

The current Trailblazer serialization branch intentionally does not serialize the full live navigation stack.

Out of scope today:

- inline serialization of live `IGuide` instances
- movement-group coordinator caches and memberships
- animation handler references
- chart registrations, path caches, or global pathing state
- full voxel occupancy maps

Some state is also intentionally rebuilt instead of persisted directly:

- `Navigator.Forward`
- `NavTurning` buffered turn state
- `NavMotor.CurrentState`
- frame-local delta accumulators
- temporary traversal caches such as `FrameSlopeAngle`

The core now also supports immediate and deferred stable external links through `ChronicleContext` and `RecordLinks`, but the current Trailblazer runtime does not need direct `RecordLinks` usage in its core navigation branch at this time.

## 5. Current Boundary and Tradeoffs

Trailblazer's current serialization boundary is:

- JSON and MemoryPack Chronicler backends
- explicit type-owned `RecordData(...)` behavior
- load-into-existing-instance semantics
- navigator, steering, turning, and motor branches

This gives Trailblazer a workable starting point without overcommitting to a full save-game architecture too early.

Current tradeoffs of this boundary:

- it is safe and explicit, but not yet turnkey for whole-world persistence
- it rebuilds live guides from recorded requests instead of serializing guide instances directly
- it keeps deterministic state local to the types that understand it
- it still needs broader coverage before it becomes a complete framework-level persistence solution

## 6. Trailblazer Semantics to Preserve

When extending this system, keep these rules in mind:

- prefer explicit record code over implicit serializer behavior for runtime systems
- serialize authoritative state, not convenience caches
- rebuild derived values on load where practical
- avoid serializing live references when a stable identifier or reconstructed state is safer
- use canonical declared defaults in `RecordValues.Look(...)`, never the current live field value
- treat omitted payload entries as "load the declared default," not "keep the current in-memory value"
- keep deterministic ordering and fixed-point state intact
- keep load behavior obvious and testable

This is especially important in Trailblazer because pathing and navigation are sensitive to:

- simulation order
- traversal-state transitions
- platform attachment state
- cache invalidation
- frame-local intermediate values

## 7. Verification Added So Far

Focused round-trip coverage was added for:

- `NavTurning`
- `NavMotor`
- `Navigator` plus its steering, turning, and motor branches

The current Trailblazer-focused tests verify that:

- supported navigator state round-trips through the JSON and MemoryPack chroniclers
- guided A* and flow-field steering sessions survive round-trip
- path-request configuration and A* waypoint progress survive round-trip
- guides are reacquired after load instead of being serialized inline
- partial payload loads preserve omitted branches on existing initialized runtime shells
- older payloads missing newer fields fall back to current runtime defaults
- invalid request payloads fail closed and clear active steering sessions instead of leaving broken live state
- disabled locomotions clear transient runtime state on load even when serialized payloads still carry stale transient values
- unresolved deferred links throw explicit load errors for both transports
- turning configuration survives round-trip while active turn runtime state is rebuilt on load
- moving-platform state survives round-trip
- locomotion runtime state survives round-trip
- grouped steering can be explicitly prewarmed after load while lazy rejoin remains available as a fallback
- rebuild-oriented load behavior matches across the JSON and MemoryPack transports
- a restored navigator remains usable for later simulation

## 8. Next Best Steps

The Trailblazer-specific branch is in solid shape now, but a few non-blocking follow-ups are still valuable:

1. Add an end-to-end host workflow guide or sample that shows batch save/load for multiple navigators, including chart prerequisites, restore ordering, and optional `PrewarmMovementGroup()` calls.
2. Decide whether any additional Trailblazer-owned global state should ever expose optional snapshot surfaces, or whether pathing/global managers remain rebuild-only by design.
3. Expand `RecordData(...)` coverage into other Trailblazer branches only as those runtime surfaces stabilize and prove they contain authoritative state worth persisting.

## 9. Trailblazer Extension Guidance

When adding `RecordData(...)` to more types:

1. Start with the authoritative state.
2. Skip or rebuild frame-local caches unless they are truly required for restore correctness.
3. Prefer `RecordDeep` for nested runtime-owned objects.
4. Use `RecordLinks` for stable references to external or runtime-owned objects.
5. Use value fields for small deterministic structs and enums.
6. Add a focused round-trip test in the same change.
7. Verify the type still behaves correctly after load, not just that the raw values match.

That approach is what the current `Navigator -> NavMotor -> LocomotionHandler -> Locomotions` branch follows.
