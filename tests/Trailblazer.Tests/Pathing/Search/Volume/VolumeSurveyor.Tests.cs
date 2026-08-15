using System;
using System.Linq;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class VolumeSurveyorTests : IDisposable
{
    public VolumeSurveyorTests()
    {
        TestWorld.Setup();
        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        TestWorld.World.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        VolumeMediumRules.ClearGasVoxelRule();
        VolumeMediumRules.ClearLiquidVoxelRule();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void FindPath_ShouldReturnEmpty_ForNullZeroDisplacementAndInvalidRequests()
    {
        VolumeSurveyor.Shared.FindPath(null!).HasPath.Should().BeFalse();

        GuidedPathTestScene.AddOpen(TestWorld.Context, Vector3d.Zero);

        VolumePathRequest sameVoxel = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, Vector3d.Zero, Vector3d.Zero, Fixed64.One));
        VolumeSurveyor.Shared.FindPath(sameVoxel).HasPath.Should().BeFalse();

        VolumePathRequest invalid = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, Vector3d.Zero, Vector3d.Zero, Fixed64.One));
        invalid.UpdateRequest(new Vector3d(64, 0, 0), Vector3d.Zero, Fixed64.One).Should().BeFalse();
        VolumeSurveyor.Shared.FindPath(invalid).HasPath.Should().BeFalse();
    }

    [Fact]
    public void FindPath_ShouldReturnEmpty_WhenNoRouteExists()
    {
        GuidedPathTestScene.AddOpen(TestWorld.Context, Vector3d.Zero);
        GuidedPathTestScene.AddOpen(TestWorld.Context, new Vector3d(2, 0, 0));

        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One));

        VolumeSurveyResult result = VolumeSurveyor.Shared.FindPath(request);

        result.HasPath.Should().BeFalse();
        result.Waypoints.Should().BeNull();
    }

    [Fact]
    public void FindPath_ShouldRejectDiagonalCornerCutting()
    {
        GuidedPathTestScene.AddOpen(TestWorld.Context, Vector3d.Zero);
        GuidedPathTestScene.AddOpen(TestWorld.Context, new Vector3d(1, 0, 1));

        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(1, 0, 1),
            Fixed64.One));

        VolumeSurveyResult result = VolumeSurveyor.Shared.FindPath(request);

        result.HasPath.Should().BeFalse();
        result.Waypoints.Should().BeNull();
    }

    [Fact]
    public void FindPath_ShouldSucceed_WithAdjacentConnectedGasVoxels()
    {
        // Two adjacent gas voxels form the minimal viable path.
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, Vector3d.Zero, TraversalMedium.Gas, "GasSuccA");
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(1, 0, 0), TraversalMedium.Gas, "GasSuccA");

        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            medium: TraversalMedium.Gas));

        VolumeSurveyResult result = VolumeSurveyor.Shared.FindPath(request);

        result.HasPath.Should().BeTrue();
        Assert.NotNull(result.Waypoints);
        result.Waypoints.Should().HaveCount(2);
        result.Waypoints[0].Position.Should().Be(Vector3d.Zero);
        result.Waypoints[^1].Position.Should().Be(new Vector3d(1, 0, 0));
        result.Waypoints[^1].IsGoal.Should().BeTrue();
    }

    [Fact]
    public void FindPath_ShouldBuildDirectionChangeWaypoints_ForLShapedGasPath()
    {
        // Five adjacent gas voxels forming an L-shape so BuildWaypoints sees a direction change.
        Vector3d[] positions = new[]
        {
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 0),
            new Vector3d(2, 0, 0), // corner — direction change here
            new Vector3d(2, 0, 1),
            new Vector3d(2, 0, 2),
        };

        foreach (Vector3d pos in positions)
            PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, pos, TraversalMedium.Gas, "GasLShape");

        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 2),
            Fixed64.One,
            medium: TraversalMedium.Gas));

        VolumeSurveyResult result = VolumeSurveyor.Shared.FindPath(request);

        result.HasPath.Should().BeTrue();
        Assert.NotNull(result.Waypoints);
        // More than just start+end because the direction change at the corner is preserved.
        result.Waypoints.Length.Should().BeGreaterThan(2);
        result.Waypoints[0].Position.Should().Be(Vector3d.Zero);
        result.Waypoints[^1].Position.Should().Be(new Vector3d(2, 0, 2));
        result.Waypoints[^1].IsGoal.Should().BeTrue();
    }

    [Fact]
    public void FindPath_ShouldTrackVolumeChartKeys_WhenPathPassesThroughVolumePartition()
    {
        // Gas voxels registered individually each produce their own VolumeChartPartition.
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, Vector3d.Zero, TraversalMedium.Gas, "GasChartKeys");
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(1, 0, 0), TraversalMedium.Gas, "GasChartKeys");

        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            medium: TraversalMedium.Gas));

        VolumeSurveyResult result = VolumeSurveyor.Shared.FindPath(request);

        result.HasPath.Should().BeTrue();
        // Chart keys from the VolumeChartPartition owners must make it into the result.
        Assert.NotNull(result.ChartsUtilized);
        result.ChartsUtilized.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void FindPath_ShouldTraverseEndNode_WhenAllowUnwalkableEndpointsIsTrue()
    {
        // With allowUnwalkableEndpoints enabled the surveyor uses the lighter Matches check
        // for the EndNode so a voxel that would normally fail clearance can still terminate the path.
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, Vector3d.Zero, TraversalMedium.Gas, "GasRelaxed");
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(1, 0, 0), TraversalMedium.Gas, "GasRelaxed");

        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true,
            medium: TraversalMedium.Gas));

        VolumeSurveyResult result = VolumeSurveyor.Shared.FindPath(request);
        AStarWaypoint[] waypoints = TestRequire.NotNull(result.Waypoints);

        result.HasPath.Should().BeTrue();
        waypoints[^1].Position.Should().Be(new Vector3d(1, 0, 0));
    }

    [Fact]
    public void FindPath_ShouldNotAddDirectionChangeWaypoints_ForStraightLinePath()
    {
        // Exercises the !lastDirection.FuzzyEqual(direction) == false branch in BuildWaypoints.
        // A 5-voxel straight line has no interior direction changes after the first segment,
        // so only the start, the first interior waypoint, and the end appear in the output.
        Vector3d[] positions = new[]
        {
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 0),
            new Vector3d(2, 0, 0),
            new Vector3d(3, 0, 0),
            new Vector3d(4, 0, 0)
        };

        foreach (Vector3d pos in positions)
            PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, pos, TraversalMedium.Gas, "GasStraight");

        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            medium: TraversalMedium.Gas));

        VolumeSurveyResult result = VolumeSurveyor.Shared.FindPath(request);

        result.HasPath.Should().BeTrue();
        // The 5-voxel straight-line path: start + first inner (lastDir=Zero differs from dir) + end = 3 waypoints.
        // Voxels at index 2 and 3 share the same direction as index 1 → not added.
        result.Waypoints?.Select(waypoint => waypoint.Position).Should().Equal(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            new Vector3d(4, 0, 0));
    }

    [Fact]
    public void FindPath_ShouldTrackSolidChartKeys_WhenPathPassesThroughSolidPartition()
    {
        // Exercises the SolidChartPartition branch in AddVoxelChartOwners by extending gas membership
        // to authored solid voxels through the host rule. The volume survey still walks raw voxels,
        // but chart-owner collection should include the solid chart that backs them.
        bool[,,] data = new bool[1, 3, 1]
        {
            {
                { true },
                { true },
                { true }
            }
        };
        PathTestFactory.RegisterFromData(TestWorld.Context, "SolidVolumeOwners", data, Vector3d.Zero);
        VolumeMediumRules.SetGasVoxelRule(static voxel => voxel != null && voxel.HasPartition<SolidChartPartition>());

        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            allowUnwalkableEndpoints: true,
            medium: TraversalMedium.Gas));

        VolumeSurveyResult result = VolumeSurveyor.Shared.FindPath(request);

        result.HasPath.Should().BeTrue();
        result.ChartsUtilized.Should().Contain("SolidVolumeOwners");
    }

    [Fact]
    public void VolumeSurveyor_ProcessNeighbors_ShouldReturnFalse_WhenCurrentVoxelHasNoRecordedMeta()
    {
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, Vector3d.Zero, TraversalMedium.Gas, "VolumeMissingMeta");
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(1, 0, 0), TraversalMedium.Gas, "VolumeMissingMeta");

        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            medium: TraversalMedium.Gas));

        Voxel current = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);

        VolumeSurveyor surveyor = new();
        ReflectionUtility.SetPrivateField(surveyor, "_request", request);

        ReflectionUtility.InvokePrivate<bool>(surveyor, "ProcessNeighbors", current).Should().BeFalse();
    }

    [Fact]
    public void VolumeSurveyor_ProcessNeighbor_ShouldUpdateOpenNeighbor_WhenLowerMovementCostIsProvided()
    {
        Vector3d[] positions = new[]
        {
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            new Vector3d(2, 0, 0),
        };

        foreach (Vector3d position in positions)
            PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, position, TraversalMedium.Gas, "VolumeHelperUpdate");

        Voxel current = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        Voxel neighbor = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(1, 0, 0));
        VolumeChartPartition neighborPartition = TestRequire.NotNull(neighbor.GetPartitionOrDefault<VolumeChartPartition>());
        neighborPartition.PathCostModifier = 25;

        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            heuristic: HeuristicMethod.Manhattan,
            medium: TraversalMedium.Gas));

        VolumeSurveyor surveyor = new();
        ReflectionUtility.SetPrivateField(surveyor, "_request", request);

        PathHeap<Voxel> heap = ReflectionUtility.GetPrivateField<PathHeap<Voxel>>(surveyor, "_heap");
        SwiftDictionary<WorldVoxelIndex, VolumeVoxelMeta> meta = ReflectionUtility.GetPrivateField<SwiftDictionary<WorldVoxelIndex, VolumeVoxelMeta>>(surveyor, "_meta");

        meta[neighbor.WorldIndex] = new VolumeVoxelMeta
        {
            MovementCost = 400,
            NextTrailIndex = current.WorldIndex
        };
        heap.Add(neighbor, 999);

        ReflectionUtility.InvokePrivate<bool>(surveyor, "ProcessNeighbor", current, neighbor, 150).Should().BeFalse();

        meta[neighbor.WorldIndex].MovementCost.Should().Be(175);
        meta[neighbor.WorldIndex].NextTrailIndex.Should().Be(current.WorldIndex);
        heap.TryGetPathCost(neighbor, out int updatedPathCost).Should().BeTrue();
        updatedPathCost.Should().Be(275);
    }

    /// <summary>
    /// Covers the <c>chartsUtilized ?? Array.Empty&lt;string&gt;()</c> null-coalescing branch
    /// in <c>VolumeSurveyResult.Create</c> when the caller passes <c>null</c>.
    /// </summary>
    [Fact]
    public void VolumeSurveyResult_Create_ShouldUseFallbackEmptyArray_WhenChartsUtilizedIsNull()
    {
        var waypoints = new[] { new AStarWaypoint { Position = Vector3d.Zero, IsGoal = true } };
        VolumeSurveyResult result = VolumeSurveyResult.Create(
            TestWorld.Context,
            waypoints,
            null!,
            TestPathRequest.CreateCacheKey(1));

        Assert.NotNull(result.ChartsUtilized);
        result.ChartsUtilized.Should().BeEmpty();
    }

    /// <summary>
    /// Covers the <c>return true</c> branch in <c>ProcessNeighbors</c> when the end node is found
    /// via a diagonal direction (VolumeSurveyor line 105). All four XZ corners are registered as gas
    /// so both diagonal legs are clear and the direct diagonal hop is valid.
    /// </summary>
    [Fact]
    public void FindPath_ShouldFindDirectDiagonalPath_WhenEndNodeIsDiagonallyAdjacent()
    {
        // Register all four corners so the diagonal legs (1,0,0) and (0,0,1) are traversable.
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, Vector3d.Zero, TraversalMedium.Gas, "GasDiagEnd");
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(1, 0, 0), TraversalMedium.Gas, "GasDiagEnd");
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(0, 0, 1), TraversalMedium.Gas, "GasDiagEnd");
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(1, 0, 1), TraversalMedium.Gas, "GasDiagEnd");

        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(1, 0, 1),
            Fixed64.One,
            medium: TraversalMedium.Gas));

        VolumeSurveyResult result = VolumeSurveyor.Shared.FindPath(request);
        AStarWaypoint[] waypoints = TestRequire.NotNull(result.Waypoints);

        result.HasPath.Should().BeTrue();
        waypoints[0].Position.Should().Be(Vector3d.Zero);
        waypoints[^1].Position.Should().Be(new Vector3d(1, 0, 1));
    }

    [Fact]
    public void VolumeSurveyor_AddVoxelChartOwners_ShouldIgnoreNullVoxel()
    {
        VolumeSurveyor surveyor = new();

        Action act = () => ReflectionUtility.InvokePrivate<object?>(surveyor, "AddVoxelChartOwners", new object[] { null! });

        act.Should().NotThrow();
        ReflectionUtility.GetPrivateField<SwiftHashSet<string>>(surveyor, "_chartKeys").Should().BeEmpty();
    }

    [Fact]
    public void VolumeSurveyor_FindPath_ShouldKeepOpenPlane8ColdAllocationsUnderBudget()
    {
        TestWorld.Reset();
        TestWorld.Setup();
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-1, -1, -1), new Vector3d(12, 4, 12)), out _);

        NavigationChartCell[,,] data = new NavigationChartCell[1, 8, 8];
        for (int x = 0; x < 8; x++)
            for (int z = 0; z < 8; z++)
                data[0, x, z] = NavigationChartCell.Gas;

        NavigationChart chart = NavigationChart.From3D("VolumeOpenPlane8Alloc", data, Vector3d.Zero, Fixed64.One);
        PathManager.Register(chart);

        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(7, 0, 7),
            Fixed64.One,
            medium: TraversalMedium.Gas));

        VolumeSurveyor.Shared.FindPath(request).HasPath.Should().BeTrue();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        VolumeSurveyResult result = VolumeSurveyor.Shared.FindPath(request);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        result.HasPath.Should().BeTrue();
        TestRequire.NotNull(result.Waypoints).Should().NotBeEmpty();
        allocated.Should().BeLessThan(160_000);

        PathManager.UnloadChart("VolumeOpenPlane8Alloc");
    }

}
