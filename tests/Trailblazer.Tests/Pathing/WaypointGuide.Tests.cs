using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class WaypointGuideTests : IDisposable
{
    public WaypointGuideTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();

        GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(12, 12, 12)),
            out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AStarGuide_ShouldHandleWaypointQueriesAndArrival()
    {
        var guide = new AStarGuide();
        guide.Initialize(AStarSurveyResult.Empty).Should().BeFalse();

        AStarSurveyResult survey = AStarSurveyResult.Create(
            BuildWaypoints(
                Vector3d.Zero,
                new Vector3d(1, 0, 0),
                new Vector3d(2, 0, 0)),
            Array.Empty<string>(),
            1);

        guide.Initialize(survey).Should().BeTrue();
        guide.CurrentWaypointIndex.Should().Be(0);
        guide.HasArrived().Should().BeFalse();

        guide.GetIndex(new Vector3d(1, 0, 0)).Should().Be(1);
        guide.TryGetMovementDirection(new Vector3d(3, 0, 0), out Vector3d movement).Should().BeTrue();
        movement.x.Should().BeLessThan(Fixed64.Zero);

        guide.GetCurrentWaypointDirection(Vector3d.Zero).Should().Be(Vector3d.Zero);

        guide.AdvanceWaypoint();
        guide.GetCurrentWaypointDirection(Vector3d.Zero).x.Should().BeGreaterThan(Fixed64.Zero);
        guide.TryGetFallbackDirection(new Vector3d(3, 0, 0), out Vector3d fallback).Should().BeTrue();
        fallback.x.Should().BeLessThan(Fixed64.Zero);

        guide.TryGetWaypointAt(1, out AStarWaypoint waypoint).Should().BeTrue();
        waypoint.Position.Should().Be(new Vector3d(1, 0, 0));
        guide.TryGetWaypointAt(5, out _).Should().BeFalse();

        guide.AdvanceWaypoint();
        guide.HasArrived().Should().BeTrue();
    }

    [Fact]
    public void AStarGuide_ShouldOnlySmooth_WhenEnoughWaypointsExist()
    {
        var shortGuide = new AStarGuide { UseSplineSmoothing = true };
        shortGuide.Initialize(AStarSurveyResult.Create(
            BuildWaypoints(
                Vector3d.Zero,
                new Vector3d(1, 0, 0),
                new Vector3d(2, 0, 0)),
            Array.Empty<string>(),
            2)).Should().BeTrue();

        shortGuide.ActiveWaypoints.Should().HaveCount(3);

        var smoothedGuide = new AStarGuide { UseSplineSmoothing = true };
        smoothedGuide.Initialize(AStarSurveyResult.Create(
            BuildWaypoints(
                Vector3d.Zero,
                new Vector3d(1, 0, 0),
                new Vector3d(2, 0, 0),
                new Vector3d(3, 0, 0)),
            Array.Empty<string>(),
            3)).Should().BeTrue();

        smoothedGuide.ActiveWaypoints.Should().HaveCount(5);
        smoothedGuide.ActiveWaypoints[0].Position.Should().Be(Vector3d.Zero);
        smoothedGuide.ActiveWaypoints[^1].Position.Should().Be(new Vector3d(3, 0, 0));
    }

    [Fact]
    public void VolumeGuide_ShouldHandleWaypointQueriesAndFallback()
    {
        var guide = new VolumeGuide();
        guide.Initialize(VolumeSurveyResult.Empty).Should().BeFalse();

        VolumeSurveyResult survey = VolumeSurveyResult.Create(
            BuildWaypoints(
                Vector3d.Zero,
                new Vector3d(1, 0, 0),
                new Vector3d(2, 0, 0)),
            Array.Empty<string>(),
            4);

        guide.Initialize(survey).Should().BeTrue();
        guide.CurrentWaypointIndex.Should().Be(1);
        guide.GetIndex(new Vector3d(1, 0, 0)).Should().Be(1);

        guide.TryGetMovementDirection(new Vector3d(3, 0, 0), out Vector3d movement).Should().BeTrue();
        movement.x.Should().BeLessThan(Fixed64.Zero);

        guide.GetCurrentWaypointDirection(Vector3d.Zero).x.Should().BeGreaterThan(Fixed64.Zero);
        guide.TryGetFallbackDirection(new Vector3d(3, 0, 0), out Vector3d fallback).Should().BeTrue();
        fallback.x.Should().BeLessThan(Fixed64.Zero);

        guide.TryGetWaypointAt(2, out AStarWaypoint waypoint).Should().BeTrue();
        waypoint.IsGoal.Should().BeTrue();
        guide.TryGetWaypointAt(-1, out _).Should().BeFalse();
    }

    [Fact]
    public void HybridGuide_ShouldHandleInitializationAndWaypointQueries()
    {
        var guide = new HybridGuide();
        guide.Initialize(null!).Should().BeFalse();
        guide.Initialize(Array.Empty<AStarWaypoint>()).Should().BeFalse();

        AStarWaypoint[] waypoints = BuildWaypoints(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            new Vector3d(2, 0, 0));

        guide.Initialize(waypoints).Should().BeTrue();
        guide.CurrentWaypointIndex.Should().Be(1);
        guide.GetIndex(new Vector3d(1, 0, 0)).Should().Be(1);

        guide.TryGetMovementDirection(new Vector3d(3, 0, 0), out Vector3d movement).Should().BeTrue();
        movement.x.Should().BeLessThan(Fixed64.Zero);

        guide.GetCurrentWaypointDirection(Vector3d.Zero).x.Should().BeGreaterThan(Fixed64.Zero);
        guide.TryGetFallbackDirection(new Vector3d(3, 0, 0), out Vector3d fallback).Should().BeTrue();
        fallback.x.Should().BeLessThan(Fixed64.Zero);

        guide.TryGetWaypointAt(0, out AStarWaypoint waypoint).Should().BeTrue();
        waypoint.Position.Should().Be(Vector3d.Zero);
        guide.TryGetWaypointAt(99, out _).Should().BeFalse();
    }

    private static AStarWaypoint[] BuildWaypoints(params Vector3d[] positions)
    {
        var waypoints = new AStarWaypoint[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            waypoints[i] = new AStarWaypoint
            {
                Position = positions[i],
                PathCost = i,
                IsGoal = i == positions.Length - 1
            };
        }

        return waypoints;
    }
}
