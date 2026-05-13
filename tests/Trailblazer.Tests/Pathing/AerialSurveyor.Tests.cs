using FixedMathSharp;
using FluentAssertions;
using GridForge;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public class AerialSurveyorTests : IDisposable
{
    public AerialSurveyorTests()
    {
        TestWorld.Setup();
        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        TestWorld.World.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AerialSurveyor_Should_FindChartlessDetour_AroundBlockedVoxel()
    {
        AddOpen(Vector3d.Zero);
        AddOpen(new Vector3d(0, 1, 0));
        AddOpen(new Vector3d(1, 1, 0));
        AddOpen(new Vector3d(2, 1, 0));
        AddOpen(new Vector3d(2, 0, 0));
        AddObstacle(new Vector3d(1, 0, 0));
        Voxel blockedVoxel = TestRequire.VoxelAt(new Vector3d(1, 0, 0));

        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One));

        VolumeSurveyResult result = VolumeSurveyor.Shared.FindPath(request);
        var waypoints = TestRequire.NotNull(result.Waypoints);
        Voxel startNode = TestRequire.NotNull(request.StartNode);
        Voxel endNode = TestRequire.NotNull(request.EndNode);

        result.HasPath.Should().BeTrue();
        waypoints.Length.Should().BeGreaterThan(2);
        waypoints[0].GlobalIndex.Should().Be(startNode.WorldIndex);
        waypoints[^1].GlobalIndex.Should().Be(endNode.WorldIndex);
        waypoints.Should().NotContain(w => w.GlobalIndex == blockedVoxel.WorldIndex);
    }

    [Fact]
    public void PathGuideFactory_Should_ReturnAerialGuide_ForBlockedAerialRequests()
    {
        AddOpen(Vector3d.Zero);
        AddOpen(new Vector3d(0, 1, 0));
        AddOpen(new Vector3d(1, 1, 0));
        AddOpen(new Vector3d(2, 1, 0));
        AddOpen(new Vector3d(2, 0, 0));
        AddObstacle(new Vector3d(1, 0, 0));

        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One));

        VolumeGuide guide = TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out VolumeGuide? createdGuide),
            createdGuide);

        VolumeSurveyResult trailMap = TestRequire.NotNull(guide.TrailMap);
        trailMap.HasPath.Should().BeTrue();
        guide.CurrentWaypointIndex.Should().BeGreaterThan(0);
    }

    [Fact]
    public void VolumeSurveyor_Should_FindWaterDetour_ThroughHostMarkedVoxels()
    {
        AddWater(new Vector3d(0, 0, 1));
        AddWater(new Vector3d(0, 0, 0));
        AddWater(new Vector3d(1, 0, 0));
        AddWater(new Vector3d(2, 0, 0));
        AddWater(new Vector3d(2, 0, 1));

        AddObstacle(new Vector3d(1, 0, 1));

        VolumePathRequest request = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, new Vector3d(0, 0, 1),
            new Vector3d(2, 0, 1),
            Fixed64.One,
            medium: TraversalMedium.Liquid));

        VolumeSurveyResult result = VolumeSurveyor.Shared.FindPath(request);
        var waypoints = TestRequire.NotNull(result.Waypoints);
        Voxel startNode = TestRequire.NotNull(request.StartNode);
        Voxel endNode = TestRequire.NotNull(request.EndNode);

        result.HasPath.Should().BeTrue();
        waypoints.Length.Should().BeGreaterThan(2);
        waypoints[0].GlobalIndex.Should().Be(startNode.WorldIndex);
        waypoints[^1].GlobalIndex.Should().Be(endNode.WorldIndex);
    }

    private static void AddObstacle(Vector3d position)
    {
        var (grid, voxel) = TestRequire.GridAndVoxelAt(position);
        grid.TryAddObstacle(
            voxel,
            new BoundsKey(position, position)).Should().BeTrue();
    }

    private static void AddWater(Vector3d position)
    {
        PathTestFactory.RegisterGeneratedVolumePoint(position, TraversalMedium.Liquid, "AerialWater");
    }

    private static void AddOpen(Vector3d position)
    {
        PathTestFactory.RegisterGeneratedVolumePoint(position, TraversalMedium.Gas, "AerialOpen");
    }
}
