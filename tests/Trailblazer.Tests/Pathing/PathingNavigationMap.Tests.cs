using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public class PathingNavigationMapTests : IDisposable
{
    public PathingNavigationMapTests()
    {
        TrailblazerWorldManager.Setup();
    }

    public void Dispose()
    {
        PathManager.Reset();

        TrailblazerWorldManager.Reset();
        TrailblazerManager.Reset();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Register_WithInitializeChartFalse_AddsMapWithoutInitializingIt()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        var map = PathTestFactory.BuildSinglePointMap("TestMap", new Vector3d(0, 0, 0));
        Assert.True(PathManager.Register(map, initializeChart: false));

        Assert.True(PathManager.TryGetNavigationChart("TestMap", out var retrieved));
        Assert.Equal(map, retrieved);
        Assert.False(map.IsInitialized);
        Voxel voxel = TestRequire.VoxelAt(new Vector3d(0, 0, 0));
        Assert.False(voxel.TryGetPartition<SolidChartPartition>(out _));

        PathManager.UnloadChart("TestMap");
    }

    [Fact]
    public void Register_InitializesChartByDefault()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        var map = PathTestFactory.BuildSinglePointMap("InitMap", new Vector3d(0, 0, 0));
        Assert.True(PathManager.Register(map));

        Assert.True(map.IsInitialized);
        Voxel voxel = TestRequire.VoxelAt(new Vector3d(0, 0, 0));
        Assert.True(voxel.TryGetPartition<SolidChartPartition>(out _));

        PathManager.UnloadChart("InitMap");
    }

    [Fact]
    public void Register_ShouldRejectDuplicateChartNames_WithoutReplacingTheOriginal()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        var original = PathTestFactory.BuildSinglePointMap("DuplicateChart", Vector3d.Zero);
        var duplicate = PathTestFactory.BuildSinglePointMap("DuplicateChart", new Vector3d(1, 0, 0));

        Assert.True(PathManager.Register(original, initializeChart: false));
        Assert.False(PathManager.Register(duplicate, initializeChart: false));
        Assert.True(PathManager.TryGetNavigationChart("DuplicateChart", out var retrieved));
        Assert.Same(original, retrieved);
        Assert.False(original.IsInitialized);
        Assert.False(duplicate.IsInitialized);

        PathManager.UnloadChart("DuplicateChart");
    }

    [Fact]
    public void AllCharts_ShouldReturnEmptyAndRegisteredSnapshots()
    {
        Assert.Empty(PathManager.AllCharts);

        var first = PathTestFactory.BuildSinglePointMap("SnapshotA", Vector3d.Zero);
        var second = PathTestFactory.BuildSinglePointMap("SnapshotB", new Vector3d(2, 0, 0));

        Assert.True(PathManager.Register(first, initializeChart: false));
        Assert.True(PathManager.Register(second, initializeChart: false));

        NavigationChart[] snapshot = new System.Collections.Generic.List<NavigationChart>(PathManager.AllCharts).ToArray();

        Assert.Equal(2, snapshot.Length);
        Assert.Contains(snapshot, chart => chart.Name == "SnapshotA");
        Assert.Contains(snapshot, chart => chart.Name == "SnapshotB");

        PathManager.UnloadChart("SnapshotA");
        PathManager.UnloadChart("SnapshotB");
    }

    [Fact]
    public void UnloadChart_ShouldIgnoreNullChart()
    {
        PathManager.UnloadChart((NavigationChart)null!);

        Assert.Empty(PathManager.AllCharts);
    }

    [Fact]
    public void InitializeAllCharts_ShouldInitializeDeferredCharts()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(6, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        var first = PathTestFactory.BuildSinglePointMap("DeferredA", Vector3d.Zero);
        var second = PathTestFactory.BuildSinglePointMap("DeferredB", new Vector3d(2, 0, 0));

        Assert.True(PathManager.Register(first, initializeChart: false));
        Assert.True(PathManager.Register(second, initializeChart: false));
        Assert.False(first.IsInitialized);
        Assert.False(second.IsInitialized);

        PathManager.InitializeAllCharts();

        Assert.True(first.IsInitialized);
        Assert.True(second.IsInitialized);
        Voxel firstVoxel = TestRequire.VoxelAt(Vector3d.Zero);
        Assert.True(firstVoxel.TryGetPartition<SolidChartPartition>(out _));
        Voxel secondVoxel = TestRequire.VoxelAt(new Vector3d(2, 0, 0));
        Assert.True(secondVoxel.TryGetPartition<SolidChartPartition>(out _));

        PathManager.UnloadChart("DeferredA");
        PathManager.UnloadChart("DeferredB");
    }

    [Fact]
    public void HasAuthoredVolumeMedium_ShouldReturnFalse_ForUnsupportedMedium()
    {
        Assert.False(PathManager.HasAuthoredVolumeMedium(TraversalMedium.Solid));
    }

    [Fact]
    public void UnloadMap_RemovesOnlyItsPartition()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        var pos = new Vector3d(0, 0, 0);
        var mapA = PathTestFactory.BuildSinglePointMap("MapA", pos);
        var mapB = PathTestFactory.BuildSinglePointMap("MapB", pos);

        PathManager.Register(mapA);
        PathManager.Register(mapB);

        PathManager.InitializeChart("MapA");
        PathManager.InitializeChart("MapB");

        // Validate partition exists and belongs to both
        Voxel voxel = TestRequire.VoxelAt(pos);
        SolidChartPartition partition = TestRequire.Partition<SolidChartPartition>(voxel);
        Assert.True(partition.BelongsTo("MapA"));
        Assert.True(partition.BelongsTo("MapB"));

        PathManager.UnloadChart("MapA");

        // Should still be there because MapB owns it
        Assert.True(voxel.TryGetPartition<SolidChartPartition>(out var afterUnload));
        Assert.True(TestRequire.NotNull(afterUnload).BelongsTo("MapB"));

        PathManager.UnloadChart("MapB");
    }

    [Fact]
    public void IsWalkable_ShouldReturnFalseForOutOfBounds()
    {
        var map = PathTestFactory.BuildSinglePointMap("BoundsTest", new Vector3d(0, 0, 0));
        Assert.False(map.IsWalkable(new Vector3d(10, 0, 10))); // Way outside

        PathManager.UnloadChart("BoundsTest");
    }

    [Fact]
    public void InitializeMap_ShouldNotDuplicatePartition()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        var map = PathTestFactory.BuildSinglePointMap("DuplicateInit", new Vector3d(0, 0, 0));
        PathManager.Register(map);
        PathManager.InitializeChart("DuplicateInit");
        PathManager.InitializeChart("DuplicateInit"); // idempotent

        TrailblazerWorldManager.TryGetGridAndVoxel(new Vector3d(0, 0, 0), out _, out Voxel? voxel);
        var count = voxel!.TryGetPartition(out SolidChartPartition? partition) ? 1 : 0;

        Assert.Equal(1, count);
        Assert.True(partition!.BelongsTo("DuplicateInit"));

        PathManager.UnloadChart("DuplicateInit");
    }

    [Fact]
    public void TryGetEffectiveCellAndOwner_ShouldReturnWinningResolvedState()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        Assert.True(PathManager.Register(BuildSingleTraversalPointChart(
            "EffectiveLow",
            Vector3d.Zero,
            TraversalMedia.Solid,
            priority: 0)));
        Assert.True(PathManager.Register(BuildSingleTraversalPointChart(
            "EffectiveHigh",
            Vector3d.Zero,
            TraversalMedia.Gas,
            priority: 1)));

        Voxel voxel = TestRequire.VoxelAt(Vector3d.Zero);

        Assert.True(PathManager.TryGetEffectiveCell(Vector3d.Zero, out NavigationChartCell worldCell));
        Assert.True(PathManager.TryGetEffectiveCell(voxel.WorldIndex, out NavigationChartCell voxelCell));
        string worldOwner = TestRequire.Created(PathManager.TryGetEffectiveChartOwner(Vector3d.Zero, out string? createdworldOwner), createdworldOwner);
        string voxelOwner = TestRequire.Created(PathManager.TryGetEffectiveChartOwner(voxel.WorldIndex, out string? createdvoxelOwner), createdvoxelOwner);

        Assert.Equal(TraversalMedia.Gas, worldCell.TraversalKinds);
        Assert.Equal(worldCell, voxelCell);
        Assert.Equal("EffectiveHigh", worldOwner);
        Assert.Equal(worldOwner, voxelOwner);

        Assert.False(PathManager.TryGetEffectiveCell(new Vector3d(3, 0, 3), out _));
        Assert.False(PathManager.TryGetEffectiveChartOwner(new Vector3d(3, 0, 3), out _));
    }

    [Fact]
    public void TryGetEffectiveCell_ByVoxelIndex_ShouldReturnFalse_WhenVoxelHasNoResolvedState()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        Voxel voxel = TestRequire.VoxelAt(Vector3d.Zero);
        Assert.False(PathManager.TryGetEffectiveCell(voxel.WorldIndex, out _));
        Assert.False(PathManager.TryGetEffectiveChartOwner(voxel.WorldIndex, out _));
    }

    [Fact]
    public void TryGetClosestActiveTransition_ShouldReturnClosestDirectedTransitionOfRequestedType()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(12, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        PathTestFactory.RegisterSingleWalkablePoint("ClosestJumpStartA", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("ClosestJumpEndA", new Vector3d(1, 0, 0));
        PathTestFactory.RegisterSingleWalkablePoint("ClosestLandingStart", new Vector3d(3, 0, 0));
        PathTestFactory.RegisterSingleWalkablePoint("ClosestLandingEnd", new Vector3d(4, 0, 0));
        PathTestFactory.RegisterSingleWalkablePoint("ClosestJumpStartB", new Vector3d(8, 0, 0));
        PathTestFactory.RegisterSingleWalkablePoint("ClosestJumpEndB", new Vector3d(9, 0, 0));

        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "closest-jump-a",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)))));
        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "closest-landing",
            type: TraversalTransitionType.Landing,
            source: TraversalTransitionAnchor.Solid(new Vector3d(3, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(4, 0, 0)))));
        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "closest-jump-b",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(8, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(9, 0, 0)))));

        Assert.True(PathManager.TryGetClosestActiveTransition(
            new Vector3d(7, 0, 0),
            TraversalTransitionType.Jump,
            out TraversalTransition closestJump));
        Assert.Equal("closest-jump-b", closestJump.Id);
        Assert.Equal(new Vector3d(8, 0, 0), closestJump.Source.Position);

        Assert.True(PathManager.TryGetClosestActiveTransition(
            new Vector3d(3, 0, 0),
            TraversalTransitionType.Landing,
            out TraversalTransition closestLanding));
        Assert.Equal("closest-landing", closestLanding.Id);

        Assert.False(PathManager.TryGetClosestActiveTransition(
            Vector3d.Zero,
            TraversalTransitionType.Takeoff,
            out _));
    }

    [Fact]
    public void TryGetClosestActiveTransition_ShouldUseReversedBidirectionalView_AndIgnoreSuppressedTransitions()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(12, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        PathTestFactory.RegisterSingleWalkablePoint("ClosestReverseStart", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("ClosestReverseEnd", new Vector3d(1, 0, 0));
        PathTestFactory.RegisterSingleWalkablePoint("ClosestFallbackStart", new Vector3d(6, 0, 0));
        PathTestFactory.RegisterSingleWalkablePoint("ClosestFallbackEnd", new Vector3d(7, 0, 0));

        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "closest-bidirectional",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            isBidirectional: true)));
        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "closest-fallback",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(6, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(7, 0, 0)))));

        Assert.True(PathManager.TryGetClosestActiveTransition(
            new Vector3d(1, 0, 0),
            TraversalTransitionType.Jump,
            out TraversalTransition reversed));
        Assert.Equal("closest-bidirectional", reversed.Id);
        Assert.Equal(new Vector3d(1, 0, 0), reversed.Source.Position);
        Assert.Equal(Vector3d.Zero, reversed.Destination.Position);

        Assert.True(PathManager.Register(BuildSingleTraversalPointChart(
            "ClosestOverride",
            new Vector3d(1, 0, 0),
            TraversalMedia.Liquid,
            priority: 1)));

        Assert.True(PathManager.TryGetClosestActiveTransition(
            new Vector3d(1, 0, 0),
            TraversalTransitionType.Jump,
            out TraversalTransition fallback));
        Assert.Equal("closest-fallback", fallback.Id);

        PathManager.UnloadChart("ClosestOverride");

        Assert.True(PathManager.TryGetClosestActiveTransition(
            new Vector3d(1, 0, 0),
            TraversalTransitionType.Jump,
            out TraversalTransition reactivated));
        Assert.Equal("closest-bidirectional", reactivated.Id);
        Assert.Equal(new Vector3d(1, 0, 0), reactivated.Source.Position);
    }

    [Fact]
    public void TryGetClosestActiveTransition_ShouldStillReturnCloserNeighborGridTransition()
    {
        Assert.True(TrailblazerWorldManager.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)),
            out _));
        Assert.True(TrailblazerWorldManager.TryAddGrid(
            new GridConfiguration(new Vector3d(4, -4, -4), new Vector3d(12, 4, 4)),
            out _));

        PathTestFactory.RegisterSingleWalkablePoint("ClosestLocalSource", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("ClosestLocalDestination", new Vector3d(1, 0, 0));
        PathTestFactory.RegisterSingleWalkablePoint("ClosestNeighborSource", new Vector3d(4, 0, 0));
        PathTestFactory.RegisterSingleWalkablePoint("ClosestNeighborDestination", new Vector3d(5, 0, 0));

        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "closest-local-grid",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)))));
        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "closest-neighbor-grid",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(4, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(5, 0, 0)))));

        Assert.True(PathManager.TryGetClosestActiveTransition(
            new Vector3d(3, 0, 0),
            TraversalTransitionType.Jump,
            out TraversalTransition closest));
        Assert.Equal("closest-neighbor-grid", closest.Id);
        Assert.Equal(new Vector3d(4, 0, 0), closest.Source.Position);
    }

    [Fact]
    public void TryGetClosestActiveTransition_ShouldSearchAcrossSourceGrids_WhenOriginIsOutsideAnyGrid()
    {
        Assert.True(TrailblazerWorldManager.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)),
            out _));
        Assert.True(TrailblazerWorldManager.TryAddGrid(
            new GridConfiguration(new Vector3d(10, -4, -4), new Vector3d(14, 4, 4)),
            out _));

        PathTestFactory.RegisterSingleWalkablePoint("OutsideSearchNearSource", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("OutsideSearchNearDestination", new Vector3d(1, 0, 0));
        PathTestFactory.RegisterSingleWalkablePoint("OutsideSearchFarSource", new Vector3d(10, 0, 0));
        PathTestFactory.RegisterSingleWalkablePoint("OutsideSearchFarDestination", new Vector3d(11, 0, 0));

        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "outside-search-near",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)))));
        Assert.True(TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "outside-search-far",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(10, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(11, 0, 0)))));

        Assert.False(TrailblazerWorldManager.TryGetGrid(new Vector3d(8, 6, 0), out _));
        Assert.True(PathManager.TryGetClosestActiveTransition(
            new Vector3d(8, 6, 0),
            TraversalTransitionType.Jump,
            out TraversalTransition closest));
        Assert.Equal("outside-search-far", closest.Id);
    }

    [Fact]
    public void UnloadChart_ShouldClearInitializationStateSoChartCanBeRegisteredAgain()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        var map = PathTestFactory.BuildSinglePointMap("ReloadableMap", new Vector3d(0, 0, 0));
        PathManager.Register(map);
        PathManager.InitializeChart(map.Name);

        PathManager.UnloadChart(map);

        Assert.False(map.IsInitialized);
        Assert.True(PathManager.Register(map));

        PathManager.InitializeChart(map.Name);

        Voxel voxel = TestRequire.VoxelAt(new Vector3d(0, 0, 0));
        Assert.True(voxel.TryGetPartition<SolidChartPartition>(out _));

        PathManager.UnloadChart(map);
    }

    [Fact]
    public void PathManagerReset_ShouldClearInitializationStateSoSameChartInstanceCanBeRegisteredAgain()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        var map = PathTestFactory.BuildSinglePointMap("ResettableMap", new Vector3d(0, 0, 0));
        Assert.True(PathManager.Register(map));
        Assert.True(map.IsInitialized);

        PathManager.Reset();

        Assert.False(map.IsInitialized);
        Assert.True(PathManager.Register(map));
        Assert.True(map.IsInitialized);
        Voxel voxel = TestRequire.VoxelAt(new Vector3d(0, 0, 0));
        Assert.True(voxel.TryGetPartition<SolidChartPartition>(out _));

        PathManager.UnloadChart(map);
    }

    [Fact]
    public void TryGetCell_ShouldReturnDefaultMetadata_ForBooleanCharts()
    {
        bool[,,] data = new bool[1, 2, 1]
        {
            {
                { true },
                { false }
            }
        };

        var chart = NavigationChart.From3D("BoolPayload", data, Vector3d.Zero, Fixed64.One);

        Assert.True(chart.TryGetCell(Vector3d.Zero, out NavigationChartCell walkableCell));
        Assert.True(walkableCell.HasTraversalData);
        Assert.True(walkableCell.HasSolid);
        Assert.False(walkableCell.HasVolume);
        Assert.Equal(0, walkableCell.PathCostModifier);
        Assert.Equal(NavigationChartCellFlags.None, walkableCell.Flags);

        Assert.True(chart.TryGetCell(new Vector3d(1, 0, 0), out NavigationChartCell blockedCell));
        Assert.False(blockedCell.HasTraversalData);
        Assert.False(blockedCell.HasSolid);
        Assert.False(blockedCell.HasVolume);
        Assert.Equal(0, blockedCell.PathCostModifier);
        Assert.Equal(NavigationChartCellFlags.None, blockedCell.Flags);

        Assert.False(chart.TryGetCell(new Vector3d(3, 0, 0), out _));
    }

    [Fact]
    public void TryGetCell_ShouldReturnRequestedVolumeMetadata_ForBooleanCharts()
    {
        bool[,,] data = new bool[1, 2, 1]
        {
            {
                { true },
                { false }
            }
        };

        var chart = NavigationChart.From3D(
            "BoolVolumePayload",
            data,
            Vector3d.Zero,
            Fixed64.One,
            TraversalMedium.Liquid);

        Assert.True(chart.TryGetCell(Vector3d.Zero, out NavigationChartCell traversableCell));
        Assert.True(traversableCell.HasTraversalData);
        Assert.False(traversableCell.HasSolid);
        Assert.True(traversableCell.HasVolume);
        Assert.True(traversableCell.SupportsMedium(TraversalMedium.Liquid));
        Assert.False(traversableCell.SupportsMedium(TraversalMedium.Gas));
        Assert.Equal(0, traversableCell.PathCostModifier);
        Assert.Equal(NavigationChartCellFlags.None, traversableCell.Flags);

        Assert.True(chart.TryGetCell(new Vector3d(1, 0, 0), out NavigationChartCell emptyCell));
        Assert.False(emptyCell.HasTraversalData);
        Assert.False(emptyCell.HasSolid);
        Assert.False(emptyCell.HasVolume);
    }

    [Fact]
    public void From3D_ShouldRejectUnsupportedBooleanTraversalMedium()
    {
        bool[,,] data = new bool[1, 1, 1];
        data[0, 0, 0] = true;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NavigationChart.From3D(
                "InvalidBoolMedium",
                data,
                Vector3d.Zero,
                Fixed64.One,
                TraversalMedium.Unknown));
    }

    [Fact]
    public void InitializeChart_ShouldCreateVolumePartitions_ForBooleanVolumeCharts()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        bool[,,] data = new bool[3, 3, 3];
        data[1, 1, 1] = true;

        Vector3d targetPosition = Vector3d.Zero;
        var chart = NavigationChart.From3D(
            "BoolVolumeChart",
            data,
            new Vector3d(-1, -1, -1),
            Fixed64.One,
            TraversalMedium.Gas);

        PathManager.Register(chart);

        Voxel voxel = TestRequire.VoxelAt(targetPosition);
        Assert.False(voxel.TryGetPartition<SolidChartPartition>(out _));
        VolumeChartPartition volumePartition = TestRequire.Partition<VolumeChartPartition>(voxel);
        Assert.True(volumePartition.SupportsMedium(TraversalMedium.Gas));
        Assert.False(volumePartition.SupportsMedium(TraversalMedium.Liquid));

        PathManager.UnloadChart(chart.Name);
    }

    [Fact]
    public void InitializeChart_ShouldApplyStructuredCellMetadata_ToPartitions()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        NavigationChartCell[,,] data = new NavigationChartCell[3, 3, 3];
        data[1, 1, 1] = new NavigationChartCell(
            TraversalMedia.Solid,
            pathCostModifier: 7,
            flags: NavigationChartCellFlags.TransitionSourceHint);

        Vector3d targetPosition = Vector3d.Zero;
        var chart = NavigationChart.From3D("StructuredChart", data, new Vector3d(-1, -1, -1), Fixed64.One);
        PathManager.Register(chart);
        PathManager.InitializeChart(chart.Name);

        Voxel voxel = TestRequire.VoxelAt(targetPosition);
        SolidChartPartition partition = TestRequire.Partition<SolidChartPartition>(voxel);
        Assert.Equal(7, partition.PathCostModifier);
        Assert.Equal(NavigationChartCellFlags.TransitionSourceHint, partition.ChartFlags);

        PathManager.UnloadChart(chart.Name);
    }

    [Fact]
    public void OverlappingChartResolution_ShouldUseRegistrationPrecedence_AndRestoreTheNextOwnerOnUnload()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        Vector3d targetPosition = Vector3d.Zero;
        Vector3d minBounds = new(-1, -1, -1);

        NavigationChartCell[,,] mapAData = new NavigationChartCell[3, 3, 3];
        mapAData[1, 1, 1] = new NavigationChartCell(
            TraversalMedia.Solid,
            pathCostModifier: 2,
            flags: NavigationChartCellFlags.TransitionSourceHint);

        NavigationChartCell[,,] mapBData = new NavigationChartCell[3, 3, 3];
        mapBData[1, 1, 1] = new NavigationChartCell(
            TraversalMedia.Solid,
            pathCostModifier: 5,
            flags: NavigationChartCellFlags.TransitionDestinationHint);

        PathManager.Register(
            NavigationChart.From3D("StructuredA", mapAData, minBounds, Fixed64.One),
            initializeChart: false);
        PathManager.Register(
            NavigationChart.From3D("StructuredB", mapBData, minBounds, Fixed64.One),
            initializeChart: false);

        PathManager.InitializeChart("StructuredB");
        PathManager.InitializeChart("StructuredA");

        Voxel voxel = TestRequire.VoxelAt(targetPosition);
        SolidChartPartition partition = TestRequire.Partition<SolidChartPartition>(voxel);
        Assert.True(partition.BelongsTo("StructuredA"));
        Assert.True(partition.BelongsTo("StructuredB"));
        Assert.Equal("StructuredB", partition.EffectiveChartOwner);
        Assert.Equal(5, partition.PathCostModifier);
        Assert.Equal(NavigationChartCellFlags.TransitionDestinationHint, partition.ChartFlags);

        PathManager.UnloadChart("StructuredB");

        SolidChartPartition remainingPartition = TestRequire.Partition<SolidChartPartition>(voxel);
        Assert.True(remainingPartition.BelongsTo("StructuredA"));
        Assert.False(remainingPartition.BelongsTo("StructuredB"));
        Assert.Equal("StructuredA", remainingPartition.EffectiveChartOwner);
        Assert.Equal(2, remainingPartition.PathCostModifier);
        Assert.Equal(NavigationChartCellFlags.TransitionSourceHint, remainingPartition.ChartFlags);

        PathManager.UnloadChart("StructuredA");
    }

    [Fact]
    public void InitializeChart_ShouldApplyStructuredVolumeMetadata_ToVolumeChartPartitions()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        NavigationChartCell[,,] data = new NavigationChartCell[3, 3, 3];
        data[1, 1, 1] = new NavigationChartCell(
            TraversalMedia.Liquid,
            pathCostModifier: 9);

        Vector3d targetPosition = Vector3d.Zero;
        var chart = NavigationChart.From3D("StructuredVolume", data, new Vector3d(-1, -1, -1), Fixed64.One);
        PathManager.Register(chart);
        PathManager.InitializeChart(chart.Name);

        Voxel voxel = TestRequire.VoxelAt(targetPosition);
        Assert.False(voxel.TryGetPartition<SolidChartPartition>(out _));
        VolumeChartPartition volumePartition = TestRequire.Partition<VolumeChartPartition>(voxel);
        Assert.True(volumePartition.SupportsMedium(TraversalMedium.Liquid));
        Assert.False(volumePartition.SupportsMedium(TraversalMedium.Gas));
        Assert.Equal(9, volumePartition.PathCostModifier);

        PathManager.UnloadChart(chart.Name);
    }

    [Fact]
    public void InitializeChart_ShouldApplyStructuredMixedSolidLiquidCells_ToBothPartitionTypes()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        NavigationChartCell[,,] data = new NavigationChartCell[3, 3, 3];
        data[1, 1, 1] = new NavigationChartCell(
            TraversalMedia.Solid | TraversalMedia.Liquid,
            pathCostModifier: 4,
            flags: NavigationChartCellFlags.TransitionSourceHint);

        Vector3d targetPosition = Vector3d.Zero;
        var chart = NavigationChart.From3D("StructuredMixed", data, new Vector3d(-1, -1, -1), Fixed64.One);
        PathManager.Register(chart);
        PathManager.InitializeChart(chart.Name);

        Voxel voxel = TestRequire.VoxelAt(targetPosition);
        SolidChartPartition solidPartition = TestRequire.Partition<SolidChartPartition>(voxel);
        Assert.Equal(4, solidPartition.PathCostModifier);
        Assert.Equal(NavigationChartCellFlags.TransitionSourceHint, solidPartition.ChartFlags);

        VolumeChartPartition volumePartition = TestRequire.Partition<VolumeChartPartition>(voxel);
        Assert.True(volumePartition.SupportsMedium(TraversalMedium.Liquid));
        Assert.False(volumePartition.SupportsMedium(TraversalMedium.Gas));
        Assert.Equal(4, volumePartition.PathCostModifier);
        Assert.Equal("StructuredMixed", volumePartition.EffectiveChartOwner);

        PathManager.UnloadChart(chart.Name);
    }

    [Fact]
    public void OverlappingSolidAndLiquidCharts_ShouldResolveToOneEffectiveCell_AndRestoreThePreviousWinnerOnUnload()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        Vector3d targetPosition = Vector3d.Zero;
        Vector3d minBounds = new(-1, -1, -1);

        NavigationChartCell[,,] solidData = new NavigationChartCell[3, 3, 3];
        solidData[1, 1, 1] = NavigationChartCell.Solid;

        NavigationChartCell[,,] liquidData = new NavigationChartCell[3, 3, 3];
        liquidData[1, 1, 1] = NavigationChartCell.Liquid;

        PathManager.Register(NavigationChart.From3D("LowSolid", solidData, minBounds, Fixed64.One, priority: 0));
        PathManager.Register(NavigationChart.From3D("HighLiquid", liquidData, minBounds, Fixed64.One, priority: 1));

        Voxel voxel = TestRequire.VoxelAt(targetPosition);
        Assert.False(voxel.TryGetPartition<SolidChartPartition>(out _));
        VolumeChartPartition liquidPartition = TestRequire.Partition<VolumeChartPartition>(voxel);
        Assert.Equal("HighLiquid", liquidPartition.EffectiveChartOwner);
        Assert.True(liquidPartition.SupportsMedium(TraversalMedium.Liquid));
        Assert.False(liquidPartition.SupportsMedium(TraversalMedium.Gas));

        PathManager.UnloadChart("HighLiquid");

        SolidChartPartition restoredSolidPartition = TestRequire.Partition<SolidChartPartition>(voxel);
        Assert.Equal("LowSolid", restoredSolidPartition.EffectiveChartOwner);
        Assert.False(voxel.TryGetPartition<VolumeChartPartition>(out _));

        PathManager.UnloadChart("LowSolid");
    }

    [Fact]
    public void TryUpdateChartCell_ShouldRefreshLivePartitionsWithoutUnload()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        NavigationChartCell[,,] data = new NavigationChartCell[3, 3, 3];
        data[1, 1, 1] = NavigationChartCell.Solid;

        var chart = NavigationChart.From3D("LiveUpdateChart", data, new Vector3d(-1, -1, -1), Fixed64.One);
        PathManager.Register(chart);

        Voxel voxel = TestRequire.VoxelAt(Vector3d.Zero);
        SolidChartPartition solidPartition = TestRequire.Partition<SolidChartPartition>(voxel);
        Assert.Equal("LiveUpdateChart", solidPartition.EffectiveChartOwner);
        Assert.False(voxel.TryGetPartition<VolumeChartPartition>(out _));

        Assert.True(PathManager.TryUpdateChartCell(chart.Name, 1, 1, 1, NavigationChartCell.SolidLiquid));

        SolidChartPartition updatedSolidPartition = TestRequire.Partition<SolidChartPartition>(voxel);
        Assert.Equal("LiveUpdateChart", updatedSolidPartition.EffectiveChartOwner);
        VolumeChartPartition mixedVolumePartition = TestRequire.Partition<VolumeChartPartition>(voxel);
        Assert.True(mixedVolumePartition.SupportsMedium(TraversalMedium.Liquid));
        Assert.False(mixedVolumePartition.SupportsMedium(TraversalMedium.Gas));

        Assert.True(PathManager.TryUpdateChartCell(chart.Name, Vector3d.Zero, NavigationChartCell.Liquid));

        Assert.False(voxel.TryGetPartition<SolidChartPartition>(out _));
        VolumeChartPartition liquidPartition = TestRequire.Partition<VolumeChartPartition>(voxel);
        Assert.Equal("LiveUpdateChart", liquidPartition.EffectiveChartOwner);
        Assert.True(liquidPartition.SupportsMedium(TraversalMedium.Liquid));
        Assert.False(liquidPartition.SupportsMedium(TraversalMedium.Gas));
    }

    [Fact]
    public void ApplyChartUpdates_ShouldApplySparseBatchAndReportChangedCellCount()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        NavigationChartCell[,,] data = new NavigationChartCell[1, 3, 1];
        data[0, 0, 0] = NavigationChartCell.Gas;
        data[0, 2, 0] = NavigationChartCell.Gas;

        var chart = NavigationChart.From3D("BatchUpdateChart", data, Vector3d.Zero, Fixed64.One);
        PathManager.Register(chart);

        Voxel middleVoxel = TestRequire.VoxelAt(new Vector3d(1, 0, 0));
        Assert.False(middleVoxel.TryGetPartition<VolumeChartPartition>(out _));

        int changedCount = PathManager.ApplyChartUpdates(
            chart.Name,
            new[]
            {
                new NavigationChartCellUpdate(1, 0, 0, NavigationChartCell.Gas),
                new NavigationChartCellUpdate(0, 0, 0, NavigationChartCell.Gas),
                new NavigationChartCellUpdate(10, 0, 0, NavigationChartCell.Gas)
            });

        Assert.Equal(1, changedCount);
        VolumeChartPartition middleVolumePartition = TestRequire.Partition<VolumeChartPartition>(middleVoxel);
        Assert.True(middleVolumePartition.SupportsMedium(TraversalMedium.Gas));
    }

    [Fact]
    public void ChartUpdateApis_ShouldRejectUnknownChartsOutOfBoundsUpdates_AndNullBatches()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        NavigationChartCell[,,] data = new NavigationChartCell[1, 1, 1];
        data[0, 0, 0] = NavigationChartCell.Solid;

        var chart = NavigationChart.From3D("ValidationChart", data, Vector3d.Zero, Fixed64.One);
        Assert.True(PathManager.Register(chart));

        Assert.False(PathManager.TryUpdateChartCell("MissingChart", 0, 0, 0, NavigationChartCell.Empty));
        Assert.False(PathManager.TryUpdateChartCell(chart.Name, 10, 0, 0, NavigationChartCell.Empty));
        Assert.False(PathManager.TryUpdateChartCell(chart.Name, new Vector3d(4, 0, 0), NavigationChartCell.Empty));

        Assert.Equal(0, PathManager.ApplyChartUpdates(chart.Name, Array.Empty<NavigationChartCellUpdate>()));
        Assert.Equal(0, PathManager.ApplyChartUpdates("MissingChart", new[]
        {
            new NavigationChartCellUpdate(0, 0, 0, NavigationChartCell.Empty)
        }));
        Assert.Throws<ArgumentNullException>(() => PathManager.ApplyChartUpdates(chart.Name, null!));

        PathManager.UnloadChart(chart.Name);
    }

    [Fact]
    public void TryUpdateChartCell_ShouldReturnFalse_WhenCellIsUnchanged()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        NavigationChartCell[,,] data = new NavigationChartCell[1, 1, 1];
        data[0, 0, 0] = NavigationChartCell.Solid;

        var chart = NavigationChart.From3D("NoOpUpdateChart", data, Vector3d.Zero, Fixed64.One);
        Assert.True(PathManager.Register(chart));

        Assert.False(PathManager.TryUpdateChartCell(chart.Name, 0, 0, 0, NavigationChartCell.Solid));
        Assert.False(PathManager.TryUpdateChartCell(chart.Name, Vector3d.Zero, NavigationChartCell.Solid));
        Assert.True(PathManager.TryGetEffectiveCell(Vector3d.Zero, out NavigationChartCell cell));
        Assert.True(cell.HasSolid);

        PathManager.UnloadChart(chart.Name);
    }

    [Fact]
    public void TryUpdateChartCell_ShouldRemoveResolvedState_WhenLastOwnerBecomesEmpty()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        NavigationChartCell[,,] data = new NavigationChartCell[1, 1, 1];
        data[0, 0, 0] = NavigationChartCell.Solid;

        var chart = NavigationChart.From3D("EmptyRemovalChart", data, Vector3d.Zero, Fixed64.One);
        Assert.True(PathManager.Register(chart));
        Voxel voxel = TestRequire.VoxelAt(Vector3d.Zero);
        Assert.True(voxel.TryGetPartition<SolidChartPartition>(out _));

        Assert.True(PathManager.TryUpdateChartCell(chart.Name, 0, 0, 0, NavigationChartCell.Empty));

        Assert.False(PathManager.TryGetEffectiveCell(Vector3d.Zero, out _));
        Assert.False(PathManager.TryGetEffectiveChartOwner(Vector3d.Zero, out _));
        Assert.False(voxel.TryGetPartition<SolidChartPartition>(out _));
        Assert.False(voxel.TryGetPartition<VolumeChartPartition>(out _));

        PathManager.UnloadChart(chart.Name);
    }

    [Fact]
    public void TryUpdateChartCell_ShouldReevaluateManagedManualTransitions_WhenLocalTraversalSupportChanges()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        bool[,,] data = new bool[1, 2, 1]
        {
            {
                { true },
                { true }
            }
        };

        var chart = NavigationChart.From3D("MutableManualTransitionChart", data, Vector3d.Zero, Fixed64.One);
        Assert.True(PathManager.Register(chart));

        var manual = new TraversalTransition(
            id: "mutable-manual-link",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));

        Assert.True(TraversalTransitionRegistry.Register(manual));
        Assert.True(TraversalTransitionRegistry.IsRegistered(manual.Id));
        Assert.True(TraversalTransitionRegistry.IsActive(manual.Id));

        Assert.True(PathManager.TryUpdateChartCell(chart.Name, 1, 0, 0, NavigationChartCell.Liquid));

        Assert.True(TraversalTransitionRegistry.IsRegistered(manual.Id));
        Assert.False(TraversalTransitionRegistry.IsActive(manual.Id));
        Assert.Empty(TraversalTransitionRegistry.GetIncomingTransitions(new Vector3d(1, 0, 0)));

        Assert.True(PathManager.TryUpdateChartCell(chart.Name, 1, 0, 0, NavigationChartCell.Solid));

        Assert.True(TraversalTransitionRegistry.IsRegistered(manual.Id));
        Assert.True(TraversalTransitionRegistry.IsActive(manual.Id));
        Assert.Single(TraversalTransitionRegistry.GetIncomingTransitions(new Vector3d(1, 0, 0)));

        PathManager.UnloadChart(chart.Name);
    }

    [Fact]
    public void TryUpdateChartCell_ShouldPreserveEffectiveTraversalWhenWinningOwnerFallsBackToEquivalentCell()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        NavigationChart lowPriorityChart = BuildSingleTraversalPointChart(
            "FallbackOwnerLow",
            Vector3d.Zero,
            TraversalMedia.Solid,
            priority: 0);
        NavigationChart highPriorityChart = BuildSingleTraversalPointChart(
            "FallbackOwnerHigh",
            Vector3d.Zero,
            TraversalMedia.Solid,
            priority: 1);

        Assert.True(PathManager.Register(lowPriorityChart));
        Assert.True(PathManager.Register(highPriorityChart));
        string winningOwnerBeforeUpdate = TestRequire.Created(PathManager.TryGetEffectiveChartOwner(Vector3d.Zero, out string? createdwinningOwnerBeforeUpdate), createdwinningOwnerBeforeUpdate);
        Assert.Equal("FallbackOwnerHigh", winningOwnerBeforeUpdate);

        Assert.True(PathManager.TryUpdateChartCell(highPriorityChart.Name, 1, 1, 1, NavigationChartCell.Empty));

        Assert.True(PathManager.TryGetEffectiveCell(Vector3d.Zero, out NavigationChartCell effectiveCell));
        string winningOwnerAfterUpdate = TestRequire.Created(PathManager.TryGetEffectiveChartOwner(Vector3d.Zero, out string? createdwinningOwnerAfterUpdate), createdwinningOwnerAfterUpdate);
        Voxel voxel = TestRequire.VoxelAt(Vector3d.Zero);
        SolidChartPartition partition = TestRequire.Partition<SolidChartPartition>(voxel);

        Assert.True(effectiveCell.HasSolid);
        Assert.False(effectiveCell.HasVolume);
        Assert.Equal("FallbackOwnerLow", winningOwnerAfterUpdate);
        Assert.Equal("FallbackOwnerLow", partition.EffectiveChartOwner);

        PathManager.UnloadChart(highPriorityChart.Name);
        PathManager.UnloadChart(lowPriorityChart.Name);
    }

    [Fact]
    public void RegisterTraversalBuildResult_ShouldRegisterTransitionsAndInitializeChart()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        string[,,] map =
        {
            {
                { "S!" },
                { "L!" }
            }
        };

        TraversalBuildResult buildResult = new TraversalAuthoringMap(
            chartName: "AuthoredBuild",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One).Build();

        Assert.True(PathManager.Register(buildResult));
        NavigationChart chart = TestRequire.Created(PathManager.TryGetNavigationChart(buildResult.Chart.Name, out NavigationChart? createdchart), createdchart);
        Assert.Same(buildResult.Chart, chart);
        Assert.True(chart.IsInitialized);
        Voxel voxel = TestRequire.VoxelAt(Vector3d.Zero);
        Assert.True(voxel.TryGetPartition<SolidChartPartition>(out _));
        Voxel waterVoxel = TestRequire.VoxelAt(new Vector3d(1, 0, 0));
        VolumeChartPartition volumePartition = TestRequire.Partition<VolumeChartPartition>(waterVoxel);
        Assert.True(volumePartition.SupportsMedium(TraversalMedium.Liquid));

        foreach (TraversalTransition transition in buildResult.GeneratedTransitions)
            Assert.True(TraversalTransitionRegistry.IsRegistered(transition.Id));

        PathManager.UnloadChart(chart);
    }

    [Fact]
    public void RegisterTraversalBuildResult_ShouldInitializeBareWaterCells_WithoutHostRules()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        string[,,] map =
        {
            {
                { "L" },
                { "L" }
            }
        };

        TraversalBuildResult buildResult = new TraversalAuthoringMap(
            chartName: "BareWaterBuild",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One).Build();

        Assert.Empty(buildResult.GeneratedTransitions);
        Assert.True(PathManager.Register(buildResult));
        Assert.True(VolumePathRequest.TryCreate(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            out VolumePathRequest? request,
            medium: TraversalMedium.Liquid));
        TestRequire.NotNull(request);

        PathManager.UnloadChart(buildResult.Chart);
    }

    [Fact]
    public void RegisterTraversalBuildResult_ShouldKeepEquivalentManualTransitionActiveOverGeneratedOne()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        string[,,] map =
        {
            {
                { "S!" },
                { "L!" }
            }
        };

        TraversalBuildResult buildResult = new TraversalAuthoringMap(
            chartName: "ManualPrecedenceBuild",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One).Build();

        TraversalTransition generated = buildResult.GeneratedTransitions[0];
        TraversalTransition manual = new(
            id: "manual-precedence",
            type: generated.Type,
            source: generated.Source,
            destination: generated.Destination,
            pathCostModifier: generated.PathCostModifier,
            isBidirectional: generated.IsBidirectional);

        Assert.True(TraversalTransitionRegistry.Register(manual));
        Assert.True(PathManager.Register(buildResult));
        Assert.True(TraversalTransitionRegistry.IsRegistered(generated.Id));
        Assert.False(TraversalTransitionRegistry.IsActive(generated.Id));
        Assert.True(TraversalTransitionRegistry.IsActive(manual.Id));
        Assert.True(TraversalTransitionRegistry.IsActive(buildResult.GeneratedTransitions[1].Id));

        TraversalTransition[] outgoing = TraversalTransitionRegistry.GetOutgoingTransitions(generated.Source.Position);
        Assert.Single(outgoing);
        Assert.Equal(manual.Id, outgoing[0].Id);

        PathManager.UnloadChart(buildResult.Chart);
    }

    [Fact]
    public void UnloadChart_ShouldUnregisterGeneratedTransitionsFromBuildResult_AndSuppressManualTransitionsWithoutUnregisteringThem()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        string[,,] map =
        {
            {
                { "S!" },
                { "L!" }
            }
        };

        TraversalBuildResult buildResult = new TraversalAuthoringMap(
            chartName: "UnloadGeneratedTransitions",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One).Build();

        TraversalTransition generated = buildResult.GeneratedTransitions[0];
        TraversalTransition manual = new(
            id: "manual-survives-unload",
            type: generated.Type,
            source: generated.Source,
            destination: generated.Destination,
            pathCostModifier: generated.PathCostModifier,
            isBidirectional: generated.IsBidirectional);

        Assert.True(TraversalTransitionRegistry.Register(manual));
        Assert.True(PathManager.Register(buildResult));

        PathManager.UnloadChart(buildResult.Chart);

        Assert.False(PathManager.IsChartRegistered(buildResult.Chart.Name));
        Assert.False(TraversalTransitionRegistry.IsRegistered(buildResult.GeneratedTransitions[0].Id));
        Assert.False(TraversalTransitionRegistry.IsRegistered(buildResult.GeneratedTransitions[1].Id));
        Assert.True(TraversalTransitionRegistry.IsRegistered(manual.Id));
        Assert.False(TraversalTransitionRegistry.IsActive(manual.Id));
    }

    [Fact]
    public void UnloadUninitializedChart_ShouldUnregisterGeneratedTransitionsFromBuildResult()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        string[,,] map =
        {
            {
                { "S!" },
                { "L!" }
            }
        };

        TraversalBuildResult buildResult = new TraversalAuthoringMap(
            chartName: "UnloadUninitializedGeneratedTransitions",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One).Build();

        Assert.True(PathManager.Register(buildResult, initializeChart: false));
        Assert.True(TraversalTransitionRegistry.IsRegistered(buildResult.GeneratedTransitions[0].Id));
        Assert.True(TraversalTransitionRegistry.IsRegistered(buildResult.GeneratedTransitions[1].Id));

        PathManager.UnloadChart(buildResult.Chart);

        Assert.False(PathManager.IsChartRegistered(buildResult.Chart.Name));
        Assert.False(TraversalTransitionRegistry.IsRegistered(buildResult.GeneratedTransitions[0].Id));
        Assert.False(TraversalTransitionRegistry.IsRegistered(buildResult.GeneratedTransitions[1].Id));
    }

    [Fact]
    public void AuthoredGas_ShouldRestrictGasTraversalToAuthoredVoxels()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        Voxel unAuthoredVoxel = TestRequire.VoxelAt(Vector3d.Zero);
        Assert.False(VolumeVoxelFinder.IsTraversable(unAuthoredVoxel, Fixed64.One, TraversalMedium.Gas));

        string[,,] map =
        {
            {
                { "G" }
            }
        };

        TraversalBuildResult buildResult = new TraversalAuthoringMap(
            chartName: "AuthoredOpenOnly",
            sourceMap: map,
            minBounds: new Vector3d(1, 0, 0),
            interval: Fixed64.One).Build();

        Assert.True(PathManager.Register(buildResult));
        Voxel authoredOpenVoxel = TestRequire.VoxelAt(new Vector3d(1, 0, 0));
        Assert.True(VolumeVoxelFinder.IsTraversable(authoredOpenVoxel, Fixed64.One, TraversalMedium.Gas));
        Assert.False(VolumeVoxelFinder.IsTraversable(unAuthoredVoxel, Fixed64.One, TraversalMedium.Gas));

        PathManager.UnloadChart(buildResult.Chart);
    }

    [Fact]
    public void OpenVoxelRule_ShouldRequireTrailblazerPartitionPresence()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        VolumeMediumRules.SetGasVoxelRule(static voxel =>
            voxel.WorldPosition == Vector3d.Zero
            || voxel.WorldPosition == new Vector3d(1, 0, 0));

        Voxel unpartitionedVoxel = TestRequire.VoxelAt(Vector3d.Zero);
        Assert.False(VolumeVoxelFinder.IsTraversable(unpartitionedVoxel, Fixed64.One, TraversalMedium.Gas));

        PathTestFactory.RegisterSingleWalkablePoint("OpenRuleSurfaceA", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("OpenRuleSurfaceB", new Vector3d(1, 0, 0));

        Assert.True(VolumeVoxelFinder.IsTraversable(unpartitionedVoxel, Fixed64.One, TraversalMedium.Gas));
        Assert.True(VolumePathRequest.TryCreate(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            out VolumePathRequest? request,
            medium: TraversalMedium.Gas));
        TestRequire.NotNull(request);

        PathManager.UnloadChart("OpenRuleSurfaceA");
        PathManager.UnloadChart("OpenRuleSurfaceB");
    }

    [Fact]
    public void PathManagerReset_ShouldClearOpenAndWaterVoxelRules()
    {
        VolumeMediumRules.SetGasVoxelRule(static _ => true);
        VolumeMediumRules.SetLiquidVoxelRule(static _ => true);

        Assert.True(VolumeMediumRules.HasGasVoxelRule);
        Assert.True(VolumeMediumRules.HasLiquidVoxelRule);

        PathManager.Reset();

        Assert.False(VolumeMediumRules.HasGasVoxelRule);
        Assert.False(VolumeMediumRules.HasLiquidVoxelRule);
    }

    [Fact]
    public void AuthoredVolume_ShouldRemainValid_WhenMatchingHostRuleIsRemoved()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        PathTestFactory.RegisterGeneratedVolumePoint(Vector3d.Zero, TraversalMedium.Gas, "AuthoredOpenAuthority");

        Voxel voxel = TestRequire.VoxelAt(Vector3d.Zero);
        Assert.True(VolumeVoxelFinder.IsTraversable(voxel, Fixed64.One, TraversalMedium.Gas));

        VolumeMediumRules.SetGasVoxelRule(static candidate =>
            candidate != null
            && candidate.WorldPosition == Vector3d.Zero);

        Assert.True(VolumeVoxelFinder.IsTraversable(voxel, Fixed64.One, TraversalMedium.Gas));

        VolumeMediumRules.ClearGasVoxelRule();

        Assert.True(VolumeVoxelFinder.IsTraversable(voxel, Fixed64.One, TraversalMedium.Gas));
    }

    [Fact]
    public void HostLiquidRule_ShouldSupplementAuthoredGas_WithoutRemovingGasTraversal()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        PathTestFactory.RegisterGeneratedVolumePoint(Vector3d.Zero, TraversalMedium.Gas, "AuthoredOpenPlusWater");

        Voxel voxel = TestRequire.VoxelAt(Vector3d.Zero);
        Assert.True(VolumeVoxelFinder.IsTraversable(voxel, Fixed64.One, TraversalMedium.Gas));
        Assert.False(VolumeVoxelFinder.IsTraversable(voxel, Fixed64.One, TraversalMedium.Liquid));

        VolumeMediumRules.SetLiquidVoxelRule(static candidate =>
            candidate != null
            && candidate.WorldPosition == Vector3d.Zero);

        Assert.True(VolumeVoxelFinder.IsTraversable(voxel, Fixed64.One, TraversalMedium.Gas));
        Assert.True(VolumeVoxelFinder.IsTraversable(voxel, Fixed64.One, TraversalMedium.Liquid));

        VolumeMediumRules.ClearLiquidVoxelRule();

        Assert.True(VolumeVoxelFinder.IsTraversable(voxel, Fixed64.One, TraversalMedium.Gas));
        Assert.False(VolumeVoxelFinder.IsTraversable(voxel, Fixed64.One, TraversalMedium.Liquid));
    }

    [Fact]
    public void SolidChartPartitionOnlyVoxel_ShouldLoseSupplementalVolumeMembership_WhenHostRuleIsRemoved()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        PathTestFactory.RegisterSingleWalkablePoint("HostOnlyOpenStart", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("HostOnlyOpenEnd", new Vector3d(1, 0, 0));

        Voxel startVoxel = TestRequire.VoxelAt(Vector3d.Zero);
        Voxel endVoxel = TestRequire.VoxelAt(new Vector3d(1, 0, 0));
        Assert.False(VolumeVoxelFinder.IsTraversable(startVoxel, Fixed64.One, TraversalMedium.Gas));
        Assert.False(VolumeVoxelFinder.IsTraversable(endVoxel, Fixed64.One, TraversalMedium.Gas));
        Assert.Null(VolumePathRequest.Create(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            medium: TraversalMedium.Gas));

        VolumeMediumRules.SetGasVoxelRule(static voxel =>
            voxel != null
            && (voxel.WorldPosition == Vector3d.Zero
            || voxel.WorldPosition == new Vector3d(1, 0, 0)));

        Assert.True(VolumeVoxelFinder.IsTraversable(startVoxel, Fixed64.One, TraversalMedium.Gas));
        Assert.True(VolumeVoxelFinder.IsTraversable(endVoxel, Fixed64.One, TraversalMedium.Gas));
        Assert.NotNull(VolumePathRequest.Create(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            medium: TraversalMedium.Gas));

        VolumeMediumRules.ClearGasVoxelRule();

        Assert.False(VolumeVoxelFinder.IsTraversable(startVoxel, Fixed64.One, TraversalMedium.Gas));
        Assert.False(VolumeVoxelFinder.IsTraversable(endVoxel, Fixed64.One, TraversalMedium.Gas));
        Assert.Null(VolumePathRequest.Create(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            medium: TraversalMedium.Gas));
    }

    [Fact]
    public void UnrelatedChartUnload_ShouldNotInvalidateUnrelatedAStarCache()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        bool[,,] data = new bool[1, 3, 1]
        {
            {
                { true },
                { true },
                { true }
            }
        };

        PathTestFactory.RegisterFromData("AStarCacheChart", data, Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("UnrelatedAStarChart", new Vector3d(-3, 0, -3));

        AStarPathRequest request = TestRequire.NotNull(AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean));
        AStarGuide guide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out AStarGuide? createdGuide),
            createdGuide);
        Assert.NotNull(guide);
        PathGuideFactory.ReturnGuide(guide);

        Assert.Equal(1, PathGuideFactory.ActiveAStarGuideCount);

        PathManager.UnloadChart("UnrelatedAStarChart");

        Assert.Equal(1, PathGuideFactory.ActiveAStarGuideCount);

        PathManager.UnloadChart("AStarCacheChart");

        Assert.Equal(0, PathGuideFactory.ActiveAStarGuideCount);
    }

    [Fact]
    public void UnrelatedChartUnload_ShouldNotInvalidateUnrelatedAuthoredVolumeCache()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        NavigationChartCell[,,] data = new NavigationChartCell[1, 3, 1];
        data[0, 0, 0] = new NavigationChartCell(TraversalMedia.Gas);
        data[0, 1, 0] = new NavigationChartCell(TraversalMedia.Gas);
        data[0, 2, 0] = new NavigationChartCell(TraversalMedia.Gas);

        PathManager.Register(NavigationChart.From3D("VolumeCacheChart", data, Vector3d.Zero, Fixed64.One));
        PathManager.InitializeChart("VolumeCacheChart");
        PathTestFactory.RegisterSingleWalkablePoint("UnrelatedVolumeChart", new Vector3d(-3, 0, -3));

        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            medium: TraversalMedium.Gas));
        VolumeGuide guide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out VolumeGuide? createdGuide),
            createdGuide);
        Assert.NotNull(guide);
        PathGuideFactory.ReturnGuide(guide);

        Assert.Equal(1, PathGuideFactory.ActiveVolumeGuideCount);

        PathManager.UnloadChart("UnrelatedVolumeChart");

        Assert.Equal(1, PathGuideFactory.ActiveVolumeGuideCount);

        PathManager.UnloadChart("VolumeCacheChart");

        Assert.Equal(0, PathGuideFactory.ActiveVolumeGuideCount);
    }

    [Fact]
    public void VolumeCache_ShouldTrackSurfaceChartDependencies_WhenHostRuleUsesSolidChartPartitions()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        bool[,,] data = new bool[1, 3, 1]
        {
            {
                { true },
                { true },
                { true }
            }
        };

        PathTestFactory.RegisterFromData("SurfaceBackedVolumeChart", data, Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("UnrelatedSurfaceBackedVolumeChart", new Vector3d(-3, 0, -3));
        VolumeMediumRules.SetGasVoxelRule(static voxel =>
            voxel != null
            && voxel.HasPartition<SolidChartPartition>());

        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            medium: TraversalMedium.Gas));
        VolumeGuide guide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out VolumeGuide? createdGuide),
            createdGuide);
        Assert.NotNull(guide);
        PathGuideFactory.ReturnGuide(guide);

        Assert.Equal(1, PathGuideFactory.ActiveVolumeGuideCount);

        PathManager.UnloadChart("UnrelatedSurfaceBackedVolumeChart");

        Assert.Equal(1, PathGuideFactory.ActiveVolumeGuideCount);

        PathManager.UnloadChart("SurfaceBackedVolumeChart");

        Assert.Equal(0, PathGuideFactory.ActiveVolumeGuideCount);
    }

    [Fact]
    public void OverlappingChartInitialize_ShouldInvalidateOnlyDependentAStarCacheEntries()
    {
        var config = new GridConfiguration(new Vector3d(-8, 0, -4), new Vector3d(8, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        bool[,,] data = CreateThreeVoxelLine();

        PathTestFactory.RegisterFromData("OverlappedAStarChart", data, Vector3d.Zero);
        PathTestFactory.RegisterFromData("UnrelatedAStarChart", data, new Vector3d(-6, 0, 0));

        AStarPathRequest overlappedRequest = TestRequire.NotNull(AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean));
        AStarPathRequest unrelatedRequest = TestRequire.NotNull(AStarPathRequest.Create(
            new Vector3d(-6, 0, 0),
            new Vector3d(-4, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean));
        AStarGuide overlappedGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(overlappedRequest, out AStarGuide? createdOverlappedGuide),
            createdOverlappedGuide);
        AStarGuide unrelatedGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(unrelatedRequest, out AStarGuide? createdUnrelatedGuide),
            createdUnrelatedGuide);

        PathGuideFactory.ReturnGuide(overlappedGuide);
        PathGuideFactory.ReturnGuide(unrelatedGuide);

        Assert.Equal(2, PathGuideFactory.ActiveAStarGuideCount);

        PathManager.Register(PathTestFactory.BuildSinglePointMap("OverlapAStarChart", new Vector3d(1, 0, 0)));
        PathManager.InitializeChart("OverlapAStarChart");

        Assert.Equal(1, PathGuideFactory.ActiveAStarGuideCount);

        PathManager.UnloadChart("UnrelatedAStarChart");

        Assert.Equal(0, PathGuideFactory.ActiveAStarGuideCount);
    }

    [Fact]
    public void HiddenChartUpdate_ShouldNotInvalidateAStarCache_WhenEffectiveStateDoesNotChange()
    {
        var config = new GridConfiguration(new Vector3d(-8, 0, -4), new Vector3d(8, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        bool[,,] data = CreateThreeVoxelLine();

        PathManager.Register(NavigationChart.From3D("DominantAStarChart", data, Vector3d.Zero, Fixed64.One, priority: 1));
        PathManager.Register(NavigationChart.From3D("UnrelatedAStarChart", data, new Vector3d(-6, 0, 0), Fixed64.One));
        PathManager.Register(NavigationChart.From3D(
            "HiddenAStarChart",
            new NavigationChartCell[3, 3, 3],
            new Vector3d(0, -1, -1),
            Fixed64.One));

        AStarPathRequest overlappedRequest = TestRequire.NotNull(AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean));
        AStarPathRequest unrelatedRequest = TestRequire.NotNull(AStarPathRequest.Create(
            new Vector3d(-6, 0, 0),
            new Vector3d(-4, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean));
        AStarGuide overlappedGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(overlappedRequest, out AStarGuide? createdOverlappedGuide),
            createdOverlappedGuide);
        AStarGuide unrelatedGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(unrelatedRequest, out AStarGuide? createdUnrelatedGuide),
            createdUnrelatedGuide);

        PathGuideFactory.ReturnGuide(overlappedGuide);
        PathGuideFactory.ReturnGuide(unrelatedGuide);

        Assert.Equal(2, PathGuideFactory.ActiveAStarGuideCount);

        Assert.True(PathManager.TryUpdateChartCell("HiddenAStarChart", 1, 1, 1, NavigationChartCell.Solid));

        Assert.Equal(2, PathGuideFactory.ActiveAStarGuideCount);
    }

    [Fact]
    public void EffectiveChartUpdate_ShouldInvalidateOnlyDependentAStarCacheEntries()
    {
        var config = new GridConfiguration(new Vector3d(-8, 0, -4), new Vector3d(8, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        bool[,,] data = CreateThreeVoxelLine();

        PathManager.Register(NavigationChart.From3D("MutableAStarChart", data, Vector3d.Zero, Fixed64.One));
        PathManager.Register(NavigationChart.From3D("UnrelatedAStarChart", data, new Vector3d(-6, 0, 0), Fixed64.One));
        PathManager.Register(NavigationChart.From3D(
            "DynamicWinnerChart",
            new NavigationChartCell[3, 3, 3],
            new Vector3d(0, -1, -1),
            Fixed64.One,
            priority: 1));

        AStarPathRequest overlappedRequest = TestRequire.NotNull(AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean));
        AStarPathRequest unrelatedRequest = TestRequire.NotNull(AStarPathRequest.Create(
            new Vector3d(-6, 0, 0),
            new Vector3d(-4, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean));
        AStarGuide overlappedGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(overlappedRequest, out AStarGuide? createdOverlappedGuide),
            createdOverlappedGuide);
        AStarGuide unrelatedGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(unrelatedRequest, out AStarGuide? createdUnrelatedGuide),
            createdUnrelatedGuide);

        PathGuideFactory.ReturnGuide(overlappedGuide);
        PathGuideFactory.ReturnGuide(unrelatedGuide);

        Assert.Equal(2, PathGuideFactory.ActiveAStarGuideCount);

        Assert.True(PathManager.TryUpdateChartCell("DynamicWinnerChart", 1, 1, 1, NavigationChartCell.Solid));

        Assert.Equal(1, PathGuideFactory.ActiveAStarGuideCount);
    }

    [Fact]
    public void OverlappingChartInitialize_ShouldInvalidateOnlyDependentFlowFieldCacheEntries()
    {
        var config = new GridConfiguration(new Vector3d(-8, 0, -4), new Vector3d(8, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        bool[,,] data = CreateThreeVoxelLine();

        PathTestFactory.RegisterFromData("OverlappedFlowChart", data, Vector3d.Zero);
        PathTestFactory.RegisterFromData("UnrelatedFlowChart", data, new Vector3d(-6, 0, 0));

        FlowFieldPathRequest overlappedRequest = TestRequire.NotNull(FlowFieldPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One));
        FlowFieldPathRequest unrelatedRequest = TestRequire.NotNull(FlowFieldPathRequest.Create(
            new Vector3d(-6, 0, 0),
            new Vector3d(-4, 0, 0),
            Fixed64.One));
        FlowFieldGuide overlappedGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(overlappedRequest, out FlowFieldGuide? createdOverlappedGuide),
            createdOverlappedGuide);
        FlowFieldGuide unrelatedGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(unrelatedRequest, out FlowFieldGuide? createdUnrelatedGuide),
            createdUnrelatedGuide);

        PathGuideFactory.ReturnGuide(overlappedGuide);
        PathGuideFactory.ReturnGuide(unrelatedGuide);

        Assert.Equal(2, PathGuideFactory.ActiveFlowGuideCount);

        PathManager.Register(PathTestFactory.BuildSinglePointMap("OverlapFlowChart", new Vector3d(1, 0, 0)));
        PathManager.InitializeChart("OverlapFlowChart");

        Assert.Equal(1, PathGuideFactory.ActiveFlowGuideCount);

        PathManager.UnloadChart("UnrelatedFlowChart");

        Assert.Equal(0, PathGuideFactory.ActiveFlowGuideCount);
    }

    [Fact]
    public void OverlappingChartInitialize_ShouldInvalidateOnlyDependentVolumeCacheEntries()
    {
        var config = new GridConfiguration(new Vector3d(-8, 0, -4), new Vector3d(8, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        NavigationChartCell[,,] data = CreateThreeVoxelTraversalLine(TraversalMedia.Gas);

        PathManager.Register(NavigationChart.From3D("OverlappedVolumeChart", data, Vector3d.Zero, Fixed64.One));
        PathManager.InitializeChart("OverlappedVolumeChart");
        PathManager.Register(NavigationChart.From3D("UnrelatedVolumeChart", data, new Vector3d(-6, 0, 0), Fixed64.One));
        PathManager.InitializeChart("UnrelatedVolumeChart");

        VolumePathRequest overlappedRequest = TestRequire.NotNull(VolumePathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            medium: TraversalMedium.Gas));
        VolumePathRequest unrelatedRequest = TestRequire.NotNull(VolumePathRequest.Create(
            new Vector3d(-6, 0, 0),
            new Vector3d(-4, 0, 0),
            Fixed64.One,
            medium: TraversalMedium.Gas));
        VolumeGuide overlappedGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(overlappedRequest, out VolumeGuide? createdOverlappedGuide),
            createdOverlappedGuide);
        VolumeGuide unrelatedGuide = TestRequire.Created(
            PathGuideFactory.RequestGuide(unrelatedRequest, out VolumeGuide? createdUnrelatedGuide),
            createdUnrelatedGuide);

        PathGuideFactory.ReturnGuide(overlappedGuide);
        PathGuideFactory.ReturnGuide(unrelatedGuide);

        Assert.Equal(2, PathGuideFactory.ActiveVolumeGuideCount);

        PathManager.Register(BuildSingleTraversalPointChart("OverlapVolumeChart", new Vector3d(1, 0, 0), TraversalMedia.Gas));
        PathManager.InitializeChart("OverlapVolumeChart");

        Assert.Equal(1, PathGuideFactory.ActiveVolumeGuideCount);

        PathManager.UnloadChart("UnrelatedVolumeChart");

        Assert.Equal(0, PathGuideFactory.ActiveVolumeGuideCount);
    }

    [Fact]
    public void RegisterTraversalBuildResult_ShouldRollback_WhenGeneratedTransitionRegistrationFails()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        string[,,] map =
        {
            {
                { "S!" },
                { "L!" }
            }
        };

        TraversalBuildResult buildResult = new TraversalAuthoringMap(
            chartName: "RollbackBuild",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One).Build();

        TraversalTransition preRegisteredTransition = buildResult.GeneratedTransitions[1];
        Assert.True(TraversalTransitionRegistry.Register(preRegisteredTransition));

        Assert.False(PathManager.Register(buildResult));
        Assert.False(PathManager.IsChartRegistered(buildResult.Chart.Name));
        Assert.False(buildResult.Chart.IsInitialized);
        Assert.False(TraversalTransitionRegistry.IsRegistered(buildResult.GeneratedTransitions[0].Id));
        Assert.True(TraversalTransitionRegistry.IsRegistered(preRegisteredTransition.Id));
        Voxel voxel = TestRequire.VoxelAt(Vector3d.Zero);
        Assert.False(voxel.TryGetPartition<SolidChartPartition>(out _));
    }

    [Fact]
    public void ChartUpdate_ShouldPreserveUnrelatedGeneratedTransitions_FromTraversalBuildCharts()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        string[,,] map =
        {
            {
                { "S!" },
                { "L!" },
                { "S" }
            }
        };

        TraversalBuildResult buildResult = new TraversalAuthoringMap(
            chartName: "MutableBuildChart",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One).Build();

        Assert.True(PathManager.Register(buildResult, initializeChart: false));
        Assert.True(TraversalTransitionRegistry.IsRegistered(buildResult.GeneratedTransitions[0].Id));
        Assert.True(TraversalTransitionRegistry.IsRegistered(buildResult.GeneratedTransitions[1].Id));

        Assert.True(PathManager.TryUpdateChartCell(buildResult.Chart.Name, 2, 0, 0, NavigationChartCell.Empty));

        Assert.True(PathManager.IsChartRegistered(buildResult.Chart.Name));
        Assert.True(TraversalTransitionRegistry.IsRegistered(buildResult.GeneratedTransitions[0].Id));
        Assert.True(TraversalTransitionRegistry.IsRegistered(buildResult.GeneratedTransitions[1].Id));
    }

    [Fact]
    public void ChartUpdate_ShouldRegenerateAffectedGeneratedTransitions_FromTraversalBuildCharts()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        string[,,] map =
        {
            {
                { "S!" },
                { "L!" }
            }
        };

        TraversalBuildResult buildResult = new TraversalAuthoringMap(
            chartName: "MutableRegeneratedBuildChart",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One).Build();

        Assert.True(PathManager.Register(buildResult, initializeChart: false));
        Assert.True(TraversalTransitionRegistry.IsRegistered(buildResult.GeneratedTransitions[0].Id));
        Assert.True(TraversalTransitionRegistry.IsRegistered(buildResult.GeneratedTransitions[1].Id));

        Assert.True(PathManager.TryUpdateChartCell(buildResult.Chart.Name, 1, 0, 0, NavigationChartCell.Empty));

        Assert.False(TraversalTransitionRegistry.IsRegistered(buildResult.GeneratedTransitions[0].Id));
        Assert.False(TraversalTransitionRegistry.IsRegistered(buildResult.GeneratedTransitions[1].Id));

        Assert.True(PathManager.TryUpdateChartCell(
            buildResult.Chart.Name,
            1,
            0,
            0,
            new NavigationChartCell(
                TraversalMedia.Liquid,
                generatedTransitionMedia: TraversalMedia.Liquid)));

        Assert.True(TraversalTransitionRegistry.IsRegistered(buildResult.GeneratedTransitions[0].Id));
        Assert.True(TraversalTransitionRegistry.IsRegistered(buildResult.GeneratedTransitions[1].Id));
    }

    [Fact]
    public void ChartUpdate_ShouldGenerateTransitions_ForBuildChartsThatStartedWithoutAny()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        string[,,] map =
        {
            {
                { "S!" },
                { "." },
                { "." }
            }
        };

        TraversalBuildResult buildResult = new TraversalAuthoringMap(
            chartName: "MutableLatentBuildChart",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One).Build();

        Assert.Empty(buildResult.GeneratedTransitions);
        Assert.True(PathManager.Register(buildResult, initializeChart: false));

        Assert.True(PathManager.TryUpdateChartCell(
            buildResult.Chart.Name,
            1,
            0,
            0,
            new NavigationChartCell(
                TraversalMedia.Liquid,
                generatedTransitionMedia: TraversalMedia.Liquid)));

        Assert.Empty(TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d.Zero));
        Assert.Empty(TraversalTransitionRegistry.GetIncomingTransitions(Vector3d.Zero));

        PathManager.InitializeChart(buildResult.Chart.Name);

        TraversalTransition[] outgoing = TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d.Zero);
        TraversalTransition[] incoming = TraversalTransitionRegistry.GetIncomingTransitions(Vector3d.Zero);

        Assert.Single(outgoing);
        Assert.Equal(TraversalTransitionType.SwimEntry, outgoing[0].Type);
        Assert.Single(incoming);
        Assert.Equal(TraversalTransitionType.SwimExit, incoming[0].Type);
    }

    [Fact]
    public void Overlap_ShouldSuppressPlainChartGeneratedTransitions_ThenReactivateThemOnUnload()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        NavigationChartCell[,,] generatedCells =
        {
            {
                { new NavigationChartCell(TraversalMedia.Solid, generatedTransitionMedia: TraversalMedia.Solid) },
                { new NavigationChartCell(TraversalMedia.Liquid, generatedTransitionMedia: TraversalMedia.Liquid) }
            }
        };

        NavigationChartCell[,,] overrideCells =
        {
            {
                { new NavigationChartCell(TraversalMedia.Solid) },
                { new NavigationChartCell(TraversalMedia.Liquid) }
            }
        };

        NavigationChart generatedChart = NavigationChart.From3D(
            "PlainGeneratedChart",
            generatedCells,
            Vector3d.Zero,
            Fixed64.One,
            priority: 0);
        NavigationChart overrideChart = NavigationChart.From3D(
            "PlainGeneratedOverride",
            overrideCells,
            Vector3d.Zero,
            Fixed64.One,
            priority: 1);

        Assert.True(PathManager.Register(generatedChart));

        TraversalTransition[] beforeOverride = TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d.Zero);
        Assert.Single(beforeOverride);
        Assert.Equal(TraversalTransitionType.SwimEntry, beforeOverride[0].Type);

        Assert.True(PathManager.Register(overrideChart));
        Assert.Empty(TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d.Zero));
        Assert.Empty(TraversalTransitionRegistry.GetIncomingTransitions(new Vector3d(1, 0, 0)));

        PathManager.UnloadChart(overrideChart);

        TraversalTransition[] afterUnload = TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d.Zero);
        Assert.Single(afterUnload);
        Assert.Equal(TraversalTransitionType.SwimEntry, afterUnload[0].Type);
    }

    [Fact]
    public void Overlap_ShouldSuppressManagedManualTransitions_ThenReactivateThemOnUnload()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        PathTestFactory.RegisterSingleWalkablePoint("ManualManagedSource", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("ManualManagedDestination", new Vector3d(1, 0, 0));

        var manual = new TraversalTransition(
            id: "managed-manual-link",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));

        Assert.True(TraversalTransitionRegistry.Register(manual));
        Assert.True(TraversalTransitionRegistry.IsRegistered(manual.Id));
        Assert.True(TraversalTransitionRegistry.IsActive(manual.Id));

        NavigationChart overrideChart = BuildSingleTraversalPointChart(
            "ManagedManualOverride",
            Vector3d.Zero,
            TraversalMedia.Liquid,
            priority: 1);
        Assert.True(PathManager.Register(overrideChart));

        Assert.True(TraversalTransitionRegistry.IsRegistered(manual.Id));
        Assert.False(TraversalTransitionRegistry.IsActive(manual.Id));

        PathManager.UnloadChart(overrideChart);

        Assert.True(TraversalTransitionRegistry.IsRegistered(manual.Id));
        Assert.True(TraversalTransitionRegistry.IsActive(manual.Id));
    }

    [Fact]
    public void GlobalGridReset_ShouldHardResetPathManagerChartsAndTransitions()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TrailblazerWorldManager.TryAddGrid(config, out _);

        NavigationChart sourceChart = PathTestFactory.RegisterSingleWalkablePoint("ExternalResetSource", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("ExternalResetDestination", new Vector3d(1, 0, 0));

        var manual = new TraversalTransition(
            id: "external-reset-transition",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));

        Assert.True(TraversalTransitionRegistry.Register(manual));
        Assert.True(PathManager.IsChartRegistered(sourceChart.Name));
        Assert.True(TraversalTransitionRegistry.IsRegistered(manual.Id));

        TrailblazerWorldManager.Reset();

        Assert.False(PathManager.IsChartRegistered(sourceChart.Name));
        Assert.False(TraversalTransitionRegistry.IsRegistered(manual.Id));
        Assert.Empty(PathManager.AllCharts);
    }

    [Fact]
    public void GlobalGridRemoveAndAdd_ShouldRebuildInitializedCharts_AndReactivateManagedManualTransitions()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        Assert.True(TrailblazerWorldManager.TryAddGrid(config, out ushort gridIndex));

        NavigationChart sourceChart = PathTestFactory.RegisterSingleWalkablePoint("ExternalRemoveSource", Vector3d.Zero);
        NavigationChart destinationChart = PathTestFactory.RegisterSingleWalkablePoint("ExternalRemoveDestination", new Vector3d(1, 0, 0));

        var manual = new TraversalTransition(
            id: "external-remove-transition",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)));

        Assert.True(TraversalTransitionRegistry.Register(manual));
        Assert.True(TraversalTransitionRegistry.IsActive(manual.Id));
        Voxel sourceVoxel = TestRequire.VoxelAt(Vector3d.Zero);
        Assert.True(sourceVoxel.TryGetPartition<SolidChartPartition>(out _));

        Assert.True(TrailblazerWorldManager.TryRemoveGrid(gridIndex));
        FlushExternalGridBridge();

        Assert.True(PathManager.IsChartRegistered(sourceChart.Name));
        Assert.True(PathManager.IsChartRegistered(destinationChart.Name));
        NavigationChart removedSourceChart = TestRequire.Created(PathManager.TryGetNavigationChart(sourceChart.Name, out NavigationChart? createdremovedSourceChart), createdremovedSourceChart);
        Assert.True(removedSourceChart.IsInitialized);
        Assert.True(TraversalTransitionRegistry.IsRegistered(manual.Id));
        Assert.False(TraversalTransitionRegistry.IsActive(manual.Id));
        Assert.False(PathManager.TryGetEffectiveCell(Vector3d.Zero, out _));

        Assert.True(TrailblazerWorldManager.TryAddGrid(config, out _));
        FlushExternalGridBridge();

        Assert.True(TraversalTransitionRegistry.IsRegistered(manual.Id));
        Assert.True(TraversalTransitionRegistry.IsActive(manual.Id));
        Assert.True(PathManager.TryGetEffectiveCell(Vector3d.Zero, out NavigationChartCell restoredCell));
        Assert.True(restoredCell.HasSolid);
        Voxel rebuiltSourceVoxel = TestRequire.VoxelAt(Vector3d.Zero);
        Assert.True(rebuiltSourceVoxel.TryGetPartition<SolidChartPartition>(out _));
    }

    [Fact]
    public void GlobalGridRemoveAndAdd_ShouldSuppressGeneratedTransitions_AndReactivateThemAfterRebuild()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        Assert.True(TrailblazerWorldManager.TryAddGrid(config, out ushort gridIndex));

        NavigationChartCell[,,] generatedCells =
        {
            {
                { new NavigationChartCell(TraversalMedia.Solid, generatedTransitionMedia: TraversalMedia.Solid) },
                { new NavigationChartCell(TraversalMedia.Liquid, generatedTransitionMedia: TraversalMedia.Liquid) }
            }
        };

        NavigationChart generatedChart = NavigationChart.From3D(
            "ExternalGeneratedChart",
            generatedCells,
            Vector3d.Zero,
            Fixed64.One,
            priority: 0);

        Assert.True(PathManager.Register(generatedChart));

        TraversalTransition[] beforeRemove = TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d.Zero);
        Assert.Single(beforeRemove);
        string generatedTransitionId = beforeRemove[0].Id;
        Assert.Equal(TraversalTransitionType.SwimEntry, beforeRemove[0].Type);

        Assert.True(TrailblazerWorldManager.TryRemoveGrid(gridIndex));
        FlushExternalGridBridge();

        Assert.True(PathManager.IsChartRegistered(generatedChart.Name));
        Assert.True(TraversalTransitionRegistry.IsRegistered(generatedTransitionId));
        Assert.False(TraversalTransitionRegistry.IsActive(generatedTransitionId));
        Assert.Empty(TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d.Zero));

        Assert.True(TrailblazerWorldManager.TryAddGrid(config, out _));
        FlushExternalGridBridge();

        TraversalTransition[] afterAdd = TraversalTransitionRegistry.GetOutgoingTransitions(Vector3d.Zero);
        Assert.Single(afterAdd);
        Assert.Equal(generatedTransitionId, afterAdd[0].Id);
        Assert.Equal(TraversalTransitionType.SwimEntry, afterAdd[0].Type);
    }

    [Fact]
    public void GlobalGridChange_ShouldRebuildOnlyIntersectingCharts()
    {
        Assert.True(TrailblazerWorldManager.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)),
            out ushort leftGridIndex));
        Assert.True(TrailblazerWorldManager.TryAddGrid(
            new GridConfiguration(new Vector3d(10, -4, -4), new Vector3d(18, 4, 4)),
            out ushort rightGridIndex));

        NavigationChart leftChart = PathTestFactory.RegisterSingleWalkablePoint("ChangedLeftChart", Vector3d.Zero);
        NavigationChart rightChart = PathTestFactory.RegisterSingleWalkablePoint("ChangedRightChart", new Vector3d(10, 0, 0));

        Assert.True(leftChart.IsInitialized);
        Assert.True(rightChart.IsInitialized);
        Voxel leftVoxel = TestRequire.VoxelAt(Vector3d.Zero);
        Voxel rightVoxel = TestRequire.VoxelAt(new Vector3d(10, 0, 0));
        Assert.True(leftVoxel.TryGetPartition<SolidChartPartition>(out _));
        Assert.True(rightVoxel.TryGetPartition<SolidChartPartition>(out _));

        TrailblazerWorldManager.IncrementGridVersion(rightGridIndex, false);
        FlushExternalGridBridge();

        Assert.True(leftChart.IsInitialized);
        Assert.True(rightChart.IsInitialized);
        Assert.True(leftVoxel.TryGetPartition<SolidChartPartition>(out _));
        Assert.True(rightVoxel.TryGetPartition<SolidChartPartition>(out _));
        Assert.True(PathManager.TryGetEffectiveCell(Vector3d.Zero, out NavigationChartCell leftCell));
        Assert.True(leftCell.HasSolid);
        Assert.True(PathManager.TryGetEffectiveCell(new Vector3d(10, 0, 0), out NavigationChartCell rightCell));
        Assert.True(rightCell.HasSolid);
        Assert.Equal(leftGridIndex, leftVoxel.GridIndex);
    }

    [Fact]
    public void GlobalGridChange_ShouldIgnoreBoundsWithoutIntersectingCharts()
    {
        Assert.True(TrailblazerWorldManager.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)),
            out _));
        Assert.True(TrailblazerWorldManager.TryAddGrid(
            new GridConfiguration(new Vector3d(20, -4, -4), new Vector3d(28, 4, 4)),
            out ushort farGridIndex));

        NavigationChart leftChart = PathTestFactory.RegisterSingleWalkablePoint("UnchangedChart", Vector3d.Zero);

        Assert.True(leftChart.IsInitialized);
        Voxel leftVoxel = TestRequire.VoxelAt(Vector3d.Zero);
        Assert.True(leftVoxel.TryGetPartition<SolidChartPartition>(out _));

        TrailblazerWorldManager.IncrementGridVersion(farGridIndex, false);
        FlushExternalGridBridge();

        Assert.True(leftChart.IsInitialized);
        Assert.True(leftVoxel.TryGetPartition<SolidChartPartition>(out _));
        Assert.True(PathManager.TryGetEffectiveCell(Vector3d.Zero, out NavigationChartCell cell));
        Assert.True(cell.HasSolid);
    }

    [Fact]
    public void GlobalGridChange_ShouldNotInitializeDeferredIntersectingCharts()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        Assert.True(TrailblazerWorldManager.TryAddGrid(config, out ushort gridIndex));

        NavigationChart deferredChart = PathTestFactory.BuildSinglePointMap("DeferredGridChangeChart", Vector3d.Zero);
        Assert.True(PathManager.Register(deferredChart, initializeChart: false));
        Assert.False(deferredChart.IsInitialized);
        Voxel voxel = TestRequire.VoxelAt(Vector3d.Zero);
        Assert.False(voxel.TryGetPartition<SolidChartPartition>(out _));

        TrailblazerWorldManager.IncrementGridVersion(gridIndex, false);
        FlushExternalGridBridge();

        Assert.False(deferredChart.IsInitialized);
        Assert.False(voxel.TryGetPartition<SolidChartPartition>(out _));
        Assert.False(PathManager.TryGetEffectiveCell(Vector3d.Zero, out _));

        PathManager.UnloadChart(deferredChart);
    }

    [Fact]
    public void TryUpdateChartCell_ShouldPersistChangesWhileGridIsMissing_AndApplyThemWhenGridReturns()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        Assert.True(TrailblazerWorldManager.TryAddGrid(config, out ushort gridIndex));

        NavigationChartCell[,,] data = new NavigationChartCell[1, 1, 1];
        data[0, 0, 0] = NavigationChartCell.Solid;

        var chart = NavigationChart.From3D("DeferredGridUpdateChart", data, Vector3d.Zero, Fixed64.One);
        Assert.True(PathManager.Register(chart));
        Assert.True(chart.IsInitialized);

        Assert.True(TrailblazerWorldManager.TryRemoveGrid(gridIndex));
        FlushExternalGridBridge();
        Assert.True(chart.IsInitialized);
        Assert.False(PathManager.TryGetEffectiveCell(Vector3d.Zero, out _));

        Assert.True(PathManager.TryUpdateChartCell(chart.Name, 0, 0, 0, NavigationChartCell.Liquid));

        Assert.True(TrailblazerWorldManager.TryAddGrid(config, out _));
        FlushExternalGridBridge();
        Assert.True(PathManager.TryGetEffectiveCell(Vector3d.Zero, out NavigationChartCell restoredCell));
        Assert.False(restoredCell.HasSolid);
        Assert.True(restoredCell.SupportsMedium(TraversalMedium.Liquid));
        Voxel rebuiltVoxel = TestRequire.VoxelAt(Vector3d.Zero);
        Assert.False(rebuiltVoxel.TryGetPartition<SolidChartPartition>(out _));
        VolumeChartPartition volumePartition = TestRequire.Partition<VolumeChartPartition>(rebuiltVoxel);
        Assert.True(volumePartition.SupportsMedium(TraversalMedium.Liquid));
    }

    [Fact]
    public void TryGetMaxSearchSize_ShouldReturnGridSize_WhenBothVoxelsShareAGrid()
    {
        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4));
        Assert.True(TrailblazerWorldManager.TryAddGrid(config, out _));
        Voxel start = TestRequire.VoxelAt(Vector3d.Zero);
        Voxel end = TestRequire.VoxelAt(new Vector3d(1, 0, 0));
        VoxelGrid grid = TestRequire.Grid(start.GridIndex);

        Assert.True(PathManager.TryGetMaxSearchSize(start, end, out int maxSearchSize));
        Assert.Equal(grid.Size, maxSearchSize);
    }

    [Fact]
    public void TryGetMaxSearchSize_ShouldReturnCombinedSize_WhenVoxelsSpanDifferentGrids()
    {
        Assert.True(TrailblazerWorldManager.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)),
            out _));
        Assert.True(TrailblazerWorldManager.TryAddGrid(
            new GridConfiguration(new Vector3d(10, -4, -4), new Vector3d(18, 4, 4)),
            out _));

        Voxel start = TestRequire.VoxelAt(Vector3d.Zero);
        Voxel end = TestRequire.VoxelAt(new Vector3d(10, 0, 0));
        VoxelGrid startGrid = TestRequire.Grid(start.GridIndex);
        VoxelGrid endGrid = TestRequire.Grid(end.GridIndex);

        Assert.True(PathManager.TryGetMaxSearchSize(start, end, out int maxSearchSize));
        Assert.Equal(startGrid.Size + endGrid.Size, maxSearchSize);
    }

    [Fact]
    public void TryGetMaxSearchSize_ShouldReturnFalse_WhenEitherVoxelGridIsMissing()
    {
        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4));
        Assert.True(TrailblazerWorldManager.TryAddGrid(config, out ushort gridIndex));
        Voxel start = TestRequire.VoxelAt(Vector3d.Zero);
        Voxel end = TestRequire.VoxelAt(new Vector3d(1, 0, 0));

        Assert.True(TrailblazerWorldManager.TryRemoveGrid(gridIndex));

        Assert.False(PathManager.TryGetMaxSearchSize(start, end, out int maxSearchSize));
        Assert.Equal(0, maxSearchSize);
    }

    [Fact]
    public void NeedsPath_ShouldReturnTrue_WhenLineCrossesUnpartitionedVoxels()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        Assert.True(TrailblazerWorldManager.TryAddGrid(config, out _));

        Assert.True(PathManager.NeedsPath(Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One));
    }

    [Fact]
    public void NeedsPath_ShouldReturnFalse_WhenLineIsCoveredByWalkableSurface()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        Assert.True(TrailblazerWorldManager.TryAddGrid(config, out _));

        PathTestFactory.RegisterFromData("NeedsPathWalkableChart", CreateThreeVoxelLine(), Vector3d.Zero);

        Assert.False(PathManager.NeedsPath(Vector3d.Zero, new Vector3d(2, 0, 0), Fixed64.One));

        PathManager.UnloadChart("NeedsPathWalkableChart");
    }

    [Fact]
    public void NeedsPath_ShouldReturnTrue_WhenUnitSizeExceedsPartitionClearance()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        Assert.True(TrailblazerWorldManager.TryAddGrid(config, out _));

        PathTestFactory.RegisterFromData("NeedsPathImpassableChart", CreateThreeVoxelLine(), Vector3d.Zero);

        Assert.True(PathManager.NeedsPath(Vector3d.Zero, new Vector3d(2, 0, 0), (Fixed64)10));

        PathManager.UnloadChart("NeedsPathImpassableChart");
    }

    private static bool[,,] CreateThreeVoxelLine()
    {
        return new bool[1, 3, 1]
        {
            {
                { true },
                { true },
                { true }
            }
        };
    }

    private static NavigationChartCell[,,] CreateThreeVoxelTraversalLine(TraversalMedia traversalMedia)
    {
        NavigationChartCell[,,] data = new NavigationChartCell[1, 3, 1];
        data[0, 0, 0] = new NavigationChartCell(traversalMedia);
        data[0, 1, 0] = new NavigationChartCell(traversalMedia);
        data[0, 2, 0] = new NavigationChartCell(traversalMedia);
        return data;
    }

    private static NavigationChart BuildSingleTraversalPointChart(
        string chartName,
        Vector3d position,
        TraversalMedia traversalMedia,
        int priority = 0)
    {
        Vector3d minBounds = position - new Vector3d(1, 1, 1);
        NavigationChartCell[,,] data = new NavigationChartCell[3, 3, 3];
        data[1, 1, 1] = new NavigationChartCell(traversalMedia);
        return NavigationChart.From3D(chartName, data, minBounds, Fixed64.One, priority);
    }

    private static void FlushExternalGridBridge()
    {
        PathManagerExternalGridBridge.FlushPendingGridChanges();
    }
}
