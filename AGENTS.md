# Trailblazer Contributor Guide

## Purpose

Trailblazer is a framework-agnostic deterministic navigation library for
lockstep simulations and games. The library currently targets `netstandard2.1`
and `net8.0`, uses fixed-point math via `FixedMathSharp`.

Current priorities:

1. Preserve deterministic behavior first.
2. Reduce time complexity and avoid unnecessary allocations in hot paths.
3. Fix correctness issues before broad refactors.
4. Add XML documentation and concise comments for non-obvious logic.
5. Close test coverage gaps and keep the suite reliable in `Release`.

## Start Here

Read these in order before making non-trivial changes:

1. [`docs/wiki/Overview.md`](docs/wiki/Overview.md)
2. [`README.md`](README.md)
3. The relevant source folder under [`src/Trailblazer`](src/Trailblazer)
4. The matching test area under
   [`tests/Trailblazer.Tests`](tests/Trailblazer.Tests)
5. [`src/Trailblazer/Trailblazer.csproj`](src/Trailblazer/Trailblazer.csproj)
   and
   [`tests/Trailblazer.Tests/Trailblazer.Tests.csproj`](tests/Trailblazer.Tests/Trailblazer.Tests.csproj)

## Source of Truth

When code and docs disagree, prefer the code.

Keep these aligned whenever behavior or public API changes:

- [`README.md`](README.md)
- [`docs/wiki/Overview.md`](docs/wiki/Overview.md)
- [`docs/wiki/Serialization.md`](docs/wiki/Serialization.md) when serialization
  behavior or Chronicler guidance changes
- the relevant source and test files under [`src/Trailblazer`](src/Trailblazer)
  and [`tests/Trailblazer.Tests`](tests/Trailblazer.Tests)

## Repository Map

| Path                                                                       | Purpose                                                       | Notes                                                                                                                                                                                  |
| -------------------------------------------------------------------------- | ------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [`docs`](docs)                                                             | Design notes and high-level explanations                      | Start with `docs/wiki/Overview.md`; `Serialization.md`, `PathManager.md`, `Navigator.md`, `NavSteering.md`, `NavTurning.md`, `NavMotor.md`, and `Gravity.md` are subsystem references. |
| [`src/Trailblazer`](src/Trailblazer)                                       | Main library project                                          | Multi-targets `netstandard2.1` and `net8.0`.                                                                                                                                           |
| [`src/Trailblazer/Pathing`](src/Trailblazer/Pathing)                       | Chart management, A*, flow field, guide caching, voxel lookup | Most performance-sensitive and correctness-sensitive area.                                                                                                                             |
| [`src/Trailblazer/Navigation`](src/Trailblazer/Navigation)                 | Runtime navigation stack                                      | `Navigator`, `NavSteering`, `NavTurning`, `NavMotor`, locomotions.                                                                                                                     |
| [`src/Trailblazer/Serialization`](src/Trailblazer/Serialization)           | Chronicler serialization layer                                | Contains `IRecordable`, `IChronicler`, JSON/MemoryPack transports, stable-link support, and the shared `README.md` API reference.                                                      |
| [`src/Trailblazer/Support`](src/Trailblazer/Support)                       | Shared helper abstractions                                    | Small but still part of public surface area.                                                                                                                                           |
| [`tests/Trailblazer.Tests`](tests/Trailblazer.Tests)                       | xUnit v3 test project                                         | Uses FluentAssertions, Moq, FixedMathSharp, GridForge.                                                                                                                                 |
| [`tests/Trailblazer.Tests/Pathing`](tests/Trailblazer.Tests/Pathing)       | Pathing-focused tests                                         | A*, flow field, chart behavior, heap behavior.                                                                                                                                         |
| [`tests/Trailblazer.Tests/Navigation`](tests/Trailblazer.Tests/Navigation) | Navigation-focused tests                                      | Navigator, steering, turning, motor, locomotion behaviors.                                                                                                                             |
| [`tests/Trailblazer.Tests/Support`](tests/Trailblazer.Tests/Support)       | Fixtures and helper factories                                 | Important because most runtime state is global/static.                                                                                                                                 |

Ignore generated output when reviewing structure:

- `bin/`
- `obj/`
- `TestResults/`
- `.vs/`

## Runtime Architecture

The main flow is:

Hosts should call `TrailblazerManager.Initialize()` once during application
startup before the first fixed-step frame. The manager keeps a lazy first-use
fallback, but explicit bootstrap is the intended integration path.

1. `TrailblazerManager` advances simulation time and ticks guide-cache cleanup.
2. `PathManager` owns chart registration, chart initialization/unloading,
   partition pooling, and walkability/neighbor queries.
3. `AStarSurveyor` and `FlowFieldSurveyor` compute reusable survey results.
4. `PathGuideFactory` and `ReusableSurveyResultCache<T>` cache and return guide
   data.
5. `NavSteering` turns a path request into a heading, line-of-sight shortcut,
   repath, and group steering.
6. `NavTurning` orients the navigator toward the requested heading.
7. `NavMotor` and locomotion handlers apply deterministic movement state
   changes.
8. `Navigator` coordinates the above components and exposes the
   simulation-facing API.

Representative entry points:

- [`src/Trailblazer/TrailblazerManager.cs`](src/Trailblazer/TrailblazerManager.cs)
- [`src/Trailblazer/Pathing/PathManager.cs`](src/Trailblazer/Pathing/PathManager.cs)
- [`src/Trailblazer/Pathing/Search/PathGuideFactory.cs`](src/Trailblazer/Pathing/Search/PathGuideFactory.cs)
- [`src/Trailblazer/Navigation/Navigator.cs`](src/Trailblazer/Navigation/Navigator.cs)
- [`src/Trailblazer/Navigation/Steering/NavSteering.cs`](src/Trailblazer/Navigation/Steering/NavSteering.cs)
- [`src/Trailblazer/Navigation/Motor/NavMotor.cs`](src/Trailblazer/Navigation/Motor/NavMotor.cs)
- [`src/Trailblazer/Navigation/Turning/NavTurning.cs`](src/Trailblazer/Navigation/Turning/NavTurning.cs)

## Serialization Status

Trailblazer currently uses the Chronicler serialization layer under
[`src/Trailblazer/Serialization`](src/Trailblazer/Serialization).

Important current rules:

- Trailblazer serializes through explicit `IRecordable.RecordData(...)`
  implementations rather than relying on serializer attributes for runtime
  graphs.
- The active transports are `JsonRecordSerializer` and
  `MemoryPackRecordSerializer`.
- The current Trailblazer coverage is the navigation branch: `Navigator`,
  `NavSteering`, `NavTurning`, `NavMotor`, `LocomotionHandler`, and the
  locomotion types.
- The load model is populate-existing-instance only. Hosts create and initialize
  runtime shells first, then Chronicler populates supported state.
- Trailblazer intentionally does not use Chronicler as a construct-from-data
  object factory.
- Host bindings are not serialized.
- Movement-group coordinator state is rebuild-only runtime state. Group intent
  is serialized per steering session, and hosts may call
  `PrewarmMovementGroup()` after load to seed the coordinator before the next
  frame.

If you touch serialization work, read:

- [`docs/wiki/Serialization.md`](docs/wiki/Serialization.md) for
  Trailblazer-specific coverage and runtime behavior

## External Dependencies

The main external packages shape how this project should be changed:

- `FixedMathSharp`: fixed-point math and deterministic vector/quaternion types.
- `GridForge`: voxel grids, spatial queries, global grid management, and chart
  backing data.
- `SwiftCollections`: dictionaries, lists, queues, object pools, and related
  low-allocation collection types.

Do not casually replace these with standard floating-point or non-deterministic
alternatives.

## Determinism Rules

Any change that affects simulation order, iteration order, rounding, path
scoring, or update timing is high risk.

Always prefer:

- `Fixed64`, `Vector3d`, and `FixedQuaternion` over `float`, `double`, and
  `System.Numerics`.
- Frame-based reasoning through `TrailblazerManager.FrameRate`, `DeltaTime`, and
  `FrameCount`.
- Stable and explicit ordering when cache keys, path scoring, or traversal
  decisions depend on iteration.
- Existing lockstep-friendly patterns over convenience shortcuts.

Avoid introducing:

- Floating-point math in simulation logic.
- Time-dependent APIs such as `DateTime.Now`, timers, or wall-clock scheduling
  in runtime code.
- Randomness without a deterministic seed and explicit ownership.
- Hidden allocations or LINQ in per-frame or per-node hot paths unless a
  benchmark or profile justifies it.
- Changes that make results depend on platform-specific collection ordering.

## Coding Style and Documentation

Observed project conventions:

- `LangVersion` is `11.0`.
- `ImplicitUsings` are disabled.
- Library nullable context is disabled; tests use nullable enabled.
- XML doc output is generated for the library, but warning `1591` is suppressed.
- Namespace-folder matching is not enforced.

Contributor expectations for code and docs:

- Add or improve XML `<summary>` tags for public and externally meaningful
  internal APIs when touching them.
- Add brief comments only where the logic is hard to infer from the code alone.
- Preserve ASCII unless the file already requires otherwise.
- Keep comments factual. Explain invariants, edge conditions, or reasons behind
  tricky logic.
- Do not add comment noise around obvious assignments or straight-line code.
- Split reusable or generic infrastructure into focused types and files instead
  of bundling it into an unrelated runtime class. Prefer one primary type per
  file unless the extra type is tightly scoped and truly private to that
  implementation.
- Prefer `SwiftCollections` over `System.Collections*` types when a suitable
  collection already exists there, especially in runtime or hot-path code. If
  you intentionally keep a BCL collection, the reason should be obvious from the
  code or called out in review.

## Performance Guidance

Optimization work should focus on proven hot paths and data-structure behavior,
not cosmetic micro-tuning.

Likely hotspots:

- `PathManager` chart initialization, neighbor binding, and unload/invalidate
  flow.
- `AStarSurveyor` node expansion and edge-validation work.
- `FlowFieldSurveyor` flood generation and flow-vector generation.
- `PathGuideFactory` and `ReusableSurveyResultCache<T>` cache hit/miss and
  eviction behavior.
- `NavSteering.GetHeading(...)`, line-of-sight checks, stuck detection, and
  combined steering logic.

Optimization rules:

- Preserve path correctness before reducing allocations.
- Do not knowingly land avoidable steady-state inefficiencies in new runtime or
  pathing infrastructure with the expectation of "optimizing it later"; new
  stateful runtime code should start lean in both allocation behavior and update
  complexity.
- Pool only when lifetime management stays obvious and testable.
- Be careful with cache invalidation; stale guide reuse is worse than a small
  allocation.
- Avoid broad refactors across pathing and navigation in one change set.
- If complexity changes, add or update tests that pin the edge cases affected by
  the new logic.

## Testing Workflow

Use these baseline commands:

```bash
dotnet restore Trailblazer.slnx
dotnet build Trailblazer.slnx --configuration Release
dotnet test Trailblazer.slnx --configuration Release
```

Important note:

- Building the library also produces NuGet packages because
  `GeneratePackageOnBuild` is enabled in the library project.

For focused work, prefer targeted runs first, then a full solution run:

```bash
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~NavSteering
```

## Test Design Expectations

Tests should mirror the runtime area being changed.

Current coverage is strongest around:

- A* surveyor behavior
- flow field surveyor behavior
- navigation chart and heap behavior
- steering behavior
- turning behavior
- navigator behavior
- locomotion and motor behavior

Coverage appears lighter or absent around:

- `TrailblazerManager`
- `PathGuideFactory`
- `ReusableSurveyResultCache<T>`
- `VoxelFinder`
- some support helpers and invalidation edge cases

When touching global/static state, use the existing fixtures and patterns:

- [`tests/Trailblazer.Tests/Support/TrailblazerFixture.cs`](tests/Trailblazer.Tests/Support/TrailblazerFixture.cs)
- [`tests/Trailblazer.Tests/Support/PathingFixture.cs`](tests/Trailblazer.Tests/Support/PathingFixture.cs)
- [`tests/Trailblazer.Tests/Support/PathTestFactory.cs`](tests/Trailblazer.Tests/Support/PathTestFactory.cs)

Reset requirements are important because runtime state is shared through static
managers:

- `GlobalGridManager.Setup()` / `GlobalGridManager.Reset()`
- `PathManager.Reset()`
- `TrailblazerManager.Reset()`

Do not leave charts, partitions, guides, or grid globals dirty after a test.

## Recommended Change Workflow

For both humans and AI agents, use this order:

1. Read the relevant doc page and the touched source file.
2. Read the matching tests before changing the implementation.
3. Identify deterministic invariants and global-state implications.
4. Make the smallest coherent code change that addresses the issue.
5. Add or update tests in the same change.
6. Add XML docs or clarifying comments while the code is open.
7. Run focused tests.
8. Run the full `Release` suite before closing the work.
9. Update `README.md` or `docs/*` if public behavior or developer workflow
   changed.
10. If serialization behavior or load semantics changed, update both
    serialization docs in the same pass.

## Guidance for AI Agents

If you are an automated coding agent working in this repository:

- Do not trust high-level docs blindly; validate against the code and tests.
- Do not broaden scope from one subsystem into another unless the change truly
  requires it.
- Call out any build or test failures explicitly, with exact file references.
- Treat cache invalidation, chart ownership, partition reuse, and static manager
  state as high-risk areas.
- Treat serialization boundaries and load semantics as high-risk areas. Avoid
  silently broadening from populate-existing-instance loads into
  construct-from-data behavior.
- Prefer focused edits plus verification over sweeping cleanup.
- If you change a public API or behavior, update both tests and docs in the same
  pass.
- If you add comments, comment the invariant or the reason, not the syntax.
- Do not leave generic helpers buried inside unrelated classes when they can
  stand alone as reusable support types.
- Reach for `SwiftCollections` first before introducing `System.Collections`,
  `System.Collections.Generic`, or `System.Collections.Concurrent` into library
  code.

## Guidance for Human Contributors

This codebase is small enough that local consistency matters more than abstract
purity.

Prefer:

- mirror source/test naming when adding files
- focused patches over broad folder-wide rewrites
- release-mode verification for pathing/navigation behavior
- documenting assumptions about voxel topology, unit size, and line-of-sight
  rules

Be especially careful when changing:

- path cache keys
- partition ownership and neighbor binding
- locomotion transitions
- stop/arrival thresholds
- line-of-sight shortcut logic
- any logic guarded by `#if DEBUG`
