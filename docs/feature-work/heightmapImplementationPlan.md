# Deterministic Heightmaps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox
> (`- [ ]`) syntax for tracking.

**Goal:** Add context-owned deterministic heightmap consumption so Trailblazer can sample
environment ground height without engine raycasts and optionally ground navigators to those samples.

**Architecture:** Heightmaps are a new context-owned domain parallel to pathing and navigation.
Each heightmap layer owns its own X/Z sample lattice, compressed short storage, and vertical
selection band; the `TrailblazerWorldContext.Heightmaps` service registers layers and resolves
deterministic ground samples. `Navigator` integration is opt-in and uses sampled ground/contact Y
to update `SurfaceLevel` and, when configured, project grounded root Y without creating fake
vertical velocity.

**Tech Stack:** C# 11, `netstandard2.1`, `net8.0`, `FixedMathSharp`, `GridForge.GridWorld`,
`SwiftCollections.Dimensions.SwiftShortArray2D`, Chronicler, xUnit v3, FluentAssertions.

---

## Purpose

The legacy prototype under `docs/feature-work/prototype` baked Unity raycast results into a dense
2D short map, then queried X/Z world position to recover a fixed-point Y value. The useful behavior
is still valuable for Trailblazer: a preconfigured deterministic surface-height lookup that can
avoid per-frame host physics probes when a navigator is grounded.

The core library should consume prebuilt height data only. Engine-specific baking, Unity raycasts,
editor tools, and scene-layer selection belong in a future host or Unity integration package.

## Agreed Design Decisions

- The sampled value is environment ground/contact Y, not navigator body/root Y.
- Root Y projection is derived from sampled ground Y plus `Navigator.FootPositionAdjust` plus a
  configured clearance offset.
- Heightmaps are owned by `TrailblazerWorldContext`, exposed through `TrailblazerWorldContext.Heightmaps`.
- Heightmaps are not attached to one `WorldVoxelIndex` or one grid. A layer can cover one grid, many
  grids, or part of a grid.
- Runtime sampling uses the layer's own rectangular X/Z bounds and point index map.
- Multi-level worlds use multiple heightmap layers. Each layer maps X/Z to one ground Y and carries
  a vertical selection band for level routing.
- Navigator integration supports explicit modes:
  - `Disabled`
  - `SurfaceLevelOnly`
  - `SurfaceLevelAndPosition`
- The active layer is sticky while still valid so an agent under a platform does not snap up to the
  platform merely because both surfaces share X/Z.
- Compressed short storage is the only runtime representation, with explicit compression metadata:
  `GroundY = ReferenceHeight + CompressedValue * HeightStep`.
- Raw `Fixed64` input may be accepted by a convenience factory for tests, procedural setup, and
  developer ergonomics, but it must compress once during construction and then use the same runtime
  storage and sampling path as baked compressed data.
- The public docs must explain why both factories exist: `FromCompressed(...)` is the preferred
  baked/runtime path, while `FromHeights(...)` is a setup-time convenience when the caller already
  has fixed-point heights and is willing to quantize them with explicit compression metadata.
- No Unity, `float`, `double`, `Mathf`, `Physics`, wall-clock APIs, or static singleton lookups
  enter runtime code.

## Non-Goals

- Do not build Unity raycast baking into `src/Trailblazer`.
- Do not infer walkability or path connectivity from heightmaps.
- Do not merge heightmaps into `NavigationChart` or pathing partitions.
- Do not add per-frame automatic layer scans for every navigator that has not opted in.
- Do not support multiple Y values inside one heightmap sample. Multi-level overlap is represented
  by multiple layers.
- Do not use heightmap projection while a navigator is airborne, swimming, flying, climbing,
  mantling, or otherwise under host-owned vertical control.

## Core Invariants

- Sampling is deterministic and allocation-free after registration.
- A heightmap layer answers exactly one question: given world X/Z inside its bounds, what is the
  environment ground/contact Y for that layer?
- Layer selection is deterministic when multiple layers cover the same X/Z.
- Selection uses current foot/contact Y, not root Y, when deciding which vertical band can be used.
- Position projection updates current and last position together so terrain-following correction
  does not create fake vertical velocity, acceleration, fall distance, or stuck-detection noise.
- Invalid samples fail through `Try...` APIs instead of returning `Fixed64.Zero`.
- Compression range and precision are explicit data, not hidden consequences of `Fixed64` raw bits.
- Runtime sampling has one storage flow and one algorithm. It must not branch between raw and
  compressed height storage.
- Resetting or disposing one context clears only that context's heightmap registry.

## Target Public Surface

These signatures are the intended API shape. Implementation may adjust names only when local
conventions require a better fit.

```csharp
TrailblazerWorldContext context = TrailblazerWorldContext.Attach(world);

var compression = new HeightmapCompression(
    referenceHeight: (Fixed64)(-20),
    heightStep: Fixed64.One / 256);

var surface = HeightmapSurface.FromCompressed(
    name: "ArenaGround",
    samples: compressedSamples,
    minBounds: new Vector3d(-50, 0, -50),
    interval: Fixed64.Half,
    compression: compression);

// Convenience only: this compresses the fixed-point input once and then uses the same runtime
// storage and sampling path as FromCompressed.
var testSurface = HeightmapSurface.FromHeights(
    name: "ArenaGroundForTests",
    heights: fixedPointSamples,
    minBounds: new Vector3d(-50, 0, -50),
    interval: Fixed64.Half,
    compression: compression);

context.Heightmaps.Register(
    surface,
    minSelectionY: (Fixed64)(-1),
    maxSelectionY: (Fixed64)2,
    priority: 0);

if (context.Heightmaps.TrySampleGround(position, out HeightmapSample sample))
{
    Fixed64 groundY = sample.GroundY;
}

navigator.ConfigureHeightmapGrounding(
    mode: HeightmapGroundingMode.SurfaceLevelAndPosition,
    layerName: "ArenaGround",
    groundOffset: Fixed64.Zero,
    snapTolerance: Fixed64.One);
```

## File Structure Target

| File | Responsibility |
| --- | --- |
| `src/Trailblazer/Heightmaps/HeightmapCompression.cs` | Explicit short-to-`Fixed64` compression metadata and clamp helpers. |
| `src/Trailblazer/Heightmaps/HeightmapSample.cs` | Immutable result containing ground Y, layer name, sample position, and selection metadata. |
| `src/Trailblazer/Heightmaps/HeightmapSurface.cs` | Immutable X/Z lattice, bounds, compressed storage, compressed/convenience factories, and bilinear sampling. |
| `src/Trailblazer/Heightmaps/HeightmapLayerRegistration.cs` | Context registration metadata: layer name, surface, vertical band, priority, order. |
| `src/Trailblazer/Heightmaps/HeightmapWorldState.cs` | Context-local layer dictionary, deterministic registration order, and reset state. |
| `src/Trailblazer/Heightmaps/TrailblazerHeightmapService.cs` | Public context-owned register, unregister, lookup, and sampling API. |
| `src/Trailblazer/Navigation/Navigator/Heightmaps/HeightmapGroundingMode.cs` | Navigator opt-in mode enum. |
| `src/Trailblazer/Navigation/Navigator/Heightmaps/NavigatorHeightmapGroundingSettings.cs` | Navigator-owned grounding configuration and active-layer cache. |
| `src/Trailblazer/Runtime/TrailblazerWorldContext.cs` | Adds `Heightmaps` service construction, reset, and disposal integration. |
| `src/Trailblazer/Navigation/Navigator/Navigator.cs` | Adds opt-in configuration, protected sampling hook, and grounded projection. |
| `tests/Trailblazer.Tests/Heightmaps/HeightmapSurface.Tests.cs` | Bounds, index conversion, compressed sampling, interpolation, edge clamping, and `FromHeights` one-time compression. |
| `tests/Trailblazer.Tests/Heightmaps/HeightmapCompression.Tests.cs` | Compression precision, range clamping, and deterministic quantization behavior. |
| `tests/Trailblazer.Tests/Heightmaps/TrailblazerHeightmapService.Tests.cs` | Context registry, multi-layer selection, sticky active layer, and reset behavior. |
| `tests/Trailblazer.Tests/Navigation/Navigator/NavigatorHeightmapGrounding.Tests.cs` | Navigator `SurfaceLevelOnly` and `SurfaceLevelAndPosition` behavior. |
| `docs/wiki/HEIGHTMAPS.MD` | User-facing heightmap domain guide. |
| `docs/wiki/OVERVIEW.md` | Adds heightmaps to the high-level architecture. |
| `README.md` | Adds a concise heightmap usage example and host responsibility note. |

## Phase 0 - Baseline And Naming

**Goal:** Lock down the feature boundary before source changes.

- [x] Confirm the namespace and folder name will be `Trailblazer.Heightmaps`.
- [x] Confirm `HeightmapSurface` stores environment ground/contact Y.
- [x] Confirm `HeightmapGroundingMode.SurfaceLevelAndPosition` is the only mode that projects
  root Y.
- [x] Confirm `groundOffset` means extra clearance above sampled ground after
  `FootPositionAdjust` is applied.
- [x] Confirm layer bands use inclusive lower bound and exclusive upper bound:
  `minSelectionY <= contactY && contactY < maxSelectionY`.
- [x] Record any additional open questions in `docs/feature-work/hardeningPlans.md` instead of
  widening this plan.

## Phase 1 - Compressed Heightmap Surface And Sampling

**Goal:** Add the immutable data model, explicit compression metadata, and O(1) compressed
heightmap sampling.

**Files:**

- Create: `src/Trailblazer/Heightmaps/HeightmapCompression.cs`
- Create: `src/Trailblazer/Heightmaps/HeightmapSample.cs`
- Create: `src/Trailblazer/Heightmaps/HeightmapSurface.cs`
- Test: `tests/Trailblazer.Tests/Heightmaps/HeightmapCompression.Tests.cs`
- Test: `tests/Trailblazer.Tests/Heightmaps/HeightmapSurface.Tests.cs`

**Implementation outline:**

```csharp
namespace Trailblazer.Heightmaps;

public readonly struct HeightmapCompression
{
    public Fixed64 ReferenceHeight { get; }
    public Fixed64 HeightStep { get; }

    public HeightmapCompression(Fixed64 referenceHeight, Fixed64 heightStep);
    public Fixed64 Decompress(short compressed);
    public short CompressClamped(Fixed64 groundY);
}
```

```csharp
public readonly struct HeightmapSample
{
    public string LayerName { get; }
    public Vector3d QueryPosition { get; }
    public Fixed64 GroundY { get; }
    public Fixed64 DistanceFromSelectionY { get; }
}
```

```csharp
public sealed class HeightmapSurface
{
    public string Name { get; }
    public Vector3d MinBounds { get; }
    public Vector3d MaxBounds { get; }
    public Fixed64 Interval { get; }
    public int Width { get; }
    public int Depth { get; }
    public HeightmapCompression Compression { get; }

    public static HeightmapSurface FromCompressed(
        string name,
        SwiftShortArray2D samples,
        Vector3d minBounds,
        Fixed64 interval,
        HeightmapCompression compression);

    public static HeightmapSurface FromHeights(
        string name,
        Fixed64[,] heights,
        Vector3d minBounds,
        Fixed64 interval,
        HeightmapCompression compression);

    public bool TrySampleGround(Vector3d worldPosition, out Fixed64 groundY);
}
```

**Tasks:**

- [x] Write tests proving `HeightmapCompression` rejects `HeightStep <= Fixed64.Zero`.
- [x] Write tests proving `Decompress(0)` returns `ReferenceHeight`.
- [x] Write tests proving positive and negative compressed values use
  `ReferenceHeight + compressed * HeightStep`.
- [x] Write tests proving `CompressClamped` clamps values outside the representable short range.
- [x] Write tests proving `FromCompressed` rejects null data, empty dimensions, and non-positive
  interval.
- [x] Write tests proving `FromHeights` rejects null data and compresses into the same sampling
  result as an equivalent `SwiftShortArray2D` passed to `FromCompressed`.
- [x] Write tests proving negative world X/Z outside `MinBounds` fails instead of truncating to
  zero.
- [x] Write tests proving exact edge positions clamp to the last valid sample instead of sampling
  one past the array.
- [x] Write tests proving bilinear interpolation uses four neighboring samples and deterministic
  `FixedMath.LinearInterpolate`.
- [x] Write XML docs on `FromCompressed(...)` explaining that it is the preferred baked/runtime
  path and avoids setup-time quantization work.
- [x] Write XML docs on `FromHeights(...)` explaining that it is a setup-time convenience for tests,
  generated data, or host tooling that already has fixed-point heights; it still quantizes once and
  stores compressed shorts internally.
- [x] Implement `HeightmapCompression`.
- [x] Implement `HeightmapSurface` with `SwiftShortArray2D` as the only internal sample storage.
- [x] Implement `FromCompressed(...)`.
- [x] Implement `FromHeights(...)` by compressing the provided `Fixed64[,]` into a
  `SwiftShortArray2D` during construction and then delegating to the same internal initialization as
  `FromCompressed(...)`.
- [x] Implement floor-based local index conversion:
  `localX = (worldX - MinBounds.x) / Interval`, `x0 = localX.FloorToInt()`.
- [x] Implement clamped neighbor lookup using `x1 = FixedMath.Min(x0 + 1, Width - 1)` and the same
  rule for Z.
- [x] Decompress only the four sampled corners into local `Fixed64` values during
  `TrySampleGround`.
- [x] Run:

```bash
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~Heightmap
```

Expected: heightmap surface and compression tests pass.

## Phase 2 - Context-Owned Heightmap Service

**Goal:** Register, unregister, reset, and sample heightmap layers through `TrailblazerWorldContext`.

**Files:**

- Create: `src/Trailblazer/Heightmaps/HeightmapLayerRegistration.cs`
- Create: `src/Trailblazer/Heightmaps/HeightmapWorldState.cs`
- Create: `src/Trailblazer/Heightmaps/TrailblazerHeightmapService.cs`
- Modify: `src/Trailblazer/Runtime/TrailblazerWorldContext.cs`
- Test: `tests/Trailblazer.Tests/Heightmaps/TrailblazerHeightmapService.Tests.cs`

**Implementation outline:**

```csharp
public sealed class TrailblazerHeightmapService
{
    public bool Register(
        HeightmapSurface surface,
        Fixed64 minSelectionY,
        Fixed64 maxSelectionY,
        int priority = 0);

    public bool Unregister(string layerName);
    public bool IsRegistered(string layerName);
    public bool TryGetRegistration(string layerName, out HeightmapLayerRegistration registration);
    public bool TrySampleGround(Vector3d worldPosition, out HeightmapSample sample);
    public void Reset();
}
```

**Tasks:**

- [x] Write tests proving duplicate layer names are rejected inside one context.
- [x] Write tests proving two separate contexts can register the same layer name independently.
- [x] Write tests proving `Reset()` clears only the current context's heightmap registry.
- [x] Write tests proving service APIs throw clear disposed-context errors after context disposal.
- [x] Implement `HeightmapWorldState` with `SwiftDictionary<string, HeightmapLayerRegistration>`.
- [x] Add `TrailblazerWorldContext.Heightmaps` construction beside `Pathing`, `Guides`, and
  `Navigation`.
- [x] Add heightmap service reset/disposal into `TrailblazerWorldContext.Reset()` and `Dispose()`.
- [x] Run:

```bash
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~TrailblazerHeightmapService
```

Expected: service tests pass.

## Phase 3 - Multi-Level Layer Selection

**Goal:** Resolve overlapping X/Z layers deterministically using vertical selection bands.

**Files:**

- Modify: `src/Trailblazer/Heightmaps/HeightmapLayerRegistration.cs`
- Modify: `src/Trailblazer/Heightmaps/TrailblazerHeightmapService.cs`
- Test: `tests/Trailblazer.Tests/Heightmaps/TrailblazerHeightmapService.Tests.cs`

**Selection rules:**

1. Candidate layer X/Z bounds must contain the query position.
2. Candidate vertical band must contain the query contact Y, using the inclusive/exclusive rule
   from Phase 0.
3. If the caller supplies a current active layer and that layer is still valid, select it.
4. Otherwise select the valid candidate with the smallest absolute distance between query contact Y
   and sampled ground Y.
5. If distance ties, higher priority wins.
6. If priority ties, earlier registration order wins.

**Tasks:**

- [x] Add tests for ground at Y 0 and platform at Y 3 sharing the same X/Z, with contact Y near
  ground selecting the ground layer.
- [x] Add tests for the same X/Z with contact Y near platform selecting the platform layer.
- [x] Add tests proving sticky active layer wins while still valid.
- [x] Add tests proving sticky active layer is abandoned when the query contact Y leaves its
  vertical band.
- [x] Add tests proving priority and registration order break exact ties deterministically.
- [x] Implement overload:

```csharp
public bool TrySampleGround(
    Vector3d worldPosition,
    string? preferredLayerName,
    out HeightmapSample sample);
```

- [x] Keep the no-preference overload deterministic by using the same candidate ordering without a
  preferred layer.
- [x] Run:

```bash
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~TrailblazerHeightmapService
```

Expected: multi-layer service tests pass.

## Phase 4 - Navigator Grounding Opt-In

**Goal:** Let navigators consume heightmaps without weakening host ownership of traversal probing.

**Files:**

- Create: `src/Trailblazer/Navigation/Navigator/Heightmaps/HeightmapGroundingMode.cs`
- Create: `src/Trailblazer/Navigation/Navigator/Heightmaps/NavigatorHeightmapGroundingSettings.cs`
- Modify: `src/Trailblazer/Navigation/Navigator/Navigator.cs`
- Test: `tests/Trailblazer.Tests/Navigation/Navigator/NavigatorHeightmapGrounding.Tests.cs`

**Implementation outline:**

```csharp
public enum HeightmapGroundingMode
{
    Disabled,
    SurfaceLevelOnly,
    SurfaceLevelAndPosition
}
```

```csharp
public virtual void ConfigureHeightmapGrounding(
    HeightmapGroundingMode mode,
    string? layerName = null,
    Fixed64? groundOffset = null,
    Fixed64? snapTolerance = null);
```

```csharp
protected bool TryApplyHeightmapGrounding(
    bool updateMotorState = false,
    Fixed64? surfaceFriction = null,
    MotionTransfer motionTransfer = MotionTransfer.None);
```

**Tasks:**

- [x] Write tests proving `Disabled` performs no sampling and leaves `SurfaceLevel` and position
  unchanged.
- [x] Write tests proving `SurfaceLevelOnly` samples ground Y and calls `SetGroundContact` without
  changing navigator root Y.
- [x] Write tests proving `SurfaceLevelAndPosition` sets root Y to
  `sample.GroundY + FootPositionAdjust + groundOffset`.
- [x] Write tests proving root projection shifts `Position` and `LastPosition` together.
- [x] Write tests proving projection is skipped when current medium is not `TraversalMedium.Solid`.
- [x] Write tests proving projection is skipped when the absolute root correction exceeds
  `snapTolerance`.
- [x] Write tests proving a configured layer name preserves the active layer across frames while
  valid.
- [x] Implement `NavigatorHeightmapGroundingSettings` with mode, layer name, active layer name,
  ground offset, and snap tolerance.
- [x] Add `ConfigureHeightmapGrounding(...)` to `Navigator`.
- [x] Add a protected `TryApplyHeightmapGrounding(...)` hook that concrete navigators can call from
  `CheckTrekCondition()`.
- [x] Keep the base navigator from performing hidden heightmap probes unless the opt-in helper is
  called by the host/concrete navigator.
- [x] Run:

```bash
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~NavigatorHeightmapGrounding
```

Expected: navigator heightmap grounding tests pass.

## Phase 5 - Serialization Alignment

**Goal:** Preserve navigator heightmap opt-in settings across populate-existing-instance loads
without serializing host-owned heightmap data through navigators.

**Files:**

- Modify: `src/Trailblazer/Navigation/Navigator/Navigator.cs`
- Modify: `src/Trailblazer/Navigation/Navigator/Heightmaps/NavigatorHeightmapGroundingSettings.cs`
- Test: `tests/Trailblazer.Tests/Navigation/Navigator/NavigatorSerialization.Tests.cs`
- Docs: `docs/wiki/SERIALIZATION.MD`

**Tasks:**

- [x] Add serialization tests proving mode, layer name, active layer name, ground offset, and snap
  tolerance round-trip through Chronicler.
- [x] Add a load test proving missing registered heightmap data does not construct a heightmap and
  causes `TryApplyHeightmapGrounding(...)` to return false.
- [x] Record only navigator-owned settings. Do not record heightmap samples or registrations inside
  `Navigator.RecordData(...)`.
- [x] Bind restored settings to the already-bound `TrailblazerWorldContext` during load, matching
  existing navigator load behavior.
- [x] Update `docs/wiki/SERIALIZATION.MD` to state that hosts must register heightmaps on the
  context before loaded navigators can use heightmap grounding.
- [x] Run:

```bash
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~NavigatorSerialization
```

Expected: navigator serialization tests pass.

## Phase 6 - Documentation And Examples

**Goal:** Document the domain before exposing it as alpha-facing API.

**Files:**

- Create: `docs/wiki/HEIGHTMAPS.MD`
- Modify: `docs/wiki/OVERVIEW.md`
- Modify: `README.md`

**Docs content:**

- Explain that heightmaps consume prebuilt environment ground/contact Y.
- Explain that compressed `SwiftShortArray2D` is the only runtime storage path.
- Explain when to use `FromCompressed(...)` versus `FromHeights(...)`:
  - use `FromCompressed(...)` for baked maps, serialized map data, and production runtime setup
    because the caller already has compact samples
  - use `FromHeights(...)` for tests, generated maps, and developer tooling that already has
    fixed-point heights and wants Trailblazer to perform the one-time quantization step
- Explain the precision/range tradeoff controlled by `HeightmapCompression.ReferenceHeight` and
  `HeightmapCompression.HeightStep`.
- Explain vertical selection bands for multi-level worlds.
- Explain navigator opt-in modes and why projection is grounded-only.
- Explain that Unity or engine-specific baking is outside core Trailblazer.
- Include one compact sample for registering a compressed layer and one compact sample for
  navigator grounding.

**Tasks:**

- [ ] Add `HEIGHTMAPS.MD` with sections for model, registration, sampling, multi-level layers,
  navigator grounding, and host responsibilities.
- [ ] Link `HEIGHTMAPS.MD` from `docs/wiki/OVERVIEW.md`.
- [ ] Add a short README feature bullet and usage example.
- [ ] Keep docs aligned with exact type and method names landed in source.

## Phase 7 - Full Verification

**Goal:** Prove the feature is stable in Release across the solution.

**Commands:**

```bash
dotnet build Trailblazer.slnx --configuration Release
dotnet test Trailblazer.slnx --configuration Release
```

Expected: build succeeds and the full Release suite passes.

If implementation changes public behavior or serialized fields, also run the focused docs review:

```bash
rg -n "Heightmap|heightmap|SurfaceLevel|CheckTrekCondition|Navigator" README.md docs/wiki src/Trailblazer tests/Trailblazer.Tests
```

Expected: heightmap references are intentional, public docs match source names, and no stale
prototype-only names such as `HeightMapSaver` appear in public API docs.

## Risk Register

| Risk | Mitigation |
| --- | --- |
| Agents snap from ground to an overhead platform at shared X/Z. | Use vertical selection bands plus sticky active layer selection. |
| Projection creates fake vertical velocity or fall distance. | Shift `Position` and `LastPosition` together during grounded projection. |
| Compression silently loses too much precision. | Store `HeightStep` explicitly and test round-trip tolerances. |
| Heightmaps become pathing topology by accident. | Keep heightmaps separate from `NavigationChart`, partitions, requests, and guide caches. |
| Host collision and heightmap projection fight each other. | Make navigator grounding an explicit protected helper called by concrete `CheckTrekCondition()`. |
| Context disposal leaves stale registrations. | Add context-local reset and disposal tests. |

## Exit Criteria

- Heightmap surfaces can sample compressed data deterministically.
- `FromHeights(...)` compresses fixed-point input once and then uses the same runtime path as
  `FromCompressed(...)`.
- Context services can register overlapping multi-level layers and resolve samples by vertical band.
- Navigators can opt into `SurfaceLevelOnly` or `SurfaceLevelAndPosition` behavior.
- Grounded projection uses contact Y plus `FootPositionAdjust` plus configured offset.
- Navigator serialization preserves opt-in settings without serializing heightmap data.
- README and wiki docs explain the feature and its host responsibilities.
- Focused heightmap and navigator tests pass in Release.
- Full solution build and test pass in Release.
