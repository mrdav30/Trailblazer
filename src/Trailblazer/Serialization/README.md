# Chronicler API Reference

This document describes the reusable Chronicler serialization layer that Trailblazer currently ships inside the main library and intends to extract into its own shared project later.

If you need Trailblazer-specific coverage and runtime behavior, read [`../../../docs/SERIALIZATION.MD`](../../../docs/SERIALIZATION.MD).

The code referenced here lives in:

- `src/Trailblazer/Serialization/IRecordable.cs`
- `src/Trailblazer/Serialization/IChronicler.cs`
- `src/Trailblazer/Serialization/RecordValues.cs`
- `src/Trailblazer/Serialization/RecordDeep.cs`
- `src/Trailblazer/Serialization/RecordLinks.cs`
- `src/Trailblazer/Serialization/ChronicleContext.cs`
- `src/Trailblazer/Serialization/ChronicleLinkRegistry.cs`
- `src/Trailblazer/Serialization/JsonRecordSerializer.cs`
- `src/Trailblazer/Serialization/MemoryPackRecordSerializer.cs`

## 1. What Chronicler Is

Chronicler is an explicit, transport-neutral serialization API built around type-owned record passes.

The core idea is:

- serializable types implement `IRecordable`
- those types write and read their own state through `RecordData(IChronicler chronicler)`
- `RecordValues` handles leaf value data
- `RecordDeep` handles owned nested `IRecordable` objects
- `RecordLinks` handles stable references to external or runtime-owned objects through a session-scoped registry

This is intentionally different from relying on serializer attributes alone.

## 2. Core API

The current core API surface is:

```csharp
public interface IRecordable
{
    void RecordData(IChronicler chronicler);
}

public interface IChronicler
{
    ChronicleContext Context { get; }
    SerializationMode Mode { get; }
    void LookValue<T>(ref T value, string name, T defaultValue = default);
    void LookDeep<T>(ref T value, string name) where T : class, IRecordable;
    void LookLink<T>(
        ref T value,
        string name,
        string slot = null,
        RecordLinkResolveMode resolveMode = RecordLinkResolveMode.Immediate,
        Action<T> assignLoadedValue = null);
}
```

Helper entry points:

```csharp
RecordValues.Look(chronicler, ref value, "name", defaultValue);
RecordDeep.Look(chronicler, ref nestedObject, "name");
RecordLinks.Look(chronicler, ref externalRef, "name");
RecordLinks.LookDeferred(
    chronicler,
    externalRef,
    "name",
    resolved => Owner.ExternalRef = resolved);
```

Current mode enums:

- `SerializationMode`: `Saving`, `Loading`
- `RecordLinkResolveMode`: `Immediate`, `Deferred`

## 3. Transports

The current Chronicler transports are:

- `JsonRecordSerializer.Serialize(IRecordable target, bool writeIndented = false)`
- `JsonRecordSerializer.Serialize(IRecordable target, ChronicleContext context, bool writeIndented = false)`
- `JsonRecordSerializer.Populate(IRecordable target, string json)`
- `JsonRecordSerializer.Populate(IRecordable target, string json, ChronicleContext context)`
- `MemoryPackRecordSerializer.Serialize(IRecordable target)`
- `MemoryPackRecordSerializer.Serialize(IRecordable target, ChronicleContext context)`
- `MemoryPackRecordSerializer.Populate(IRecordable target, byte[] data)`
- `MemoryPackRecordSerializer.Populate(IRecordable target, byte[] data, ChronicleContext context)`

Important details:

- the Chronicler contract is serializer-agnostic
- JSON and MemoryPack share the same `IChronicler` contract
- types serialized through `RecordData(...)` generally do not need transport-specific attributes
- transport-specific attributes should usually be reserved for leaf values still serialized directly by the transport

## 4. Stable Links and Context

The stable-link system is built around:

- `ChronicleContext`
- `ChronicleContext.Links`
- `IRecordLinkResolver<T>`
- `RecordLinks`

Important design rules:

- the link registry is built into the serialization layer, but it is session-scoped rather than a hidden global singleton
- link lookup is type-based by default, with an optional slot override for cases where one type needs multiple resolution strategies
- immediate links resolve during the current `RecordData(...)` call
- deferred links resolve after the full load graph has finished its `RecordData(...)` pass
- the registry can be backed either by custom resolvers or by directly registered runtime instances

This gives Chronicler three distinct lanes:

- `RecordValues` for pure data
- `RecordDeep` for owned nested objects
- `RecordLinks` for external or runtime-owned objects

## 5. Current Load Model

The current implementation loads into existing initialized instances.

That means:

1. the host creates the runtime object graph or shell first
2. the transport populates supported state into that existing graph
3. deep-loaded nested objects must already exist before `LookDeep(...)` can populate them

Today this is designed for:

- restoring state into a known runtime shell
- deterministic snapshot/restore experiments
- proving the shape of the shared API before broadening scope

Chronicler intentionally stays focused on transporting state into an existing runtime shell.

Constructing runtime objects, choosing concrete types, and orchestrating framework bootstrap are expected to live in host code or in a separate opt-in factory layer rather than in the base Chronicler contract.

So the current core is not designed for:

- constructing arbitrary object graphs from serialized data alone
- polymorphic root construction
- full save bootstrap from transport data only

## 6. Extension Guidance

When adding Chronicler support to a new type:

1. Start with the authoritative state.
2. Skip or rebuild frame-local caches unless they are truly required for restore correctness.
3. Prefer `RecordDeep` for owned nested runtime objects.
4. Use `RecordLinks` for stable references to external or runtime-owned objects.
5. Use value fields for small deterministic structs and enums.
6. Add a focused round-trip test in the same change.
7. Verify the type still behaves correctly after load, not just that raw values match.
