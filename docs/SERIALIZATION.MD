# Serialization Reference

This document is the current reference for Trailblazer's Chronicler-style serialization MVP.

If you only need the high-level architecture, read `OVERVIEW.md`.
If you need the runtime systems currently covered by this MVP, read:

- `NAVIGATOR.MD`
- `NAVMOTOR.MD`

The code referenced here lives primarily in:

- `src/Trailblazer/Serialization/*`
- `src/Trailblazer/Navigation/Navigator.cs`
- `src/Trailblazer/Navigation/Steering/NavSteering.cs`
- `src/Trailblazer/Navigation/Turning/NavTurning.cs`
- `src/Trailblazer/Navigation/Motor/NavMotor.cs`
- `src/Trailblazer/Navigation/Motor/Locomotions/*`

## 1. What This System Is

Trailblazer includes an explicit serialization layer built around the Chronicler API.

The core idea is:

- serializable types implement `IRecordable`
- those types write and read their own state through `RecordData(IChronicler chronicler)`
- `RecordValues` handles value data
- `RecordDeep` handles nested `IRecordable` objects

This is intentionally different from relying on serializer attributes alone.

The explicit record pass makes it easier to:

- separate durable state from frame-local or derived state
- keep deterministic runtime types in control of their own save/load behavior
- support multiple transports later without rewriting each runtime type
- reuse the same pattern in other libraries in the same stack

## 2. Current Transports

The current chronicler backends are:

- `JsonRecordSerializer.Serialize(IRecordable target, bool writeIndented = false)`
- `JsonRecordSerializer.Populate(IRecordable target, string json)`
- `MemoryPackRecordSerializer.Serialize(IRecordable target)`
- `MemoryPackRecordSerializer.Populate(IRecordable target, byte[] data)`

These transports are implemented in:

- `src/Trailblazer/Serialization/JsonRecordSerializer.cs`
- `src/Trailblazer/Serialization/MemoryPackRecordSerializer.cs`

Important details:

- the Chronicler contract is serializer-agnostic
- JSON and MemoryPack now share the same chronicler contract
- `System.Text.Json` and `MemoryPack` are referenced explicitly so this works on both `netstandard2.1` and `net8.0`
- downstream libraries such as `FixedMathSharp` and `SwiftCollections` already support `System.Text.Json` and `MemoryPack`, which is why this layer stays transport-neutral
- types serialized through `RecordData(...)` generally do not need transport-specific attributes; reserve those for leaf values that are still serialized directly by a transport

## 3. Core API

The current API surface is:

```csharp
public interface IRecordable
{
    void RecordData(IChronicler chronicler);
}

public interface IChronicler
{
    SerializationMode Mode { get; }
    void LookValue<T>(ref T value, string name, T defaultValue = default);
    void LookDeep<T>(ref T value, string name) where T : class, IRecordable;
}
```

Helpers:

```csharp
RecordValues.Look(chronicler, ref value, "name", defaultValue);
RecordDeep.Look(chronicler, ref nestedObject, "name");
```

`SerializationMode` currently has:

- `Saving`
- `Loading`

## 4. Current MVP Coverage

This MVP is intentionally limited to:

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

In practice that means the first pass covers:

- navigator transform and motion state
- navigator steering session state
- navigator turning configuration
- navigator motor-owned traversal state
- locomotion configuration
- locomotion resumable runtime state
- moving-platform attachment state inside the motor branch and guided steering branch

## 5. Current Load Model

The MVP loads into existing initialized instances.

This is a deliberate constraint.

The current load path assumes:

1. the host has already created the runtime object
2. the runtime object has already been initialized enough to own its nested controllers
3. a Chronicler transport such as `JsonRecordSerializer.Populate(...)` or `MemoryPackRecordSerializer.Populate(...)` then overwrites supported state

This means the current system is designed for:

- restoring state into a known runtime shell
- deterministic snapshot/restore experiments
- proving the shape of the API before broadening scope

It is not yet designed for:

- constructing arbitrary controller graphs from serialized data alone
- polymorphic navigator creation
- full save-game bootstrap of the entire framework

## 6. What Is Serialized Today

### 6.1 Navigator

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

### 6.2 NavSteering

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
- falls back to a new path request on the next steering tick if a guide could not be reacquired immediately

### 6.3 NavTurning

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

### 6.4 NavMotor

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

### 6.5 LocomotionHandler

`LocomotionHandler.RecordData(...)` currently persists:

- `IsInControl`
- all six locomotion instances through `RecordDeep`

### 6.6 Individual Locomotions

Each locomotion persists its configuration plus its resumable runtime state.

Examples:

- `MoveLocomotion`: speed/acceleration/gravity configuration plus `FrameVelocity`
- `JumpLocomotion`: jump configuration plus jump/cooldown state
- `FallLocomotion`: fall configuration plus fall start/end state
- `SlideLocomotion`: slide configuration plus `IsSliding`
- `SwimLocomotion`: swim configuration plus swim/dive/underwater timers
- `PlatformLocomotion`: platform configuration plus active/previous/hold platform state, movement-transfer state, local attachment state, and tracked platform velocities

## 7. What Is Not Serialized Yet

The current MVP intentionally does not serialize the full live navigation stack.

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

## 8. Current Boundary and Tradeoffs

The current MVP boundary is:

- transport-neutral Chronicler API
- JSON and MemoryPack backends
- explicit type-owned save/load behavior
- load-into-existing-instance semantics
- navigator, steering, turning, and motor branches

This gives Trailblazer a workable starting point without overcommitting to a full save-game architecture too early.

Tradeoffs of this MVP:

- it is safe and explicit, but not yet turnkey for whole-world persistence
- it rebuilds live guides from recorded requests instead of serializing guide instances directly
- it keeps deterministic state local to the types that understand it
- it still needs broader coverage before it becomes a complete framework-level persistence solution

## 9. Serialization Semantics to Preserve

When extending this system, keep these rules in mind:

- prefer explicit record code over implicit serializer behavior for runtime systems
- serialize authoritative state, not convenience caches
- rebuild derived values on load where practical
- avoid serializing live references when a stable identifier or reconstructed state is safer
- keep deterministic ordering and fixed-point state intact
- keep load behavior obvious and testable

This is especially important in Trailblazer because pathing and navigation are sensitive to:

- simulation order
- traversal-state transitions
- platform attachment state
- cache invalidation
- frame-local intermediate values

## 10. Verification Added With This MVP

Focused round-trip coverage was added for:

- `NavTurning`
- `NavMotor`
- `Navigator` plus its steering, turning, and motor branches

The current tests verify that:

- supported state round-trips through the JSON and MemoryPack chroniclers
- guided A* and flow-field steering sessions survive round-trip
- path-request configuration and A* waypoint progress survive round-trip
- guides are reacquired after load instead of being serialized inline
- turning configuration survives round-trip while active turn runtime state is rebuilt on load
- moving-platform state survives round-trip
- locomotion runtime state survives round-trip
- a restored navigator remains usable for later simulation

## 11. Next Best Steps

The next best steps after this MVP are:

1. Define a stable reference strategy for external/runtime-owned objects that should not be serialized inline.
2. Decide whether movement-group coordinator state should be rebuilt globally or whether steering sessions should always rejoin lazily after load.
3. Decide whether the framework should support construct-from-data loading in addition to populate-existing-instance loading.
4. Add more targeted tests around partial loads, disabled locomotions, backward-compatible defaults, failed request rehydration, and transport-parity rebuild behavior.

## 12. Extension Guidance

When adding `RecordData(...)` to more types:

1. Start with the authoritative state.
2. Skip or rebuild frame-local caches unless they are truly required for restore correctness.
3. Prefer `RecordDeep` for nested runtime-owned objects.
4. Use value fields for small deterministic structs and enums.
5. Add a focused round-trip test in the same change.
6. Verify the type still behaves correctly after load, not just that the raw values match.

That approach is what this MVP follows for the `Navigator -> NavMotor -> LocomotionHandler -> Locomotions` branch.
