using FixedMathSharp;
using FluentAssertions;
using GridForge;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public class AerialSurveyorTests : IDisposable
{
    public AerialSurveyorTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();

        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        GlobalGridManager.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AerialSurveyor_Should_FindChartlessDetour_AroundBlockedVoxel()
    {
        AddObstacle(new Vector3d(1, 0, 0));
        GlobalGridManager.TryGetVoxel(new Vector3d(1, 0, 0), out Voxel blockedVoxel).Should().BeTrue();

        AerialPathRequest request = AerialPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One);

        AerialSurveyResult result = AerialSurveyor.Shared.FindPath(request);

        result.HasPath.Should().BeTrue();
        result.Waypoints.Length.Should().BeGreaterThan(2);
        result.Waypoints[0].GlobalIndex.Should().Be(request.StartNode.GlobalIndex);
        result.Waypoints[^1].GlobalIndex.Should().Be(request.EndNode.GlobalIndex);
        result.Waypoints.Should().NotContain(w => w.GlobalIndex == blockedVoxel.GlobalIndex);
    }

    [Fact]
    public void PathGuideFactory_Should_ReturnAerialGuide_ForBlockedAerialRequests()
    {
        AddObstacle(new Vector3d(1, 0, 0));

        AerialPathRequest request = AerialPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One);

        PathGuideFactory.RequestGuide(request, out AerialGuide guide).Should().BeTrue();

        guide.Should().NotBeNull();
        guide.TrailMap.HasPath.Should().BeTrue();
        guide.CurrentWaypointIndex.Should().BeGreaterThan(0);
    }

    private static void AddObstacle(Vector3d position)
    {
        GlobalGridManager.TryGetVoxel(position, out Voxel voxel).Should().BeTrue();
        GridObstacleManager.TryAddObstacle(
            voxel.GlobalIndex,
            new BoundsKey(position, position)).Should().BeTrue();
    }
}
