# Legacy API Deletion Checklist

This checklist turns the clean-break manifest in
`gridTopologyNavigationMapRefactorPlan.md` into a mechanical removal gate. All
items start unchecked because Phase 0 records the legacy surface; later phases
check an item only after its replacement is authoritative and every production
consumer has moved.

The branch does not ship obsolete wrappers, forwarding facades, compatibility
adapters, or dual authorities. For every checked item, the same change must:

1. prove the replacement with focused behavior tests;
2. show zero production references to the legacy symbol;
3. delete the implementation and its legacy-only tests/docs;
4. update `tests/Trailblazer.Tests/Phase0/PublicApiSnapshot.txt`; and
5. pass `PublicApiSnapshotTests` plus the full Release suite.

## Map And Topology Authority

- [ ] Delete `NavigationChart`, `NavigationChartCell`, and
  `NavigationChartCellUpdate`; replace them with immutable one-grid
  `NavigationMap` bakes, addressed cells, and overlay deltas.
- [ ] Delete `NavigationChartRegistration`, `ResolvedChartVoxelState`, and chart
  overlap/priority state; replace them with the context map registry and
  composed graph snapshots.
- [ ] Delete interval-based `TraversalAuthoringMap`, `TraversalBuildResult`,
  `TraversalLegend`, and old token results; replace them with topology-local
  map builders/importers.
- [ ] Delete `ChartOwnerUtility`, chart grid-bridge requests, and chart
  diagnostics/extensions; replace them with map-version dependencies and graph
  diagnostics.
- [ ] Delete `SolidChartPartition` and `VolumeChartPartition`; replace them with
  immutable bake data, compact semantic overlay pages, and GridForge physical
  state pages.
- [ ] Delete per-node 26-slot neighbor arrays; replace them with implicit native
  adjacency and compact explicit edges.
- [ ] Delete `TrailblazerGridCompatibility`; replace it with per-map binding
  admission and the dedicated GridForge navigation seam.
- [ ] Delete `TrailblazerWorldContext.VoxelSize`; replace it with per-grid
  metrics and explicit world-unit settings.

## Agent, Query, And Search Authority

- [ ] Delete `Navigator.Size`, `ISteer.Size`, optional/scalar setup size, and
  request `UnitSize`; replace them with required `KinematicBodyShape` and
  `NavigationAgentProfile` values.
- [ ] Delete `MaxPathSearchRange`; replace it with `NavigationWorkBudget`.
- [ ] Delete public `HeuristicMethod`, `AStarSurveyor.StraightCost`, and
  `AStarSurveyor.DiagonalCost`; replace them with the certified internal
  Euclidean-or-zero heuristic.
- [ ] Delete `DiagonalTraversalLegs`; replace it with topology-kernel witnesses.
- [ ] Delete `AlternativeVoxelFinder`, `SolidVoxelFinder`, and
  `VolumeVoxelFinder`; replace them with bounded map-node endpoint resolution
  and navigation rays.
- [ ] Delete `FlowFieldSamplingGrid` and cubic interpolation; replace them with
  exact-node selected-edge sampling and guide-local portal progression.
- [ ] Delete duplicated `VolumeSurveyor` traversal logic; replace it with the
  shared graph-search core.
- [ ] Delete the mutable public `AStarPathRequest`, `FlowFieldPathRequest`,
  `VolumePathRequest`, and `HybridPathRequest` hierarchy; replace it with
  immutable `PathQuery` plus an internal resolved query.
- [ ] Delete the old `PathRequestCacheKey` field model and `ChartsUtilized`;
  replace them with immutable payload keys and sorted graph dependency stamps.

## Service, Mutation, And Persistence Authority

- [ ] Delete runtime `VolumeMediumRules`, `TrailblazerVolumeRulesService`, and
  `VolumeMediumRulesState`; replace them with addressed semantic overlay cell
  operations.
- [ ] Delete public transition register/unregister/query mutation through
  `TrailblazerTransitionService`, `TraversalTransitionRegistry`, and
  `TraversalTransitionRegistryState`; replace it with transition overlay
  operations and read-only leased diagnostics.
- [ ] Delete the static ambient `PathManager` facade, `PathManager.EnterState`,
  and thread-static test helpers; replace them with explicit context services
  and fixtures.
- [ ] Delete current chart/request methods on `TrailblazerPathingService` and
  `PathRequestContextResolver`; replace them with per-grid map registry/query
  methods and snapshot leases.
- [ ] Delete chart fields from generated-transition and guided-volume records;
  replace them with stable map addresses and traversal intent.
- [ ] Delete old Navigator, NavSteering, and guided-volume serialized request
  shapes. Old saves are rejected; no compatibility reader is retained.

## Mechanical Commands

```powershell
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj --configuration Release --filter FullyQualifiedName~PublicApiSnapshotTests
dotnet test Trailblazer.slnx --configuration Release
```

The snapshot stores one line per exported type and fingerprints its declaration
and declared public members. A changed member count or signature changes that
type's hash, so removals cannot disappear into an unrelated refactor.
