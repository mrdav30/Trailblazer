using System;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public class PathingNavigationMapTests : IDisposable
{
    public PathingNavigationMapTests()
    {
        TestWorld.Setup();
    }

    public void Dispose()
    {
        PathManager.Reset();

        TestWorld.Reset();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Register_WithInitializeChartFalse_AddsMapWithoutInitializingIt()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TestWorld.World.TryAddGrid(config, out _);

        var map = PathTestFactory.BuildSinglePointMap("TestMap", new Vector3d(0, 0, 0));
        Assert.True(PathManager.Register(map, initializeChart: false));

        Assert.True(PathManager.TryGetNavigationChart("TestMap", out var retrieved));
        Assert.Equal(map, retrieved);
        Assert.False(PathManager.IsChartInitialized(map));
        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(0, 0, 0));
        Assert.False(voxel.TryGetPartition<SolidChartPartition>(out _));

        PathManager.UnloadChart("TestMap");
    }

    [Fact]
    public void Register_InitializesChartByDefault()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TestWorld.World.TryAddGrid(config, out _);

        var map = PathTestFactory.BuildSinglePointMap("InitMap", new Vector3d(0, 0, 0));
        Assert.True(PathManager.Register(map));

        Assert.True(PathManager.IsChartInitialized(map));
        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(0, 0, 0));
        Assert.True(voxel.TryGetPartition<SolidChartPartition>(out _));

        PathManager.UnloadChart("InitMap");
    }

    [Fact]
    public void Register_ShouldRejectDuplicateChartNames_WithoutReplacingTheOriginal()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TestWorld.World.TryAddGrid(config, out _);

        var original = PathTestFactory.BuildSinglePointMap("DuplicateChart", Vector3d.Zero);
        var duplicate = PathTestFactory.BuildSinglePointMap("DuplicateChart", new Vector3d(1, 0, 0));

        Assert.True(PathManager.Register(original, initializeChart: false));
        Assert.False(PathManager.Register(duplicate, initializeChart: false));
        Assert.True(PathManager.TryGetNavigationChart("DuplicateChart", out var retrieved));
        Assert.Same(original, retrieved);
        Assert.False(PathManager.IsChartInitialized(original));
        Assert.False(PathManager.IsChartInitialized(duplicate));

        PathManager.UnloadChart("DuplicateChart");
    }

    [Fact]
    public void Register_ShouldThrowWithoutRegisteringChart_WhenChartIntervalDiffersFromWorldVoxelSize()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TestWorld.World.TryAddGrid(config, out _);

        var chart = NavigationChart.From3D(
            "MismatchedInterval",
            new bool[1, 2, 1] { { { true }, { true } } },
            Vector3d.Zero,
            Fixed64.Half);

        Assert.Throws<ArgumentException>(() => PathManager.Register(chart));

        Assert.False(PathManager.IsChartRegistered(chart.Name));
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
        TestWorld.World.TryAddGrid(config, out _);

        var first = PathTestFactory.BuildSinglePointMap("DeferredA", Vector3d.Zero);
        var second = PathTestFactory.BuildSinglePointMap("DeferredB", new Vector3d(2, 0, 0));

        Assert.True(PathManager.Register(first, initializeChart: false));
        Assert.True(PathManager.Register(second, initializeChart: false));
        Assert.False(PathManager.IsChartInitialized(first));
        Assert.False(PathManager.IsChartInitialized(second));

        PathManager.InitializeAllCharts();

        Assert.True(PathManager.IsChartInitialized(first));
        Assert.True(PathManager.IsChartInitialized(second));
        Voxel firstVoxel = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        Assert.True(firstVoxel.TryGetPartition<SolidChartPartition>(out _));
        Voxel secondVoxel = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(2, 0, 0));
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
        TestWorld.World.TryAddGrid(config, out _);

        var pos = new Vector3d(0, 0, 0);
        var mapA = PathTestFactory.BuildSinglePointMap("MapA", pos);
        var mapB = PathTestFactory.BuildSinglePointMap("MapB", pos);

        PathManager.Register(mapA);
        PathManager.Register(mapB);

        PathManager.InitializeChart("MapA");
        PathManager.InitializeChart("MapB");

        // Validate partition exists and belongs to both
        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, pos);
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
        TestWorld.World.TryAddGrid(config, out _);

        var map = PathTestFactory.BuildSinglePointMap("DuplicateInit", new Vector3d(0, 0, 0));
        PathManager.Register(map);
        PathManager.InitializeChart("DuplicateInit");
        PathManager.InitializeChart("DuplicateInit"); // idempotent

        TestWorld.World.TryGetGridAndVoxel(new Vector3d(0, 0, 0), out _, out Voxel? voxel);
        var count = voxel!.TryGetPartition(out SolidChartPartition? partition) ? 1 : 0;

        Assert.Equal(1, count);
        Assert.True(partition!.BelongsTo("DuplicateInit"));

        PathManager.UnloadChart("DuplicateInit");
    }

    [Fact]
    public void TryGetEffectiveCellAndOwner_ShouldReturnWinningResolvedState()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TestWorld.World.TryAddGrid(config, out _);

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

        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);

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
        TestWorld.World.TryAddGrid(config, out _);

        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        Assert.False(PathManager.TryGetEffectiveCell(voxel.WorldIndex, out _));
        Assert.False(PathManager.TryGetEffectiveChartOwner(voxel.WorldIndex, out _));
    }

    [Fact]
    public void UnloadChart_ShouldClearInitializationStateSoChartCanBeRegisteredAgain()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TestWorld.World.TryAddGrid(config, out _);

        var map = PathTestFactory.BuildSinglePointMap("ReloadableMap", new Vector3d(0, 0, 0));
        PathManager.Register(map);
        PathManager.InitializeChart(map.Name);

        PathManager.UnloadChart(map);

        Assert.False(PathManager.IsChartInitialized(map));
        Assert.True(PathManager.Register(map));

        PathManager.InitializeChart(map.Name);

        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(0, 0, 0));
        Assert.True(voxel.TryGetPartition<SolidChartPartition>(out _));

        PathManager.UnloadChart(map);
    }

    [Fact]
    public void PathManagerReset_ShouldClearInitializationStateSoSameChartInstanceCanBeRegisteredAgain()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TestWorld.World.TryAddGrid(config, out _);

        var map = PathTestFactory.BuildSinglePointMap("ResettableMap", new Vector3d(0, 0, 0));
        Assert.True(PathManager.Register(map));
        Assert.True(PathManager.IsChartInitialized(map));

        PathManager.Reset();

        Assert.False(PathManager.IsChartInitialized(map));
        Assert.True(PathManager.Register(map));
        Assert.True(PathManager.IsChartInitialized(map));
        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(0, 0, 0));
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
        TestWorld.World.TryAddGrid(config, out _);

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

        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, targetPosition);
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
        TestWorld.World.TryAddGrid(config, out _);

        NavigationChartCell[,,] data = new NavigationChartCell[3, 3, 3];
        data[1, 1, 1] = new NavigationChartCell(
            TraversalMedia.Solid,
            pathCostModifier: 7,
            flags: NavigationChartCellFlags.TransitionSourceHint);

        Vector3d targetPosition = Vector3d.Zero;
        var chart = NavigationChart.From3D("StructuredChart", data, new Vector3d(-1, -1, -1), Fixed64.One);
        PathManager.Register(chart);
        PathManager.InitializeChart(chart.Name);

        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, targetPosition);
        SolidChartPartition partition = TestRequire.Partition<SolidChartPartition>(voxel);
        Assert.Equal(7, partition.PathCostModifier);
        Assert.Equal(NavigationChartCellFlags.TransitionSourceHint, partition.ChartFlags);

        PathManager.UnloadChart(chart.Name);
    }

    [Fact]
    public void OverlappingChartResolution_ShouldUseRegistrationPrecedence_AndRestoreTheNextOwnerOnUnload()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TestWorld.World.TryAddGrid(config, out _);

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

        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, targetPosition);
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
        TestWorld.World.TryAddGrid(config, out _);

        NavigationChartCell[,,] data = new NavigationChartCell[3, 3, 3];
        data[1, 1, 1] = new NavigationChartCell(
            TraversalMedia.Liquid,
            pathCostModifier: 9);

        Vector3d targetPosition = Vector3d.Zero;
        var chart = NavigationChart.From3D("StructuredVolume", data, new Vector3d(-1, -1, -1), Fixed64.One);
        PathManager.Register(chart);
        PathManager.InitializeChart(chart.Name);

        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, targetPosition);
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
        TestWorld.World.TryAddGrid(config, out _);

        NavigationChartCell[,,] data = new NavigationChartCell[3, 3, 3];
        data[1, 1, 1] = new NavigationChartCell(
            TraversalMedia.Solid | TraversalMedia.Liquid,
            pathCostModifier: 4,
            flags: NavigationChartCellFlags.TransitionSourceHint);

        Vector3d targetPosition = Vector3d.Zero;
        var chart = NavigationChart.From3D("StructuredMixed", data, new Vector3d(-1, -1, -1), Fixed64.One);
        PathManager.Register(chart);
        PathManager.InitializeChart(chart.Name);

        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, targetPosition);
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
        TestWorld.World.TryAddGrid(config, out _);

        Vector3d targetPosition = Vector3d.Zero;
        Vector3d minBounds = new(-1, -1, -1);

        NavigationChartCell[,,] solidData = new NavigationChartCell[3, 3, 3];
        solidData[1, 1, 1] = NavigationChartCell.Solid;

        NavigationChartCell[,,] liquidData = new NavigationChartCell[3, 3, 3];
        liquidData[1, 1, 1] = NavigationChartCell.Liquid;

        PathManager.Register(NavigationChart.From3D("LowSolid", solidData, minBounds, Fixed64.One, priority: 0));
        PathManager.Register(NavigationChart.From3D("HighLiquid", liquidData, minBounds, Fixed64.One, priority: 1));

        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, targetPosition);
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
        TestWorld.World.TryAddGrid(config, out _);

        NavigationChartCell[,,] data = new NavigationChartCell[3, 3, 3];
        data[1, 1, 1] = NavigationChartCell.Solid;

        var chart = NavigationChart.From3D("LiveUpdateChart", data, new Vector3d(-1, -1, -1), Fixed64.One);
        PathManager.Register(chart);

        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
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
        TestWorld.World.TryAddGrid(config, out _);

        NavigationChartCell[,,] data = new NavigationChartCell[1, 3, 1];
        data[0, 0, 0] = NavigationChartCell.Gas;
        data[0, 2, 0] = NavigationChartCell.Gas;

        var chart = NavigationChart.From3D("BatchUpdateChart", data, Vector3d.Zero, Fixed64.One);
        PathManager.Register(chart);

        Voxel middleVoxel = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(1, 0, 0));
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
        TestWorld.World.TryAddGrid(config, out _);

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
        TestWorld.World.TryAddGrid(config, out _);

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
        TestWorld.World.TryAddGrid(config, out _);

        NavigationChartCell[,,] data = new NavigationChartCell[1, 1, 1];
        data[0, 0, 0] = NavigationChartCell.Solid;

        var chart = NavigationChart.From3D("EmptyRemovalChart", data, Vector3d.Zero, Fixed64.One);
        Assert.True(PathManager.Register(chart));
        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        Assert.True(voxel.TryGetPartition<SolidChartPartition>(out _));

        Assert.True(PathManager.TryUpdateChartCell(chart.Name, 0, 0, 0, NavigationChartCell.Empty));

        Assert.False(PathManager.TryGetEffectiveCell(Vector3d.Zero, out _));
        Assert.False(PathManager.TryGetEffectiveChartOwner(Vector3d.Zero, out _));
        Assert.False(voxel.TryGetPartition<SolidChartPartition>(out _));
        Assert.False(voxel.TryGetPartition<VolumeChartPartition>(out _));

        PathManager.UnloadChart(chart.Name);
    }

    [Fact]
    public void TryUpdateChartCell_ShouldPreserveEffectiveTraversalWhenWinningOwnerFallsBackToEquivalentCell()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        TestWorld.World.TryAddGrid(config, out _);

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
        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        SolidChartPartition partition = TestRequire.Partition<SolidChartPartition>(voxel);

        Assert.True(effectiveCell.HasSolid);
        Assert.False(effectiveCell.HasVolume);
        Assert.Equal("FallbackOwnerLow", winningOwnerAfterUpdate);
        Assert.Equal("FallbackOwnerLow", partition.EffectiveChartOwner);

        PathManager.UnloadChart(highPriorityChart.Name);
        PathManager.UnloadChart(lowPriorityChart.Name);
    }

    [Fact]
    public void GlobalGridChange_ShouldRebuildOnlyIntersectingCharts()
    {
        Assert.True(TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)),
            out ushort leftGridIndex));
        Assert.True(TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(10, -4, -4), new Vector3d(18, 4, 4)),
            out ushort rightGridIndex));

        NavigationChart leftChart = PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "ChangedLeftChart", Vector3d.Zero);
        NavigationChart rightChart = PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "ChangedRightChart", new Vector3d(10, 0, 0));

        Assert.True(PathManager.IsChartInitialized(leftChart));
        Assert.True(PathManager.IsChartInitialized(rightChart));
        Voxel leftVoxel = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        Voxel rightVoxel = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(10, 0, 0));
        Assert.True(leftVoxel.TryGetPartition<SolidChartPartition>(out _));
        Assert.True(rightVoxel.TryGetPartition<SolidChartPartition>(out _));

        TestWorld.World.IncrementGridVersion(rightGridIndex, false);
        FlushExternalGridBridge();

        Assert.True(PathManager.IsChartInitialized(leftChart));
        Assert.True(PathManager.IsChartInitialized(rightChart));
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
        Assert.True(TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)),
            out _));
        Assert.True(TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(20, -4, -4), new Vector3d(28, 4, 4)),
            out ushort farGridIndex));

        NavigationChart leftChart = PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "UnchangedChart", Vector3d.Zero);

        Assert.True(PathManager.IsChartInitialized(leftChart));
        Voxel leftVoxel = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        Assert.True(leftVoxel.TryGetPartition<SolidChartPartition>(out _));

        TestWorld.World.IncrementGridVersion(farGridIndex, false);
        FlushExternalGridBridge();

        Assert.True(PathManager.IsChartInitialized(leftChart));
        Assert.True(leftVoxel.TryGetPartition<SolidChartPartition>(out _));
        Assert.True(PathManager.TryGetEffectiveCell(Vector3d.Zero, out NavigationChartCell cell));
        Assert.True(cell.HasSolid);
    }

    [Fact]
    public void GlobalGridChange_ShouldNotInitializeDeferredIntersectingCharts()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        Assert.True(TestWorld.World.TryAddGrid(config, out ushort gridIndex));

        NavigationChart deferredChart = PathTestFactory.BuildSinglePointMap("DeferredGridChangeChart", Vector3d.Zero);
        Assert.True(PathManager.Register(deferredChart, initializeChart: false));
        Assert.False(PathManager.IsChartInitialized(deferredChart));
        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        Assert.False(voxel.TryGetPartition<SolidChartPartition>(out _));

        TestWorld.World.IncrementGridVersion(gridIndex, false);
        FlushExternalGridBridge();

        Assert.False(PathManager.IsChartInitialized(deferredChart));
        Assert.False(voxel.TryGetPartition<SolidChartPartition>(out _));
        Assert.False(PathManager.TryGetEffectiveCell(Vector3d.Zero, out _));

        PathManager.UnloadChart(deferredChart);
    }

    [Fact]
    public void TryUpdateChartCell_ShouldPersistChangesWhileGridIsMissing_AndApplyThemWhenGridReturns()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        Assert.True(TestWorld.World.TryAddGrid(config, out ushort gridIndex));

        NavigationChartCell[,,] data = new NavigationChartCell[1, 1, 1];
        data[0, 0, 0] = NavigationChartCell.Solid;

        var chart = NavigationChart.From3D("DeferredGridUpdateChart", data, Vector3d.Zero, Fixed64.One);
        Assert.True(PathManager.Register(chart));
        Assert.True(PathManager.IsChartInitialized(chart));

        Assert.True(TestWorld.World.TryRemoveGrid(gridIndex));
        FlushExternalGridBridge();
        Assert.True(PathManager.IsChartInitialized(chart));
        Assert.False(PathManager.TryGetEffectiveCell(Vector3d.Zero, out _));

        Assert.True(PathManager.TryUpdateChartCell(chart.Name, 0, 0, 0, NavigationChartCell.Liquid));

        Assert.True(TestWorld.World.TryAddGrid(config, out _));
        FlushExternalGridBridge();
        Assert.True(PathManager.TryGetEffectiveCell(Vector3d.Zero, out NavigationChartCell restoredCell));
        Assert.False(restoredCell.HasSolid);
        Assert.True(restoredCell.SupportsMedium(TraversalMedium.Liquid));
        Voxel rebuiltVoxel = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        Assert.False(rebuiltVoxel.TryGetPartition<SolidChartPartition>(out _));
        VolumeChartPartition volumePartition = TestRequire.Partition<VolumeChartPartition>(rebuiltVoxel);
        Assert.True(volumePartition.SupportsMedium(TraversalMedium.Liquid));
    }

    [Fact]
    public void TryGetMaxSearchSize_ShouldReturnGridSize_WhenBothVoxelsShareAGrid()
    {
        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4));
        Assert.True(TestWorld.World.TryAddGrid(config, out _));
        Voxel start = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        Voxel end = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(1, 0, 0));
        VoxelGrid grid = TestRequire.Grid(TestWorld.Context, start.GridIndex);

        Assert.True(PathManager.TryGetMaxSearchSize(start, end, out int maxSearchSize));
        Assert.Equal(grid.Size, maxSearchSize);
    }

    [Fact]
    public void TryGetMaxSearchSize_ShouldReturnCombinedSize_WhenVoxelsSpanDifferentGrids()
    {
        Assert.True(TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)),
            out _));
        Assert.True(TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(10, -4, -4), new Vector3d(18, 4, 4)),
            out _));

        Voxel start = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        Voxel end = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(10, 0, 0));
        VoxelGrid startGrid = TestRequire.Grid(TestWorld.Context, start.GridIndex);
        VoxelGrid endGrid = TestRequire.Grid(TestWorld.Context, end.GridIndex);

        Assert.True(PathManager.TryGetMaxSearchSize(start, end, out int maxSearchSize));
        Assert.Equal(startGrid.Size + endGrid.Size, maxSearchSize);
    }

    [Fact]
    public void TryGetMaxSearchSize_ShouldReturnFalse_WhenEitherVoxelGridIsMissing()
    {
        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4));
        Assert.True(TestWorld.World.TryAddGrid(config, out ushort gridIndex));
        Voxel start = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        Voxel end = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(1, 0, 0));

        Assert.True(TestWorld.World.TryRemoveGrid(gridIndex));

        Assert.False(PathManager.TryGetMaxSearchSize(start, end, out int maxSearchSize));
        Assert.Equal(0, maxSearchSize);
    }

    [Fact]
    public void TryGetMaxSearchSize_ShouldRejectVoxelFromRecycledGridGeneration()
    {
        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4));
        Assert.True(TestWorld.World.TryAddGrid(config, out ushort gridIndex));
        Voxel stale = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);

        Assert.True(TestWorld.World.TryRemoveGrid(gridIndex));
        Assert.True(TestWorld.World.TryAddGrid(config, out ushort replacementGridIndex));
        Assert.Equal(gridIndex, replacementGridIndex);

        Voxel replacement = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        Assert.NotEqual(stale.WorldIndex.GridSpawnToken, replacement.WorldIndex.GridSpawnToken);

        Assert.False(PathManager.TryGetMaxSearchSize(TestWorld.World, stale, replacement, out int staticSearchSize));
        Assert.Equal(0, staticSearchSize);
        Assert.False(TestWorld.Context.Pathing.TryGetMaxSearchSize(stale, replacement, out int serviceSearchSize));
        Assert.Equal(0, serviceSearchSize);
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
