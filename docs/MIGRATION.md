# Trailblazer Migration Guide

## Migrating From Trailblazer v1.x To v2.x

Trailblazer v2 replaces the mutable, ambient v1 pathing model with one explicit,
context-owned navigation graph. The cutover is intentionally breaking:
Trailblazer does not ship compatibility adapters, forwarding overloads, or
fallback readers for retired request records.

Use this guide when upgrading from any v1.x package.

### v2 Upgrade Checklist

- Update package references to `Trailblazer` v2.x, or `Trailblazer.Lean` v2.x
  when the rest of the LSF dependency stack uses Lean packages.
- Replace ambient pathing ownership with one `TrailblazerWorldContext` per
  active GridForge `GridWorld`.
- Replace mutable charts and world-position authoring with immutable
  `NavigationMap` values bound to normalized GridForge configurations.
- Publish maps, overlays, removals, and exact area-policy revisions through
  `context.Pathing`, then advance publication with `context.Simulate()`.
- Replace every legacy request family with one complete `PathQuery`.
- Replace old guide factories and pooled guide interfaces with disposable A* or
  Flow leases from `context.Guides`.
- Treat every transition as an explicit host-owned action and complete the
  exact `NavigationTransitionInstruction` supplied by its lease.
- Restore grids, maps, policies, and overlays before populating guided
  Navigators.
- Re-record deterministic replays and serialized fixtures that depended on v1
  request, guide, map, or controller schemas.
- Run both `Release` and `ReleaseLean` validation after the source migration
  compiles.

## Required Host Changes

1. Create or attach one `TrailblazerWorldContext` for each active `GridWorld`.
2. Build one immutable `NavigationMap` per normalized GridForge configuration.
3. Publish maps and exact `NavigationAreaPolicy` revisions through
   `context.Pathing.Admit(...)`.
4. Represent runtime mining, media, connection, and explicit-action changes as
   addressed `NavigationOverlayTransaction` values.
5. Persist and replay those overlay operations before restoring guided
   Navigators.
6. Replace every old request with one immutable `PathQuery` carrying explicit
   agent geometry, start medium, target media, policy, algorithm, work budget,
   and transition permission.
7. Consume `NavigationGuideLease` or `NavigationFlowFieldLease` and explicitly
   complete every selected `NavigationTransitionInstruction`.

## Removed And Replaced

| Removed model | Current model |
| --- | --- |
| `NavigationChart`, its cell/update/registration types, overlap priority, and diagnostic extensions | immutable `NavigationMap`, complete `NavigationCell`, addressed overlays, and `GetNavigationGraphDiagnostics()` |
| World-position, scalar interval, and dense `[y,x,z]` authoring APIs | stable map ID plus topology-local GridForge `VoxelIndex` under a normalized configuration binding |
| Ambient `PathManager`, `PathingWorldState`, and thread-local state swapping | explicit `TrailblazerWorldContext.Pathing` ownership |
| `SolidChartPartition` and `VolumeChartPartition` | one medium-state graph keyed by address plus exact `TraversalMedium` |
| `TrailblazerGridCompatibility` and external grid-bridge requests | direct committed-change integration from the context-bound `GridWorld` |
| `TrailblazerWorldContext.VoxelSize` | per-grid GridForge metrics and explicit world-unit controller settings |
| Scalar agent size, mutable foot adjustment, and request unit size | required `KinematicBodyShape` inside `NavigationAgentProfile` |
| `MaxPathSearchRange`, heuristic choice, and straight/diagonal constants | finite multi-counter `NavigationWorkBudget` and certified fixed-point geometry |
| Old endpoint finder families and public endpoint-policy helpers | `NavigationEndpoint`, `EndpointResolutionPolicy`, and bounded graph admission |
| Mutable `AStarPathRequest`, `FlowFieldPathRequest`, `VolumePathRequest`, and `HybridPathRequest` families | one immutable `PathQuery` |
| Separate volume routing, generated transition registry, and runtime medium rules | `NavigationCell.Media`, explicit definitions/rules, and unified A*/Flow traversal |
| Old guide factories, pooled guide interfaces, and request cache keys | context-owned `TrailblazerGuideService` and immutable dependency-stamped payload leases |
| Old serialized request/session fields | required current schemas; incompatible records reject transactionally |

## Map Identity And Overlays

A map ID is stable host identity, but one map also binds exactly one normalized
GridForge configuration. Addresses are `NavigationCellAddress` values containing
that map ID and a topology-local `VoxelIndex`; they are not world-space or
array-coordinate aliases.

The map bake is immutable baseline truth. Runtime changes use one atomic
`NavigationOverlayCommitOperation` containing addressed map deltas. Each cell
operation carries a complete semantic payload or an explicit suppress/revert
decision. Connections and explicit transition definitions have equivalent
upsert/suppress/revert operations.

When replacing a bake, `PreserveAndRevalidate` keeps the exact overlay and
dynamic-address set and rejects the replacement if the recomposed candidate is
invalid. `Clear` discards that runtime set atomically.

## Query And Guide Cutover

The host must now provide exact physical intent. `TraversalIntent.StartMedium`
is one of Solid, Gas, or Liquid; `TargetMedia` is a nonempty subset of the
profile's allowed media. Disabling transitions excludes every semantic action,
including same-medium Jump or Climb.

Guide payloads are immutable, while cursor and pending-action state belong to
one lease. A transition instruction cannot be reconstructed or completed
through another acquisition. Move to its source action position, perform the
host action once, retry transient completion without repeating the action, and
reacquire if publication returns `Stale`.

## Serialization Cutover

`PathQueryRecord` round-trips an exact standalone query. Navigator persistence
stores durable session intent and rebuilds start position/medium from the
restored controller shell. Neither record embeds maps, policies, overlays,
graph payloads, guide cursors, dependencies, pending actions, or committed-cell
metadata.

Restore GridForge grids first, then publish maps and policies, replay overlays
in deterministic order, and only then populate guided Navigators. Old field
names and schema versions are rejected; there is no fallback reader.

## Final Public Name Cleanup

The same major version corrects misleading motion names and removes misspellings
and redundant controller accessors:

| Removed name | Current name or access |
| --- | --- |
| `Navigator.IsGuideded` | `Navigator.IsGuided` |
| `Navigator.AddVelocityDelta(...)` | `Navigator.AddLocomotionDisplacement(...)` |
| protected `Navigator._velocityDelta` | protected `Navigator._locomotionDisplacement` |
| `NavMotor.TryTraversal(...)` output `velocityDelta` | `locomotionDisplacement` |
| `NavMotor.TryTraversal(...)` output `positionDelta` | `platformDisplacement` |
| `NavMotor.TryTraversal(...)` output `rotationDelta` | `platformRotationDelta` |
| `PlatformLocomotion.InteriaApplied` | `PlatformLocomotion.InertiaApplied` |
| `WaterLocomotion.DefaultBouyancyFactor` | `WaterLocomotion.DefaultBuoyancyFactor` |
| public `TrailblazerWorldContext.Navigation` / `TrailblazerNavigationService` | no host-facing service; bind and drive `Navigator` directly |
| component `Context` getters | context binding remains constructor/Navigator owned |
| public NavMotor module aliases | `Handler.Move`, `Handler.Jump`, `Handler.Water`, `Handler.Fly`, and `Handler.Climb` |
| `LocomotionProfile.CreateBuilder(...)` | `new LocomotionProfileBuilder(...)` |

Update method calls, overrides, protected-field references, and named arguments
to the corrected motion names. These are naming-only changes: locomotion and
platform outputs remain displacements in world units, with the fixed timestep
already applied. Do not multiply them by time again. `TryTraversal(...)` keeps
its name and its existing finalization/abort lifecycle; no forwarding aliases
are provided.

The outer Navigator record is schema version 3. Earlier outer schemas reject
transactionally; they are not read through renamed-field fallbacks.

## Current v2 Guides

- [Getting started](wiki/GettingStarted.md)
- [Navigation maps](wiki/NavigationMaps.md)
- [Map authoring](wiki/MapAuthoring.md)
- [Map publication](wiki/MapPublication.md)
- [Pathing](wiki/Pathing.md)
- [Path guides](wiki/PathGuides.md)
- [Serialization](wiki/Serialization.md)
- [Troubleshooting](wiki/Troubleshooting.md)
