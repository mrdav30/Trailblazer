using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class HybridWaypointFlattenerTests : IDisposable
{
    public HybridWaypointFlattenerTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();

        GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16)),
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
    public void TryBuild_ShouldFlattenSegments_AndDeduplicateChartKeysAndAdjacentWaypoints()
    {
        RegisterLineChart("HybridFlattenChart", Vector3d.Zero, 5);

        AStarPathRequest first = AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One);
        AStarPathRequest second = AStarPathRequest.Create(
            new Vector3d(2, 0, 0),
            new Vector3d(4, 0, 0),
            Fixed64.One);

        first.Should().NotBeNull();
        second.Should().NotBeNull();

        HybridRoutePlan routePlan = new(
            new[]
            {
                HybridRouteStep.Segment(first),
                HybridRouteStep.Segment(second)
            },
            Array.Empty<TraversalTransition>(),
            0);

        HybridWaypointFlattener.TryBuild(
            routePlan,
            out AStarWaypoint[] flattenedWaypoints,
            out string[] chartKeys).Should().BeTrue();

        flattenedWaypoints.Should().NotBeNull();
        flattenedWaypoints[^1].IsGoal.Should().BeTrue();
        chartKeys.Should().Equal("HybridFlattenChart");

        int duplicatePositionCount = 0;
        for (int i = 0; i < flattenedWaypoints.Length; i++)
        {
            if (flattenedWaypoints[i].Position == new Vector3d(2, 0, 0))
                duplicatePositionCount++;
        }

        duplicatePositionCount.Should().Be(1);
    }

    [Fact]
    public void TryBuild_ShouldRejectUnsupportedSegmentRequests()
    {
        RegisterLineChart("HybridFlattenUnsupported", Vector3d.Zero, 2);
        GlobalGridManager.TryGetVoxel(Vector3d.Zero, out Voxel start).Should().BeTrue();
        GlobalGridManager.TryGetVoxel(new Vector3d(1, 0, 0), out Voxel end).Should().BeTrue();

        HybridRoutePlan routePlan = new(
            new[]
            {
                HybridRouteStep.Segment(new UnsupportedRequest(start, end))
            },
            Array.Empty<TraversalTransition>(),
            0);

        HybridWaypointFlattener.TryBuild(
            routePlan,
            out AStarWaypoint[] flattenedWaypoints,
            out string[] chartKeys).Should().BeFalse();

        flattenedWaypoints.Should().BeNull();
        chartKeys.Should().BeEmpty();
    }

    private static void RegisterLineChart(string chartName, Vector3d minBounds, int length)
    {
        bool[,,] data = new bool[1, length, 1];
        for (int i = 0; i < length; i++)
            data[0, i, 0] = true;

        PathTestFactory.RegisterFromData(chartName, data, minBounds);
    }

    private sealed class UnsupportedRequest : IPathRequest
    {
        public UnsupportedRequest(Voxel start, Voxel end)
        {
            Origin = start.WorldPosition;
            StartNode = start;
            TargetPosition = end.WorldPosition;
            EndNode = end;
        }

        public Vector3d Origin { get; }

        public Voxel StartNode { get; }

        public Vector3d TargetPosition { get; }

        public Voxel EndNode { get; }

        public Fixed64 UnitSize => Fixed64.One;

        public bool HasZeroDisplacement => StartNode == EndNode;

        public bool AllowUnwalkableEndpoints => false;

        public int MaxPathSearchRange { get; set; } = 1;

        public bool HasOrigin => true;

        public bool HasDestination => true;

        public bool HasValidEndpoints => true;

        public bool IsValid => true;

        public int RequestCacheKey => 42;

        public bool UpdateRequest(Vector3d origin, Vector3d destination, Fixed64? unitSize) => false;

        public bool TrySetOrigin(Vector3d origin, bool resetSearchRange = false) => false;

        public bool TrySetDestination(Vector3d destination, bool resetSearchRange = false) => false;

        public bool TrySetUnitSize(Fixed64 unitSize) => false;
    }
}
