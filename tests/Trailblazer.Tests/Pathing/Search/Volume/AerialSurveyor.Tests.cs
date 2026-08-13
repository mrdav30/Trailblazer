using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
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
        GuidedPathTestScene.AddOpen(TestWorld.Context, Vector3d.Zero);
        GuidedPathTestScene.AddOpen(TestWorld.Context, new Vector3d(0, 1, 0));
        GuidedPathTestScene.AddOpen(TestWorld.Context, new Vector3d(1, 1, 0));
        GuidedPathTestScene.AddOpen(TestWorld.Context, new Vector3d(2, 1, 0));
        GuidedPathTestScene.AddOpen(TestWorld.Context, new Vector3d(2, 0, 0));
        GuidedPathTestScene.AddObstacle(TestWorld.Context, new Vector3d(1, 0, 0));
        Voxel blockedVoxel = TestRequire.VoxelAt(TestWorld.Context, new Vector3d(1, 0, 0));

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
        GuidedPathTestScene.AddOpen(TestWorld.Context, Vector3d.Zero);
        GuidedPathTestScene.AddOpen(TestWorld.Context, new Vector3d(0, 1, 0));
        GuidedPathTestScene.AddOpen(TestWorld.Context, new Vector3d(1, 1, 0));
        GuidedPathTestScene.AddOpen(TestWorld.Context, new Vector3d(2, 1, 0));
        GuidedPathTestScene.AddOpen(TestWorld.Context, new Vector3d(2, 0, 0));
        GuidedPathTestScene.AddObstacle(TestWorld.Context, new Vector3d(1, 0, 0));

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
        GuidedPathTestScene.AddWater(TestWorld.Context, new Vector3d(0, 0, 1));
        GuidedPathTestScene.AddWater(TestWorld.Context, new Vector3d(0, 0, 0));
        GuidedPathTestScene.AddWater(TestWorld.Context, new Vector3d(1, 0, 0));
        GuidedPathTestScene.AddWater(TestWorld.Context, new Vector3d(2, 0, 0));
        GuidedPathTestScene.AddWater(TestWorld.Context, new Vector3d(2, 0, 1));

        GuidedPathTestScene.AddObstacle(TestWorld.Context, new Vector3d(1, 0, 1));

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

}
