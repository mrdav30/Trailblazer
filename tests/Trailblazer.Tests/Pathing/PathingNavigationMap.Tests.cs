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
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();
    }

    public void Dispose()
    {
        PathManager.Reset();

        GlobalGridManager.Reset();
        TrailblazerManager.Reset();

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Register_AddsMapToManager()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        GlobalGridManager.TryAddGrid(config, out _);

        var map = PathTestFactory.BuildSinglePointMap("TestMap", new Vector3d(0, 0, 0));
        PathManager.Register(map);

        Assert.True(PathManager.TryGetNavigationChart("TestMap", out var retrieved));
        Assert.Equal(map, retrieved);

        PathManager.UnloadChart("TestMap");
    }

    [Fact]
    public void InitializeMap_AddsPartitionToExpectedVoxel()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        GlobalGridManager.TryAddGrid(config, out _);

        var map = PathTestFactory.BuildSinglePointMap("InitMap", new Vector3d(0, 0, 0));
        PathManager.Register(map);
        PathManager.InitializeChart("InitMap");

        Assert.True(GlobalGridManager.TryGetGridAndVoxel(new Vector3d(0, 0, 0), out _, out Voxel voxel));
        Assert.True(voxel.TryGetPartition<PathPartition>(out _));

        PathManager.UnloadChart("InitMap");
    }

    [Fact]
    public void UnloadMap_RemovesOnlyItsPartition()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        GlobalGridManager.TryAddGrid(config, out _);

        var pos = new Vector3d(0, 0, 0);
        var mapA = PathTestFactory.BuildSinglePointMap("MapA", pos);
        var mapB = PathTestFactory.BuildSinglePointMap("MapB", pos);

        PathManager.Register(mapA);
        PathManager.Register(mapB);

        PathManager.InitializeChart("MapA");
        PathManager.InitializeChart("MapB");

        // Validate partition exists and belongs to both
        GlobalGridManager.TryGetGridAndVoxel(pos, out _, out Voxel voxel);
        voxel.TryGetPartition(out PathPartition partition);
        Assert.True(partition.BelongsTo("MapA"));
        Assert.True(partition.BelongsTo("MapB"));

        PathManager.UnloadChart("MapA");

        // Should still be there because MapB owns it
        Assert.True(voxel.TryGetPartition<PathPartition>(out var afterUnload));
        Assert.True(afterUnload.BelongsTo("MapB"));

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
        GlobalGridManager.TryAddGrid(config, out _);

        var map = PathTestFactory.BuildSinglePointMap("DuplicateInit", new Vector3d(0, 0, 0));
        PathManager.Register(map);
        PathManager.InitializeChart("DuplicateInit");
        PathManager.InitializeChart("DuplicateInit"); // idempotent

        GlobalGridManager.TryGetGridAndVoxel(new Vector3d(0, 0, 0), out _, out Voxel voxel);
        var count = voxel.TryGetPartition<PathPartition>(out var partition) ? 1 : 0;

        Assert.True(count == 1);
        Assert.True(partition.BelongsTo("DuplicateInit"));

        PathManager.UnloadChart("DuplicateInit");
    }

    [Fact]
    public void UnloadChart_ShouldClearInitializationStateSoChartCanBeRegisteredAgain()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        GlobalGridManager.TryAddGrid(config, out _);

        var map = PathTestFactory.BuildSinglePointMap("ReloadableMap", new Vector3d(0, 0, 0));
        PathManager.Register(map);
        PathManager.InitializeChart(map.Name);

        PathManager.UnloadChart(map);

        Assert.False(map.IsInitialized);
        Assert.True(PathManager.Register(map));

        PathManager.InitializeChart(map.Name);

        Assert.True(GlobalGridManager.TryGetGridAndVoxel(new Vector3d(0, 0, 0), out _, out Voxel voxel));
        Assert.True(voxel.TryGetPartition<PathPartition>(out _));

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
        Assert.True(walkableCell.HasSurface);
        Assert.False(walkableCell.HasVolume);
        Assert.Equal(0, walkableCell.PathCostModifier);
        Assert.Equal(NavigationChartCellFlags.None, walkableCell.Flags);

        Assert.True(chart.TryGetCell(new Vector3d(1, 0, 0), out NavigationChartCell blockedCell));
        Assert.False(blockedCell.HasTraversalData);
        Assert.False(blockedCell.HasSurface);
        Assert.False(blockedCell.HasVolume);
        Assert.Equal(0, blockedCell.PathCostModifier);
        Assert.Equal(NavigationChartCellFlags.None, blockedCell.Flags);

        Assert.False(chart.TryGetCell(new Vector3d(3, 0, 0), out _));
    }

    [Fact]
    public void InitializeChart_ShouldApplyStructuredCellMetadata_ToPartitions()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        GlobalGridManager.TryAddGrid(config, out _);

        NavigationChartCell[,,] data = new NavigationChartCell[3, 3, 3];
        data[1, 1, 1] = new NavigationChartCell(
            NavigationChartTraversalKinds.Surface,
            pathCostModifier: 7,
            flags: NavigationChartCellFlags.TransitionSourceHint);

        Vector3d targetPosition = Vector3d.Zero;
        var chart = NavigationChart.From3D("StructuredChart", data, new Vector3d(-1, -1, -1), Fixed64.One);
        PathManager.Register(chart);
        PathManager.InitializeChart(chart.Name);

        Assert.True(GlobalGridManager.TryGetGridAndVoxel(targetPosition, out _, out Voxel voxel));
        Assert.True(voxel.TryGetPartition(out PathPartition partition));
        Assert.Equal(7, partition.PathCostModifier);
        Assert.Equal(NavigationChartCellFlags.TransitionSourceHint, partition.ChartFlags);

        PathManager.UnloadChart(chart.Name);
    }

    [Fact]
    public void UnloadChart_ShouldRestoreRemainingChartMetadata_WhenOwnersOverlap()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        GlobalGridManager.TryAddGrid(config, out _);

        Vector3d targetPosition = Vector3d.Zero;
        Vector3d minBounds = new(-1, -1, -1);

        NavigationChartCell[,,] mapAData = new NavigationChartCell[3, 3, 3];
        mapAData[1, 1, 1] = new NavigationChartCell(
            NavigationChartTraversalKinds.Surface,
            pathCostModifier: 2,
            flags: NavigationChartCellFlags.TransitionSourceHint);

        NavigationChartCell[,,] mapBData = new NavigationChartCell[3, 3, 3];
        mapBData[1, 1, 1] = new NavigationChartCell(
            NavigationChartTraversalKinds.Surface,
            pathCostModifier: 5,
            flags: NavigationChartCellFlags.TransitionDestinationHint);

        PathManager.Register(NavigationChart.From3D("StructuredA", mapAData, minBounds, Fixed64.One));
        PathManager.Register(NavigationChart.From3D("StructuredB", mapBData, minBounds, Fixed64.One));

        PathManager.InitializeChart("StructuredA");
        PathManager.InitializeChart("StructuredB");

        Assert.True(GlobalGridManager.TryGetGridAndVoxel(targetPosition, out _, out Voxel voxel));
        Assert.True(voxel.TryGetPartition(out PathPartition partition));
        Assert.Equal(7, partition.PathCostModifier);
        Assert.Equal(
            NavigationChartCellFlags.TransitionSourceHint | NavigationChartCellFlags.TransitionDestinationHint,
            partition.ChartFlags);

        PathManager.UnloadChart("StructuredA");

        Assert.True(voxel.TryGetPartition(out PathPartition remainingPartition));
        Assert.True(remainingPartition.BelongsTo("StructuredB"));
        Assert.False(remainingPartition.BelongsTo("StructuredA"));
        Assert.Equal(5, remainingPartition.PathCostModifier);
        Assert.Equal(NavigationChartCellFlags.TransitionDestinationHint, remainingPartition.ChartFlags);

        PathManager.UnloadChart("StructuredB");
    }

    [Fact]
    public void InitializeChart_ShouldApplyStructuredVolumeMetadata_ToVolumePartitions()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        GlobalGridManager.TryAddGrid(config, out _);

        NavigationChartCell[,,] data = new NavigationChartCell[3, 3, 3];
        data[1, 1, 1] = new NavigationChartCell(
            NavigationChartTraversalKinds.WaterVolume,
            pathCostModifier: 9);

        Vector3d targetPosition = Vector3d.Zero;
        var chart = NavigationChart.From3D("StructuredVolume", data, new Vector3d(-1, -1, -1), Fixed64.One);
        PathManager.Register(chart);
        PathManager.InitializeChart(chart.Name);

        Assert.True(GlobalGridManager.TryGetGridAndVoxel(targetPosition, out _, out Voxel voxel));
        Assert.False(voxel.TryGetPartition<PathPartition>(out _));
        Assert.True(voxel.TryGetPartition(out VolumePartition volumePartition));
        Assert.True(volumePartition.SupportsTraversal(VolumeTraversalMode.Water));
        Assert.False(volumePartition.SupportsTraversal(VolumeTraversalMode.Open));
        Assert.Equal(9, volumePartition.PathCostModifier);

        PathManager.UnloadChart(chart.Name);
    }

    [Fact]
    public void RegisterTraversalBuildResult_ShouldRegisterTransitionsAndInitializeChart()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        GlobalGridManager.TryAddGrid(config, out _);

        string[,,] map =
        {
            {
                { "L!" },
                { "W!" }
            }
        };

        TraversalBuildResult buildResult = new TraversalAuthoringMap(
            chartName: "AuthoredBuild",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One).Build();

        Assert.True(PathManager.Register(buildResult));
        Assert.True(PathManager.TryGetNavigationChart(buildResult.Chart.Name, out NavigationChart chart));
        Assert.Same(buildResult.Chart, chart);
        Assert.True(chart.IsInitialized);
        Assert.True(GlobalGridManager.TryGetGridAndVoxel(Vector3d.Zero, out _, out Voxel voxel));
        Assert.True(voxel.TryGetPartition<PathPartition>(out _));
        Assert.True(GlobalGridManager.TryGetGridAndVoxel(new Vector3d(1, 0, 0), out _, out Voxel waterVoxel));
        Assert.True(waterVoxel.TryGetPartition(out VolumePartition volumePartition));
        Assert.True(volumePartition.SupportsTraversal(VolumeTraversalMode.Water));

        foreach (TraversalTransition transition in buildResult.GeneratedTransitions)
            Assert.True(TraversalTransitionRegistry.IsRegistered(transition.Id));

        PathManager.UnloadChart(chart);
    }

    [Fact]
    public void RegisterTraversalBuildResult_ShouldInitializeBareWaterCells_WithoutHostRules()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        GlobalGridManager.TryAddGrid(config, out _);

        string[,,] map =
        {
            {
                { "W" },
                { "W" }
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
            out VolumePathRequest request,
            traversalMode: VolumeTraversalMode.Water));
        Assert.NotNull(request);

        PathManager.UnloadChart(buildResult.Chart);
    }

    [Fact]
    public void AuthoredOpenVolume_ShouldRestrictOpenTraversalToAuthoredVoxels()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        GlobalGridManager.TryAddGrid(config, out _);

        Assert.True(GlobalGridManager.TryGetVoxel(Vector3d.Zero, out Voxel unAuthoredVoxel));
        Assert.False(RawVoxelFinder.IsTraversable(unAuthoredVoxel, Fixed64.One, VolumeTraversalMode.Open));

        string[,,] map =
        {
            {
                { "O" }
            }
        };

        TraversalBuildResult buildResult = new TraversalAuthoringMap(
            chartName: "AuthoredOpenOnly",
            sourceMap: map,
            minBounds: new Vector3d(1, 0, 0),
            interval: Fixed64.One).Build();

        Assert.True(PathManager.Register(buildResult));
        Assert.True(GlobalGridManager.TryGetVoxel(new Vector3d(1, 0, 0), out Voxel authoredOpenVoxel));
        Assert.True(RawVoxelFinder.IsTraversable(authoredOpenVoxel, Fixed64.One, VolumeTraversalMode.Open));
        Assert.False(RawVoxelFinder.IsTraversable(unAuthoredVoxel, Fixed64.One, VolumeTraversalMode.Open));

        PathManager.UnloadChart(buildResult.Chart);
    }

    [Fact]
    public void OpenVoxelRule_ShouldRequireTrailblazerPartitionPresence()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        GlobalGridManager.TryAddGrid(config, out _);

        VolumeTraversalRules.SetOpenVoxelRule(static voxel =>
            voxel.WorldPosition == Vector3d.Zero
            || voxel.WorldPosition == new Vector3d(1, 0, 0));

        Assert.True(GlobalGridManager.TryGetVoxel(Vector3d.Zero, out Voxel unpartitionedVoxel));
        Assert.False(RawVoxelFinder.IsTraversable(unpartitionedVoxel, Fixed64.One, VolumeTraversalMode.Open));

        PathTestFactory.RegisterSingleWalkablePoint("OpenRuleSurfaceA", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("OpenRuleSurfaceB", new Vector3d(1, 0, 0));

        Assert.True(RawVoxelFinder.IsTraversable(unpartitionedVoxel, Fixed64.One, VolumeTraversalMode.Open));
        Assert.True(VolumePathRequest.TryCreate(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            out VolumePathRequest request,
            traversalMode: VolumeTraversalMode.Open));
        Assert.NotNull(request);

        PathManager.UnloadChart("OpenRuleSurfaceA");
        PathManager.UnloadChart("OpenRuleSurfaceB");
    }

    [Fact]
    public void PathManagerReset_ShouldClearOpenAndWaterVoxelRules()
    {
        VolumeTraversalRules.SetOpenVoxelRule(static _ => true);
        VolumeTraversalRules.SetWaterVoxelRule(static _ => true);

        Assert.True(VolumeTraversalRules.HasOpenVoxelRule);
        Assert.True(VolumeTraversalRules.HasWaterVoxelRule);

        PathManager.Reset();

        Assert.False(VolumeTraversalRules.HasOpenVoxelRule);
        Assert.False(VolumeTraversalRules.HasWaterVoxelRule);
    }

    [Fact]
    public void UnrelatedChartUnload_ShouldNotInvalidateUnrelatedAStarCache()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        GlobalGridManager.TryAddGrid(config, out _);

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

        AStarPathRequest request = AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean);

        Assert.NotNull(request);
        Assert.True(PathGuideFactory.RequestGuide(request, out AStarGuide guide));
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
        GlobalGridManager.TryAddGrid(config, out _);

        NavigationChartCell[,,] data = new NavigationChartCell[1, 3, 1];
        data[0, 0, 0] = new NavigationChartCell(NavigationChartTraversalKinds.OpenVolume);
        data[0, 1, 0] = new NavigationChartCell(NavigationChartTraversalKinds.OpenVolume);
        data[0, 2, 0] = new NavigationChartCell(NavigationChartTraversalKinds.OpenVolume);

        PathManager.Register(NavigationChart.From3D("VolumeCacheChart", data, Vector3d.Zero, Fixed64.One));
        PathManager.InitializeChart("VolumeCacheChart");
        PathTestFactory.RegisterSingleWalkablePoint("UnrelatedVolumeChart", new Vector3d(-3, 0, -3));

        VolumePathRequest request = VolumePathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            traversalMode: VolumeTraversalMode.Open);

        Assert.NotNull(request);
        Assert.True(PathGuideFactory.RequestGuide(request, out VolumeGuide guide));
        Assert.NotNull(guide);
        PathGuideFactory.ReturnGuide(guide);

        Assert.Equal(1, PathGuideFactory.ActiveVolumeGuideCount);

        PathManager.UnloadChart("UnrelatedVolumeChart");

        Assert.Equal(1, PathGuideFactory.ActiveVolumeGuideCount);

        PathManager.UnloadChart("VolumeCacheChart");

        Assert.Equal(0, PathGuideFactory.ActiveVolumeGuideCount);
    }

    [Fact]
    public void VolumeCache_ShouldTrackSurfaceChartDependencies_WhenHostRuleUsesPathPartitions()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        GlobalGridManager.TryAddGrid(config, out _);

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
        VolumeTraversalRules.SetOpenVoxelRule(static voxel =>
            voxel != null
            && voxel.HasPartition<PathPartition>());

        VolumePathRequest request = VolumePathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            traversalMode: VolumeTraversalMode.Open);

        Assert.NotNull(request);
        Assert.True(PathGuideFactory.RequestGuide(request, out VolumeGuide guide));
        Assert.NotNull(guide);
        PathGuideFactory.ReturnGuide(guide);

        Assert.Equal(1, PathGuideFactory.ActiveVolumeGuideCount);

        PathManager.UnloadChart("UnrelatedSurfaceBackedVolumeChart");

        Assert.Equal(1, PathGuideFactory.ActiveVolumeGuideCount);

        PathManager.UnloadChart("SurfaceBackedVolumeChart");

        Assert.Equal(0, PathGuideFactory.ActiveVolumeGuideCount);
    }

    [Fact]
    public void RegisterTraversalBuildResult_ShouldRollback_WhenGeneratedTransitionRegistrationFails()
    {
        var config = new GridConfiguration(new Vector3d(-4, 0, -4), new Vector3d(4, 0, 4));
        GlobalGridManager.TryAddGrid(config, out _);

        string[,,] map =
        {
            {
                { "L!" },
                { "W!" }
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
        Assert.True(GlobalGridManager.TryGetGridAndVoxel(Vector3d.Zero, out _, out Voxel voxel));
        Assert.False(voxel.TryGetPartition<PathPartition>(out _));
    }
}
