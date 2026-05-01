using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class WaypointGuideTests : IDisposable
{
    public WaypointGuideTests()
    {
        TrailblazerWorldManager.Setup();
        TrailblazerWorldManager.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(12, 12, 12)),
            out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TrailblazerWorldManager.Reset();
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
    public void VolumeGuide_ShouldReturnDefaults_WhenInitializationFailed()
    {
        // Exercises the TrailMap == null guard in TryGetMovementDirection,
        // GetCurrentWaypointDirection, TryGetFallbackDirection, and TryGetWaypointAt,
        // reached when Initialize was never called or failed.
        var guide = new VolumeGuide();
        guide.Initialize(VolumeSurveyResult.Empty).Should().BeFalse();

        // TrailMap is null after a failed init — all queries should return defaults safely.
        guide.TryGetMovementDirection(Vector3d.Zero, out Vector3d dir).Should().BeFalse();
        dir.Should().Be(Vector3d.Zero);

        guide.GetCurrentWaypointDirection(Vector3d.Zero).Should().Be(Vector3d.Zero);

        guide.TryGetFallbackDirection(Vector3d.Zero, out Vector3d fallback).Should().BeFalse();
        fallback.Should().Be(Vector3d.Zero);

        guide.TryGetWaypointAt(0, out _).Should().BeFalse();
    }

    [Fact]
    public void VolumeGuide_GetCurrentWaypointDirection_ShouldReturnZero_WhenIndexOutOfRange()
    {
        // Exercises the CurrentWaypointIndex >= ActiveWaypoints.Length guard in
        // GetCurrentWaypointDirection by advancing the index past the last waypoint.
        VolumeSurveyResult survey = VolumeSurveyResult.Create(
            BuildWaypoints(new Vector3d(1, 0, 0), new Vector3d(2, 0, 0)),
            Array.Empty<string>(),
            5);

        var guide = new VolumeGuide();
        guide.Initialize(survey).Should().BeTrue();
        var waypoints = TestRequire.NotNull(survey.Waypoints);

        // Advance past the last waypoint.
        for (int i = 0; i < waypoints.Length; i++)
            guide.AdvanceWaypoint();

        guide.GetCurrentWaypointDirection(Vector3d.Zero).Should().Be(Vector3d.Zero,
            "index past end triggers the out-of-range guard");
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

    [Fact]
    public void HybridGuide_ShouldReturnFalseAndZero_WhenActiveWaypointsAreNullOrExhausted()
    {
        // Before initialization ActiveWaypoints is null — covers the null/empty guards in
        // TryGetMovementDirection, GetCurrentWaypointDirection, and TryGetFallbackDirection.
        var fresh = new HybridGuide();
        fresh.TryGetMovementDirection(Vector3d.Zero, out _).Should().BeFalse();
        fresh.TryGetFallbackDirection(Vector3d.Zero, out _).Should().BeFalse();
        fresh.GetCurrentWaypointDirection(Vector3d.Zero).Should().Be(Vector3d.Zero);

        // A single-waypoint guide whose only waypoint is at Vector3d.Zero:
        // CurrentWaypointIndex stays 0, and waypoint == Zero triggers the zero-direction guard.
        var zeroGuide = new HybridGuide();
        zeroGuide.Initialize(BuildWaypoints(Vector3d.Zero)).Should().BeTrue();
        zeroGuide.GetCurrentWaypointDirection(new Vector3d(1, 0, 0)).Should().Be(Vector3d.Zero);

        // Advancing past the end of the waypoint list exhausts the index, triggering the
        // out-of-range guard in GetCurrentWaypointDirection.
        var guide = new HybridGuide();
        guide.Initialize(BuildWaypoints(new Vector3d(1, 0, 0), new Vector3d(2, 0, 0))).Should().BeTrue();
        guide.AdvanceWaypoint();
        guide.AdvanceWaypoint();
        guide.AdvanceWaypoint();
        guide.GetCurrentWaypointDirection(Vector3d.Zero).Should().Be(Vector3d.Zero);
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
