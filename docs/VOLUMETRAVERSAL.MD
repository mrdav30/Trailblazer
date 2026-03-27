# Volume Traversal Reference

This document explains Trailblazer's raw-volume traversal model, authored `VolumeChartPartition` state, and the role of `VolumeMediumRules` as a supplemental volume-membership layer.

Use this file when you need to understand:

- why `VolumePathRequest` can work without surface partitions
- how authored volume cells and host rules work together
- how raw-volume endpoint resolution differs from chart-backed endpoint resolution
- how volume traversal interacts with authored transitions

If you need the broader request model first, read `PATHING.MD`.
If you need authored handoff registration, pair this with `TRANSITIONS.MD`.

Relevant code:

- `src/Trailblazer/Pathing/Support/Partition/VolumeChartPartition.cs`
- `src/Trailblazer/Pathing/Support/Volume/VolumeMediumRules.cs`
- `src/Trailblazer/Navigation/Motor/Support/TraversalMedium.cs`
- `src/Trailblazer/Pathing/Support/SolidVoxelFinder/VolumeVoxelFinder.cs`
- `src/Trailblazer/Pathing/Search/Volume/VolumePathRequest.cs`
- `src/Trailblazer/Pathing/Search/Volume/VolumeSurveyor.cs`
- `src/Trailblazer/Pathing/Search/Volume/VolumeGuide.cs`

## 1. Why Volume Traversal Matters

Surface pathing is great at answering:

- where stable ground or other intentional structure exists
- how chart-backed units should move across that structure

Raw volume traversal covers the other side of the world model:

- free-flight through authored or host-defined gas volume
- swimming through authored or host-defined liquid volume
- detours through authored or explicitly configured 3D volume around blocked voxels
- spaces where the important rule is "stay inside this medium" instead of "stay on this surface"

That is the job of Trailblazer's volume traversal flow.

## 2. Core Model

Volume traversal is built from four pieces:

- authored `VolumeChartPartition` state
- `TraversalMedium`
- `VolumeMediumRules`
- raw-volume resolution through `VolumeVoxelFinder` and `VolumeSurveyor`

Current supported volume media are:

- `Gas`
- `Liquid`

The key mental model is:

- charts author which voxels contribute solid traversal, volume traversal, or both
- `VolumeChartPartition` says which authored raw voxels belong to a supported traversal medium
- `VolumeMediumRules` can supplement authored membership with host-specific logic on voxels that already belong to Trailblazer's runtime traversal world
- raw-volume pathing still respects blocked space and unit clearance

## 3. `VolumeMediumRules`

`VolumeMediumRules` is the host-facing supplemental configuration point for constrained raw-volume traversal.

Its public surface is:

- `HasGasVoxelRule`
- `SetGasVoxelPartition<TPartition>()`
- `SetGasVoxelRule(VoxelRule rule)`
- `ClearGasVoxelRule()`
- `HasLiquidVoxelRule`
- `SetLiquidVoxelPartition<TPartition>()`
- `SetLiquidVoxelRule(VoxelRule rule)`
- `ClearLiquidVoxelRule()`

### 3.1 What The Rules Actually Mean

The rules do not replace normal traversability checks, and they are not the only source of volume membership.

They are an additional layer on top of:

- voxel existence
- blocked state
- partition-backed clearance for the requested `UnitSize`
- any authored `VolumeChartPartition` state already attached to the voxel

The current rule APIs are extend-only:

- they can add gas or liquid membership
- they can opt `SolidChartPartition`-only voxels into a volume medium
- they do not suppress authored `VolumeChartPartition` membership
- removing a host rule returns a voxel to its authored/base state

Host rules do not make fully unpartitioned voxels valid.

In raw-volume code, a voxel must be both:

- traversable
- compatible with the requested `TraversalMedium`

Trailblazer can answer "is this voxel authored gas volume or authored liquid volume?" directly from chart initialization, while still letting hosts layer on engine-specific gas or liquid logic when needed.

### 3.2 Gas Versus Liquid

`TraversalMedium.Gas`:

- is configured when authored gas volume exists or a host gas rule exists
- matches authored gas-volume voxels directly
- may also match host-defined gas-volume voxels through `VolumeMediumRules`
- remains valid on authored gas-volume voxels even if the host gas rule is later removed
- never falls back to generic unauthored space
- still respects blockers and clearance

`TraversalMedium.Liquid`:

- is configured when authored liquid volume exists or a host liquid rule exists
- matches authored liquid voxels directly
- may also match additional host-defined liquid voxels through `VolumeMediumRules`
- remains valid on authored liquid voxels even if the host liquid rule is later removed
- still requires the voxel to belong to Trailblazer's runtime traversal world

### 3.3 Host Rule Configuration

Use `SetGasVoxelPartition<TPartition>()` or `SetLiquidVoxelPartition<TPartition>()` when:

- your host already marks medium membership with a partition type

Use `SetGasVoxelRule(...)` or `SetLiquidVoxelRule(...)` when:

- medium membership depends on custom logic beyond authored chart data
- you want to supplement authored media instead of replacing it

Examples:

```csharp
VolumeMediumRules.SetGasVoxelPartition<MyGasVolumePartition>();
VolumeMediumRules.SetLiquidVoxelPartition<MyLiquidVolumePartition>();
```

```csharp
VolumeMediumRules.SetGasVoxelRule(voxel =>
    voxel != null
    && voxel.HasPartition<MyGasVolumePartition>());

VolumeMediumRules.SetLiquidVoxelRule(voxel =>
    voxel != null
    && voxel.HasPartition<MyLiquidVolumePartition>());
```

### 3.4 Global Lifetime

`VolumeMediumRules` is static and global.

Important lifecycle rules:

- `PathManager.Reset()` clears the installed gas and liquid rules
- `PathManager.Reset()` also clears authored `VolumeChartPartition` state by unloading charts
- changing a host rule invalidates cached volume guides
- unloading a chart invalidates only cached volume guides whose `ChartsUtilized` includes that chart
- overlapping chart initialization invalidates cached volume guides for the already-active chart owners touched by the change

That means tests, world reloads, and runtime bootstrap code must reapply host rules when needed.

## 4. Raw-Volume Endpoint Resolution

Chart-backed requests use `SolidVoxelFinder`.
Volume requests use `VolumeVoxelFinder`.

That is a very important difference.

`VolumeVoxelFinder` does not require a surface chart route, but it does require compatible Trailblazer partitioned space.

Instead it resolves endpoints against raw voxels and checks:

- blocked state
- unit clearance
- traversal-medium match
- authored `VolumeChartPartition` state when present
- `SolidChartPartition` or `VolumeChartPartition` presence before raw-volume traversal is even considered

### 4.1 Endpoint Resolution Order

For a volume request, endpoint resolution is:

1. try the direct voxel
2. if endpoint relaxation is active, try a nearby traversable raw-volume neighbor
3. if endpoint relaxation is active, trace toward the other endpoint and take the first compatible voxel found

Important nuance:

- if the requested medium is not configured, endpoint resolution fails immediately
- snapped fallback voxels still have to match the requested `TraversalMedium` through `VolumeMediumRules`
- cached volume results record the chart owners used along the resolved raw path so local chart changes can invalidate only affected results

### 4.2 `allowUnwalkableEndpoints` In Volume Requests

`allowUnwalkableEndpoints` in volume requests is still only an endpoint policy.

It can relax start or end acceptance, and it can enable endpoint snapping after direct-voxel failure, but it does not make the entire corridor ignore blockers.

Volume requests also keep a size-based endpoint fallback path:

- if the exact voxel matches the requested medium but fails only because required clearance is too large for that cell, endpoint snapping may still run even when `allowUnwalkableEndpoints` is false

For direct-path validation, relaxed endpoints still have to:

- match the requested `TraversalMedium`

Interior voxels still have to be truly traversable.

## 5. Raw-Volume Traversability

`VolumeVoxelFinder.IsTraversable(...)` combines:

- base raw-voxel traversability
- volume-medium filtering

Base traversability means:

- the voxel exists
- it is not blocked
- it has enough clearance for the requested `UnitSize`

If a `VolumeChartPartition` is present, volume traversal honors its authored membership and its clearance logic.
If only a `SolidChartPartition` is present, volume traversal still honors that partition's clearance logic.
If neither partition is present, the voxel is rejected immediately.

This is important in mixed worlds:

- volume traversal does not ignore the rest of the voxel world
- it simply stops requiring a chart-backed route across the entire segment

## 6. `VolumePathRequest` In Practice

Use `VolumePathRequest` when:

- there is no meaningful surface route for the space
- movement should stay inside a constrained or authored volume such as liquid
- gas-volume travel should detour around blockers using raw voxels

Examples:

```csharp
var request = VolumePathRequest.Create(
    origin,
    destination,
    Fixed64.One,
    medium: TraversalMedium.Gas);
```

```csharp
var request = VolumePathRequest.Create(
    origin,
    destination,
    Fixed64.One,
    medium: TraversalMedium.Liquid);
```

Important nuance:

- liquid request creation succeeds when authored liquid volume exists or a host liquid rule exists
- gas request creation succeeds only when authored gas volume exists or a host gas rule exists

## 7. Why Complex Worlds Depend On This

Volume traversal is what keeps Trailblazer from forcing every traversal problem into surface routes.

It matters in worlds with:

- aircraft or free-flight spaces
- lakes, rivers, or flooded interiors
- blocked straight-line corridors that still have authored or host-configured 3D detours
- shoreline or docking areas where surface and medium traversal meet

Without authored or host-defined volume membership, you usually end up with one of two bad outcomes:

- volume travel is impossible because nothing tells the library what space counts as valid medium
- callers accidentally assume generic empty space is traversable when it is not part of Trailblazer's authored/runtime model

## 8. Relationship To Transitions

`VolumeMediumRules` and `TraversalTransitionRegistry` solve different problems, but they work together.

`TraversalTransitionRegistry` answers:

- where a route may hand off into or out of raw volume

Volume traversal answers:

- which voxels are actually valid for that volume segment

For example:

- `SwimEntry` and `SwimExit` transitions depend on `TraversalMedium.Liquid`
- those transitions are only useful when the intended volume voxels resolve as valid liquid traversal
- `Takeoff` and `Landing` can use `TraversalMedium.Gas`

Important nuance:

- a valid transition does not make the middle volume segment valid
- the volume segment still has to resolve through `VolumeVoxelFinder` and `VolumeSurveyor`

## 9. Usage Patterns

### 9.1 Authored Liquid Volume

If your chart authoring already produces `L` or `L!` cells, raw liquid traversal can work without any host rule.

### 9.2 Supplemental Host Volume Logic

Use `VolumeMediumRules` when your world has additional gas or liquid membership that is not authored through `NavigationChart`.

### 9.3 Gas-Volume Detour

Use `TraversalMedium.Gas` when movement should stay chart-optional and 3D, but still avoid blocked voxels inside authored or explicitly configured gas volume.

## 10. Common Gotchas

- Charts can author liquid or gas volume for you.
- `TraversalMedium.Gas` and `TraversalMedium.Liquid` still respect blockers and clearance.
- `TraversalMedium.Gas` and `TraversalMedium.Liquid` fail until valid authored or host-configured gas or liquid membership exists.
- `VolumeMediumRules` is global static state.
- `PathManager.Reset()` clears both host volume rules and authored volume partitions.
- Volume traversal does not require charts, but it still depends on real voxels existing.
- `allowUnwalkableEndpoints` only relaxes endpoints; it does not make the whole route ignore blocked voxels.

## 11. AI And Contributor Notes

If you are changing volume traversal behavior, inspect these files together:

- `src/Trailblazer/Pathing/Support/Partition/VolumeChartPartition.cs`
- `src/Trailblazer/Pathing/Support/Volume/VolumeMediumRules.cs`
- `src/Trailblazer/Pathing/Support/SolidVoxelFinder/VolumeVoxelFinder.cs`
- `src/Trailblazer/Pathing/Search/Volume/VolumePathRequest.cs`
- `src/Trailblazer/Pathing/Search/Volume/VolumeSurveyor.cs`
- `src/Trailblazer/Pathing/Search/Hybrid/HybridRoutePlanner.cs`

Useful tests:

- `tests/Trailblazer.Tests/Pathing/TraversalAuthoringMapTests.cs`
- `tests/Trailblazer.Tests/Pathing/PathingNavigationMap.Tests.cs`
- `tests/Trailblazer.Tests/Pathing/AerialSurveyor.Tests.cs`
- `tests/Trailblazer.Tests/Navigation/Steering/NavSteering.Tests.cs`

High-risk changes include:

- endpoint relaxation rules
- direct-path validation in raw volume
- medium matching versus base traversability
- cache invalidation for authored volume topology
- reset behavior for global rules

## 12. Where To Read Next

- `PATHING.MD` for the request-level pathing model
- `TRANSITIONS.MD` for authored handoffs into and out of raw volume
- `CHARTS.MD` for the authored chart model
- `PATHGUIDES.MD` for `VolumeGuide` and guide-factory behavior
