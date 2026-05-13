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
        TestWorld.Setup();
        TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16)),
            out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TryBuild_ShouldFlattenSegments_AndDeduplicateChartKeysAndAdjacentWaypoints()
    {
        PathTestFactory.RegisterLineChart("HybridFlattenChart", Vector3d.Zero, 5);

        AStarPathRequest first = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One));
        AStarPathRequest second = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, new Vector3d(2, 0, 0),
            new Vector3d(4, 0, 0),
            Fixed64.One));

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
            out AStarWaypoint[]? flattenedWaypoints,
            out string[] chartKeys).Should().BeTrue();

        AStarWaypoint[] actualWaypoints = TestRequire.NotNull(flattenedWaypoints);
        actualWaypoints[^1].IsGoal.Should().BeTrue();
        chartKeys.Should().Equal("HybridFlattenChart");

        int duplicatePositionCount = 0;
        for (int i = 0; i < actualWaypoints.Length; i++)
        {
            if (actualWaypoints[i].Position == new Vector3d(2, 0, 0))
                duplicatePositionCount++;
        }

        duplicatePositionCount.Should().Be(1);
    }

    [Fact]
    public void TryBuild_ShouldRejectUnsupportedSegmentRequests()
    {
        PathTestFactory.RegisterLineChart("HybridFlattenUnsupported", Vector3d.Zero, 2);
        Voxel start = TestRequire.VoxelAt(Vector3d.Zero);
        Voxel end = TestRequire.VoxelAt(new Vector3d(1, 0, 0));

        HybridRoutePlan routePlan = new(
            new[]
            {
                HybridRouteStep.Segment(new UnsupportedRequest(start, end))
            },
            Array.Empty<TraversalTransition>(),
            0);

        HybridWaypointFlattener.TryBuild(
            routePlan,
            out AStarWaypoint[]? flattenedWaypoints,
            out string[] chartKeys).Should().BeFalse();

        flattenedWaypoints.Should().BeNull();
        chartKeys.Should().BeEmpty();
    }

    [Fact]
    public void TryBuild_ShouldReturnFalse_ForNullRoutePlan()
    {
        HybridWaypointFlattener.TryBuild(
            null!,
            out AStarWaypoint[]? waypoints,
            out string[] chartKeys).Should().BeFalse();

        waypoints.Should().BeNull();
        chartKeys.Should().BeEmpty();
    }

    [Fact]
    public void TryBuild_ShouldSucceed_WithVolumeSegmentStep()
    {
        // Register a gas corridor for the volume step and a solid point for the surrounding segments.
        PathTestFactory.RegisterLineChart("HybridFlattenVolStart", Vector3d.Zero, 2);
        PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(2, 0, 0), TraversalMedium.Gas, "HybridFlattenGas");
        PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(3, 0, 0), TraversalMedium.Gas, "HybridFlattenGas");
        PathTestFactory.RegisterLineChart("HybridFlattenVolEnd", new Vector3d(4, 0, 0), 2);

        Voxel volStart = TestRequire.VoxelAt(new Vector3d(2, 0, 0));
        Voxel volEnd = TestRequire.VoxelAt(new Vector3d(3, 0, 0));

        VolumePathRequest volRequest = TestRequire.NotNull(VolumePathRequest.Create(TestWorld.Context, new Vector3d(2, 0, 0),
            new Vector3d(3, 0, 0),
            Fixed64.One,
            medium: TraversalMedium.Gas));

        AStarPathRequest leadUp = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One));

        HybridRoutePlan plan = new(
            new[]
            {
                HybridRouteStep.Segment(leadUp),
                HybridRouteStep.Segment(volRequest),
            },
            Array.Empty<TraversalTransition>(),
            0);

        bool success = HybridWaypointFlattener.TryBuild(
            plan,
            out AStarWaypoint[]? flattenedWaypoints,
            out string[] chartKeys);

        success.Should().BeTrue();
        AStarWaypoint[] actualWaypoints = TestRequire.NotNull(flattenedWaypoints);
        actualWaypoints.Length.Should().BeGreaterThan(0);
        actualWaypoints[^1].IsGoal.Should().BeTrue();
    }

    [Fact]
    public void TryBuild_ShouldFlattenWaypointSteps_IntoOutput()
    {
        // Exercises the HybridRouteStepKind.Waypoint case in the flattener loop.
        // A plan built entirely from explicit waypoint steps should produce a non-empty
        // flattened array with the last waypoint marked as goal.
        HybridRoutePlan plan = new(
            new[]
            {
                HybridRouteStep.Waypoint(TestWorld.Context, new Vector3d(0, 0, 0), additionalCost: 0),
                HybridRouteStep.Waypoint(TestWorld.Context, new Vector3d(1, 0, 0), additionalCost: 1),
                HybridRouteStep.Waypoint(TestWorld.Context, new Vector3d(2, 0, 0), additionalCost: 1)
            },
            Array.Empty<TraversalTransition>(),
            0);

        HybridWaypointFlattener.TryBuild(
            plan,
            out AStarWaypoint[]? flattenedWaypoints,
            out string[] chartKeys).Should().BeTrue();

        AStarWaypoint[] actualWaypoints = TestRequire.NotNull(flattenedWaypoints);
        actualWaypoints.Length.Should().Be(3);
        actualWaypoints[0].Position.Should().Be(Vector3d.Zero);
        actualWaypoints[^1].IsGoal.Should().BeTrue();
        chartKeys.Should().BeEmpty("waypoint steps carry no chart keys");
    }

    [Fact]
    public void TryBuild_ShouldReturnFalse_WhenAStarGuideIsNull()
    {
        // Exercises the aStarGuide == null early-return branch in TryAppendSegmentWaypoints.
        // Two isolated single-cell charts have no partition neighbors, so A* produces no path
        // and RequestAStar returns null.
        PathTestFactory.RegisterSingleWalkablePoint("HybridFlattenIsolatedA", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("HybridFlattenIsolatedB", new Vector3d(7, 0, 0));

        AStarPathRequest request = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(7, 0, 0),
            Fixed64.One));

        HybridRoutePlan plan = new(
            new[] { HybridRouteStep.Segment(request) },
            Array.Empty<TraversalTransition>(),
            0);

        HybridWaypointFlattener.TryBuild(
            plan,
            out AStarWaypoint[]? flattenedWaypoints,
            out string[] chartKeys).Should().BeFalse();

        flattenedWaypoints.Should().BeNull();
    }

    [Fact]
    public void TryBuild_ShouldReturnFalse_WhenRouteHasNoSteps()
    {
        // Exercises the waypoints.Count == 0 guard: an empty steps array produces no
        // waypoints and TryBuild returns false.
        HybridRoutePlan plan = new(
            Array.Empty<HybridRouteStep>(),
            Array.Empty<TraversalTransition>(),
            0);

        HybridWaypointFlattener.TryBuild(
            plan,
            out AStarWaypoint[]? flattenedWaypoints,
            out string[] chartKeys).Should().BeFalse();

        flattenedWaypoints.Should().BeNull();
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

        public TrailblazerWorldContext Context => TestWorld.Context;

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
