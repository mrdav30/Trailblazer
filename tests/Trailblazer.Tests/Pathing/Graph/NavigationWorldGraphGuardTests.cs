using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class NavigationWorldGraphGuardTests
{
    private static readonly NavigationCell SolidCell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        Fixed64.Zero,
        Fixed64.One);

    [Fact]
    public void EmptyGraph_ShouldFailClosedForMissingDurableAddresses()
    {
        NavigationWorldGraph graph = NavigationWorldGraph.Empty;
        var missing = new NavigationCellAddress("missing", new VoxelIndex(3, 2, 1));

        graph.TryGetSeamPrism(missing, out GridCellPrism prism).Should().BeFalse();
        prism.Should().Be(default(GridCellPrism));
        graph.TryGetMediumStateRef(missing, TraversalMedium.Solid, out NavigationMediumStateRef state)
            .Should().BeFalse();
        state.Should().Be(default(NavigationMediumStateRef));
        graph.TryGetStructuralMediumStateRef(
                missing,
                TraversalMedium.Solid,
                out NavigationMediumStateRef structuralState)
            .Should().BeFalse();
        structuralState.Should().Be(default(NavigationMediumStateRef));
        graph.TryGetCoveredAddressGeneration(
                configurationOrdinal: 0,
                out string mapId,
                out GridCoveredAddressGeneration ordinalGeneration)
            .Should().BeFalse();
        mapId.Should().BeEmpty();
        ordinalGeneration.Should().Be(default(GridCoveredAddressGeneration));
        graph.TryGetCoveredAddressGeneration(
                "missing",
                out GridCoveredAddressGeneration mapGeneration)
            .Should().BeFalse();
        mapGeneration.Should().Be(default(GridCoveredAddressGeneration));
    }

    [Fact]
    public void CellDiagnostics_ShouldMirrorExactObstacleBlockageLifecycle()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(configuration, out ushort gridIndex).Should().BeTrue();
        VoxelGrid grid = context.World.ActiveGrids[gridIndex];
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, SolidCell)
            .Build();
        var commit = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: 1);
        context.Pathing.Admit(commit).Should().BeTrue();
        SimulateUntilTerminal(context, commit.Receipt);
        grid.TryGetVoxel(default(VoxelIndex), out Voxel? voxel).Should().BeTrue();
        var obstacle = context.World.AllocateObstacleToken();

        grid.TryAddObstacle(voxel!, obstacle).Should().BeTrue();
        context.Simulate();

        NavigationGraphCellDiagnostic blocked =
            context.Pathing.GetNavigationGraphDiagnostics().Maps[0].Cells[0];
        blocked.IsPresent.Should().BeTrue();
        blocked.ObstacleCount.Should().Be(1);
        blocked.IsBlocked.Should().BeTrue();

        grid.TryRemoveObstacle(voxel!, obstacle).Should().BeTrue();
        context.Simulate();

        NavigationGraphCellDiagnostic clear =
            context.Pathing.GetNavigationGraphDiagnostics().Maps[0].Cells[0];
        clear.IsPresent.Should().BeTrue();
        clear.ObstacleCount.Should().Be(0);
        clear.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void NeighborLookup_ShouldRejectTheExactDirectionCountBoundary()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var commit = new NavigationMapCommitOperation(
            new PreparedNavigationMap(
                new NavigationMapBuilder("map", binding)
                    .AddCell(default, SolidCell)
                    .Build(),
                bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: 1);
        context.Pathing.Admit(commit).Should().BeTrue();
        SimulateUntilTerminal(context, commit.Receipt);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetNodeRef(
                new NavigationCellAddress("map", default),
                out NavigationNodeRef source)
            .Should().BeTrue();

        int completeCount = lease.Graph.GetCompleteDirectionCount(source);
        lease.Graph.TryGetCompleteNeighbor(
                source,
                completeCount,
                out NavigationNodeRef complete,
                out bool isPrimary)
            .Should().BeFalse();
        complete.Should().Be(default(NavigationNodeRef));
        isPrimary.Should().BeFalse();
        int primaryCount = lease.Graph.GetPrimaryDirectionCount(source);
        lease.Graph.TryGetPrimaryNeighbor(source, primaryCount, out NavigationNodeRef primary)
            .Should().BeFalse();
        primary.Should().Be(default(NavigationNodeRef));
    }

    [Fact]
    public void Diagnostics_ShouldTruncateAtTheConfiguredAddressCeiling()
    {
        TrailblazerWorldContextSettings settings = CreateSettings(maxBaselineAddresses: 1);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: settings);
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, SolidCell)
            .AddCell(new VoxelIndex(1, 0, 0), SolidCell)
            .Build();
        var commit = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: 1);
        context.Pathing.Admit(commit).Should().BeTrue();
        SimulateUntilTerminal(context, commit.Receipt);

        NavigationGraphDiagnosticsSnapshot diagnostics =
            context.Pathing.GetNavigationGraphDiagnostics();

        diagnostics.IsTruncated.Should().BeTrue();
        diagnostics.Maps.Should().ContainSingle();
        diagnostics.Maps[0].Cells.Should().ContainSingle();
        diagnostics.Maps[0].Cells[0].Index.Should().Be(default(VoxelIndex),
            "the bounded prefix remains in canonical address order");
    }

    [Fact]
    public void GraphRuntimeDispose_ShouldBeIdempotent()
    {
        using var world = new GridWorld();
        var runtime = new NavigationGraphRuntime(
            world,
            TrailblazerWorldContextSettings.Default);
        runtime.Dispose();

        System.Action disposeAgain = runtime.Dispose;

        disposeAgain.Should().NotThrow();
    }

    [Fact]
    public void PendingMapCommit_ShouldIncludeExactOperationPagesInDiagnostics()
    {
        using var world = new GridWorld();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, SolidCell)
            .AddCell(new VoxelIndex(1, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(2, 0, 0), SolidCell)
            .Build();
        using var runtime = new NavigationGraphRuntime(
            world,
            CreateSettings(maxOverlaySlots: 1));
        var commit = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: 1);
        runtime.Admit(commit).Should().BeTrue();

        runtime.Maintain(frame: 1);

        commit.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        runtime.RetainedOperationWorkPageCount.Should().BePositive();
        NavigationGraphDiagnosticsSnapshot pending = runtime.GetDiagnostics(maximumCells: 0);
        int expectedPages = checked(
            runtime.Current.PersistentPageCount
            + pending.BaselineRebuildPageCount
            + runtime.RetainedCompositionWorkPageCount
            + runtime.RetainedOperationWorkPageCount
            + pending.PendingAreaPolicyCount);
        pending.PersistentGraphPageCount.Should().Be(expectedPages);

        for (int frame = 2;
             frame < 64 && commit.Receipt.Status == NavigationOperationStatus.Pending;
             frame++)
        {
            runtime.Maintain(frame);
        }

        commit.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        runtime.RetainedOperationWorkPageCount.Should().Be(0);
    }

    private static void SimulateUntilTerminal(
        TrailblazerWorldContext context,
        NavigationOperationReceipt receipt)
    {
        for (int frame = 0;
             frame < 64 && receipt.Status == NavigationOperationStatus.Pending;
             frame++)
        {
            context.Simulate();
        }
        receipt.Status.Should().Be(NavigationOperationStatus.Applied);
    }

    private static TrailblazerWorldContextSettings CreateSettings(
        int? maxBaselineAddresses = null,
        int? maxOverlaySlots = null)
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        MaintenanceWorkBudget source = defaults.MaintenanceBudget;
        var budget = new MaintenanceWorkBudget(
            source.MaxConsumedEnvelopes,
            maxBaselineAddresses ?? source.MaxBaselineAddresses,
            maxOverlaySlots ?? source.MaxOverlaySlots,
            source.MaxComponentNodes,
            source.MaxSeamCandidateProbes,
            source.MaxExplicitEdges,
            source.MaxDependencyEntries,
            source.MaxSurfaceComponentEdges);
        return new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            budget,
            defaults.GuideSampleBudget,
            defaults.MovementGroupPadding,
            defaults.MaxIngressEntries,
            defaults.MaxIngressBytes,
            defaults.MaxActiveSnapshots,
            defaults.MaxActiveSnapshotBytes,
            defaults.MaxRetiredSnapshots,
            defaults.MaxRetiredSnapshotBytes,
            defaults.MaxPersistentGraphPages,
            defaults.MaxDynamicCellSlotsPerMap,
            defaults.MaxDynamicCellSlots,
            defaults.NavigationAreaCount,
            defaults.MaxAreaPolicies,
            defaults.MaxAreaRulesPerPolicy,
            defaults.MaxAreaRules,
            defaults.MaxConcurrentSnapshotLeases,
            defaults.QueryLimits);
    }
}
