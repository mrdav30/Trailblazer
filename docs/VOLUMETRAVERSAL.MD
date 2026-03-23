# Volume Traversal Reference

This document explains Trailblazer's raw-volume traversal model and the role of `VolumeTraversalRules` in making volume requests meaningful.

Use this file when you need to understand:

- why `VolumePathRequest` can work without charts
- why water traversal needs host configuration
- how raw-volume endpoint resolution differs from chart-backed endpoint resolution
- how volume traversal interacts with authored transitions

If you need the broader request model first, read `PATHING.MD`.
If you need authored handoff registration, pair this with `TRANSITIONS.MD`.

Relevant code:

- `src/Trailblazer/Pathing/Support/VolumeTraversalRules.cs`
- `src/Trailblazer/Pathing/Support/VolumeTraversalMode.cs`
- `src/Trailblazer/Pathing/VoxelFinder/RawVoxelFinder.cs`
- `src/Trailblazer/Pathing/Volume/VolumePathRequest.cs`
- `src/Trailblazer/Pathing/Volume/VolumeSurveyor.cs`
- `src/Trailblazer/Pathing/Volume/VolumeGuide.cs`

## 1. Why Volume Traversal Matters

Charts are a surface model.

They are great at answering:

- where ground or other intentional walkable structure exists
- how chart-backed units should move across that structure

They are not a good fit for:

- open 3D travel
- swimming through water volume
- chartless detours around blocked voxels
- spaces where the important rule is "stay inside this medium" instead of "stay on this chart"

That is the job of Trailblazer's volume traversal flow.

## 2. Core Model

Volume traversal is built from three pieces:

- `VolumeTraversalMode`
- `VolumeTraversalRules`
- raw-volume resolution through `RawVoxelFinder` and `VolumeSurveyor`

Current volume modes are:

- `Open`
- `Water`

The key mental model is:

- charts say where surface traversal is allowed
- volume rules say which raw voxels belong to a constrained traversal medium
- raw-volume pathing still respects blocked space and unit clearance

## 3. `VolumeTraversalRules`

`VolumeTraversalRules` is the host-facing configuration point for constrained raw-volume traversal.

Its public surface is:

- `HasWaterVoxelRule`
- `SetWaterVoxelPartition<TPartition>()`
- `SetWaterVoxelRule(VoxelRule rule)`
- `ClearWaterVoxelRule()`

### 3.1 What The Rules Actually Mean

The rules do not replace normal traversability checks.

They are an additional filter on top of:

- voxel existence
- blocked state
- partition or raw-neighbor clearance for the requested `UnitSize`

In raw-volume code, a voxel must be both:

- traversable
- compatible with the requested `VolumeTraversalMode`

That is why `VolumeTraversalRules` matters so much: it defines what "water" means to the library without asking Trailblazer to guess from engine-specific world data.

### 3.2 Open Versus Water

`VolumeTraversalMode.Open`:

- is always considered configured
- does not require a host rule
- still respects blockers and clearance

`VolumeTraversalMode.Water`:

- is only configured when a water rule exists
- fails fast when no water rule is installed
- only allows voxels that satisfy the host-defined water test

### 3.3 Two Ways To Configure Water

Use `SetWaterVoxelPartition<TPartition>()` when:

- your host already marks water voxels with a partition type

Use `SetWaterVoxelRule(...)` when:

- water membership depends on custom logic beyond one partition type

Examples:

```csharp
VolumeTraversalRules.SetWaterVoxelPartition<MyWaterPartition>();
```

```csharp
VolumeTraversalRules.SetWaterVoxelRule(voxel =>
    voxel != null
    && voxel.HasPartition<MyWaterPartition>());
```

### 3.4 Global Lifetime

`VolumeTraversalRules` is static and global.

Important lifecycle rule:

- `PathManager.Reset()` clears the installed water rule

That means tests, world reloads, and runtime bootstrap code must reapply volume rules when needed.

## 4. Raw-Volume Endpoint Resolution

Chart-backed requests use `VoxelFinder`.
Volume requests use `RawVoxelFinder`.

That is a very important difference.

`RawVoxelFinder` does not require `PathPartition` ownership.

Instead it resolves endpoints against raw voxels and checks:

- blocked state
- unit clearance
- traversal-mode match

### 4.1 Endpoint Resolution Order

For a volume request, endpoint resolution is:

1. try the direct voxel
2. try a nearby traversable raw-volume neighbor
3. trace toward the other endpoint and take the first compatible voxel found

Important nuance:

- if the requested mode is not configured, endpoint resolution fails immediately

### 4.2 `allowUnwalkableEndNode` In Volume Requests

`allowUnwalkableEndNode` in volume requests is still only an endpoint policy.

It can relax start or end acceptance, but it does not make the entire corridor ignore blockers.

For direct-path validation, relaxed endpoints still have to:

- match the requested `VolumeTraversalMode`

Interior voxels still have to be truly traversable.

## 5. Raw-Volume Traversability

`RawVoxelFinder.IsTraversable(...)` combines:

- base raw-voxel traversability
- volume-mode filtering

Base traversability means:

- the voxel exists
- it is not blocked
- it has enough clearance for the requested `UnitSize`

If a `PathPartition` is present, volume traversal still honors its clearance logic.
If no `PathPartition` is present, raw-volume clearance falls back to neighbor-space checks.

This is important in mixed worlds:

- volume traversal does not ignore the rest of the voxel world
- it simply stops requiring chart ownership

## 6. `VolumePathRequest` In Practice

Use `VolumePathRequest` when:

- there is no meaningful chart for the space
- movement should stay inside a constrained volume such as water
- open-volume travel should detour around blockers using raw voxels

Examples:

```csharp
var request = VolumePathRequest.Create(
    origin,
    destination,
    Fixed64.One,
    traversalMode: VolumeTraversalMode.Open);
```

```csharp
var request = VolumePathRequest.Create(
    origin,
    destination,
    Fixed64.One,
    traversalMode: VolumeTraversalMode.Water);
```

Important nuance:

- water-mode creation fails if `VolumeTraversalRules` has no water rule
- open-mode creation does not need extra setup

## 7. Why Complex Worlds Depend On This

Volume traversal is what keeps Trailblazer from forcing every traversal problem into charts.

It matters in worlds with:

- aircraft or free-flight spaces
- lakes, rivers, or flooded interiors
- blocked straight-line corridors that still have chartless 3D detours
- shoreline or docking areas where surface and medium traversal meet

Without volume traversal rules, you usually end up with one of two bad outcomes:

- water is treated like generic open space
- water travel is impossible because nothing tells the library what counts as water

With volume rules in place, you get a clean split:

- surface structure lives in charts
- medium membership lives in host-authored volume rules

## 8. Relationship To Transitions

`VolumeTraversalRules` and `TraversalTransitionRegistry` solve different problems, but they work together.

`TraversalTransitionRegistry` answers:

- where a route may hand off into or out of raw volume

`VolumeTraversalRules` answers:

- which voxels are actually valid for that volume segment

For example:

- `SwimEntry` and `SwimExit` transitions depend on `VolumeTraversalMode.Water`
- those transitions are only useful when the water rule marks the intended volume voxels
- `Takeoff` and `Landing` can use `VolumeTraversalMode.Open`

Important nuance:

- a valid transition does not make the middle volume segment valid
- the volume segment still has to resolve through `RawVoxelFinder` and `VolumeSurveyor`

## 9. Usage Patterns

### 9.1 Host-Marked Water

```csharp
VolumeTraversalRules.SetWaterVoxelPartition<MyWaterPartition>();

var request = VolumePathRequest.Create(
    new Vector3d(0, 0, 1),
    new Vector3d(4, 0, 1),
    Fixed64.One,
    traversalMode: VolumeTraversalMode.Water);
```

### 9.2 Open-Volume Detour

Use `VolumeTraversalMode.Open` when movement should stay chart-optional and 3D, but still avoid blocked voxels.

This is the pathing model used for aerial-style detours in the current codebase.

## 10. Common Gotchas

- Charts do not mark water for you.
- `VolumeTraversalMode.Open` still respects blockers and clearance.
- `VolumeTraversalMode.Water` fails until a water rule is configured.
- `VolumeTraversalRules` is global static state.
- `PathManager.Reset()` clears the installed water rule.
- Volume traversal does not require charts, but it still depends on real voxels existing.
- `allowUnwalkableEndNode` only relaxes endpoints; it does not make the whole route ignore blocked voxels.

## 11. AI And Contributor Notes

If you are changing volume traversal behavior, inspect these files together:

- `src/Trailblazer/Pathing/Support/VolumeTraversalRules.cs`
- `src/Trailblazer/Pathing/VoxelFinder/RawVoxelFinder.cs`
- `src/Trailblazer/Pathing/Volume/VolumePathRequest.cs`
- `src/Trailblazer/Pathing/Volume/VolumeSurveyor.cs`
- `src/Trailblazer/Pathing/Hybrid/HybridRoutePlanner.cs`

Useful tests:

- `tests/Trailblazer.Tests/Pathing/AerialSurveyor.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/AStarTransitionFallback.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/FlowFieldTransitionFallback.Tests.cs`
- `tests/Trailblazer.Tests/Navigation/Steering/NavSteering.Tests.cs`

High-risk changes include:

- endpoint relaxation rules
- direct-path validation in raw volume
- mode matching versus base traversability
- reset behavior for global rules

## 12. Where To Read Next

- `PATHING.MD` for the request-level pathing model
- `TRANSITIONS.MD` for authored handoffs into and out of raw volume
- `CHARTS.MD` for the surface-side world model
- `PATHGUIDES.MD` for `VolumeGuide` and guide-factory behavior
