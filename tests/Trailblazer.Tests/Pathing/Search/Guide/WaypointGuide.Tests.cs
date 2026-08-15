using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class WaypointGuideTests : IDisposable
{
    public WaypointGuideTests()
    {
        TestWorld.Setup();
        TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(12, 12, 12)),
            out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void VolumeGuide_ShouldHandleWaypointQueriesAndFallback()
    {
        var guide = new VolumeGuide();
        guide.Initialize(VolumeSurveyResult.Empty).Should().BeFalse();

        VolumeSurveyResult survey = VolumeSurveyResult.Create(
            TestWorld.Context,
            BuildWaypoints(
                Vector3d.Zero,
                new Vector3d(1, 0, 0),
                new Vector3d(2, 0, 0)),
            Array.Empty<string>(),
            TestPathRequest.CreateCacheKey(4));

        guide.Initialize(survey).Should().BeTrue();
        guide.CurrentWaypointIndex.Should().Be(1);
        guide.GetIndex(new Vector3d(1, 0, 0)).Should().Be(1);

        guide.TryGetMovementDirection(new Vector3d(3, 0, 0), out Vector3d movement).Should().BeTrue();
        movement.X.Should().BeLessThan(Fixed64.Zero);

        guide.GetCurrentWaypointDirection(Vector3d.Zero).X.Should().BeGreaterThan(Fixed64.Zero);
        guide.TryGetFallbackDirection(new Vector3d(3, 0, 0), out Vector3d fallback).Should().BeTrue();
        fallback.X.Should().BeLessThan(Fixed64.Zero);

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
            TestWorld.Context,
            BuildWaypoints(new Vector3d(1, 0, 0), new Vector3d(2, 0, 0)),
            Array.Empty<string>(),
            TestPathRequest.CreateCacheKey(5));

        var guide = new VolumeGuide();
        guide.Initialize(survey).Should().BeTrue();
        var waypoints = TestRequire.NotNull(survey.Waypoints);

        // Advance past the last waypoint.
        for (int i = 0; i < waypoints.Length; i++)
            guide.AdvanceWaypoint();

        guide.GetCurrentWaypointDirection(Vector3d.Zero).Should().Be(Vector3d.Zero,
            "index past end triggers the out-of-range guard");
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
