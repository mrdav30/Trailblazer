# Volume Traversal Reference

This document explains Trailblazer's raw-volume traversal model, authored `VolumePartition` state, and the role of `VolumeTraversalRules` as a supplemental volume-membership layer.

Use this file when you need to understand:

- why `VolumePathRequest` can work without surface partitions
- how authored volume cells and host rules work together
- how raw-volume endpoint resolution differs from chart-backed endpoint resolution
- how volume traversal interacts with authored transitions

If you need the broader request model first, read `PATHING.MD`.
If you need authored handoff registration, pair this with `TRANSITIONS.MD`.

Relevant code:

- `src/Trailblazer/Pathing/Support/Partition/VolumePartition.cs`
- `src/Trailblazer/Pathing/Support/VolumeTraversalRules.cs`
- `src/Trailblazer/Pathing/Support/VolumeTraversalMode.cs`
- `src/Trailblazer/Pathing/VoxelFinder/RawVoxelFinder.cs`
- `src/Trailblazer/Pathing/Volume/VolumePathRequest.cs`
- `src/Trailblazer/Pathing/Volume/VolumeSurveyor.cs`
- `src/Trailblazer/Pathing/Volume/VolumeGuide.cs`

## 1. Why Volume Traversal Matters

Surface pathing is great at answering:

- where stable ground or other intentional structure exists
- how chart-backed units should move across that structure

Raw volume traversal covers the other side of the world model:

- open 3D travel
- swimming through authored or host-defined water volume
- chartless detours around blocked voxels
- spaces where the important rule is "stay inside this medium" instead of "stay on this surface"

That is the job of Trailblazer's volume traversal flow.

## 2. Core Model

Volume traversal is built from four pieces:

- authored `VolumePartition` state
- `VolumeTraversalMode`
- `VolumeTraversalRules`
- raw-volume resolution through `RawVoxelFinder` and `VolumeSurveyor`

Current volume modes are:

- `Open`
- `Water`

The key mental model is:

- charts author which voxels contribute surface traversal, volume traversal, or both
- `VolumePartition` says which authored raw voxels belong to a supported traversal medium
- `VolumeTraversalRules` can supplement authored membership with host-specific logic
- raw-volume pathing still respects blocked space and unit clearance

## 3. `VolumeTraversalRules`

`VolumeTraversalRules` is the host-facing supplemental configuration point for constrained raw-volume traversal.

Its public surface is:

- `HasWaterVoxelRule`
- `SetWaterVoxelPartition<TPartition>()`
- `SetWaterVoxelRule(VoxelRule rule)`
- `ClearWaterVoxelRule()`

### 3.1 What The Rules Actually Mean

The rules do not replace normal traversability checks, and they are not the only source of volume membership.

They are an additional layer on top of:

- voxel existence
- blocked state
- partition or raw-neighbor clearance for the requested `UnitSize`
- any authored `VolumePartition` state already attached to the voxel

In raw-volume code, a voxel must be both:

- traversable
- compatible with the requested `VolumeTraversalMode`

Trailblazer can answer "is this voxel authored open volume or authored water volume?" directly from chart initialization, while still letting hosts layer on engine-specific water logic when needed.

### 3.2 Open Versus Water

`VolumeTraversalMode.Open`:

- is always considered configured
- does not require a host rule
- matches authored open-volume voxels when any authored open-volume space exists
- otherwise falls back to the legacy permissive open-volume behavior for chartless worlds
- still respects blockers and clearance

`VolumeTraversalMode.Water`:

- is configured when authored water volume exists or a host water rule exists
- matches authored water voxels directly
- may also match additional host-defined water voxels through `VolumeTraversalRules`

### 3.3 Two Ways To Configure Water

Use `SetWaterVoxelPartition<TPartition>()` when:

- your host already marks extra water voxels with a partition type

Use `SetWaterVoxelRule(...)` when:

- water membership depends on custom logic beyond authored chart data

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

Important lifecycle rules:

- `PathManager.Reset()` clears the installed water rule
- `PathManager.Reset()` also clears authored `VolumePartition` state by unloading charts

That means tests, world reloads, and runtime bootstrap code must reapply host rules when needed.

## 4. Raw-Volume Endpoint Resolution

Chart-backed requests use `VoxelFinder`.
Volume requests use `RawVoxelFinder`.

That is a very important difference.

`RawVoxelFinder` does not require `PathPartition` ownership.

Instead it resolves endpoints against raw voxels and checks:

- blocked state
- unit clearance
- traversal-mode match
- authored `VolumePartition` state when present

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

If a `VolumePartition` is present, volume traversal honors its authored membership and its clearance logic.
If only a `PathPartition` is present, volume traversal still honors that partition's clearance logic.
If neither partition is present, raw-volume clearance falls back to neighbor-space checks.

This is important in mixed worlds:

- volume traversal does not ignore the rest of the voxel world
- it simply stops requiring surface-partition ownership

## 6. `VolumePathRequest` In Practice

Use `VolumePathRequest` when:

- there is no meaningful surface route for the space
- movement should stay inside a constrained or authored volume such as water
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

- water-mode creation succeeds when authored water volume exists or a host water rule exists
- open-mode creation does not need extra setup

## 7. Why Complex Worlds Depend On This

Volume traversal is what keeps Trailblazer from forcing every traversal problem into surface routes.

It matters in worlds with:

- aircraft or free-flight spaces
- lakes, rivers, or flooded interiors
- blocked straight-line corridors that still have chartless 3D detours
- shoreline or docking areas where surface and medium traversal meet

Without authored or host-defined volume membership, you usually end up with one of two bad outcomes:

- water is treated like generic open space
- water travel is impossible because nothing tells the library what counts as water

## 8. Relationship To Transitions

`VolumeTraversalRules` and `TraversalTransitionRegistry` solve different problems, but they work together.

`TraversalTransitionRegistry` answers:

- where a route may hand off into or out of raw volume

Volume traversal answers:

- which voxels are actually valid for that volume segment

For example:

- `SwimEntry` and `SwimExit` transitions depend on `VolumeTraversalMode.Water`
- those transitions are only useful when the intended volume voxels resolve as valid water
- `Takeoff` and `Landing` can use `VolumeTraversalMode.Open`

Important nuance:

- a valid transition does not make the middle volume segment valid
- the volume segment still has to resolve through `RawVoxelFinder` and `VolumeSurveyor`

## 9. Usage Patterns

### 9.1 Authored Water Volume

If your chart authoring already produces `W` or `W!` cells, raw water traversal can work without any host rule.

### 9.2 Supplemental Host Water Logic

Use `VolumeTraversalRules` when your world has additional water voxels that are not authored through `NavigationChart`.

### 9.3 Open-Volume Detour

Use `VolumeTraversalMode.Open` when movement should stay chart-optional and 3D, but still avoid blocked voxels.

When authored open-volume cells exist, `Open` now respects that authored space instead of treating every non-blocked voxel as valid.

## 10. Common Gotchas

- Charts can author water or open volume for you.
- `VolumeTraversalMode.Open` still respects blockers and clearance.
- `VolumeTraversalMode.Water` still fails until valid water membership exists.
- `VolumeTraversalRules` is global static state.
- `PathManager.Reset()` clears both host water rules and authored volume partitions.
- Volume traversal does not require charts, but it still depends on real voxels existing.
- `allowUnwalkableEndNode` only relaxes endpoints; it does not make the whole route ignore blocked voxels.

## 11. AI And Contributor Notes

If you are changing volume traversal behavior, inspect these files together:

- `src/Trailblazer/Pathing/Support/Partition/VolumePartition.cs`
- `src/Trailblazer/Pathing/Support/VolumeTraversalRules.cs`
- `src/Trailblazer/Pathing/VoxelFinder/RawVoxelFinder.cs`
- `src/Trailblazer/Pathing/Volume/VolumePathRequest.cs`
- `src/Trailblazer/Pathing/Volume/VolumeSurveyor.cs`
- `src/Trailblazer/Pathing/Hybrid/HybridRoutePlanner.cs`

Useful tests:

- `tests/Trailblazer.Tests/Pathing/TraversalAuthoringMapTests.cs`
- `tests/Trailblazer.Tests/Pathing/PathingNavigationMap.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/AerialSurveyor.Tests.cs`
- `tests/Trailblazer.Tests/Navigation/Steering/NavSteering.Tests.cs`

High-risk changes include:

- endpoint relaxation rules
- direct-path validation in raw volume
- mode matching versus base traversability
- cache invalidation for authored volume topology
- reset behavior for global rules

## 12. Where To Read Next

- `PATHING.MD` for the request-level pathing model
- `TRANSITIONS.MD` for authored handoffs into and out of raw volume
- `CHARTS.MD` for the authored chart model
- `PATHGUIDES.MD` for `VolumeGuide` and guide-factory behavior
