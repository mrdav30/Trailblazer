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
        if (TrailblazerWorldManager.IsActive)
            TrailblazerWorldManager.Reset();
        else
            TrailblazerWorldManager.Setup();

        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        TrailblazerWorldManager.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TrailblazerWorldManager.Reset();
        TrailblazerManager.Reset();
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
        TrailblazerWorldManager.TryGetVoxel(new Vector3d(1, 0, 0), out Voxel? blockedVoxel).Should().BeTrue();

        VolumePathRequest request = VolumePathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One);

        VolumeSurveyResult result = VolumeSurveyor.Shared.FindPath(request);

        result.HasPath.Should().BeTrue();
        result.Waypoints.Length.Should().BeGreaterThan(2);
        result.Waypoints[0].GlobalIndex.Should().Be(request.StartNode.WorldIndex);
        result.Waypoints[^1].GlobalIndex.Should().Be(request.EndNode.WorldIndex);
        result.Waypoints.Should().NotContain(w => w.GlobalIndex == blockedVoxel!.WorldIndex);
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

        VolumePathRequest request = VolumePathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One);

        PathGuideFactory.RequestGuide(request, out VolumeGuide guide).Should().BeTrue();

        guide.Should().NotBeNull();
        guide.TrailMap.HasPath.Should().BeTrue();
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

        VolumePathRequest request = VolumePathRequest.Create(
            new Vector3d(0, 0, 1),
            new Vector3d(2, 0, 1),
            Fixed64.One,
            medium: TraversalMedium.Liquid);

        request.Should().NotBeNull();

        VolumeSurveyResult result = VolumeSurveyor.Shared.FindPath(request);

        result.HasPath.Should().BeTrue();
        result.Waypoints.Length.Should().BeGreaterThan(2);
        result.Waypoints[0].GlobalIndex.Should().Be(request.StartNode.WorldIndex);
        result.Waypoints[^1].GlobalIndex.Should().Be(request.EndNode.WorldIndex);
    }

    private static void AddObstacle(Vector3d position)
    {
        TrailblazerWorldManager.TryGetGridAndVoxel(position, out VoxelGrid? grid, out Voxel? voxel).Should().BeTrue();
        grid!.TryAddObstacle(
            voxel!,
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
