# Chronicler Migration Strategy

This document outlines a staged plan for extracting the generic Chronicler serialization layer out of Trailblazer and into its own project and package.

The goal is to make Chronicler reusable across the broader deterministic stack while keeping Trailblazer focused on Trailblazer-specific runtime state and integration behavior.

## 1. Goals

The extraction should:

- keep the current `populate-existing-instance` load model intact
- preserve the current JSON and MemoryPack transports
- preserve the stable-link model built around `ChronicleContext`
- keep Trailblazer integration behavior unchanged for covered runtime branches
- minimize risk by separating generic code from Trailblazer-specific record types

The extraction should not:

- turn Chronicler into a construct-from-data object factory
- force Trailblazer to change its host-owned lifecycle model
- mix Trailblazer runtime policy into the generic package

## 2. Recommended Package Shape

Recommended first cut:

- one package: `Chronicler`
- one project: `src/Chronicler/Chronicler.csproj`
- one primary namespace: `Chronicler`

Why start with one package:

- the current code is still compact
- JSON and MemoryPack are already part of the shipped behavior
- a single extraction reduces namespace churn and packaging overhead

Possible later split if dependency pressure grows:

- `Chronicler` for core abstractions plus context/link logic
- `Chronicler.Json` for the JSON transport
- `Chronicler.MemoryPack` for the MemoryPack transport

That split should be delayed until there is a concrete reason, such as consumers wanting the core without one of the transports.

## 3. Files To Move

These files are generic Chronicler infrastructure and should move into the new project:

- `src/Trailblazer/Serialization/ChronicleContext.cs`
- `src/Trailblazer/Serialization/ChronicleLinkRegistry.cs`
- `src/Trailblazer/Serialization/IChronicler.cs`
- `src/Trailblazer/Serialization/IRecordLinkResolver.cs`
- `src/Trailblazer/Serialization/IRecordable.cs`
- `src/Trailblazer/Serialization/JsonRecordSerializer.cs`
- `src/Trailblazer/Serialization/MemoryPackRecordEnvelope.cs`
- `src/Trailblazer/Serialization/MemoryPackRecordSerializer.cs`
- `src/Trailblazer/Serialization/RecordDeep.cs`
- `src/Trailblazer/Serialization/RecordLinkResolveMode.cs`
- `src/Trailblazer/Serialization/RecordLinks.cs`
- `src/Trailblazer/Serialization/RecordValues.cs`
- `src/Trailblazer/Serialization/SerializationMode.cs`

These supporting docs should move or be rewritten in the new project:

- `src/Trailblazer/Serialization/README.md`
- `src/Trailblazer/Serialization/MIGRATION.MD`

Current note:

- the old `SerializationAttributes.Shim.cs` file has already been removed from Trailblazer
- keep it that way unless the extracted package later proves it needs a real compatibility layer for non-`net8.0` consumers
- do not reintroduce a package-local shim without a concrete portability reason

## 4. Files That Should Stay In Trailblazer

These files are Trailblazer-specific and should remain in the Trailblazer project:

- `src/Trailblazer/Serialization/PathRequestRecord.cs`
- `docs/SERIALIZATION.MD`
- Trailblazer integration tests under `tests/Trailblazer.Tests/Navigation/Navigator/NavigatorSerialization.Tests.cs`

Why `PathRequestRecord.cs` stays:

- it depends directly on Trailblazer pathing concepts like `IPathRequest`, `AStarPathRequest`, `FlowFieldPathRequest`, `IGuide`, `AStarGuide`, and `PathGuideFactory`
- it is not part of the generic Chronicler contract

Trailblazer-specific runtime types should continue to implement `IRecordable`, but the interface should come from the extracted package once the migration lands.

## 5. Test Split

Move generic Chronicler tests into the new project or a sibling test project:

- stable-link tests
- deferred-link failure tests
- transport parity tests for generic `RecordValues`, `RecordDeep`, and `RecordLinks` behavior

Keep Trailblazer integration coverage in Trailblazer:

- navigator round-trips
- steering rehydration and guide reacquisition
- movement-group prewarm behavior
- disabled locomotion load behavior
- Trailblazer-specific backward-compat payload tests

In other words:

- generic transport semantics belong with Chronicler
- runtime integration semantics belong with Trailblazer

## 6. Namespace Strategy

Recommended long-term namespace:

- `Chronicler`

There are two viable migration approaches:

1. Rename during extraction.
2. Extract first, rename in a follow-up.

Recommended approach:

1. extract first with a minimal, mechanical change set
2. keep compile behavior stable
3. then rename namespaces in a dedicated follow-up once the package boundary is proven

That keeps the highest-risk step focused on project separation rather than mixing project separation with namespace churn.

## 7. Project-Level Changes

When creating `Chronicler.csproj`, copy or recreate:

- `LangVersion`
- `ImplicitUsings`
- nullable policy appropriate for the package
- deterministic build settings
- package metadata

Dependencies the new project will likely need immediately:

- `System.Text.Json`
- `MemoryPack`

Dependencies it should not need:

- `FixedMathSharp`
- `GridForge`
- `SwiftCollections`

If the extracted project still depends on any of those, that is a sign generic and Trailblazer-specific code are still mixed.

## 8. Trailblazer Follow-Up Work After Extraction

Trailblazer should then:

1. remove the moved files from `src/Trailblazer/Serialization`
2. add a package or project reference to `Chronicler`
3. update all `using Trailblazer.Serialization;` directives to the new namespace when the rename step happens
4. keep `PathRequestRecord` and any future Trailblazer-specific record helpers in Trailblazer
5. rerun Trailblazer integration tests to ensure there is no behavior drift

## 9. Recommended Order Of Operations

Suggested extraction order:

1. Create the new `Chronicler` project in the same repository first.
2. Move the generic files listed above without changing behavior.
3. Add a generic Chronicler test project and migrate the existing generic serialization tests.
4. Point Trailblazer at the new project reference.
5. Verify Trailblazer integration tests still pass.
6. Only after the extraction is stable, decide whether to rename namespaces and whether to split transports into separate packages.

## 10. Success Criteria

The extraction is successful when:

- Trailblazer compiles against Chronicler as an external dependency
- Chronicler has no Trailblazer runtime dependencies
- generic Chronicler tests pass without referencing Trailblazer navigation/pathing code
- Trailblazer integration tests still pass unchanged in behavior
- `PathRequestRecord` and similar runtime-specific helpers remain clearly outside the generic package boundary
