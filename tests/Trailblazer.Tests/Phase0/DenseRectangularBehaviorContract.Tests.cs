using FixedMathSharp;
using FluentAssertions;
using GridForge;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using System.Linq;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Trailblazer.Tests.Navigation.Steering;
using Xunit;

namespace Trailblazer.Tests.Phase0;

/// <summary>
/// Freezes the dense rectangular behavior that the topology-aware graph must preserve intentionally.
/// These tests are characterization contracts, not endorsements of the legacy chart API.
/// </summary>
[Collection("PathingCollection")]
public sealed class DenseRectangularBehaviorContractTests : IDisposable
{
    public DenseRectangularBehaviorContractTests()
    {
        TestWorld.Setup();
        TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-8, -4, -8), new Vector3d(16, 8, 16)),
            out _).Should().BeTrue();
    }

    public void Dispose()
    {
        PathManager.Reset();
        TraversalTransitionRegistry.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AStarRoute_ShouldPreserveDenseRectangularWaypointShape()
    {
        var cells = new bool[1, 5, 3]
        {
            {
                { true,  true,  true },
                { false, false, true },
                { true,  true,  true },
                { true,  true,  true },
                { true,  true,  true }
            }
        };
        PathTestFactory.RegisterFromData(TestWorld.Context, "Phase0Route", cells, Vector3d.Zero);

        AStarGuide guide = RequestAStarGuide(new Vector3d(0, 0, 0), new Vector3d(4, 0, 0));

        guide.ActiveWaypoints.Select(waypoint => waypoint.Position).Should().Equal(
            new Vector3d(0, 0, 0),
            new Vector3d(0, 0, 1),
            new Vector3d(0, 0, 2),
            new Vector3d(1, 0, 2),
            new Vector3d(2, 0, 2),
            new Vector3d(3, 0, 1),
            new Vector3d(4, 0, 0));
        guide.ActiveWaypoints[^1].IsGoal.Should().BeTrue();

        PathGuideFactory.ReturnGuide(guide);
    }

    [Fact]
    public void AStarRoute_ShouldPreserveDeterministicTieBreakAcrossFreshSurveys()
    {
        var cells = new bool[1, 3, 3]
        {
            {
                { true, true, true },
                { true, false, true },
                { true, true, true }
            }
        };
        PathTestFactory.RegisterFromData(TestWorld.Context, "Phase0TieBreak", cells, Vector3d.Zero);
        AStarPathRequest request = CreateAStarRequest(new Vector3d(0, 0, 1), new Vector3d(2, 0, 1));

        Vector3d[][] observed = new Vector3d[4][];
        for (int i = 0; i < observed.Length; i++)
        {
            AStarSurveyResult survey = AStarSurveyor.Shared.FindPath(request);
            survey.HasPath.Should().BeTrue();
            observed[i] = TestRequire.NotNull(survey.Waypoints)
                .Select(waypoint => waypoint.Position)
                .ToArray();
        }

        observed[0].Should().Equal(
            new Vector3d(0, 0, 1),
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 0),
            new Vector3d(2, 0, 0),
            new Vector3d(2, 0, 1));
        observed.Skip(1).Should().OnlyContain(path => path.SequenceEqual(observed[0]));
    }

    [Fact]
    public void DynamicBlocker_ShouldInvalidateTheRouteAndRestoreItAfterRemoval()
    {
        PathTestFactory.RegisterSolidLine(TestWorld.Context, "Phase0DynamicBlocker", Vector3d.Zero, 3);
        AStarPathRequest request = CreateAStarRequest(Vector3d.Zero, new Vector3d(2, 0, 0));
        AStarGuide initial = RequestAStarGuide(request);
        PathGuideFactory.ReturnGuide(initial);

        var (grid, voxel) = TestRequire.GridAndVoxelAt(TestWorld.Context, new Vector3d(1, 0, 0));
        ObstacleToken obstacle = TestWorld.World.AllocateObstacleToken();
        grid!.TryAddObstacle(voxel!, obstacle).Should().BeTrue();

        PathGuideFactory.RequestGuide(request, out AStarGuide? blocked).Should().BeFalse();
        blocked.Should().BeNull();

        grid.TryRemoveObstacle(voxel, obstacle).Should().BeTrue();
        AStarGuide restored = RequestAStarGuide(request);
        restored.ActiveWaypoints[^1].Position.Should().Be(new Vector3d(2, 0, 0));
        PathGuideFactory.ReturnGuide(restored);
    }

    [Fact]
    public void TransitionFallback_ShouldPreserveOrderedTransitionRoute()
    {
        GuidedPathTestScene.RegisterTransitionFallbackScene(TestWorld.Context);
        AStarPathRequest request = TestRequire.NotNull(AStarPathRequest.Create(
            TestWorld.Context,
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            allowTraversalTransitions: true));

        HybridPathRequest hybrid = TestRequire.NotNull(HybridPathRequest.CreateFromAStar(request));

        hybrid.RoutePlan!.DirectedTransitions.Select(transition => transition.Id).Should().Equal(
            "guided-path-transition-entry",
            "guided-path-transition-exit");
        hybrid.RoutePlan.Steps.Select(step => step.Kind).Should().Equal(
            HybridRouteStepKind.Waypoint,
            HybridRouteStepKind.Waypoint,
            HybridRouteStepKind.PathSegment,
            HybridRouteStepKind.Waypoint,
            HybridRouteStepKind.Waypoint);
    }

    [Fact]
    public void FlowField_ShouldReuseOneDestinationResultAcrossCoveredOrigins()
    {
        PathTestFactory.RegisterSolidLine(TestWorld.Context, "Phase0FlowReuse", Vector3d.Zero, 8);
        FlowFieldPathRequest firstRequest = TestRequire.NotNull(FlowFieldPathRequest.Create(
            TestWorld.Context,
            new Vector3d(5, 0, 0),
            new Vector3d(7, 0, 0),
            Fixed64.One));
        FlowFieldPathRequest secondRequest = TestRequire.NotNull(FlowFieldPathRequest.Create(
            TestWorld.Context,
            new Vector3d(6, 0, 0),
            new Vector3d(7, 0, 0),
            Fixed64.One));

        FlowFieldGuide first = TestRequire.Created(
            PathGuideFactory.RequestGuide(firstRequest, out FlowFieldGuide? createdFirst),
            createdFirst);
        FlowFieldGuide second = TestRequire.Created(
            PathGuideFactory.RequestGuide(secondRequest, out FlowFieldGuide? createdSecond),
            createdSecond);

        secondRequest.RequestCacheKey.Should().Be(firstRequest.RequestCacheKey);
        second.FlowMap.Should().BeSameAs(first.FlowMap);

        PathGuideFactory.ReturnGuide(first);
        PathGuideFactory.ReturnGuide(second);
    }

    [Fact]
    public void ChartInvalidation_ShouldMakeCheckedOutAndCachedGuidesStale()
    {
        PathTestFactory.RegisterSolidLine(TestWorld.Context, "Phase0GuideInvalidation", Vector3d.Zero, 4);
        AStarPathRequest request = CreateAStarRequest(Vector3d.Zero, new Vector3d(3, 0, 0));
        AStarGuide checkedOut = RequestAStarGuide(request);
        AStarGuide cached = RequestAStarGuide(request);
        PathGuideFactory.ReturnGuide(cached);

        PathGuideFactory.TotalAStarGuideCount.Should().Be(1);
        PathGuideFactory.InvalidateCacheFor("Phase0GuideInvalidation");

        PathGuideFactory.TotalAStarGuideCount.Should().Be(0);
        checkedOut.TryGetMovementDirection(Vector3d.Zero, out _).Should().BeFalse();
        PathGuideFactory.ReturnGuide(checkedOut);
    }

    [Fact]
    public void SteeringController_ShouldAdvanceAWaypointWhenCloseAndAligned()
    {
        var cells = new bool[1, 3, 3]
        {
            {
                { true, true, true },
                { true, false, true },
                { true, true, true }
            }
        };
        PathTestFactory.RegisterFromData(TestWorld.Context, "Phase0WaypointFollowing", cells, Vector3d.Zero);
        var (grid, voxel) = TestRequire.GridAndVoxelAt(TestWorld.Context, new Vector3d(1, 0, 1));
        grid!.TryAddObstacle(voxel!, TestWorld.World.AllocateObstacleToken()).Should().BeTrue();

        var agent = new MockSteerAgent(new Vector3d(0, 0, 1));
        var steering = new NavSteering(TestWorld.Context, agent.Radius);
        AStarPathRequest request = CreateAStarRequest(new Vector3d(0, 0, 1), new Vector3d(2, 0, 1));

        steering.ApplyPathRequest(request);
        steering.GetHeading(agent);
        AStarGuide guide = steering.TrailGuide.Should().BeOfType<AStarGuide>().Subject;
        guide.AdvanceWaypoint();
        guide.AdvanceWaypoint();
        int before = guide.CurrentWaypointIndex;

        steering.GetHeading(agent);

        guide.CurrentWaypointIndex.Should().BeGreaterThan(before);
        steering.StopMove();
    }

    private static AStarPathRequest CreateAStarRequest(Vector3d origin, Vector3d destination)
    {
        return TestRequire.NotNull(AStarPathRequest.Create(
            TestWorld.Context,
            origin,
            destination,
            Fixed64.One));
    }

    private static AStarGuide RequestAStarGuide(Vector3d origin, Vector3d destination) =>
        RequestAStarGuide(CreateAStarRequest(origin, destination));

    private static AStarGuide RequestAStarGuide(AStarPathRequest request)
    {
        return TestRequire.Created(
            PathGuideFactory.RequestGuide(request, out AStarGuide? guide),
            guide);
    }
}
