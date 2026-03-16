# Serialization Reference

This document is the current reference for Trailblazer's Chronicler-style serialization MVP.

If you only need the high-level architecture, read `OVERVIEW.md`.
If you need the runtime systems currently covered by this MVP, read:

- `NAVIGATOR.MD`
- `NAVMOTOR.MD`

The code referenced here lives primarily in:

- `src/Trailblazer/Serialization/*`
- `src/Trailblazer/Navigation/Navigator.cs`
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
- navigator motor-owned traversal state
- locomotion configuration
- locomotion resumable runtime state
- moving-platform attachment state inside the motor branch

## 5. Current Load Model

The MVP loads into existing initialized instances.

This is a deliberate constraint.

The current load path assumes:

1. the host has already created the runtime object
2. the runtime object has already been initialized enough to own its nested controllers
3. `JsonRecordSerializer.Populate(...)` then overwrites supported state

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
- `_frameCondition`
- `Motor`

On load it also:

- rebuilds `Forward` from `Rotation`
- clears frame deltas
- clears the current frame request
- forces guided runtime state off for now
- clears and recomputes occupancy
- reinitializes `Turning` if present
- clears active steering runtime state if `Steering` is present

### 6.2 NavMotor

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

### 6.3 LocomotionHandler

`LocomotionHandler.RecordData(...)` currently persists:

- `IsInControl`
- all six locomotion instances through `RecordDeep`

### 6.4 Individual Locomotions

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

- `NavSteering`
- `NavTurning`
- path requests and active guide ownership
- movement-group runtime state
- animation handler references
- chart registrations, path caches, or global pathing state
- full voxel occupancy maps

Some state is also intentionally rebuilt instead of persisted directly:

- `Navigator.Forward`
- `NavMotor.CurrentState`
- frame-local delta accumulators
- temporary traversal caches such as `FrameSlopeAngle`

## 8. Current Boundary and Tradeoffs

The current MVP boundary is:

- transport-neutral Chronicler API
- JSON and MemoryPack backends
- explicit type-owned save/load behavior
- load-into-existing-instance semantics
- motor branch only

This gives Trailblazer a workable starting point without overcommitting to a full save-game architecture too early.

Tradeoffs of this MVP:

- it is safe and explicit, but not yet turnkey for whole-world persistence
- it avoids serializing path/guide runtime ownership prematurely
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

- `NavMotor`
- `Navigator` plus its motor branch

The current tests verify that:

- supported state round-trips through the JSON and MemoryPack chroniclers
- moving-platform state survives round-trip
- locomotion runtime state survives round-trip
- a restored navigator remains usable for later simulation

## 11. Next Best Steps

The next best steps after this MVP are:

1. Expand coverage into `NavSteering`.
2. Decide how path requests, guide ownership, and cached guide reacquisition should load.
3. Decide whether `NavTurning` should serialize buffered turn state or be rebuilt on load.
4. Define a stable reference strategy for external/runtime-owned objects that should not be serialized inline.
5. Decide whether the framework should support construct-from-data loading in addition to populate-existing-instance loading.
6. Add more targeted tests around partial loads, disabled locomotions, and backward-compatible defaults.

## 12. Extension Guidance

When adding `RecordData(...)` to more types:

1. Start with the authoritative state.
2. Skip or rebuild frame-local caches unless they are truly required for restore correctness.
3. Prefer `RecordDeep` for nested runtime-owned objects.
4. Use value fields for small deterministic structs and enums.
5. Add a focused round-trip test in the same change.
6. Verify the type still behaves correctly after load, not just that the raw values match.

That approach is what this MVP follows for the `Navigator -> NavMotor -> LocomotionHandler -> Locomotions` branch.
