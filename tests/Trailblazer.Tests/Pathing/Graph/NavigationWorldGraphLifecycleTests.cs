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
public sealed class NavigationWorldGraphLifecycleTests
{
    private static readonly NavigationCell SolidCell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        Fixed64.Zero,
        Fixed64.One);

    [Theory]
    [InlineData(GridTopologyKind.RectangularPrism, GridStorageKind.Dense)]
    [InlineData(GridTopologyKind.RectangularPrism, GridStorageKind.Sparse)]
    [InlineData(GridTopologyKind.HexPrism, GridStorageKind.Dense)]
    [InlineData(GridTopologyKind.HexPrism, GridStorageKind.Sparse)]
    public void MapBeforeGrid_ShouldRemainDormantThenMaterializeAllMatrixMembers(
        GridTopologyKind topology,
        GridStorageKind storage)
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = CreateConfiguration(topology, storage);
        NavigationMap map = CreateMap("map", configuration, default);

        AdmitMap(context, map, sequence: 1, bakeVersion: 1);
        context.Simulate();

        GetState(context, default).IsMaterialized.Should().BeFalse();
        AddGrid(context.World, configuration, storage, default);
        context.Simulate();

        NavigationGraphCellState state = GetState(context, default);
        state.IsMaterialized.Should().BeTrue();
        state.IsPresent.Should().BeTrue();
        state.HasCell.Should().BeTrue();
    }

    [Theory]
    [InlineData(GridTopologyKind.RectangularPrism, GridStorageKind.Dense)]
    [InlineData(GridTopologyKind.RectangularPrism, GridStorageKind.Sparse)]
    [InlineData(GridTopologyKind.HexPrism, GridStorageKind.Dense)]
    [InlineData(GridTopologyKind.HexPrism, GridStorageKind.Sparse)]
    public void GridBeforeMap_ShouldCaptureExactAddressBaseline(
        GridTopologyKind topology,
        GridStorageKind storage)
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = CreateConfiguration(topology, storage);
        AddGrid(context.World, configuration, storage, default);
        NavigationMap map = CreateMap("map", configuration, default);

        AdmitMap(context, map, sequence: 1, bakeVersion: 1);
        context.Simulate();

        GetState(context, default).IsPresent.Should().BeTrue();
    }

    [Fact]
    public void CommittedObstacleFinalState_ShouldMirrorWithoutVoxelRetention()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = CreateConfiguration(
            GridTopologyKind.RectangularPrism,
            GridStorageKind.Dense);
        VoxelGrid grid = AddGrid(context.World, configuration, GridStorageKind.Dense, default);
        AdmitMap(context, CreateMap("map", configuration, default), sequence: 1, bakeVersion: 1);
        context.Simulate();
        grid.TryGetVoxel(default(VoxelIndex), out Voxel? voxel).Should().BeTrue();
        var obstacle = context.World.AllocateObstacleToken();

        grid.TryAddObstacle(voxel!, obstacle).Should().BeTrue();
        context.Simulate();
        GetState(context, default).ObstacleCount.Should().Be(1);

        grid.TryRemoveObstacle(voxel!, obstacle).Should().BeTrue();
        context.Simulate();
        GetState(context, default).IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void DynamicSlot_ShouldNeverBeReusedAcrossRevertAndSparsePresenceChanges()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = CreateConfiguration(
            GridTopologyKind.RectangularPrism,
            GridStorageKind.Sparse);
        VoxelIndex baked = default;
        VoxelIndex dynamic = new(1, 0, 0);
        VoxelGrid grid = AddGrid(context.World, configuration, GridStorageKind.Sparse, baked);
        AdmitMap(context, CreateMap("map", configuration, baked), sequence: 1, bakeVersion: 1);
        context.Simulate();

        AdmitCellOverlay(context, NavigationCellOverlayOperation.Set(dynamic, SolidCell), sequence: 2);
        context.Simulate();
        NavigationGraphCellState before = GetState(context, dynamic);
        before.IsDynamic.Should().BeTrue();
        before.IsPresent.Should().BeFalse();

        grid.TryAddVoxel(dynamic, out _).Should().BeTrue();
        context.Simulate();
        GetState(context, dynamic).IsPresent.Should().BeTrue();

        grid.TryRemoveVoxel(dynamic).Should().BeTrue(
            "Trailblazer owns no voxel partition or subscription that can pin sparse storage");
        context.Simulate();
        GetState(context, dynamic).IsPresent.Should().BeFalse();

        AdmitCellOverlay(context, NavigationCellOverlayOperation.RevertToBake(dynamic), sequence: 3);
        context.Simulate();
        NavigationGraphCellState reverted = GetState(context, dynamic);
        reverted.Slot.Should().Be(before.Slot);
        reverted.HasCell.Should().BeFalse();

        AdmitCellOverlay(context, NavigationCellOverlayOperation.Set(dynamic, SolidCell), sequence: 4);
        context.Simulate();
        GetState(context, dynamic).Slot.Should().Be(before.Slot);
    }

    [Fact]
    public void DynamicOverlaySet_ShouldCaptureOnlyNewAddressAndCopyTouchedPages()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = CreateConfiguration(
            GridTopologyKind.RectangularPrism,
            GridStorageKind.Sparse);
        VoxelIndex dynamic = new(1, 0, 0);
        AddGrid(context.World, configuration, GridStorageKind.Sparse, default);
        AdmitMap(context, CreateMap("map", configuration, default), 1, 1);
        context.Simulate();

        AdmitCellOverlay(context, NavigationCellOverlayOperation.Set(dynamic, SolidCell), 2);
        context.Simulate();

        NavigationGraphMapDiagnostic added = context.Pathing.GetNavigationGraphDiagnostics().Maps[0];
        added.LastBaselineAddressCount.Should().Be(1);
        added.LastCopiedSemanticPages.Should().Be(1);
        added.LastCopiedPhysicalPages.Should().BeLessThanOrEqualTo(1);

        NavigationCell changed = new(
            TraversalMedia.Liquid,
            TraversalCapability.None,
            default,
            Fixed64.One,
            Fixed64.One,
            Fixed64.One);
        AdmitCellOverlay(context, NavigationCellOverlayOperation.Set(dynamic, changed), 3);
        context.Simulate();

        NavigationGraphMapDiagnostic updated = context.Pathing.GetNavigationGraphDiagnostics().Maps[0];
        updated.LastBaselineAddressCount.Should().Be(0);
        updated.LastCopiedSemanticPages.Should().Be(1);
        updated.LastCopiedPhysicalPages.Should().Be(0);
    }

    [Fact]
    public void DependencyStamp_ShouldTrackOnlySelectedComponentsAndPages()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NavigationMap first = CreateWideMap("A", xOffset: 0, cellCount: 65);
        NavigationMap second = CreateWideMap("B", xOffset: 256, cellCount: 1);
        AdmitMap(context, first, sequence: 1, bakeVersion: 1);
        AdmitMap(context, second, sequence: 2, bakeVersion: 1);
        var policyKey = new NavigationAreaPolicyKey("ground", 1);
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            new NavigationAreaPolicy(
                policyKey,
                new[] { new NavigationAreaRule(true, Fixed64.Zero) }),
            publicationSequence: 3,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(policyOperation).Should().BeTrue();
        context.Simulate();

        GraphDependencyStamp stamp;
        using (NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!)
        {
            lease.Graph.TryGetDependencyStamp(
                    policyKey,
                    new[] { "A" },
                    new[] { new GraphPageDependencyAddress("A", 0) },
                    out stamp)
                .Should().BeTrue();
        }

        AdmitCellOverlay(
            context,
            "B",
            NavigationCellOverlayOperation.Set(default, LiquidCell()),
            sequence: 4);
        context.Simulate();
        using (NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!)
            lease.Graph.IsDependencyCurrent(stamp).Should().BeTrue();

        AdmitCellOverlay(
            context,
            "A",
            NavigationCellOverlayOperation.Set(new VoxelIndex(64, 0, 0), LiquidCell()),
            sequence: 5);
        context.Simulate();
        using (NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!)
            lease.Graph.IsDependencyCurrent(stamp).Should().BeTrue();

        AdmitCellOverlay(
            context,
            "A",
            NavigationCellOverlayOperation.Set(default, LiquidCell()),
            sequence: 6);
        context.Simulate();
        using (NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!)
            lease.Graph.IsDependencyCurrent(stamp).Should().BeFalse();
    }

    [Fact]
    public void OverlaySetWhileGridAbsent_ShouldMaterializeWhenGridAppears()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = CreateConfiguration(
            GridTopologyKind.RectangularPrism,
            GridStorageKind.Sparse);
        VoxelIndex dynamic = new(1, 0, 0);
        AdmitMap(context, CreateMap("map", configuration, default), 1, 1);
        context.Simulate();
        AdmitCellOverlay(context, NavigationCellOverlayOperation.Set(dynamic, SolidCell), 2);
        context.Simulate();
        NavigationGraphCellState dormant = GetState(context, dynamic);
        dormant.HasCell.Should().BeTrue();
        dormant.IsMaterialized.Should().BeFalse();

        context.World.TryAddGrid(configuration, new[] { default(VoxelIndex), dynamic }, out _)
            .Should().BeTrue();
        context.Simulate();

        NavigationGraphCellState materialized = GetState(context, dynamic);
        materialized.IsMaterialized.Should().BeTrue();
        materialized.IsPresent.Should().BeTrue();
        materialized.Slot.Should().Be(dormant.Slot);
    }

    [Fact]
    public void PhysicalEventQueuedBeforeDynamicOverlay_ShouldSurviveOlderMapBaseline()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = CreateConfiguration(
            GridTopologyKind.RectangularPrism,
            GridStorageKind.Sparse);
        VoxelIndex dynamic = new(1, 0, 0);
        VoxelGrid grid = AddGrid(context.World, configuration, GridStorageKind.Sparse, default);
        AdmitMap(context, CreateMap("map", configuration, default), 1, 1);
        context.Simulate();
        grid.TryAddVoxel(dynamic, out _).Should().BeTrue();
        AdmitCellOverlay(context, NavigationCellOverlayOperation.Set(dynamic, SolidCell), 2);

        context.Simulate();

        NavigationGraphCellState state = GetState(context, dynamic);
        state.HasCell.Should().BeTrue();
        state.IsPresent.Should().BeTrue();
    }

    [Fact]
    public void PhysicalEventQueuedBeforeExistingCellOverlay_ShouldSurviveDeltaBaseline()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = CreateConfiguration(
            GridTopologyKind.RectangularPrism,
            GridStorageKind.Dense);
        VoxelGrid grid = AddGrid(context.World, configuration, GridStorageKind.Dense, default);
        AdmitMap(context, CreateMap("map", configuration, default), 1, 1);
        context.Simulate();
        grid.TryGetVoxel(default(VoxelIndex), out Voxel? voxel).Should().BeTrue();
        grid.TryAddObstacle(voxel!, context.World.AllocateObstacleToken()).Should().BeTrue();
        AdmitCellOverlay(context, NavigationCellOverlayOperation.Set(default, SolidCell), 2);

        context.Simulate();

        NavigationGraphCellState state = GetState(context, default);
        state.IsPresent.Should().BeTrue();
        state.ObstacleCount.Should().Be(1);
    }

    [Fact]
    public void GridSlotReuse_ShouldNotAliasRemovedGeneration()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = CreateConfiguration(
            GridTopologyKind.RectangularPrism,
            GridStorageKind.Dense);
        VoxelGrid first = AddGrid(context.World, configuration, GridStorageKind.Dense, default);
        AdmitMap(context, CreateMap("map", configuration, default), sequence: 1, bakeVersion: 1);
        context.Simulate();
        long firstGeneration = GetState(context, default).GridSpawnToken;

        context.World.TryRemoveGrid(first.GridIndex).Should().BeTrue();
        context.Simulate();
        GetState(context, default).IsMaterialized.Should().BeFalse();

        VoxelGrid replacement = AddGrid(context.World, configuration, GridStorageKind.Dense, default);
        replacement.GridIndex.Should().Be(first.GridIndex);
        context.Simulate();
        NavigationGraphCellState state = GetState(context, default);
        state.IsMaterialized.Should().BeTrue();
        state.GridSpawnToken.Should().NotBe(firstGeneration);
    }

    [Fact]
    public void SnapshotLease_ShouldKeepOldSemanticRootReadableAfterPublication()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = CreateConfiguration(
            GridTopologyKind.RectangularPrism,
            GridStorageKind.Dense);
        AddGrid(context.World, configuration, GridStorageKind.Dense, default);
        AdmitMap(context, CreateMap("map", configuration, default), sequence: 1, bakeVersion: 1);
        context.Simulate();
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Should().NotBeNull();

        AdmitCellOverlay(context, NavigationCellOverlayOperation.Suppress(default), sequence: 2);
        context.Simulate();

        lease.Graph.TryGetMap("map", out NavigationMapInstance? oldMap).Should().BeTrue();
        oldMap!.TryGetEffectiveCell(0, out _).Should().BeTrue();
        GetState(context, default).HasCell.Should().BeFalse();
    }

    [Fact]
    public void MapReplacement_ShouldPreserveOrClearOverlayExactlyAsRequested()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = CreateConfiguration(
            GridTopologyKind.RectangularPrism,
            GridStorageKind.Dense);
        AddGrid(context.World, configuration, GridStorageKind.Dense, default);
        NavigationMap map = CreateMap("map", configuration, default);
        AdmitMap(context, map, sequence: 1, bakeVersion: 1);
        context.Simulate();
        AdmitCellOverlay(context, NavigationCellOverlayOperation.Suppress(default), sequence: 2);
        context.Simulate();

        var preserve = new NavigationMapCommitOperation(
            new PreparedNavigationMap(CreateMap("map", configuration, default), 2),
            OverlayReplacementPolicy.PreserveAndRevalidate,
            3,
            context.FrameCount + 1);
        context.Pathing.Admit(preserve).Should().BeTrue();
        context.Simulate();
        GetState(context, default).HasCell.Should().BeFalse();

        var clear = new NavigationMapCommitOperation(
            new PreparedNavigationMap(CreateMap("map", configuration, default), 3),
            OverlayReplacementPolicy.Clear,
            4,
            context.FrameCount + 1);
        context.Pathing.Admit(clear).Should().BeTrue();
        context.Simulate();
        GetState(context, default).HasCell.Should().BeTrue();
    }

    [Fact]
    public void SameMapObjectReplacement_ShouldAdvanceBakeAndClearIdentity()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = CreateConfiguration(
            GridTopologyKind.RectangularPrism,
            GridStorageKind.Dense);
        AddGrid(context.World, configuration, GridStorageKind.Dense, default);
        NavigationMap map = CreateMap("map", configuration, default);
        AdmitMap(context, map, sequence: 1, bakeVersion: 1);
        context.Simulate();
        AdmitCellOverlay(context, NavigationCellOverlayOperation.Suppress(default), sequence: 2);
        context.Simulate();
        AdmitCellOverlay(context, NavigationCellOverlayOperation.RevertToBake(default), sequence: 3);
        context.Simulate();
        using NavigationWorldGraphLease before = context.Pathing.TryAcquireNavigationGraph()!;
        before.Graph.TryGetMap("map", out NavigationMapInstance? prior).Should().BeTrue();

        var replace = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, 2),
            OverlayReplacementPolicy.Clear,
            4,
            context.FrameCount + 1);
        context.Pathing.Admit(replace).Should().BeTrue();
        context.Simulate();
        using NavigationWorldGraphLease after = context.Pathing.TryAcquireNavigationGraph()!;
        after.Graph.TryGetMap("map", out NavigationMapInstance? next).Should().BeTrue();

        replace.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        next.Should().NotBeSameAs(prior);
        next!.BakeVersion.Should().Be(2);
        next.DynamicSlotGeneration.Should().Be(prior!.DynamicSlotGeneration + 1);
    }

    [Fact]
    public void PreserveReplacement_ShouldKeepDynamicSlotHistoryAcrossChangedBakeSize()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(3, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        AddGrid(context.World, configuration, GridStorageKind.Dense, default);
        AdmitMap(context, CreateMap("map", configuration, default), 1, 1);
        context.Simulate();
        VoxelIndex dynamic = new(2, 0, 0);
        AdmitCellOverlay(context, NavigationCellOverlayOperation.Set(dynamic, SolidCell), 2);
        context.Simulate();
        int originalSlot = GetState(context, dynamic).Slot;
        using NavigationWorldGraphLease oldLease = context.Pathing.TryAcquireNavigationGraph()!;

        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap replacement = new NavigationMapBuilder("map", binding)
            .AddCell(default, SolidCell)
            .AddCell(new VoxelIndex(1, 0, 0), SolidCell)
            .Build();
        var preserve = new NavigationMapCommitOperation(
            new PreparedNavigationMap(replacement, 2),
            OverlayReplacementPolicy.PreserveAndRevalidate,
            3,
            context.FrameCount + 1);
        context.Pathing.Admit(preserve).Should().BeTrue();

        context.Simulate();

        preserve.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        GetState(context, dynamic).Slot.Should().Be(originalSlot);
        oldLease.Graph.TryGetMap("map", out NavigationMapInstance? oldMap).Should().BeTrue();
        oldMap!.TryGetSlot(dynamic, out int oldSlot).Should().BeTrue();
        oldSlot.Should().Be(originalSlot);
    }

    [Fact]
    public void PreserveReplacement_ShouldRejectBakeThatAbsorbsReservedDynamicAddress()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        AddGrid(context.World, configuration, GridStorageKind.Dense, default);
        VoxelIndex dynamic = new(2, 0, 0);
        AdmitMap(context, CreateMap("map", configuration, default), 1, 1);
        context.Simulate();
        AdmitCellOverlay(context, NavigationCellOverlayOperation.Set(dynamic, SolidCell), 2);
        context.Simulate();
        AdmitCellOverlay(context, NavigationCellOverlayOperation.RevertToBake(dynamic), 3);
        context.Simulate();

        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap replacement = new NavigationMapBuilder("map", binding)
            .AddCell(default, SolidCell)
            .AddCell(dynamic, SolidCell)
            .Build();
        var operation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(replacement, 2),
            OverlayReplacementPolicy.PreserveAndRevalidate,
            4,
            context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();

        context.Simulate();

        operation.Receipt.Rejection.Should().Be(NavigationOperationRejection.ValidationFailed);
        GetState(context, dynamic).HasCell.Should().BeFalse();
    }

    [Fact]
    public void InstallingAnotherMap_ShouldNotDiscardQueuedPhysicalChange()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration firstConfiguration = CreateConfiguration(
            GridTopologyKind.RectangularPrism,
            GridStorageKind.Dense);
        GridConfiguration secondConfiguration = new(
            new Vector3d(10, 0, 0),
            new Vector3d(12, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        VoxelGrid firstGrid = AddGrid(
            context.World,
            firstConfiguration,
            GridStorageKind.Dense,
            default);
        AddGrid(context.World, secondConfiguration, GridStorageKind.Dense, default);
        AdmitMap(context, CreateMap("map", firstConfiguration, default), 1, 1);
        context.Simulate();
        firstGrid.TryGetVoxel(default(VoxelIndex), out Voxel? voxel).Should().BeTrue();
        firstGrid.TryAddObstacle(voxel!, context.World.AllocateObstacleToken()).Should().BeTrue();

        var second = new NavigationMapCommitOperation(
            new PreparedNavigationMap(CreateMap("other", secondConfiguration, default), 1),
            OverlayReplacementPolicy.Clear,
            2,
            context.FrameCount + 1);
        context.Pathing.Admit(second).Should().BeTrue();
        context.Simulate();

        GetState(context, default).IsBlocked.Should().BeTrue();
        context.Pathing.TryGetNavigationGraphCellState("other", default, out NavigationGraphCellState other)
            .Should().BeTrue();
        other.IsPresent.Should().BeTrue();
    }

    [Fact]
    public void WorldEvents_ShouldRemainContextLocal()
    {
        using TrailblazerWorldContext first = TrailblazerWorldContext.CreateOwned();
        using TrailblazerWorldContext second = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = CreateConfiguration(
            GridTopologyKind.RectangularPrism,
            GridStorageKind.Dense);
        VoxelGrid firstGrid = AddGrid(first.World, configuration, GridStorageKind.Dense, default);
        AddGrid(second.World, configuration, GridStorageKind.Dense, default);
        AdmitMap(first, CreateMap("map", configuration, default), 1, 1);
        AdmitMap(second, CreateMap("map", configuration, default), 1, 1);
        first.Simulate();
        second.Simulate();
        firstGrid.TryGetVoxel(default(VoxelIndex), out Voxel? voxel).Should().BeTrue();

        firstGrid.TryAddObstacle(voxel!, first.World.AllocateObstacleToken()).Should().BeTrue();
        first.Simulate();
        second.Simulate();

        GetState(first, default).IsBlocked.Should().BeTrue();
        GetState(second, default).IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void Diagnostics_ShouldCopyExactIdentitySemanticSourceAndRetention()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = CreateConfiguration(
            GridTopologyKind.HexPrism,
            GridStorageKind.Sparse);
        AddGrid(context.World, configuration, GridStorageKind.Sparse, default);
        AdmitMap(context, CreateMap("map", configuration, default), 1, 1);
        context.Simulate();
        AdmitCellOverlay(context, NavigationCellOverlayOperation.Suppress(default), 2);
        context.Simulate();

        NavigationGraphDiagnosticsSnapshot snapshot = context.Pathing.GetNavigationGraphDiagnostics();
        configuration.TryNormalize(out NormalizedGridConfiguration normalized).Should().BeTrue();

        snapshot.GraphVersion.Should().BePositive();
        snapshot.ActiveSnapshotBytes.Should().BePositive();
        snapshot.Maps.Should().ContainSingle();
        NavigationGraphMapDiagnostic map = snapshot.Maps[0];
        map.MapId.Should().Be("map");
        map.TopologyKind.Should().Be(GridTopologyKind.HexPrism);
        map.StorageKind.Should().Be(GridStorageKind.Sparse);
        map.WorldSpawnToken.Should().Be(context.World.SpawnToken);
        map.GridSpawnToken.Should().BePositive();
        map.ConfigurationKey.Should().Be(normalized.Key);
        map.Cells.Should().ContainSingle();
        map.Cells[0].SemanticSource.Should().Be(NavigationCellSemanticSource.OverlaySuppressed);
        map.Cells[0].HasCell.Should().BeFalse();
    }

    [Theory]
    [InlineData(GridStorageKind.Dense, 1, NavigationCellLookupKind.Sorted)]
    [InlineData(GridStorageKind.Sparse, 10, NavigationCellLookupKind.Direct)]
    public void BakedLookup_ShouldFollowAuthoredByteDensityNotPhysicalStorage(
        GridStorageKind storage,
        int authoredCells,
        NavigationCellLookupKind expected)
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(9, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: storage);
        VoxelIndex[] addresses = new VoxelIndex[authoredCells];
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var builder = new NavigationMapBuilder("map", binding);
        for (int i = 0; i < authoredCells; i++)
        {
            addresses[i] = new VoxelIndex(i, 0, 0);
            builder.AddCell(addresses[i], SolidCell);
        }
        if (storage == GridStorageKind.Sparse)
            context.World.TryAddGrid(configuration, addresses, out _).Should().BeTrue();
        else
            context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        AdmitMap(context, builder.Build(), 1, 1);

        context.Simulate();

        context.Pathing.GetNavigationGraphDiagnostics().Maps[0].LookupKind.Should().Be(expected);
    }

    [Fact]
    public void ExplicitDependencyOverlay_ShouldSplitStructuralComponentsDeterministically()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration firstConfiguration = CreateConfiguration(
            GridTopologyKind.RectangularPrism,
            GridStorageKind.Dense);
        GridConfiguration secondConfiguration = new(
            new Vector3d(10, 0, 0),
            new Vector3d(12, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        AddGrid(context.World, firstConfiguration, GridStorageKind.Dense, default);
        AddGrid(context.World, secondConfiguration, GridStorageKind.Dense, default);
        firstConfiguration.TryNormalize(out NormalizedGridConfiguration firstBinding).Should().BeTrue();
        secondConfiguration.TryNormalize(out NormalizedGridConfiguration secondBinding).Should().BeTrue();
        var transition = new TraversalTransitionDefinition(
            "ladder",
            TraversalTransitionType.Climb,
            default,
            TraversalMedium.Solid,
            new NavigationCellAddress("other", default),
            TraversalMedium.Solid,
            TraversalCapability.Climb);
        NavigationMap firstMap = new NavigationMapBuilder("map", firstBinding)
            .AddCell(default, SolidCell)
            .AddTransition(transition)
            .Build();
        NavigationMap secondMap = new NavigationMapBuilder("other", secondBinding)
            .AddCell(default, SolidCell)
            .Build();
        AdmitMap(context, firstMap, 1, 1);
        AdmitMap(context, secondMap, 2, 1);
        context.Simulate();

        NavigationGraphDiagnosticsSnapshot connected = context.Pathing.GetNavigationGraphDiagnostics();
        connected.Maps[0].ComponentId.Should().Be(connected.Maps[1].ComponentId);
        connected.Maps.Should().OnlyContain(map => map.IncidentExplicitEdgeCount == 1);

        var suppress = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(
                new NavigationOverlayTransaction(
                    new[]
                    {
                        new NavigationMapOverlayDelta(
                            "map",
                            transitions: new[] { TraversalTransitionOverlayOperation.Suppress("ladder") })
                    })),
            3,
            context.FrameCount + 1);
        context.Pathing.Admit(suppress).Should().BeTrue();
        context.Simulate();

        NavigationGraphDiagnosticsSnapshot split = context.Pathing.GetNavigationGraphDiagnostics();
        split.Maps[0].ComponentId.Should().NotBe(split.Maps[1].ComponentId);
        split.Maps.Should().OnlyContain(map => map.IncidentExplicitEdgeCount == 0);
        split.Maps[0].ComponentVersion.Should().BeGreaterThan(connected.Maps[0].ComponentVersion);
    }

    [Fact]
    public void ContextReset_ShouldClearPublishedAndPendingGraphAuthority()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = CreateConfiguration(
            GridTopologyKind.RectangularPrism,
            GridStorageKind.Dense);
        AddGrid(context.World, configuration, GridStorageKind.Dense, default);
        NavigationMap map = CreateMap("map", configuration, default);
        AdmitMap(context, map, sequence: 1, bakeVersion: 1);
        context.Simulate();
        var pending = new NavigationMapRemoveOperation(
            "map",
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 10);
        context.Pathing.Admit(pending).Should().BeTrue();

        context.Reset();

        pending.Receipt.Status.Should().Be(NavigationOperationStatus.Superseded);
        context.Pathing.TryGetNavigationGraphCellState("map", default, out _).Should().BeFalse();
        AdmitMap(context, map, sequence: 1, bakeVersion: 1);
        context.Simulate();
        GetState(context, default).HasCell.Should().BeTrue();
    }

    [Fact]
    public void SameBatchRemoveThenCommit_ShouldPublishTheReplacementProducedBySequenceOrder()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = CreateConfiguration(
            GridTopologyKind.RectangularPrism,
            GridStorageKind.Dense);
        NavigationMap original = CreateMap("map", configuration, default);
        AdmitMap(context, original, 1, 1);
        context.Simulate();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap replacement = new NavigationMapBuilder("map", binding)
            .AddCell(new VoxelIndex(1, 0, 0), SolidCell)
            .Build();
        int frame = context.FrameCount + 1;
        var remove = new NavigationMapRemoveOperation("map", 2, frame);
        var commit = new NavigationMapCommitOperation(
            new PreparedNavigationMap(replacement, 2),
            OverlayReplacementPolicy.Clear,
            3,
            frame);
        context.Pathing.Admit(remove).Should().BeTrue();
        context.Pathing.Admit(commit).Should().BeTrue();

        while (commit.Receipt.Status == NavigationOperationStatus.Pending)
            context.Simulate();

        remove.Receipt.Status.Should().Be(NavigationOperationStatus.Superseded);
        commit.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        context.Pathing.TryGetNavigationGraphCellState("map", default, out _).Should().BeFalse();
        context.Pathing.TryGetNavigationGraphCellState(
            "map", new VoxelIndex(1, 0, 0), out NavigationGraphCellState state).Should().BeTrue();
        state.HasCell.Should().BeTrue();
    }

    [Fact]
    public void SameBatchCommitThenRemove_ShouldPublishRemovalProducedBySequenceOrder()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = CreateConfiguration(
            GridTopologyKind.RectangularPrism,
            GridStorageKind.Dense);
        AdmitMap(context, CreateMap("map", configuration, default), 1, 1);
        context.Simulate();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap replacement = new NavigationMapBuilder("map", binding)
            .AddCell(new VoxelIndex(1, 0, 0), SolidCell)
            .Build();
        int frame = context.FrameCount + 1;
        var commit = new NavigationMapCommitOperation(
            new PreparedNavigationMap(replacement, 2),
            OverlayReplacementPolicy.Clear,
            2,
            frame);
        var remove = new NavigationMapRemoveOperation("map", 3, frame);
        context.Pathing.Admit(commit).Should().BeTrue();
        context.Pathing.Admit(remove).Should().BeTrue();

        while (remove.Receipt.Status == NavigationOperationStatus.Pending)
            context.Simulate();

        commit.Receipt.Status.Should().Be(NavigationOperationStatus.Superseded);
        remove.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        context.Pathing.TryGetNavigationGraphCellState("map", default, out _).Should().BeFalse();
        context.Pathing.TryGetNavigationGraphCellState("map", new VoxelIndex(1, 0, 0), out _)
            .Should().BeFalse();
    }

    [Fact]
    public void SameBatchCellDeltas_ShouldPrepareSemanticPagesInOperationOrder()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = CreateConfiguration(
            GridTopologyKind.RectangularPrism,
            GridStorageKind.Dense);
        AdmitMap(context, CreateMap("map", configuration, default), 1, 1);
        context.Simulate();
        int frame = context.FrameCount + 1;
        var first = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta("map", new[]
                {
                    NavigationCellOverlayOperation.Set(new VoxelIndex(1, 0, 0), SolidCell)
                })
            })),
            2,
            frame);
        var second = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta("map", new[]
                {
                    NavigationCellOverlayOperation.Suppress(new VoxelIndex(1, 0, 0))
                })
            })),
            3,
            frame);
        context.Pathing.Admit(first).Should().BeTrue();
        context.Pathing.Admit(second).Should().BeTrue();

        while (second.Receipt.Status == NavigationOperationStatus.Pending)
            context.Simulate();

        first.Receipt.Status.Should().Be(NavigationOperationStatus.Superseded);
        second.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        context.Pathing.TryGetNavigationGraphCellState(
            "map", new VoxelIndex(1, 0, 0), out NavigationGraphCellState state).Should().BeTrue();
        state.HasCell.Should().BeFalse();
    }

    private static void AdmitMap(
        TrailblazerWorldContext context,
        NavigationMap map,
        long sequence,
        long bakeVersion)
    {
        var operation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, bakeVersion),
            OverlayReplacementPolicy.Clear,
            sequence,
            context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
    }

    private static void AdmitCellOverlay(
        TrailblazerWorldContext context,
        NavigationCellOverlayOperation cell,
        long sequence) =>
        AdmitCellOverlay(context, "map", cell, sequence);

    private static void AdmitCellOverlay(
        TrailblazerWorldContext context,
        string mapId,
        NavigationCellOverlayOperation cell,
        long sequence)
    {
        var operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(
                new NavigationOverlayTransaction(
                    new[] { new NavigationMapOverlayDelta(mapId, new[] { cell }) })),
            sequence,
            context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
    }

    private static NavigationGraphCellState GetState(
        TrailblazerWorldContext context,
        VoxelIndex index)
    {
        context.Pathing.TryGetNavigationGraphCellState("map", index, out NavigationGraphCellState state)
            .Should().BeTrue();
        return state;
    }

    private static NavigationMap CreateMap(
        string mapId,
        GridConfiguration configuration,
        VoxelIndex index)
    {
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        return new NavigationMapBuilder(mapId, binding)
            .AddCell(index, SolidCell)
            .Build();
    }

    private static NavigationMap CreateWideMap(string mapId, int xOffset, int cellCount)
    {
        var configuration = new GridConfiguration(
            new Vector3d(xOffset, 0, 0),
            new Vector3d(xOffset + cellCount - 1, 0, 0),
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var builder = new NavigationMapBuilder(mapId, binding);
        for (int x = 0; x < cellCount; x++)
            builder.AddCell(new VoxelIndex(x, 0, 0), SolidCell);
        return builder.Build();
    }

    private static NavigationCell LiquidCell() => new(
        TraversalMedia.Liquid,
        TraversalCapability.None,
        default,
        Fixed64.One,
        Fixed64.Zero,
        Fixed64.One);

    private static VoxelGrid AddGrid(
        GridWorld world,
        GridConfiguration configuration,
        GridStorageKind storage,
        VoxelIndex initial)
    {
        bool added = storage == GridStorageKind.Sparse
            ? world.TryAddGrid(configuration, new[] { initial }, out ushort index)
            : world.TryAddGrid(configuration, out index);
        added.Should().BeTrue();
        return world.ActiveGrids[index];
    }

    private static GridConfiguration CreateConfiguration(
        GridTopologyKind topology,
        GridStorageKind storage) => new(
        Vector3d.Zero,
        topology == GridTopologyKind.HexPrism
            ? new Vector3d(4, 1, 4)
            : new Vector3d(2, 1, 1),
        topologyKind: topology,
        topologyMetrics: topology == GridTopologyKind.HexPrism
            ? GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, HexOrientation.PointyTop)
            : GridTopologyMetrics.Rectangular(Fixed64.One),
        storageKind: storage);
}
